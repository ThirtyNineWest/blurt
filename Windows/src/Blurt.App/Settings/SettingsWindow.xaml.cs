using System.Windows;
using Blurt.App.Services;
using Blurt.Engine;

namespace Blurt.App.Settings;

/// <summary>
/// The Settings window: General (shortcut, activation, cue voice) and Advanced (transcription,
/// styles, updates, developer mode, reset). One instance lives for the app's lifetime; closing it
/// hides it.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    private readonly InstallReset _reset;
    private readonly Action _restart;

    /// <summary>
    /// Creates the window over <paramref name="model"/>. A confirmed reset runs <paramref name="reset"/>
    /// and, when every step landed, <paramref name="restart"/>.
    /// </summary>
    public SettingsWindow(SettingsViewModel model, InstallReset reset, Action restart)
    {
        InitializeComponent();
        _model = model;
        _reset = reset;
        _restart = restart;
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
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnResetConfirmed()
    {
        if (_reset.Run() is { } alert)
        {
            MessageBox.Show(this, alert.Message, alert.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _restart();
    }
}
