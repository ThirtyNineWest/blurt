using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blurt.App.Services;
using Blurt.Engine;

namespace Blurt.App.Settings;

/// <summary>
/// The Settings window: General (shortcut, activation, cue voice), Vocabulary (key terms, text
/// shortcuts), and Advanced (transcription, styles, updates, developer mode, reset). One
/// instance lives for the app's lifetime; closing it hides it.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    private readonly InstallReset _reset;
    private readonly Action _restart;
    private readonly Action _readAloudSettingsChanged;

    /// <summary>
    /// Creates the window over <paramref name="model"/>. A confirmed reset runs <paramref name="reset"/>
    /// and, when every step landed, <paramref name="restart"/>; after a partial reset it runs
    /// <paramref name="readAloudSettingsChanged"/> so the cached read-aloud settings match the stores.
    /// </summary>
    public SettingsWindow(SettingsViewModel model, InstallReset reset, Action restart, Action readAloudSettingsChanged)
    {
        InitializeComponent();
        _model = model;
        _reset = reset;
        _restart = restart;
        _readAloudSettingsChanged = readAloudSettingsChanged;
        DataContext = model;
        Title = $"{HostIdentity.Current.ProductName} Settings";
        Advanced.ResetConfirmed += OnResetConfirmed;
    }

    /// <summary>When set, a hidden window is closed for real rather than hidden.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Shows and activates the window on General, or on Advanced when <paramref name="advanced"/>.</summary>
    public void Present(bool advanced)
    {
        if (!IsVisible)
        {
            _model.Reload();
        }

        Tabs.SelectedItem = advanced ? AdvancedItem : GeneralItem;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    /// <inheritdoc/>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        CommitFocusedEdit();
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    /// <summary>
    /// A text box still focused when the window hides has not pushed its last edit to its source;
    /// <c>LostFocus</c> does not fire on deactivation.
    /// </summary>
    private static void CommitFocusedEdit()
    {
        if (Keyboard.FocusedElement is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private void OnResetConfirmed()
    {
        if (_reset.Run() is { } alert)
        {
            _readAloudSettingsChanged();
            MessageBox.Show(this, alert.Message, alert.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _restart();
    }
}
