using System.Windows;
using Blurt.App.Main;

namespace Blurt.App.Settings;

/// <summary>
/// The modal key editor the Settings account row opens: the API key entry, closing itself once a
/// submitted key is saved. Cancel closes it without saving; a validation still in flight then
/// completes, and saves only a key AssemblyAI accepted.
/// </summary>
public partial class ApiKeyEditorWindow : Window
{
    /// <summary>Creates the editor bound to <paramref name="entry"/>.</summary>
    public ApiKeyEditorWindow(ApiKeyEntryViewModel entry)
    {
        InitializeComponent();
        DataContext = entry;
        entry.Saved += OnSaved;
        Closed += (_, _) => entry.Saved -= OnSaved;
    }

    private void OnSaved()
    {
        if (IsVisible)
        {
            DialogResult = true;
        }
    }
}
