using System.Windows.Controls;

namespace Blurt.App.Settings;

/// <summary>The General tab: the shortcut and sound sections the wizard also shows. Binds to a <see cref="SettingsViewModel"/>.</summary>
public partial class GeneralTab : UserControl
{
    /// <summary>Creates the tab.</summary>
    public GeneralTab()
    {
        InitializeComponent();
    }
}
