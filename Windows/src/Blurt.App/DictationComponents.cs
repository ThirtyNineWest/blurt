using Blurt.App.Services;
using Blurt.Engine;
using Blurt.Engine.Audio;
using Blurt.Engine.Config;
using Blurt.Engine.Injection;
using Blurt.Engine.STT;
using Blurt.Engine.Windows.Audio;
using Blurt.Engine.Windows.FocusCapture;
using Blurt.Engine.Windows.Injection;

namespace Blurt.App;

/// <summary>The pipeline collaborators <see cref="AppCoordinator"/> composes into a dictation session.</summary>
/// <param name="Mic">The capture seam, also the source of the meter levels and the launch warm-up.</param>
/// <param name="Transcriber">The AssemblyAI client.</param>
/// <param name="Injector">The clipboard-paste injector.</param>
/// <param name="Focus">Focus capture, also told which windows are Blurt's own.</param>
public sealed record DictationComponents(IMicCapture Mic, ITranscriber Transcriber, IInjector Injector, UiaFocusCapture Focus)
{
    /// <summary>
    /// The real pipeline: WASAPI capture, the AssemblyAI transcriber over <paramref name="transport"/>
    /// reading the key from <paramref name="keyStore"/> and the style settings at every request, and
    /// the clipboard injector sharing one focus capture.
    /// </summary>
    public static DictationComponents Production(IHttpTransport transport, IApiKeyGateway keyStore, SettingsStores settings)
    {
        var focus = new UiaFocusCapture();
        var transcriber = new AssemblyAITranscriber(
            () => keyStore.Current,
            transport,
            enhancedTranscripts: () => settings.EnhancedTranscripts.IsEnabled,
            customStyle: () => settings.Styles.ActiveInstructions);
        return new DictationComponents(new WasapiMicCapture(), transcriber, new ClipboardInjector(focus), focus);
    }
}
