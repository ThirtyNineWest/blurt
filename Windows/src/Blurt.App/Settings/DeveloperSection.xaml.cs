using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Diagnostics;

namespace Blurt.App.Settings;

/// <summary>
/// The Developer section: the developer-mode switch and the two log paths, each with a button that
/// copies it to the clipboard on the user's click. Binds to a <see cref="SettingsViewModel"/>.
/// </summary>
public partial class DeveloperSection : UserControl
{
    /// <summary>Creates the section.</summary>
    public DeveloperSection()
    {
        InitializeComponent();
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
        {
            return;
        }

        try
        {
            Clipboard.SetText(path);
        }
        catch (COMException failure)
        {
            BlurtLog.Error("DeveloperSection", $"could not copy the log path: {failure.Message}");
        }
    }
}
