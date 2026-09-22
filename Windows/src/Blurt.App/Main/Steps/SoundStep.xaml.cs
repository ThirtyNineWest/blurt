using System.Windows.Controls;

namespace Blurt.App.Main.Steps;

/// <summary>The Sound step and Settings section: the cue voice picker, grouped by voice family, with None first. Binds to a <see cref="Settings.DictationSettingsViewModel"/>.</summary>
public partial class SoundStep : UserControl
{
    /// <summary>Creates the view.</summary>
    public SoundStep()
    {
        InitializeComponent();
    }
}
