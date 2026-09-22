using Blurt.Engine.Audio;
using FluentAssertions;

namespace Blurt.Engine.Tests.Audio;

public sealed class MicCaptureDefaultsTests
{
    [Test]
    public async Task DefaultLevelsFeedIsEmptyAndWarmUpIsANoOp()
    {
        IMicCapture mic = new BareMic();
        await mic.WarmUpAsync();
        var count = 0;

        await foreach (var _ in mic.Levels)
        {
            count++;
        }

        count.Should().Be(0);
    }

    [Test]
    public async Task DefaultCancelCaptureStopsAndDiscards()
    {
        var bare = new BareMic();
        IMicCapture mic = bare;

        await mic.CancelCaptureAsync();

        bare.Stops.Should().Be(1);
    }

    private sealed class BareMic : IMicCapture
    {
        private int _stops;

        public int Stops => Volatile.Read(ref _stops);

        public Task<IAsyncEnumerable<byte[]>> StartAsync(CancellationToken ct) =>
            Task.FromResult(AsyncEnumerable.Empty<byte[]>());

        public Task<int> StopAsync()
        {
            Interlocked.Increment(ref _stops);
            return Task.FromResult(0);
        }
    }
}
