using System.Windows.Controls;

namespace Blurt.App.Main;

/// <summary>
/// The ready view's recent dictations: up to three rows in a card whose height is reserved for a full
/// list, or an empty-state line. Binds to a <see cref="ReadyViewModel"/>.
/// </summary>
public partial class RecentDictationsView : UserControl
{
    /// <summary>Creates the view.</summary>
    public RecentDictationsView()
    {
        InitializeComponent();
    }
}
