using System.Net.WebSockets;
using System.Text;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.STT;

namespace Blurt.Engine.TTS;

/// <summary>The production <see cref="ISpeechSocket"/>, over <see cref="ClientWebSocket"/>.</summary>
public sealed class ClientWebSocketSpeechSocket : ISpeechSocket
{
    private const string LogCategory = "tts";
    private const int ReceiveBufferSize = 16 * 1024;

    private readonly ClientWebSocket _socket;
    private int _closed;

    private ClientWebSocketSpeechSocket(ClientWebSocket socket) => _socket = socket;

    /// <summary>
    /// Connects to <paramref name="url"/> with the raw <paramref name="apiKey"/> in
    /// <c>Authorization</c> and the Blurt <c>User-Agent</c>. Matches <see cref="SpeechSocketConnector"/>.
    /// </summary>
    /// <returns>The open socket.</returns>
    /// <exception cref="WebSocketException">The connection or upgrade failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public static async Task<ISpeechSocket> ConnectAsync(Uri url, string apiKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(apiKey);
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("Authorization", apiKey);
            socket.Options.SetRequestHeader(UserAgent.HeaderField, UserAgent.Current);
            await socket.ConnectAsync(url, ct);
            return new ClientWebSocketSpeechSocket(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The text of a whole message: UTF-8 for text and binary messages alike, null for a close message.</summary>
    public static string? Decode(WebSocketMessageType type, ReadOnlySpan<byte> payload) =>
        type == WebSocketMessageType.Close ? null : Encoding.UTF8.GetString(payload);

    /// <inheritdoc/>
    public Task SendAsync(string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    /// <inheritdoc/>
    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Decode(result.MessageType, message.GetBuffer().AsSpan(0, (int)message.Length));
                }
            }
        }
        catch (Exception exception) when (Volatile.Read(ref _closed) != 0
            && exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void Close(bool normally)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        if (normally)
        {
            _ = CloseNormallyAsync();
            return;
        }

        _socket.Abort();
        _socket.Dispose();
    }

    /// <summary>Sends the close frame, logs a failure to send it, and disposes the socket either way. Never throws.</summary>
    private async Task CloseNormallyAsync()
    {
        try
        {
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        }
        catch (Exception exception)
        {
            BlurtLog.Warn(LogCategory, $"The text-to-speech socket did not close cleanly: {exception.GetType().Name}");
        }
        finally
        {
            _socket.Dispose();
        }
    }
}
