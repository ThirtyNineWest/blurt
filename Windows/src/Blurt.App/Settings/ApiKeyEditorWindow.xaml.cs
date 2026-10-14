using System.Windows;
using Blurt.App.Main;
using Blurt.App.Services;

namespace Blurt.App.Settings;

/// <summary>
/// The modal key editor the Settings account row opens. Each opening gets its own entry view model,
/// so a submission from an earlier opening can never close this one. Closing the window by any
/// route cancels a submission in flight before it can save; the dialog result is true only when a
/// key was validated and saved.
/// </summary>
public partial class ApiKeyEditorWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ApiKeyEntryViewModel _entry;

    /// <summary>Creates the editor over <paramref name="apiKey"/>.</summary>
    public ApiKeyEditorWindow(ApiKeyModel apiKey)
    {
        InitializeComponent();
        _entry = new ApiKeyEntryViewModel(apiKey, _lifetime.Token);
        DataContext = _entry;
        _entry.Saved += OnSaved;
        Closed += OnClosed;
    }

    private void OnSaved()
    {
        if (IsVisible)
        {
            DialogResult = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        _entry.Saved -= OnSaved;
        _entry.Dispose();
        _lifetime.Dispose();
    }
}
