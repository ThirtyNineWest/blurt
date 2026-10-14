using System.Runtime.InteropServices;
using Blurt.Engine.STT;
using Blurt.Engine.Windows.Audio;
using Blurt.Engine.Windows.Audio.Interop;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Audio;

public sealed class MicCaptureFormatTests
{
    [Test]
    public void TheRequestedFormatIsTheGeometryTheUploadAssumes()
    {
        var format = WaveFormatEx.CaptureRequest;

        format.FormatTag.Should().Be(1, "PCM integer samples, not float");
        format.Channels.Should().Be((ushort)SyncSTTLimits.ChannelCount);
        format.SamplesPerSec.Should().Be((uint)SyncSTTLimits.SampleRate);
        format.BitsPerSample.Should().Be((ushort)SyncSTTLimits.BitDepth);
        format.BlockAlign.Should().Be((ushort)(SyncSTTLimits.BytesPerSample * SyncSTTLimits.ChannelCount));
        format.AvgBytesPerSec.Should().Be(32_000);
        format.ExtraSize.Should().Be(0);
    }

    [Test]
    public void TheFormatStructHasTheNativeWaveFormatExLayout()
    {
        Marshal.SizeOf<WaveFormatEx>().Should().Be(18);
    }

    [TestCase(unchecked((int)0x80070005), MicCaptureFailure.AccessDenied)]
    [TestCase(unchecked((int)0x88890004), MicCaptureFailure.NoDevice)]
    [TestCase(unchecked((int)0x80070490), MicCaptureFailure.NoDevice)]
    [TestCase(unchecked((int)0x88890008), MicCaptureFailure.Failed)]
    [TestCase(unchecked((int)0x80004005), MicCaptureFailure.Failed)]
    public void HResultsMapToTheirFailureKind(int hresult, MicCaptureFailure expected)
    {
        var exception = MicCaptureException.FromHResult(hresult);

        exception.Failure.Should().Be(expected);
    }

    [Test]
    public void AGenericFailureKeepsItsHResult()
    {
        var exception = MicCaptureException.Failed(unchecked((int)0x88890008));

        exception.HResult.Should().Be(unchecked((int)0x88890008));
        exception.Message.Should().Be("The microphone failed to open (0x88890008).");
    }

    [Test]
    public void EachFailureHasAHumanReadableMessage()
    {
        MicCaptureException.AccessDenied().Message.Should().Be("Microphone access is turned off in Windows privacy settings.");
        MicCaptureException.NoDevice().Message.Should().Be("No microphone is available.");
        MicCaptureException.NotLive().Message.Should().Be("The microphone didn't start.");
    }

    [Test]
    public void EachNamedFailureHasADistinctHResult()
    {
        int[] hresults =
        [
            MicCaptureException.AccessDenied().HResult,
            MicCaptureException.NoDevice().HResult,
            MicCaptureException.NotLive().HResult,
        ];

        hresults.Should().OnlyHaveUniqueItems();
    }
}
