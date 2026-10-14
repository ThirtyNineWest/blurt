using System.Collections.ObjectModel;
using Blurt.App.Services;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The Vocabulary tab: the key terms string and the text shortcuts list. Every write goes through
/// its store, then the view model re-reads the store. Use on the UI thread.
/// </summary>
public sealed class VocabularyViewModel : ObservableObject
{
    private readonly KeyTermsStore _keyTerms;
    private readonly TextShortcutStore _shortcuts;
    private TextShortcut? _selected;

    /// <summary>Creates the view model over the key terms and text shortcut stores in <paramref name="settings"/>.</summary>
    public VocabularyViewModel(SettingsStores settings)
    {
        _keyTerms = settings.KeyTerms;
        _shortcuts = settings.TextShortcuts;
        Reload();
    }

    /// <summary>The raw comma-separated key terms as typed; blank clears them.</summary>
    public string KeyTerms
    {
        get => _keyTerms.Raw ?? string.Empty;
        set
        {
            if (value == KeyTerms)
            {
                return;
            }

            _keyTerms.Raw = value;
            Raise(nameof(KeyTerms));
        }
    }

    /// <summary>The stored shortcuts, in the user's order.</summary>
    public ObservableCollection<TextShortcut> Shortcuts { get; } = [];

    /// <summary>The list's selected row, or null.</summary>
    public TextShortcut? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            _selected = value;
            Raise(nameof(Selected), nameof(CanEditOrRemove));
        }
    }

    /// <summary>Whether another shortcut may be added.</summary>
    public bool CanAdd => Shortcuts.Count < TextShortcutStore.ShortcutLimit;

    /// <summary>Whether a row is selected.</summary>
    public bool CanEditOrRemove => Selected is not null;

    /// <summary>The match keys of every stored shortcut other than <paramref name="id"/>, for the editor's duplicate check.</summary>
    public IReadOnlySet<string> OtherKeys(Guid id) =>
        _shortcuts.Shortcuts.Where(s => s.Id != id).Select(s => TextShortcutStore.MatchKey(s.Trigger)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Writes <paramref name="edited"/> into the stored list, replacing the shortcut with its id or appending it.</summary>
    public void Save(TextShortcut edited)
    {
        var updated = _shortcuts.Shortcuts.ToList();
        var index = updated.FindIndex(s => s.Id == edited.Id);
        if (index >= 0)
        {
            updated[index] = edited;
        }
        else
        {
            updated.Add(edited);
        }

        _shortcuts.Shortcuts = updated;
        Reload();
        Selected = Shortcuts.FirstOrDefault(s => s.Id == edited.Id);
    }

    /// <summary>Removes the shortcut carrying <paramref name="id"/>.</summary>
    public void Delete(Guid id)
    {
        _shortcuts.Shortcuts = _shortcuts.Shortcuts.Where(s => s.Id != id).ToList();
        Reload();
    }

    /// <summary>Re-reads both stores. A selection naming a removed row is cleared.</summary>
    public void Reload()
    {
        var selectedId = Selected?.Id;
        Shortcuts.Clear();
        foreach (var shortcut in _shortcuts.Shortcuts)
        {
            Shortcuts.Add(shortcut);
        }

        Selected = Shortcuts.FirstOrDefault(s => s.Id == selectedId);
        Raise(nameof(KeyTerms), nameof(CanAdd));
    }
}
