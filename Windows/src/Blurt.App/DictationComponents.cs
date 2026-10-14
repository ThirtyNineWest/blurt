using Blurt.App.Services;
using Blurt.Engine;
using Blurt.Engine.Audio;
using Blurt.Engine.Config;
using Blurt.Engine.Injection;
using Blurt.Engine.STT;
using Blurt.Engine.TTS;
using Blurt.Engine.Windows.Audio;
using Blurt.Engine.Windows.FocusCapture;
using Blurt.Engine.Windows.Hotkey;
using Blurt.Engine.Windows.Injection;

namespace Blurt.App;

/// <summary>The pipeline collaborators <see cref="AppCoordinator"/> composes into a dictation session.</summary>
/// <param name="Mic">The capture seam, also the source of the meter levels and the launch warm-up.</param>
/// <param name="Transcriber">The AssemblyAI client.</param>
/// <param name="Injector">The clipboard-paste injector.</param>
/// <param name="Focus">Focus capture, also told which windows are Blurt's own.</param>
/// <param name="UserInput">When the user last typed or clicked, fed by the low-level hooks and read by the injector.</param>
/// <param name="Speaker">Reads selected text, and answers about it, aloud.</param>
/// <param name="ReadSelection">Returns the focused app's selected text, or null when there is none.</param>
public sealed record DictationComponents(
    IMicCapture Mic,
    ITranscriber Transcriber,
    IInjector Injector,
    UiaFocusCapture Focus,
    UserInputClock UserInput,
    SelectionSpeaker Speaker,
    Func<CancellationToken, Task<string?>> ReadSelection)
{
    /// <summary>
    /// The real pipeline: WASAPI capture, the AssemblyAI transcriber over <paramref name="transport"/>
    /// reading the key from <paramref name="keyStore"/> and the style settings at every request, and
    /// the clipboard injector sharing one focus capture, and the read-aloud speaker playing through
    /// WASAPI with the selection read over that same focus capture.
    /// </summary>
    public static DictationComponents Production(IHttpTransport transport, IApiKeyGateway keyStore, SettingsStores settings)
    {
        var focus = new UiaFocusCapture();
        var userInput = new UserInputClock();
        var transcriber = new AssemblyAITranscriber(
            () => keyStore.Current,
            transport,
            enhancedTranscripts: () => settings.EnhancedTranscripts.IsEnabled,
            customStyle: () => settings.Styles.ActiveInstructions);
        var speaker = new SelectionSpeaker(
            new AssemblyAISpeechSynthesizer(() => keyStore.Current, ClientWebSocketSpeechSocket.ConnectAsync),
            new ReadAloudLLM(() => keyStore.Current, transport),
            rate => new WasapiPcmPlayer(AssemblyAISpeechSynthesizer.SampleRate, rate));
        return new DictationComponents(
            new WasapiMicCapture(),
            transcriber,
            new ClipboardInjector(focus, userInput),
            focus,
            userInput,
            speaker,
            ct => SelectionSpeaker.FocusedSelectionAsync(focus, new SelectionCopy(), ct));
    }
}
