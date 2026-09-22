using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Blurt.App.Cues;
using Blurt.App.Services;
using Blurt.App.Tray;
using Blurt.App.Update;
using Blurt.Engine;
using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Permissions;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Update;
using Blurt.Engine.Windows.Config;
using Blurt.Engine.Windows.Permissions;

namespace Blurt.App;

/// <summary>
/// The composition root: adopts the host identity, enforces a single instance, routes diagnostics
/// to the file log, builds the engine and the shell, and arms dictation when setup is complete.
/// </summary>
public partial class App : Application
{
    private const string LogCategory = "App";

    private Mutex? _instanceMutex;
    private FileLogSink? _logSink;
    private AppCoordinator? _coordinator;
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;
    private UpdateCheckModel? _updates;
    private bool _quitting;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
#if DEBUG
        var identity = HostIdentity.BlurtDev with { LogDirectoryName = "Blurt-dev" };
#else
        var identity = HostIdentity.Blurt;
#endif
        HostIdentity.Configure(identity);
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\Blurt-{identity.Subsystem}-{identity.LogDirectoryName}", out var createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        _logSink = new FileLogSink(WindowsPaths.LogDirectory(identity));
        BlurtLog.Sink = _logSink.Write;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        SessionEnding += OnSessionEnding;
        BlurtLog.Info(LogCategory, $"starting {UpdateAlertContent.AppVersionLabel(UpdateCheckModel.AssemblyVersion())} as {identity.CredentialTarget}, data in {WindowsPaths.AppDataDirectory(identity)}");

        Compose(identity);
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        if (_logSink is not null)
        {
            BlurtLog.Info(LogCategory, $"exiting with code {e.ApplicationExitCode}");
            BlurtLog.Sink = null;
            _logSink.Dispose();
        }

        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void Compose(HostIdentity identity)
    {
        var transport = new HttpClientTransport();
        var catalog = SoundPackCatalogLoader.Load(SoundPackCatalogLoader.DefaultPath);
        var settings = new SettingsStores(new JsonSettingsStore(WindowsPaths.SettingsFile(identity)), catalog);
        var keyStore = new CredentialManagerApiKeyStore(identity.CredentialTarget);
        var validator = new ApiKeyValidator(transport);
        var apiKey = new ApiKeyModel(keyStore, validator.ValidateAsync);
        var log = new DictationLog(
            () => settings.DeveloperMode.IsEnabled,
            WindowsPaths.LogDirectory(identity),
            STTPrompt.Text,
            KeytermsBoost.Fitted);
        var permissions = new PermissionsChecker(MicrophoneAccess.Probe);
        var components = DictationComponents.Production(transport, keyStore, settings);

        var coordinator = new AppCoordinator(Dispatcher, components, settings, apiKey, log, permissions);
        _coordinator = coordinator;

        var mainWindow = new MainWindow(coordinator);
        _mainWindow = mainWindow;
        var mainHandle = new WindowInteropHelper(mainWindow).EnsureHandle();
        components.Focus.IgnoreWindow(mainHandle);
        mainWindow.Closed += (_, _) => components.Focus.ForgetWindow(mainHandle);
        mainWindow.Closing += OnMainWindowClosing;

        var updates = new UpdateCheckModel(
            new UpdateChecker(transport),
            UpdateCheckModel.AssemblyVersion(),
            settings.LastUpdateCheck,
            () => _mainWindow);
        _updates = updates;

        var tray = new TrayIcon(settings.TriggerKey);
        _tray = tray;
        tray.OpenRequested += ShowMainWindow;
        tray.SettingsRequested += ShowMainWindow;
        tray.CheckForUpdatesRequested += () => Observe(updates.CheckNowAsync(), "update check");
        tray.QuitRequested += () => Observe(QuitAsync(), "quit");

        coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        coordinator.SetupBlocked += ShowMainWindow;
        coordinator.Start();
        mainWindow.Show();

        var ready = SetupReadiness.IsReady(permissions.Check(), apiKey.HasKey);
        BlurtLog.Info(LogCategory, $"setup ready: {ready}");
        if (ready)
        {
            coordinator.ShowOverlayArmed();
        }

        Observe(updates.CheckAtLaunchAsync(() => SetupReadiness.IsReady(permissions.Check(), apiKey.HasKey)), "launch update check");
    }

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppCoordinator.TrayStatus) && _coordinator is { } coordinator)
        {
            _tray?.SetStatus(coordinator.TrayStatus);
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is not { } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private async Task QuitAsync()
    {
        if (_quitting)
        {
            return;
        }

        BlurtLog.Info(LogCategory, "quit requested");
        try
        {
            await TearDownAsync();
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        BlurtLog.Info(LogCategory, $"session ending ({e.ReasonSessionEnding})");
        Observe(TearDownAsync(), "session-end teardown");
    }

    private async Task TearDownAsync()
    {
        _quitting = true;
        _tray?.Dispose();
        _tray = null;
        if (_coordinator is { } coordinator)
        {
            _coordinator = null;
            await coordinator.DisposeAsync();
        }
    }

    private static void Observe(Task task, string operation) => TaskObserver.Observe(task, LogCategory, operation);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        BlurtLog.Error(LogCategory, $"unhandled UI exception: {e.Exception}");
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        BlurtLog.Error(LogCategory, $"unhandled exception (terminating: {e.IsTerminating}): {e.ExceptionObject}");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        BlurtLog.Error(LogCategory, $"unobserved task exception: {e.Exception}");
}
