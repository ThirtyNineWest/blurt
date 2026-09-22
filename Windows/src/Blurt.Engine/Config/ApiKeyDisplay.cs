namespace Blurt.Engine.Config;

/// <summary>
/// How the stored AssemblyAI key is presented in the setup/settings account row, and the wording of
/// the controls around it. The masking rule reveals only a fixed tail: enough to tell two accounts
/// apart, not enough to be useful to anyone reading the screen.
/// </summary>
public abstract record ApiKeyDisplay
{
    private ApiKeyDisplay()
    {
    }

    /// <summary>No key is stored.</summary>
    public static readonly ApiKeyDisplay NotConnected = new NotConnectedDisplay();

    /// <summary>How many trailing characters a masked key reveals.</summary>
    public const int RevealedTailLength = 4;

    /// <summary>
    /// Shortest stored key that gets a revealed tail at all — twice the tail, so at least half the key
    /// always stays hidden.
    /// </summary>
    public const int MinimumLengthToMask = RevealedTailLength * 2;

    /// <summary>Resolves what to show for <paramref name="key"/>, trimmed first.</summary>
    public static ApiKeyDisplay Resolve(string? key)
    {
        var trimmed = key.TrimmedNonEmpty();
        if (trimmed is null)
        {
            return NotConnected;
        }

        return trimmed.Length >= MinimumLengthToMask
            ? new ConnectedDisplay($"••••{trimmed[^RevealedTailLength..]}")
            : new ConnectedDisplay(null);
    }

    /// <summary>Whether a key is stored.</summary>
    public abstract bool IsConnected { get; }

    /// <summary>The row's status text: the masked key once one exists, otherwise plain prose.</summary>
    public abstract string StatusText { get; }

    /// <summary>Whether <see cref="StatusText"/> is an identifier that should be monospaced, rather than prose.</summary>
    public abstract bool RendersIdentifier { get; }

    /// <summary>Spoken by a screen reader; spells the state out instead of reading the mask bullets.</summary>
    public abstract string AccessibilityLabel { get; }

    /// <summary>Title of the row's button. Carries an ellipsis, since it opens a sheet.</summary>
    public string EditButtonTitle => IsConnected ? "Change…" : "Connect…";

    /// <summary>Title of the sheet's default action: verify-then-store, not a bare save.</summary>
    public string CommitButtonTitle => IsConnected ? "Update" : "Connect";

    /// <summary>The sheet's headline explanation.</summary>
    public string Rationale => IsConnected
        ? "Paste a new key to replace the one Blurt is using."
        : "Blurt needs an AssemblyAI API key to transcribe your speech. A free-tier key works.";

    private sealed record NotConnectedDisplay : ApiKeyDisplay
    {
        public override bool IsConnected => false;

        public override string StatusText => "Not connected";

        public override bool RendersIdentifier => false;

        public override string AccessibilityLabel => "Not connected";
    }

    /// <summary>A key is stored. <paramref name="MaskedTail"/> is bullets plus the revealed tail, or null when the key is too short to reveal any of it.</summary>
    public sealed record ConnectedDisplay(string? MaskedTail) : ApiKeyDisplay
    {
        /// <inheritdoc/>
        public override bool IsConnected => true;

        /// <inheritdoc/>
        public override string StatusText => MaskedTail ?? "Connected";

        /// <inheritdoc/>
        public override bool RendersIdentifier => MaskedTail is not null;

        /// <inheritdoc/>
        public override string AccessibilityLabel => MaskedTail is null
            ? "Connected"
            : $"Connected, key ending {MaskedTail[^RevealedTailLength..]}";
    }
}
