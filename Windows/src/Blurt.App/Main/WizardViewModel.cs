using System.ComponentModel;
using Blurt.App.Services;
using Blurt.App.Settings;
using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Permissions;
using Blurt.Engine.Windows.Permissions;

namespace Blurt.App.Main;

/// <summary>
/// Backs the main window: the setup wizard's step and controls, and whether the ready view shows.
/// <see cref="Refresh"/> re-reads the permission status, and readiness drives the overlay only on
/// its transitions, so a steady-state poll never disturbs a live dictation. Use on
/// the UI thread.
/// </summary>
public sealed class WizardViewModel : ObservableObject
{
    private const string LogCategory = "Wizard";

    private readonly AppCoordinator _coordinator;
    private readonly PermissionsChecker _permissions;
    private readonly SetupWizard _wizard;
    private PermissionStatus _status;
    private bool _isReady;
    private bool _isTestingMicrophone;
    private string? _microphoneTestResult;

    /// <summary>Creates the view model and arms the overlay when setup is already complete.</summary>
    public WizardViewModel(AppCoordinator coordinator, PermissionsChecker permissions, DictationSettingsViewModel dictation)
    {
        _coordinator = coordinator;
        _permissions = permissions;
        Dictation = dictation;
        KeyEntry = new ApiKeyEntryViewModel(coordinator.ApiKey);
        _status = permissions.Check();
        _isReady = SetupReadiness.IsReady(_status, coordinator.ApiKey.HasKey);
        _wizard = new SetupWizard(_isReady);
        BlurtLog.Info(LogCategory, $"setup ready: {_isReady}");
        if (_isReady)
        {
            coordinator.ShowOverlayArmed();
        }

        coordinator.ApiKey.PropertyChanged += OnApiKeyChanged;
        dictation.PropertyChanged += OnDictationChanged;
        KeyEntry.Saved += OnKeySaved;
    }

    /// <summary>Raised when a previously granted permission was revoked, so the window can come forward.</summary>
    public event Action? NeedsForeground;

    /// <summary>The shortcut and sound pickers.</summary>
    public DictationSettingsViewModel Dictation { get; }

    /// <summary>Whether setup is ready: the permission granted and a key stored.</summary>
    public bool IsReady => _isReady;

    /// <summary>Whether the main window shows the ready view rather than the wizard.</summary>
    public bool ShowsReadyView => _wizard.ShowsReadyView(_isReady);

    /// <summary>The wizard step on screen.</summary>
    public SetupStep Step => _wizard.Step;

    /// <summary>"Step 2 of 4".</summary>
    public string StepCaption => $"Step {_wizard.StepNumber} of {SetupWizard.Steps.Count}";

    /// <summary>Whether Back applies on this step.</summary>
    public bool CanGoBack => _wizard.CanGoBack;

    /// <summary>Whether Continue is enabled on this step.</summary>
    public bool CanContinue => _wizard.CanContinue(_status, _coordinator.ApiKey.HasKey);

    /// <summary>"Finish" on the last step, "Continue" before it.</summary>
    public string ContinueTitle => _wizard.IsLastStep ? "Finish" : "Continue";

    /// <summary>The API key step's entry; a saved key advances past that step.</summary>
    public ApiKeyEntryViewModel KeyEntry { get; }

    /// <summary>Whether microphone access is granted, as of the last poll.</summary>
    public bool MicrophoneGranted => _status.Microphone;

    /// <summary>The Microphone step's footer, naming the trigger key.</summary>
    public string MicrophoneFooter => $"Blurt records only after you start dictating with {Dictation.TriggerKeyLabel}.";

    /// <summary>Whether a microphone test is running.</summary>
    public bool IsTestingMicrophone
    {
        get => _isTestingMicrophone;
        private set => Set(ref _isTestingMicrophone, value);
    }

    /// <summary>The last microphone test's result sentence, or null before any test.</summary>
    public string? MicrophoneTestResult
    {
        get => _microphoneTestResult;
        private set => Set(ref _microphoneTestResult, value);
    }

    /// <summary>
    /// Re-reads the permission status, updates readiness from it and <see cref="ApiKeyModel.HasKey"/>, arms or hides the
    /// overlay on a readiness transition, and retries a failed key hook install while ready.
    /// </summary>
    public void Refresh()
    {
        var latest = _permissions.Check();
        var revoked = latest.LostGrant(since: _status);
        var changed = latest != _status;
        _status = latest;
        if (changed)
        {
            Raise(nameof(MicrophoneGranted));
        }

        SyncReadiness(raiseAlways: changed);
        if (revoked)
        {
            NeedsForeground?.Invoke();
        }

        if (_isReady)
        {
            _coordinator.RetryKeyHookInstallIfNeeded();
        }
    }

    /// <summary>Advances the wizard when the current step allows it.</summary>
    public void Continue()
    {
        if (_wizard.Continue(_status, _coordinator.ApiKey.HasKey))
        {
            RaiseWizard();
        }
    }

    /// <summary>Moves the wizard back one step.</summary>
    public void Back()
    {
        if (_wizard.Back())
        {
            RaiseWizard();
        }
    }

    /// <summary>
    /// Re-reads whether a key is stored, then moves the wizard to the API key step, so a key deleted
    /// outside the app shows the step instead of the ready view.
    /// </summary>
    public void ShowApiKeyStep()
    {
        _coordinator.ApiKey.RefreshStatus();
        _wizard.Show(SetupStep.ApiKey);
        RaiseWizard();
    }

    /// <summary>Opens Windows Settings at Privacy &gt; Microphone.</summary>
    public static void OpenMicrophonePrivacySettings()
    {
        try
        {
            PermissionsChecker.OpenMicrophonePrivacySettings();
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            BlurtLog.Error(LogCategory, $"could not open privacy settings: {failure.Message}");
        }
    }

    /// <summary>Opens the default capture device off the UI thread and shows what happened.</summary>
    public async Task TestMicrophoneAsync()
    {
        if (IsTestingMicrophone)
        {
            return;
        }

        IsTestingMicrophone = true;
        try
        {
            var state = await Task.Run(MicrophoneAccess.ProbeDevice);
            MicrophoneTestResult = state.Description();
        }
        finally
        {
            IsTestingMicrophone = false;
        }

        Refresh();
    }

    private void SyncReadiness(bool raiseAlways = true)
    {
        var ready = SetupReadiness.IsReady(_status, _coordinator.ApiKey.HasKey);
        if (ready == _isReady && !raiseAlways)
        {
            return;
        }

        if (ready != _isReady)
        {
            _isReady = ready;
            BlurtLog.Info(LogCategory, $"setup ready: {ready}");
            if (ready)
            {
                _coordinator.ShowOverlayArmed();
            }
            else
            {
                _wizard.ReadinessLost(_status, _coordinator.ApiKey.HasKey);
                _coordinator.HideOverlay();
            }
        }

        Raise(nameof(IsReady));
        RaiseWizard();
    }

    private void RaiseWizard() =>
        Raise(nameof(Step), nameof(StepCaption), nameof(CanGoBack), nameof(CanContinue), nameof(ContinueTitle), nameof(ShowsReadyView));

    private void OnApiKeyChanged(object? sender, PropertyChangedEventArgs e) => SyncReadiness();

    private void OnKeySaved()
    {
        if (Step == SetupStep.ApiKey)
        {
            Continue();
        }
    }

    private void OnDictationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DictationSettingsViewModel.TriggerKeyLabel))
        {
            Raise(nameof(MicrophoneFooter));
        }
    }
}
