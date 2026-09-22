namespace Blurt.Engine.Hotkey;

/// <summary>
/// How the dictation trigger key activates a recording. The default keeps one key doing both jobs —
/// a quick tap toggles recording on and off, a longer hold is push-to-talk — and the other two modes
/// narrow it to a single gesture. The raw value is what <see cref="TriggerActivationStore"/> persists:
/// renaming a case is free, changing its raw value is not.
/// </summary>
public enum TriggerActivation
{
    /// <summary>Tap to toggle and hold to talk — the shipped default.</summary>
    TapOrHold,

    /// <summary>Tap to toggle only: every release latches recording on, however long the key was held.</summary>
    Tap,

    /// <summary>Hold to talk only: recording runs exactly while the key is down, so a release always stops.</summary>
    Hold,
}

/// <summary>Persistence raw values, decoding, and presentation for <see cref="TriggerActivation"/>.</summary>
public static class TriggerActivations
{
    /// <summary>Every mode, in declaration order.</summary>
    public static IReadOnlyList<TriggerActivation> All { get; } =
        [TriggerActivation.TapOrHold, TriggerActivation.Tap, TriggerActivation.Hold];

    /// <summary>The value <see cref="TriggerActivationStore"/> persists.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="activation"/> is not a defined value.</exception>
    public static string RawValue(this TriggerActivation activation) => activation switch
    {
        TriggerActivation.TapOrHold => "TapOrHold",
        TriggerActivation.Tap => "Tap",
        TriggerActivation.Hold => "Hold",
        _ => throw new ArgumentOutOfRangeException(nameof(activation), activation, null),
    };

    /// <summary>Decodes a persisted raw value, falling back to tap-or-hold for unset (null) or unknown values.</summary>
    public static TriggerActivation FromPersisted(string? raw) => raw switch
    {
        "Tap" => TriggerActivation.Tap,
        "Hold" => TriggerActivation.Hold,
        _ => TriggerActivation.TapOrHold,
    };

    /// <summary>Menu-picker title, e.g. "Tap or hold".</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="activation"/> is not a defined value.</exception>
    public static string Label(this TriggerActivation activation) => activation switch
    {
        TriggerActivation.TapOrHold => "Tap or hold",
        TriggerActivation.Tap => "Tap",
        TriggerActivation.Hold => "Hold",
        _ => throw new ArgumentOutOfRangeException(nameof(activation), activation, null),
    };

    /// <summary>The Shortcut section's footer sentence: how to dictate under this mode.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="activation"/> is not a defined value.</exception>
    public static string Guidance(this TriggerActivation activation) => activation switch
    {
        TriggerActivation.TapOrHold =>
            "Tap to start and tap again to stop, or hold the key and release to dictate.",
        TriggerActivation.Tap => "Tap to start and tap again to stop dictating.",
        TriggerActivation.Hold => "Hold the key while you speak and release to stop dictating.",
        _ => throw new ArgumentOutOfRangeException(nameof(activation), activation, null),
    };

    /// <summary>
    /// Whether releasing the trigger latches the recording on (tap-to-toggle) rather than stopping it,
    /// given how long this press was held. Only presses that started the recording from idle ask — a
    /// release over an already-latched recording always stops (see <see cref="DictationKeyGate.ModifierUp(TimeSpan)"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="activation"/> is not a defined value.</exception>
    internal static bool LatchesOnRelease(this TriggerActivation activation, TimeSpan held, TimeSpan holdThreshold) =>
        activation switch
        {
            TriggerActivation.TapOrHold => held < holdThreshold,
            TriggerActivation.Tap => true,
            TriggerActivation.Hold => false,
            _ => throw new ArgumentOutOfRangeException(nameof(activation), activation, null),
        };
}
