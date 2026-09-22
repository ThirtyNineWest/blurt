using System.Collections.ObjectModel;
using Blurt.App.Services;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// The custom style profiles and the enhanced-transcripts switch, shared by the main window's style
/// switcher and the Settings window's Custom Styles list. Every write goes through
/// <see cref="StyleProfileStore"/> or <see cref="EnhancedTranscriptsStore"/>, then the view model
/// re-reads the store. Use on the UI thread.
/// </summary>
public sealed class StylesViewModel : ObservableObject
{
    private readonly StyleProfileStore _store;
    private readonly EnhancedTranscriptsStore _enhanced;
    private readonly StyleChoices _choices;
    private IReadOnlyList<StyleProfile> _profiles = [];

    /// <summary>Creates the view model over the style and enhanced-transcripts stores in <paramref name="settings"/>.</summary>
    public StylesViewModel(SettingsStores settings)
    {
        _store = settings.Styles;
        _enhanced = settings.EnhancedTranscripts;
        _choices = new StyleChoices(_store);
        _choices.Changed += OnChoicesChanged;
        OnChoicesChanged();
    }

    /// <summary>Default followed by one entry per profile, for the switcher.</summary>
    public ObservableCollection<StyleChoice> Choices => _choices.Entries;

    /// <summary>The stored profiles, in the user's order.</summary>
    public IReadOnlyList<StyleProfile> Profiles
    {
        get => _profiles;
        private set => Set(ref _profiles, value);
    }

    /// <summary>The name of the style the next dictation uses, or null for Default.</summary>
    public string? ActiveName => _choices.Active?.Name;

    /// <summary>The most UTF-8 bytes one profile's instructions may hold.</summary>
    public int CharacterLimit => _store.CharacterLimit;

    /// <summary>Whether another profile may be added.</summary>
    public bool CanAdd => StyleProfileDraft.CanAdd(Profiles.Count);

    /// <summary>Whether enhanced transcripts are on. Styles apply only while they are.</summary>
    public bool EnhancedTranscripts
    {
        get => _enhanced.IsEnabled;
        set
        {
            if (value == _enhanced.IsEnabled)
            {
                return;
            }

            _enhanced.IsEnabled = value;
            Raise(nameof(EnhancedTranscripts), nameof(StylesFooter));
        }
    }

    /// <summary>The Custom Styles footer: the limit, or why styles are disabled.</summary>
    public string StylesFooter => EnhancedTranscripts
        ? $"Up to {StyleProfileStore.ProfileLimit} styles."
        : "Style preferences need enhanced transcripts turned on.";

    /// <summary>Writes <paramref name="edited"/> into the stored list, replacing the profile with its id or appending it.</summary>
    public void Save(StyleProfile edited)
    {
        _store.Profiles = StyleProfileDraft.Upserted(_store.Profiles, edited);
        Reload();
    }

    /// <summary>Removes the profile carrying <paramref name="id"/> from the stored list.</summary>
    public void Delete(Guid id)
    {
        _store.Profiles = StyleProfileDraft.Removed(_store.Profiles, id);
        Reload();
    }

    /// <summary>Re-reads the profiles and the active one from the store.</summary>
    public void Reload() => _choices.Reload();

    private void OnChoicesChanged()
    {
        Profiles = _store.Profiles;
        Raise(nameof(ActiveName), nameof(CanAdd));
    }
}
