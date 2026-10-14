namespace Blurt.Engine.TTS;

/// <summary>The socket operations <see cref="AssemblyAISpeechSynthesizer"/> uses, so a test can play the service's side.</summary>
public interface ISpeechSocket
{
    /// <summary>Sends <paramref name="text"/> as one text message. Throws once the socket is closed.</summary>
    Task SendAsync(string text, CancellationToken ct);

    /// <summary>The next whole message, decoded as UTF-8 text; null once the socket is closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken ct);

    /// <summary>
    /// Closes the socket: normally after <c>Terminate</c>, abruptly on failure or cancel. Ends any
    /// pending receive. Only the first call has an effect.
    /// </summary>
    void Close(bool normally);
}

/// <summary>Opens a speech socket to <paramref name="url"/>, authenticated with the raw <paramref name="apiKey"/>.</summary>
public delegate Task<ISpeechSocket> SpeechSocketConnector(Uri url, string apiKey, CancellationToken ct);
