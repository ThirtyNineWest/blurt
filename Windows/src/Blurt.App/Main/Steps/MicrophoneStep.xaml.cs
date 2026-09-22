using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Diagnostics;

namespace Blurt.App.Main.Steps;

/// <summary>
/// The microphone step: whether access is granted, a way to the Privacy settings page, and a test
/// that opens the default capture device. Binds to a <see cref="WizardViewModel"/>.
/// </summary>
public partial class MicrophoneStep : UserControl
{
    /// <summary>Creates the step.</summary>
    public MicrophoneStep()
    {
        InitializeComponent();
    }

    private void OnOpenPrivacySettingsClick(object sender, RoutedEventArgs e) => WizardViewModel.OpenMicrophonePrivacySettings();

    private async void OnTestMicrophoneClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WizardViewModel model)
        {
            return;
        }

        try
        {
            await model.TestMicrophoneAsync();
        }
        catch (Exception failure)
        {
            BlurtLog.Error("MicrophoneStep", $"microphone test failed: {failure.GetType().Name}: {failure.Message}");
        }
    }
}
