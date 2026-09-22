using System.Windows;
using Blurt.Engine;

namespace Blurt.App;

/// <summary>The main window: the product name and the live dictation phase, bound through the coordinator.</summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the window bound to <paramref name="coordinator"/>.</summary>
    public MainWindow(AppCoordinator coordinator)
    {
        InitializeComponent();
        DataContext = coordinator;
        Title = HostIdentity.Current.ProductName;
        ProductName.Text = HostIdentity.Current.ProductName;
    }
}
