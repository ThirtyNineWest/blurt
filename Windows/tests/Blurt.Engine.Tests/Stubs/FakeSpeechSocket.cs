using System.Net.WebSockets;
using System.Threading.Channels;
using Blurt.Engine.TTS;

namespace Blurt.Engine.Tests.Stubs;

/// <summary>
/// Plays the TTS service's side of the socket. Each command sent is passed to the responder, and
/// the frames it returns are queued for <see cref="ReceiveAsync"/>. A receive with nothing queued
/// waits until more arrives or the socket closes, so a responder that returns nothing models a
/// server that never answers.
/// </summary>
public sealed class FakeSpeechSocket(Func<string, IReadOnlyList<string>> respond) : ISpeechSocket
{
    /// <summary>The service's acknowledgement of one flush.</summary>
    public const string FlushDone = """{"type":"FlushDone"}""";

    private readonly Lock _gate = new();
    private readonly List<string> _sent = [];
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();
    private bool? _closedNormally;

    /// <summary>Every command sent, in order.</summary>
    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent];
            }
        }
    }

    /// <summary>Null while open; otherwise whether the first close was the normal kind.</summary>
    public bool? ClosedNormally
    {
        get
        {
            lock (_gate)
            {
                return _closedNormally;
            }
        }
    }

    /// <summary>Records <paramref name="text"/> and queues the responder's frames; fails once closed.</summary>
    public Task SendAsync(string text, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_closedNormally is not null)
            {
                return Task.FromException(new WebSocketException("The socket is closed."));
            }

            _sent.Add(text);
        }

        foreach (var frame in respond(text))
        {
            _incoming.Writer.TryWrite(frame);
        }

        return Task.CompletedTask;
    }

    /// <summary>The next queued frame, or null once the socket is closed or dropped.</summary>
    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        try
        {
            return await _incoming.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>Records the first close's kind and ends any pending receive.</summary>
    public void Close(bool normally)
    {
        lock (_gate)
        {
            _closedNormally ??= normally;
        }

        _incoming.Writer.TryComplete();
    }

    /// <summary>Drops the connection from the server's side, without recording a close.</summary>
    public void Drop() => _incoming.Writer.TryComplete();

    /// <summary>An <c>Audio</c> frame carrying <paramref name="bytes"/> as base64.</summary>
    public static string Audio(byte[] bytes) => $$"""{"type":"Audio","audio":"{{Convert.ToBase64String(bytes)}}"}""";

    /// <summary>
    /// The live service's answers: nothing for <c>Generate</c>, and for each <c>Flush</c> one audio
    /// chunk <c>[index, 0]</c> (the first flush is index 1) then <see cref="FlushDone"/>.
    /// </summary>
    public static Func<string, IReadOnlyList<string>> Service()
    {
        var flushes = 0;
        return command =>
        {
            if (!command.Contains("\"Flush\"", StringComparison.Ordinal))
            {
                return [];
            }

            var index = Interlocked.Increment(ref flushes);
            return [Audio([(byte)index, 0]), FlushDone];
        };
    }
}
