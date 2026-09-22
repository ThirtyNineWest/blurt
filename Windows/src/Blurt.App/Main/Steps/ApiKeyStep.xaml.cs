using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Blurt.Engine;
using Blurt.Engine.Config;
using Blurt.Engine.Diagnostics;

namespace Blurt.App.Main.Steps;

/// <summary>
/// The API key step: a masked field whose commit validates the key with AssemblyAI before saving.
/// A recoverable failure shows inline under the field; a storage fault shows as an alert.
/// Binds to an <see cref="ApiKeyEntryViewModel"/>.
/// </summary>
public partial class ApiKeyStep : UserControl
{
    private const string LogCategory = "ApiKeyStep";

    private static readonly Uri SignupUrl = new("https://www.assemblyai.com/dashboard/signup");

    /// <summary>Creates the step.</summary>
    public ApiKeyStep()
    {
        InitializeComponent();
        Loaded += (_, _) => KeyField.Focus();
    }

    private ApiKeyEntryViewModel? Model => DataContext as ApiKeyEntryViewModel;

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        model.DraftHasText = KeyField.Password.TrimmedNonEmpty() is not null;
        model.InlineError = null;
    }

    private async void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        if (Model is not { CanSubmit: true } model || KeyField.Password.TrimmedNonEmpty() is not { } key)
        {
            return;
        }

        try
        {
            var report = await model.SubmitAsync(key);
            switch (report)
            {
                case null:
                    KeyField.Clear();
                    break;
                case ApiKeyFailureReport.AlertReport alert:
                    MessageBox.Show(Window.GetWindow(this), alert.Message, alert.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                default:
                    KeyField.Focus();
                    break;
            }
        }
        catch (Exception failure)
        {
            BlurtLog.Error(LogCategory, $"key submission failed: {failure.GetType().Name}: {failure.Message}");
        }
    }

    private void OnGetKeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SignupUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            BlurtLog.Error(LogCategory, $"could not open the signup page: {failure.Message}");
        }
    }
}
