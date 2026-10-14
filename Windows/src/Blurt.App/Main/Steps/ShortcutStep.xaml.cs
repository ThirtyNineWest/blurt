using System.Windows.Controls;

namespace Blurt.App.Main.Steps;

/// <summary>The Shortcut step and Settings section: the trigger key and activation pickers, with the activation's guidance as the footer. Binds to a <see cref="Settings.DictationSettingsViewModel"/>.</summary>
public partial class ShortcutStep : UserControl
{
    /// <summary>Creates the view.</summary>
    public ShortcutStep()
    {
        InitializeComponent();
    }
}
