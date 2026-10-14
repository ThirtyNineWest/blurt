using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Blurt.Engine.Config;

/// <summary>
/// One entry of the style switcher: Default or a profile. Setting <see cref="IsActive"/> true
/// activates the entry through its <see cref="StyleChoices"/>; a false write is ignored without
/// notifying, since only activating another entry deactivates this one.
/// </summary>
public sealed class StyleChoice : INotifyPropertyChanged
{
    private readonly StyleChoices _owner;
    private bool _isActive;

    internal StyleChoice(StyleChoices owner, StyleProfile? profile, bool isActive)
    {
        _owner = owner;
        Profile = profile;
        _isActive = isActive;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The profile, or null for Default.</summary>
    public StyleProfile? Profile { get; }

    /// <summary>The switcher label.</summary>
    public string Name => Profile?.Name ?? StyleProfileStore.DefaultStyleName;

    /// <summary>Whether this entry is the active style. Setting it true activates it; setting it false does nothing.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (value && !_isActive)
            {
                _owner.Activate(Profile);
            }
        }
    }

    internal void Mark(bool isActive)
    {
        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
    }
}

/// <summary>
/// The style switcher's entries over a <see cref="StyleProfileStore"/>: Default followed by one entry
/// per profile, exactly one of them active. Activation writes through the store, then every entry
/// is re-marked from what the store reads back.
/// </summary>
public sealed class StyleChoices
{
    private readonly StyleProfileStore _store;

    /// <summary>Creates the entries from <paramref name="store"/>'s current profiles.</summary>
    public StyleChoices(StyleProfileStore store)
    {
        _store = store;
        Reload();
    }

    /// <summary>Raised after an activation or a reload changed the entries or the active one.</summary>
    public event Action? Changed;

    /// <summary>Default followed by one entry per profile, in the user's order.</summary>
    public ObservableCollection<StyleChoice> Entries { get; } = [];

    /// <summary>The active profile as last read, or null for Default.</summary>
    public StyleProfile? Active { get; private set; }

    /// <summary>Makes <paramref name="profile"/> active, or Default when null, and re-marks the entries.</summary>
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

    /// <summary>Re-reads the profiles and the active one, rebuilding the entries only when the profiles changed.</summary>
    public void Reload()
    {
        var profiles = _store.Profiles;
        var active = _store.Active;
        var sameEntries = Entries.Count == profiles.Count + 1
            && Entries.Skip(1).Select(entry => entry.Profile).SequenceEqual(profiles);
        if (sameEntries)
        {
            foreach (var entry in Entries)
            {
                entry.Mark(entry.Profile?.Id == active?.Id);
            }
        }
        else
        {
            Entries.Clear();
            Entries.Add(new StyleChoice(this, null, active is null));
            foreach (var profile in profiles)
            {
                Entries.Add(new StyleChoice(this, profile, profile.Id == active?.Id));
            }
        }

        Active = active;
        Changed?.Invoke();
    }
}
