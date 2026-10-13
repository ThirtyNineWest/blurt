using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The Vocabulary tab: key terms and the text shortcuts list with its editor. Double-click or
/// Enter edits the selected row; Delete removes it without confirmation. Binds to a
/// <see cref="SettingsViewModel"/>.
/// </summary>
public partial class VocabularyTab : UserControl
{
    /// <summary>Creates the tab.</summary>
    public VocabularyTab()
    {
        InitializeComponent();
    }

    private VocabularyViewModel? Model => (DataContext as SettingsViewModel)?.Vocabulary;

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (Model is { CanAdd: true } model)
        {
            OpenEditor(model, new TextShortcut(string.Empty, string.Empty), isExisting: false);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e) => EditSelected();

    private void OnRemoveClick(object sender, RoutedEventArgs e) => RemoveSelected();

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                EditSelected();
                e.Handled = true;
                break;
            case Key.Delete:
                RemoveSelected();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private void EditSelected()
    {
        if (Model is { Selected: { } selected } model)
        {
            OpenEditor(model, selected, isExisting: true);
        }
    }

    private void RemoveSelected()
    {
        if (Model is { Selected: { } selected } model)
        {
            model.Delete(selected.Id);
        }
    }

    private void OpenEditor(VocabularyViewModel model, TextShortcut shortcut, bool isExisting)
    {
        var editor = new TextShortcutEditor(shortcut, isExisting, model.OtherKeys(shortcut.Id)) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        if (editor.Deleted)
        {
            model.Delete(shortcut.Id);
        }
        else if (editor.Result is { } edited)
        {
            model.Save(edited);
        }
    }
}
