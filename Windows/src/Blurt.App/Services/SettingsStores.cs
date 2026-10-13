using Blurt.Engine.Audio;
using Blurt.Engine.Config;
using Blurt.Engine.Hotkey;
using Blurt.Engine.Pipeline;
using Blurt.Engine.STT;
using Blurt.Engine.Update;

namespace Blurt.App.Services;

/// <summary>Every typed settings store, built once over the one shared <see cref="ISettingsStore"/>.</summary>
public sealed class SettingsStores
{
    /// <summary>Builds the stores over <paramref name="store"/>, resolving sound packs against <paramref name="soundPacks"/>.</summary>
    public SettingsStores(ISettingsStore store, SoundPackCatalog soundPacks)
    {
        Store = store;
        SoundPacks = soundPacks;
        TriggerKey = new TriggerKeyStore(store);
        TriggerActivation = new TriggerActivationStore(store);
        KeyTerms = new KeyTermsStore(store);
        TextShortcuts = new TextShortcutStore(store);
        DeveloperMode = new DeveloperModeStore(store);
        EnhancedTranscripts = new EnhancedTranscriptsStore(store);
        LastUpdateCheck = new LastUpdateCheckStore(store);
        OverlayOrigin = new OverlayOriginStore(store);
        Styles = new StyleProfileStore(store, CleanupInstruction.CustomStyleBudget);
        SoundPack = new SoundPackStore(store, soundPacks);
    }

    /// <summary>The underlying settings file.</summary>
    public ISettingsStore Store { get; }

    /// <summary>The selectable cue voices.</summary>
    public SoundPackCatalog SoundPacks { get; }

    /// <summary>The dictation trigger key.</summary>
    public TriggerKeyStore TriggerKey { get; }

    /// <summary>Tap, hold, or both.</summary>
    public TriggerActivationStore TriggerActivation { get; }

    /// <summary>The user's key terms.</summary>
    public KeyTermsStore KeyTerms { get; }

    /// <summary>The spoken phrases expanded into saved text.</summary>
    public TextShortcutStore TextShortcuts { get; }

    /// <summary>Whether the dictation logs are written.</summary>
    public DeveloperModeStore DeveloperMode { get; }

    /// <summary>Whether the rewritten transcript is used.</summary>
    public EnhancedTranscriptsStore EnhancedTranscripts { get; }

    /// <summary>When an update check last completed.</summary>
    public LastUpdateCheckStore LastUpdateCheck { get; }

    /// <summary>The overlay pill's dragged position.</summary>
    public OverlayOriginStore OverlayOrigin { get; }

    /// <summary>The custom style profiles and the active one.</summary>
    public StyleProfileStore Styles { get; }

    /// <summary>The selected cue voice.</summary>
    public SoundPackStore SoundPack { get; }
}
