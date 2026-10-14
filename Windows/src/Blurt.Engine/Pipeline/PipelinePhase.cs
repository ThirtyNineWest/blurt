namespace Blurt.Engine.Pipeline;

/// <summary>
/// Where a dictation is. Every projection is abstract, so adding a phase does not compile until it
/// states whether it is terminal, capturing, and how the overlay and tray present it. A phase that
/// wrongly read as non-terminal would strand the trigger's gate and swallow the next press.
/// </summary>
public abstract record PipelinePhase
{
    /// <summary>Nothing in flight.</summary>
    public static readonly PipelinePhase Idle = new IdlePhase();

    /// <summary>
    /// The press was accepted and the microphone is being brought up but is not yet delivering
    /// frames. Never presented as live capture, so the user is not cued to speak into a dead mic.
    /// Non-terminal, so a second press during it is refused.
    /// </summary>
    public static readonly PipelinePhase Connecting = new ConnectingPhase();

    /// <summary>Audio is being captured.</summary>
    public static readonly PipelinePhase Recording = new RecordingPhase();

    /// <summary>The recording is sealed and the transcript is awaited.</summary>
    public static readonly PipelinePhase Transcribing = new TranscribingPhase();

    /// <summary>The transcript is being pasted.</summary>
    public static readonly PipelinePhase Injecting = new InjectingPhase();

    /// <summary>The user cancelled the dictation.</summary>
    public static readonly PipelinePhase Cancelled = new CancelledPhase();

    /// <summary>The transcript was pasted into the focused field.</summary>
    public static readonly PipelinePhase Pasted = new PastedPhase();

    /// <summary>The transcript had nowhere to land and was left on the clipboard. Not an error.</summary>
    public static readonly PipelinePhase NoTarget = new NoTargetPhase();

    /// <summary>Returns the handed-back phase carrying the spoken <paramref name="transcript"/>.</summary>
    public static PipelinePhase HandedBack(string transcript) => new HandedBackPhase(transcript);

    private PipelinePhase()
    {
    }

    /// <summary>Returns the failed phase carrying <paramref name="error"/>.</summary>
    public static PipelinePhase Failed(BlurtError error) => new FailedPhase(error);

    /// <summary>Whether the dictation has finished or never started.</summary>
    public abstract bool IsTerminal { get; }

    /// <summary>Whether audio capture is in progress or being brought up (<see cref="Connecting"/> or <see cref="Recording"/>).</summary>
    public abstract bool IsCapturing { get; }

    /// <summary>
    /// The blocker behind this phase when it is an unfinished setup step rather than a fault;
    /// null for every other phase and every genuine failure.
    /// </summary>
    public virtual BlurtError? SetupBlocker => null;

    /// <summary>How this phase is presented on the overlay pill.</summary>
    public abstract OverlayUIState OverlayState { get; }

    /// <summary>How this phase is reflected on the tray icon.</summary>
    public abstract TrayStatus TrayStatus { get; }

    /// <summary>The case name, e.g. <c>idle</c> or <c>failed(apiKeyMissing)</c>.</summary>
    public sealed override string ToString() => CaseName;

    private protected abstract string CaseName { get; }

    /// <summary>The dictation failed with <paramref name="Error"/>. Terminal.</summary>
    public sealed record FailedPhase(BlurtError Error) : PipelinePhase
    {
        /// <inheritdoc/>
        public override bool IsTerminal => true;

        /// <inheritdoc/>
        public override bool IsCapturing => false;

        /// <inheritdoc/>
        public override BlurtError? SetupBlocker => Error.IsSetupBlocker ? Error : null;

        /// <summary>A setup blocker presents as calm idle; any other failure as the error flash carrying its description.</summary>
        public override OverlayUIState OverlayState =>
            Error.IsSetupBlocker
                ? OverlayUIState.Idle
                : OverlayUIState.Error(string.IsNullOrEmpty(Error.Description) ? "Dictation failed." : Error.Description);

        /// <inheritdoc/>
        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => $"failed({Error.DiagnosticName})";
    }

    private sealed record IdlePhase : PipelinePhase
    {
        public override bool IsTerminal => true;

        public override bool IsCapturing => false;

        public override OverlayUIState OverlayState => OverlayUIState.Idle;

        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => "idle";
    }

    private sealed record ConnectingPhase : PipelinePhase
    {
        public override bool IsTerminal => false;

        public override bool IsCapturing => true;

        public override OverlayUIState OverlayState => OverlayUIState.Connecting;

        // The tray's filled glyph means "audio is being captured", which it is not yet.
        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => "connecting";
    }

    private sealed record RecordingPhase : PipelinePhase
    {
        public override bool IsTerminal => false;

        public override bool IsCapturing => true;

        public override OverlayUIState OverlayState => OverlayUIState.Recording;

        public override TrayStatus TrayStatus => TrayStatus.Recording;

        private protected override string CaseName => "recording";
    }

    private sealed record TranscribingPhase : PipelinePhase
    {
        public override bool IsTerminal => false;

        public override bool IsCapturing => false;

        public override OverlayUIState OverlayState => OverlayUIState.Processing;

        public override TrayStatus TrayStatus => TrayStatus.Transcribing;

        private protected override string CaseName => "transcribing";
    }

    private sealed record InjectingPhase : PipelinePhase
    {
        public override bool IsTerminal => false;

        public override bool IsCapturing => false;

        // The shell reads an idle projection as "dismiss the pill"; mapping this working phase to
        // idle would fade the pill out mid-dictation and back in for the pasted notice.
        public override OverlayUIState OverlayState => OverlayUIState.Processing;

        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => "injecting";
    }

    private sealed record CancelledPhase : PipelinePhase
    {
        public override bool IsTerminal => true;

        public override bool IsCapturing => false;

        public override OverlayUIState OverlayState => OverlayUIState.Idle;

        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => "cancelled";
    }

    private sealed record PastedPhase : PipelinePhase
    {
        public override bool IsTerminal => true;

        public override bool IsCapturing => false;

        public override OverlayUIState OverlayState => OverlayUIState.Pasted;

        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => "pasted";
    }

    private sealed record NoTargetPhase : PipelinePhase
    {
        public override bool IsTerminal => true;

        public override bool IsCapturing => false;

        public override OverlayUIState OverlayState => OverlayUIState.NoTarget;

        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => "noTarget";
    }

    /// <summary>
    /// Transcription succeeded and the press asked for the text back instead of a paste
    /// (<see cref="Command.PressHandingBack"/>). <paramref name="Transcript"/> is the spoken text,
    /// before any text shortcut expands it. Terminal; presents as idle, since what happens next is the host's.
    /// </summary>
    public sealed record HandedBackPhase(string Transcript) : PipelinePhase
    {
        /// <inheritdoc/>
        public override bool IsTerminal => true;

        /// <inheritdoc/>
        public override bool IsCapturing => false;

        /// <inheritdoc/>
        public override OverlayUIState OverlayState => OverlayUIState.Idle;

        /// <inheritdoc/>
        public override TrayStatus TrayStatus => TrayStatus.Idle;

        private protected override string CaseName => "handedBack";
    }
}
