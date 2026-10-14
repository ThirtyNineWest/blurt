using System.Windows;
using System.Windows.Controls;

namespace Blurt.App.Main;

/// <summary>
/// The ready view's style switcher: Default plus one toggle per custom style, exactly one checked,
/// and a way into the Settings window's style editor. Binds to a <see cref="ReadyViewModel"/>.
/// </summary>
public partial class StyleSwitcher : UserControl
{
    /// <summary>Creates the switcher.</summary>
    public StyleSwitcher()
    {
        InitializeComponent();
    }

    /// <summary>Raised by "Edit Styles…".</summary>
    public event Action? EditStylesRequested;

    private void OnEditStylesClick(object sender, RoutedEventArgs e) => EditStylesRequested?.Invoke();
}
