using System.Windows;
using System.Windows.Controls;

namespace Blurt.App.Settings;

/// <summary>
/// The General tab: the AssemblyAI account row, then the shortcut and sound sections the wizard also
/// shows. Binds to a <see cref="SettingsViewModel"/>.
/// </summary>
public partial class GeneralTab : UserControl
{
    /// <summary>Creates the tab.</summary>
    public GeneralTab()
    {
        InitializeComponent();
    }

    private void OnEditKeyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel model)
        {
            return;
        }

        var saved = new ApiKeyEditorWindow(model.ApiKey) { Owner = Window.GetWindow(this) }.ShowDialog();
        if (saved == true)
        {
            model.RefreshKeyDisplay();
        }
    }
}
