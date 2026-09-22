using System.Windows;
using System.Windows.Controls;
using Blurt.App.Services;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The Advanced tab: enhanced transcripts, the custom styles list with its editor, the update check,
/// developer mode, and the reset. Binds to a <see cref="SettingsViewModel"/>.
/// </summary>
public partial class AdvancedTab : UserControl
{
    /// <summary>Creates the tab.</summary>
    public AdvancedTab()
    {
        InitializeComponent();
        Reset.ResetConfirmed += () => ResetConfirmed?.Invoke();
    }

    /// <summary>Raised once the user confirms "Reset Blurt…".</summary>
    public event Action? ResetConfirmed;

    private SettingsViewModel? Model => DataContext as SettingsViewModel;

    private void OnAddStyleClick(object sender, RoutedEventArgs e)
    {
        if (Model is { Styles.CanAdd: true } model)
        {
            OpenEditor(model.Styles, new StyleProfile(string.Empty, string.Empty), isExisting: false);
        }
    }

    private void OnEditStyleClick(object sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is FrameworkElement { Tag: StyleProfile profile })
        {
            OpenEditor(model.Styles, profile, isExisting: true);
        }
    }

    private void OpenEditor(StylesViewModel styles, StyleProfile profile, bool isExisting)
    {
        var editor = new StyleProfileEditor(profile, isExisting, styles.CharacterLimit) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        if (editor.Deleted)
        {
            styles.Delete(profile.Id);
        }
        else if (editor.Result is { } edited)
        {
            styles.Save(edited);
        }
    }

    private void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (Model is { } model)
        {
            TaskObserver.Observe(model.Updates.CheckNowAsync(), "AdvancedTab", "update check");
        }
    }
}
