using System.ComponentModel;
using System.Windows.Data;
using Blurt.App.Services;
using Blurt.Engine.Audio;
using Blurt.Engine.Hotkey;

namespace Blurt.App.Settings;

/// <summary>A picker entry: the value written through the store and the text the picker shows.</summary>
/// <param name="Value">The selectable value.</param>
/// <param name="Label">The text shown for it.</param>
public sealed record Choice<T>(T Value, string Label);

/// <summary>
/// The trigger key, activation, and cue voice pickers, shared by the wizard's steps and the Settings
/// window's General tab. Every write goes through the store's setter and then the coordinator hook
/// that applies it. Use on the UI thread.
/// </summary>
public sealed class DictationSettingsViewModel : ObservableObject
{
    private readonly SettingsStores _settings;
    private readonly AppCoordinator _coordinator;

    /// <summary>Creates the view model over <paramref name="settings"/>, applying changes through <paramref name="coordinator"/>.</summary>
    public DictationSettingsViewModel(SettingsStores settings, AppCoordinator coordinator)
    {
        _settings = settings;
        _coordinator = coordinator;
        TriggerKeyChoices = [.. TriggerKeys.All.Select(key => new Choice<TriggerKey>(key, key.FullName()))];
        ActivationChoices = [.. TriggerActivations.All.Select(mode => new Choice<TriggerActivation>(mode, mode.Label()))];

        List<SoundPack> packs = [SoundPack.None];
        foreach (var group in settings.SoundPacks.Groups)
        {
            packs.AddRange(settings.SoundPacks.Voices(group));
        }

        var view = new ListCollectionView(packs);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SoundPack.Group)));
        SoundPacks = view;
    }

    /// <summary>Every trigger key, labelled with its full name.</summary>
    public IReadOnlyList<Choice<TriggerKey>> TriggerKeyChoices { get; }

    /// <summary>Every activation mode, labelled for the picker.</summary>
    public IReadOnlyList<Choice<TriggerActivation>> ActivationChoices { get; }

    /// <summary>"None" followed by every voice, grouped by <see cref="SoundPack.Group"/>.</summary>
    public ICollectionView SoundPacks { get; }

    /// <summary>The persisted trigger key. Setting it persists and rebinds the key hook.</summary>
    public TriggerKey TriggerKey
    {
        get => _settings.TriggerKey.TriggerKey;
        set
        {
            if (value == TriggerKey)
            {
                return;
            }

            _settings.TriggerKey.TriggerKey = value;
            _coordinator.DictationBindingChanged();
            Raise(nameof(TriggerKey), nameof(TriggerKeyLabel));
        }
    }

    /// <summary>The trigger key's inline name, e.g. "right Ctrl".</summary>
    public string TriggerKeyLabel => TriggerKey.Label();

    /// <summary>The persisted activation mode. Setting it persists and rebinds the key hook.</summary>
    public TriggerActivation Activation
    {
        get => _settings.TriggerActivation.Activation;
        set
        {
            if (value == Activation)
            {
                return;
            }

            _settings.TriggerActivation.Activation = value;
            _coordinator.DictationBindingChanged();
            Raise(nameof(Activation), nameof(Guidance));
        }
    }

    /// <summary>How to dictate under <see cref="Activation"/>: the Shortcut section's footer.</summary>
    public string Guidance => Activation.Guidance();

    /// <summary>The persisted cue voice. Setting it persists, reloads the cues, and previews them.</summary>
    public SoundPack SoundPack
    {
        get => _settings.SoundPack.SoundPack;
        set
        {
            if (value is null || value == SoundPack)
            {
                return;
            }

            _settings.SoundPack.SoundPack = value;
            _coordinator.SoundPackChanged();
            Raise(nameof(SoundPack));
        }
    }
}
