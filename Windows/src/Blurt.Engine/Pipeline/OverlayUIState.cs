namespace Blurt.Engine.Pipeline;

/// <summary>
/// The visual state of the dictation overlay pill, a pure projection of <see cref="PipelinePhase"/>.
/// Every member is abstract, so a new state cannot compile without its spoken label and dwell.
/// </summary>
public abstract record OverlayUIState
{
    /// <summary>At rest.</summary>
    public static readonly OverlayUIState Idle = new IdleState();

    /// <summary>
    /// The press landed but the microphone is not delivering audio yet. A steady state rendered
    /// without the recording tag or meter, so the pill does not invite speech during bring-up.
    /// </summary>
    public static readonly OverlayUIState Connecting = new ConnectingState();

    /// <summary>Audio is being captured.</summary>
    public static readonly OverlayUIState Recording = new RecordingState();

    /// <summary>Transcribing or pasting.</summary>
    public static readonly OverlayUIState Processing = new ProcessingState();

    /// <summary>The transcript was pasted. A brief neutral notice.</summary>
    public static readonly OverlayUIState Pasted = new PastedState();

    /// <summary>Nothing editable was focused, so the transcript was copied. A brief neutral notice.</summary>
    public static readonly OverlayUIState NoTarget = new NoTargetState();

    private OverlayUIState()
    {
    }

    /// <summary>Returns the error flash carrying the human-readable <paramref name="message"/>.</summary>
    public static OverlayUIState Error(string message) => new ErrorState(message);

    /// <summary>The screen-reader label for the pill, also announced for the transient notices.</summary>
    public abstract string AccessibilityLabel { get; }

    /// <summary>
    /// How long the shell holds this transient notice before reverting to <see cref="Idle"/>, or
    /// null for a steady state held for as long as the pipeline is in it.
    /// </summary>
    public abstract double? NoticeDwellSeconds { get; }

    /// <summary>The case name, e.g. <c>processing</c> or <c>error(message)</c>.</summary>
    public sealed override string ToString() => CaseName;

    private protected abstract string CaseName { get; }

    /// <summary>A dictation attempt failed; <paramref name="Message"/> is the reason shown and spoken.</summary>
    public sealed record ErrorState(string Message) : OverlayUIState
    {
        /// <summary>The carried message, verbatim.</summary>
        public override string AccessibilityLabel => Message;

        /// <inheritdoc/>
        public override double? NoticeDwellSeconds => 1.6;

        private protected override string CaseName => $"error({Message})";
    }

    private sealed record IdleState : OverlayUIState
    {
        public override string AccessibilityLabel => "Blurt.";

        public override double? NoticeDwellSeconds => null;

        private protected override string CaseName => "idle";
    }

    private sealed record ConnectingState : OverlayUIState
    {
        public override string AccessibilityLabel => "Connecting to the microphone.";

        // A dwell would revert the pill to idle while the microphone is still opening.
        public override double? NoticeDwellSeconds => null;

        private protected override string CaseName => "connecting";
    }

    private sealed record RecordingState : OverlayUIState
    {
        public override string AccessibilityLabel => "Recording.";

        public override double? NoticeDwellSeconds => null;

        private protected override string CaseName => "recording";
    }

    private sealed record ProcessingState : OverlayUIState
    {
        public override string AccessibilityLabel => "Processing.";

        public override double? NoticeDwellSeconds => null;

        private protected override string CaseName => "processing";
    }

    private sealed record PastedState : OverlayUIState
    {
        public override string AccessibilityLabel => "Your dictation was pasted.";

        public override double? NoticeDwellSeconds => 0.8;

        private protected override string CaseName => "pasted";
    }

    private sealed record NoTargetState : OverlayUIState
    {
        public override string AccessibilityLabel => "No text field focused. Your dictation was copied to the clipboard.";

        public override double? NoticeDwellSeconds => 1.6;

        private protected override string CaseName => "noTarget";
    }
}
