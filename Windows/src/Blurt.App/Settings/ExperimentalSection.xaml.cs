using System.Windows.Controls;

namespace Blurt.App.Settings;

/// <summary>
/// The Experimental section: the read-aloud switch with hold-to-ask, speed, and skipping jargon,
/// and the guidance footers. Binds to a <see cref="SettingsViewModel"/>'s <see cref="SettingsViewModel.Experimental"/>.
/// </summary>
public partial class ExperimentalSection : UserControl
{
    /// <summary>Creates the section.</summary>
    public ExperimentalSection()
    {
        InitializeComponent();
    }
}
