using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Blurt.App.Main;

/// <summary>
/// The ready view's recent dictations: up to three rows in a card whose height is reserved for a full
/// list, or an empty-state line. Clicking a row, or its Copy button (shown on hover or keyboard
/// focus), copies that row's transcript. Binds to a <see cref="ReadyViewModel"/>.
/// </summary>
public partial class RecentDictationsView : UserControl
{
    /// <summary>Creates the view.</summary>
    public RecentDictationsView()
    {
        InitializeComponent();
    }

    private void OnRowClicked(object sender, MouseButtonEventArgs e) => CopyRow(sender);

    private void OnCopyClicked(object sender, RoutedEventArgs e) => CopyRow(sender);

    private static void CopyRow(object sender)
    {
        if (sender is FrameworkElement { DataContext: RecentRow row })
        {
            row.Copy();
        }
    }
}
