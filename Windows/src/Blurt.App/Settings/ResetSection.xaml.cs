using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The Reset section: one destructive button that asks for confirmation, with Cancel as the default,
/// before raising <see cref="ResetConfirmed"/>.
/// </summary>
public partial class ResetSection : UserControl
{
    /// <summary>Creates the section.</summary>
    public ResetSection()
    {
        InitializeComponent();
        FooterText.Text = InstallResetReport.Footer;
    }

    /// <summary>Raised once the user confirms the reset.</summary>
    public event Action? ResetConfirmed;

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        var confirmation = InstallResetReport.Confirmation;
        var choice = MessageBox.Show(
            Window.GetWindow(this),
            confirmation.Message,
            confirmation.Title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (choice == MessageBoxResult.OK)
        {
            ResetConfirmed?.Invoke();
        }
    }
}
