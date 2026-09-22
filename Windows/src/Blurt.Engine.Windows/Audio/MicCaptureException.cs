namespace Blurt.Engine.Windows.Audio;

/// <summary>Why a microphone capture could not start.</summary>
public enum MicCaptureFailure
{
    /// <summary>Windows privacy settings deny microphone access.</summary>
    AccessDenied,

    /// <summary>No capture endpoint exists, or it disappeared.</summary>
    NoDevice,

    /// <summary>The device opened but never delivered a packet above the liveness floor.</summary>
    NotLive,

    /// <summary>Any other audio-stack failure; <see cref="Exception.HResult"/> carries the HRESULT.</summary>
    Failed,
}

/// <summary>
/// A capture failure whose message reads as a sentence for the overlay. Each named failure has its
/// own <see cref="Exception.HResult"/>, so wrapping errors that compare by type and HResult tell them
/// apart.
/// </summary>
public sealed class MicCaptureException : Exception
{
    internal const int EAccessDenied = unchecked((int)0x80070005);
    internal const int ENotFound = unchecked((int)0x80070490);
    internal const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);
    internal const int ETimeout = unchecked((int)0x800705B4);

    private MicCaptureException(MicCaptureFailure failure, string message, int hresult)
        : base(message)
    {
        Failure = failure;
        HResult = hresult;
    }

    /// <summary>What kind of failure this is.</summary>
    public MicCaptureFailure Failure { get; }

    /// <summary>Returns a new access-denied failure.</summary>
    public static MicCaptureException AccessDenied() =>
        new(MicCaptureFailure.AccessDenied, "Microphone access is turned off in Windows privacy settings.", EAccessDenied);

    /// <summary>Returns a new no-device failure.</summary>
    public static MicCaptureException NoDevice() =>
        new(MicCaptureFailure.NoDevice, "No microphone is available.", ENotFound);

    /// <summary>Returns a new failure for a device that never delivered live input.</summary>
    public static MicCaptureException NotLive() =>
        new(MicCaptureFailure.NotLive, "The microphone didn't start.", ETimeout);

    /// <summary>Returns a new generic failure carrying <paramref name="hresult"/>.</summary>
    public static MicCaptureException Failed(int hresult) =>
        new(MicCaptureFailure.Failed, $"The microphone failed to open (0x{hresult:X8}).", hresult);

    /// <summary>
    /// Maps an audio-stack HRESULT to its failure: <c>E_ACCESSDENIED</c> to
    /// <see cref="AccessDenied"/>, <c>E_NOTFOUND</c> and <c>AUDCLNT_E_DEVICE_INVALIDATED</c> to
    /// <see cref="NoDevice"/>, anything else to <see cref="Failed"/>.
    /// </summary>
    public static MicCaptureException FromHResult(int hresult) => hresult switch
    {
        EAccessDenied => AccessDenied(),
        ENotFound or AudclntEDeviceInvalidated => NoDevice(),
        _ => Failed(hresult),
    };
}
