using System.Collections.ObjectModel;
using Blurt.App.Services;
using Blurt.Engine.Config;

namespace Blurt.App.Settings;

/// <summary>
/// One entry of the main window's style switcher: Default or a profile. Checking it activates it;
/// unchecking is ignored, since exactly one entry is always active.
/// </summary>
public sealed class StyleChoice : ObservableObject
{
    private readonly StylesViewModel _owner;
    private bool _isActive;

    /// <summary>Creates an entry for <paramref name="profile"/>, or for Default when null.</summary>
    public StyleChoice(StylesViewModel owner, StyleProfile? profile, bool isActive)
    {
        _owner = owner;
        Profile = profile;
        _isActive = isActive;
    }

    /// <summary>The profile, or null for Default.</summary>
    public StyleProfile? Profile { get; }

    /// <summary>The switcher label.</summary>
    public string Name => Profile?.Name ?? StyleProfileStore.DefaultStyleName;

    /// <summary>Whether this entry is the active style. Setting it true activates it.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (value && !_isActive)
            {
                _owner.Activate(Profile);
            }
            else
            {
                Raise(nameof(IsActive));
            }
        }
    }

    internal void Mark(bool isActive) => Set(ref _isActive, isActive, nameof(IsActive));
}

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
    private IReadOnlyList<StyleProfile> _profiles = [];
    private StyleProfile? _active;

    /// <summary>Creates the view model over the style and enhanced-transcripts stores in <paramref name="settings"/>.</summary>
    public StylesViewModel(SettingsStores settings)
    {
        _store = settings.Styles;
        _enhanced = settings.EnhancedTranscripts;
        Reload();
    }

    /// <summary>Default followed by one entry per profile, for the switcher.</summary>
    public ObservableCollection<StyleChoice> Choices { get; } = [];

    /// <summary>The stored profiles, in the user's order.</summary>
    public IReadOnlyList<StyleProfile> Profiles
    {
        get => _profiles;
        private set => Set(ref _profiles, value);
    }

    /// <summary>The name of the style the next dictation uses, or null for Default.</summary>
    public string? ActiveName => _active?.Name;

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

    /// <summary>Makes <paramref name="profile"/> active, or Default when null.</summary>
    public void Activate(StyleProfile? profile)
    {
        if (profile is null)
        {
            _store.ActivateDefault();
        }
        else
        {
            _store.Activate(profile);
        }

        Reload();
    }

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
    public void Reload()
    {
        var profiles = _store.Profiles;
        var active = _store.Active;
        var sameEntries = Choices.Count == profiles.Count + 1
            && Choices.Skip(1).Select(choice => choice.Profile).SequenceEqual(profiles);
        if (sameEntries)
        {
            foreach (var choice in Choices)
            {
                choice.Mark(choice.Profile?.Id == active?.Id);
            }
        }
        else
        {
            Choices.Clear();
            Choices.Add(new StyleChoice(this, null, active is null));
            foreach (var profile in profiles)
            {
                Choices.Add(new StyleChoice(this, profile, profile.Id == active?.Id));
            }
        }

        _active = active;
        Profiles = profiles;
        Raise(nameof(ActiveName), nameof(CanAdd));
    }
}
