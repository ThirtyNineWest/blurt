using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Diagnostics;

namespace Blurt.App.Main;

/// <summary>
/// The screen the main window shows once setup is complete: how to dictate, the listening state,
/// the style switcher, the recent dictations, and the way into Settings. Esc cancels an in-flight
/// dictation through the Cancel Dictation button. Binds to a <see cref="ReadyViewModel"/>.
/// </summary>
public partial class ReadyView : UserControl
{
    private static readonly Uri PoweredByUrl = new("https://www.assemblyai.com/blurt");

    /// <summary>Creates the view.</summary>
    public ReadyView()
    {
        InitializeComponent();
        Switcher.EditStylesRequested += () => EditStylesRequested?.Invoke();
    }

    /// <summary>Raised by the Settings button.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised by the style switcher's "Edit Styles…".</summary>
    public event Action? EditStylesRequested;

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnCancelDictationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReadyViewModel { IsCapturing: true } model)
        {
            model.CancelDictation();
        }
    }

    private void OnPoweredByClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PoweredByUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException)
        {
            BlurtLog.Error("ReadyView", $"could not open the AssemblyAI page: {failure.Message}");
        }
    }
}
