namespace Blurt.Engine;

/// <summary>
/// The pipeline's failure cases. Every classification member is abstract, so a new case cannot
/// compile without stating its description, log label, and whether it is a setup blocker or a
/// quiet degradation.
/// </summary>
public abstract record BlurtError
{
    private BlurtError()
    {
    }

    /// <summary>Human-facing copy for the failure. Free to be reworded.</summary>
    public abstract string Description { get; }

    /// <summary>Stable, machine-greppable case label for the developer-mode error log.</summary>
    public abstract string DiagnosticName { get; }

    /// <summary>Whether the error means "setup isn't finished" rather than "dictation failed".</summary>
    public abstract bool IsSetupBlocker { get; }

    /// <summary>
    /// Whether an error thrown by the injector means the transcript is on the clipboard and the
    /// pipeline shows the quiet "copied" notice instead of the error flash.
    /// </summary>
    public abstract bool IsQuietDegradation { get; }

    /// <summary>No AssemblyAI API key is saved.</summary>
    public sealed record ApiKeyMissing : BlurtError
    {
        /// <inheritdoc/>
        public override string Description => "Add your AssemblyAI API key in Settings to start dictating.";

        /// <inheritdoc/>
        public override string DiagnosticName => "apiKeyMissing";

        /// <inheritdoc/>
        public override bool IsSetupBlocker => true;

        /// <inheritdoc/>
        public override bool IsQuietDegradation => false;
    }

    /// <summary>
    /// Transcription failed. Equality compares the underlying exception's type and HResult, never
    /// its message.
    /// </summary>
    public sealed record SttFailed(Exception Underlying) : BlurtError
    {
        /// <inheritdoc/>
        public override string Description => $"Transcription failed: {Underlying.Message}";

        /// <inheritdoc/>
        public override string DiagnosticName => "sttFailed";

        /// <inheritdoc/>
        public override bool IsSetupBlocker => false;

        /// <inheritdoc/>
        public override bool IsQuietDegradation => false;

        /// <summary>True when both wrap an exception of the same type and HResult.</summary>
        public bool Equals(SttFailed? other) => other is not null && SameIdentity(Underlying, other.Underlying);

        /// <inheritdoc/>
        public override int GetHashCode() => IdentityHash(Underlying);
    }

    /// <summary>
    /// The target window closed or refused activation, or the paste could not be synthesized. When
    /// the injector throws it the transcript is already on the clipboard.
    /// </summary>
    public sealed record TargetAppLost : BlurtError
    {
        /// <inheritdoc/>
        public override string Description => "Target app lost focus or quit.";

        /// <inheritdoc/>
        public override string DiagnosticName => "targetAppLost";

        /// <inheritdoc/>
        public override bool IsSetupBlocker => false;

        /// <inheritdoc/>
        public override bool IsQuietDegradation => true;
    }

    /// <summary>
    /// Audio capture failed. Equality compares the underlying exception's type and HResult, never
    /// its message.
    /// </summary>
    public sealed record AudioCaptureFailed(Exception Underlying) : BlurtError
    {
        /// <inheritdoc/>
        public override string Description => $"Audio capture failed: {Underlying.Message}";

        /// <inheritdoc/>
        public override string DiagnosticName => "audioCaptureFailed";

        /// <inheritdoc/>
        public override bool IsSetupBlocker => false;

        /// <inheritdoc/>
        public override bool IsQuietDegradation => false;

        /// <summary>True when both wrap an exception of the same type and HResult.</summary>
        public bool Equals(AudioCaptureFailed? other) => other is not null && SameIdentity(Underlying, other.Underlying);

        /// <inheritdoc/>
        public override int GetHashCode() => IdentityHash(Underlying);
    }

    /// <summary>
    /// Nothing editable was focused at paste time, so the transcript was left on the clipboard. Not
    /// a fault.
    /// </summary>
    public sealed record NoEditableTarget : BlurtError
    {
        /// <inheritdoc/>
        public override string Description => "No text field was focused — copied to the clipboard instead.";

        /// <inheritdoc/>
        public override string DiagnosticName => "noEditableTarget";

        /// <inheritdoc/>
        public override bool IsSetupBlocker => false;

        /// <inheritdoc/>
        public override bool IsQuietDegradation => true;
    }

    private static bool SameIdentity(Exception a, Exception b) =>
        a.GetType() == b.GetType() && a.HResult == b.HResult;

    private static int IdentityHash(Exception e) => HashCode.Combine(e.GetType(), e.HResult);
}

/// <summary>An exception that carries a <see cref="BlurtError"/>; its message is the error's description.</summary>
public sealed class BlurtException(BlurtError error) : Exception(error.Description)
{
    /// <summary>The error this exception carries.</summary>
    public BlurtError Error { get; } = error;
}
