using Blurt.Engine.Hotkey;

namespace Blurt.Engine.Pipeline;

/// <summary>A sentence split around the key name, so a view can emphasize the key alone.</summary>
/// <param name="Lead">The text before the key name.</param>
/// <param name="Key">The key name.</param>
/// <param name="Tail">The text after the key name.</param>
public sealed record ReadoutSentence(string Lead, string Key, string Tail)
{
    /// <summary>The whole sentence as plain text.</summary>
    public override string ToString() => Lead + Key + Tail;
}

/// <summary>The ready view's status block: the how-to readout at rest and the listening face while recording.</summary>
public static class ReadyReadout
{
    private const string Escape = "Tap again or release to finish — Esc cancels.";

    /// <summary>
    /// The headline naming <paramref name="key"/> in full: "Tap … to start and stop." for the tap
    /// modes, "Hold … to talk, then release." for hold.
    /// </summary>
    public static ReadoutSentence Headline(TriggerKey key, TriggerActivation activation) => activation == TriggerActivation.Hold
        ? new ReadoutSentence("Hold ", key.FullName(), " to talk, then release.")
        : new ReadoutSentence("Tap ", key.FullName(), " to start and stop.");

    /// <summary>The line under the headline: the activation mode's guidance sentence.</summary>
    public static string Subtitle(TriggerActivation activation) => activation.Guidance();

    /// <summary>
    /// The line under "Listening…": the way out, prefixed with the active style's name when one is
    /// active. With no style the sentence starts at the way out, since "Blurting in Default." would
    /// read as a profile of that name.
    /// </summary>
    public static string ListeningSubtitle(string? activeStyleName) =>
        activeStyleName is null ? Escape : $"Blurting in {activeStyleName}. {Escape}";
}
