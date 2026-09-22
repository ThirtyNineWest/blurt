using System.Drawing;
using System.Windows.Forms;
using Blurt.Engine.Hotkey;
using Blurt.Engine.Pipeline;

namespace Blurt.App.Tray;

/// <summary>
/// The notification-area icon: one glyph per <see cref="TrayStatus"/> and a menu with Open Blurt,
/// Settings…, Check for Updates…, and Quit. A convenience only; nothing depends on it for
/// correctness. Create and use it on the UI thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _hint;
    private readonly Dictionary<TrayStatus, Icon> _icons;
    private readonly TriggerKeyStore _triggerKey;

    /// <summary>Creates the icon, visible, in the idle state; <paramref name="triggerKey"/> names the key in the menu's hint line.</summary>
    public TrayIcon(TriggerKeyStore triggerKey)
    {
        _triggerKey = triggerKey;
        _icons = Enum.GetValues<TrayStatus>().ToDictionary(status => status, LoadIcon);

        _hint = new ToolStripMenuItem { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_hint);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open Blurt", null, (_, _) => OpenRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Check for Updates…", null, (_, _) => CheckForUpdatesRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit Blurt", null, (_, _) => QuitRequested?.Invoke()));
        menu.Opening += (_, _) => RefreshHint();

        _notifyIcon = new NotifyIcon { ContextMenuStrip = menu };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        RefreshHint();
        SetStatus(TrayStatus.Idle);
        _notifyIcon.Visible = true;
    }

    /// <summary>Raised by "Open Blurt" and by double-clicking the icon.</summary>
    public event Action? OpenRequested;

    /// <summary>Raised by "Settings…".</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised by "Check for Updates…".</summary>
    public event Action? CheckForUpdatesRequested;

    /// <summary>Raised by "Quit Blurt".</summary>
    public event Action? QuitRequested;

    /// <summary>Shows the glyph and tooltip for <paramref name="status"/>.</summary>
    public void SetStatus(TrayStatus status)
    {
        _notifyIcon.Icon = _icons[status];
        _notifyIcon.Text = status.AccessibilityLabel();
    }

    /// <summary>Removes the icon from the notification area and releases the glyphs.</summary>
    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    private void RefreshHint() => _hint.Text = $"Tap or hold {_triggerKey.TriggerKey.Label()} to dictate and paste";

    private static Icon LoadIcon(TrayStatus status)
    {
        var name = $"Blurt.Icons.{status.IconName()}.ico";
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded tray icon {name} is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
