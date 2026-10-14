using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Blurt.App.Main.Steps;
using Blurt.Engine;
using Blurt.Engine.Permissions;

namespace Blurt.App.Main;

/// <summary>
/// The main window: the setup wizard until setup is complete, then the ready view. A timer polls
/// setup readiness for the app's whole life, whether or not the window is showing, at
/// <see cref="SetupReadiness.PollInterval"/>, so a permission revoked while the window is hidden
/// still reopens the wizard.
/// </summary>
public partial class MainWindow : Window
{
    private readonly WizardViewModel _wizard;
    private readonly DispatcherTimer _poll;
    private readonly ApiKeyStep _apiKeyStep = new();
    private readonly MicrophoneStep _microphoneStep = new();
    private readonly ShortcutStep _shortcutStep = new();
    private readonly SoundStep _soundStep = new();

    /// <summary>Creates the window over the wizard and ready view models and starts the readiness poll.</summary>
    public MainWindow(WizardViewModel wizard, ReadyViewModel ready)
    {
        InitializeComponent();
        _wizard = wizard;
        Title = HostIdentity.Current.ProductName;
        Wizard.DataContext = wizard;
        Ready.DataContext = ready;
        _apiKeyStep.DataContext = wizard.KeyEntry;
        _microphoneStep.DataContext = wizard;
        _shortcutStep.DataContext = wizard.Dictation;
        _soundStep.DataContext = wizard.Dictation;
        Ready.SettingsRequested += () => SettingsRequested?.Invoke(false);
        Ready.EditStylesRequested += () => SettingsRequested?.Invoke(true);
        wizard.PropertyChanged += OnWizardChanged;
        wizard.NeedsForeground += BringForward;
        ShowCurrentContent();

        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = SetupReadiness.PollInterval(wizard.IsReady) };
        _poll.Tick += OnPollTick;
        _poll.Start();
        Closed += (_, _) => _poll.Stop();
    }

    /// <summary>Raised to open Settings; the argument is true to open on the Advanced tab.</summary>
    public event Action<bool>? SettingsRequested;

    /// <summary>Restores, shows, and activates the window.</summary>
    public void BringForward()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    /// <summary>Moves the wizard to the API key step and brings the window forward.</summary>
    public void ShowApiKeyStep()
    {
        _wizard.ShowApiKeyStep();
        BringForward();
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        _wizard.Refresh();
        _poll.Interval = SetupReadiness.PollInterval(_wizard.IsReady);
    }

    private void OnWizardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WizardViewModel.Step) or nameof(WizardViewModel.ShowsReadyView))
        {
            ShowCurrentContent();
        }
    }

    private void ShowCurrentContent()
    {
        var showsReady = _wizard.ShowsReadyView;
        Ready.Visibility = showsReady ? Visibility.Visible : Visibility.Collapsed;
        Wizard.Visibility = showsReady ? Visibility.Collapsed : Visibility.Visible;
        object step = _wizard.Step switch
        {
            SetupStep.ApiKey => _apiKeyStep,
            SetupStep.Microphone => _microphoneStep,
            SetupStep.Shortcut => _shortcutStep,
            _ => _soundStep,
        };
        if (!ReferenceEquals(StepHost.Content, step))
        {
            StepHost.Content = step;
        }
    }

    private void OnContinueClick(object sender, RoutedEventArgs e) => _wizard.Continue();

    private void OnBackClick(object sender, RoutedEventArgs e) => _wizard.Back();
}
