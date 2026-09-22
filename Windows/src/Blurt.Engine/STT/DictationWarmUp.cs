using System.Diagnostics;
using System.Globalization;
using Blurt.Engine.Diagnostics;

namespace Blurt.Engine.STT;

public sealed partial class AssemblyAITranscriber
{
    /// <summary>
    /// The route's pre-warming endpoint, an unauthenticated no-op at the host root (unversioned,
    /// unlike the transcribe path).
    /// </summary>
    private const string WarmPath = "warm";

    private static readonly TimeSpan s_warmUpTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Pre-opens and pools a connection to the dictation host with an unauthenticated
    /// <c>GET /warm</c>, so the next request's audio starts moving without paying DNS, TCP, and TLS
    /// setup. Goes through the same transport and base URL the transcribe request uses, which is
    /// what lets that request reuse the connection. Bounded at 5 s. Never throws.
    /// </summary>
    public async Task WarmUpAsync()
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            using var timeout = new CancellationTokenSource(s_warmUpTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(WarmPath));
            request.Headers.TryAddWithoutValidation(UserAgent.HeaderField, UserAgent.Current);
            using var response = await _transport.SendAsync(request, timeout.Token);

            // Read to the end so the connection returns to the pool instead of being drained or dropped.
            await response.Content.ReadAsByteArrayAsync(timeout.Token);
        }
        catch (Exception)
        {
            // A failed warm-up only means the real request pays connection setup.
        }

        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        BlurtLog.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"warm-up connect {elapsedMs:F0}ms"));
    }
}
