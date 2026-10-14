using System.ComponentModel;
using System.Globalization;
using Blurt.App.Services;
using Blurt.Engine.Config;
using Blurt.Engine.Hotkey;

namespace Blurt.App.Settings;

/// <summary>
/// Backs the Experimental section: the read-aloud switch, hold-to-ask, the read-aloud speed, and
/// skipping jargon, each written straight to its store, plus the guidance footers that follow the
/// trigger key and activation mode. Use on the UI thread.
/// </summary>
public sealed class ExperimentalViewModel : ObservableObject
{
    private readonly SelectionSpeechStore _readAloud;
    private readonly SelectionAskStore _ask;
    private readonly ReadAloudStyleStore _style;
    private readonly DictationSettingsViewModel _dictation;

    /// <summary>Creates the view model over the read-aloud stores and the trigger pickers.</summary>
    public ExperimentalViewModel(SettingsStores settings, DictationSettingsViewModel dictation)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dictation);
        _readAloud = settings.SelectionSpeech;
        _ask = settings.SelectionAsk;
        _style = settings.ReadAloudStyle;
        _dictation = dictation;
        SpeedChoices = [.. ReadAloudStyleStore.SpeedChoices.Select(c => new Choice<double>(c, ReadAloudStyleStore.SpeedLabel(c, CultureInfo.CurrentCulture)))];
        dictation.PropertyChanged += OnDictationChanged;
    }

    /// <summary>Whether a press over selected text reads it aloud.</summary>
    public bool ReadAloud
    {
        get => _readAloud.IsEnabled;
        set
        {
            if (value == _readAloud.IsEnabled)
            {
                return;
            }

            _readAloud.IsEnabled = value;
            Raise(nameof(ReadAloud), nameof(ShowsReadOptions), nameof(ShowsAskNote), nameof(ShowsSkipNote));
        }
    }

    /// <summary>Whether a hold over selected text records a request about it and reads the answer.</summary>
    public bool Ask
    {
        get => _ask.IsEnabled;
        set
        {
            if (value == _ask.IsEnabled)
            {
                return;
            }

            _ask.IsEnabled = value;
            Raise(nameof(Ask), nameof(ShowsAskNote), nameof(HowToUse));
        }
    }

    /// <summary>The read-aloud speed, snapped to one of <see cref="SpeedChoices"/>.</summary>
    public double Speed
    {
        get => _style.Speed;
        set
        {
            if (value.Equals(_style.Speed))
            {
                return;
            }

            _style.Speed = value;
            Raise(nameof(Speed));
        }
    }

    /// <summary>The selectable speeds with their labels.</summary>
    public IReadOnlyList<Choice<double>> SpeedChoices { get; }

    /// <summary>Whether code, links, and long numbers are skipped when reading.</summary>
    public bool SkipsJargon
    {
        get => _style.SkipsJargon;
        set
        {
            if (value == _style.SkipsJargon)
            {
                return;
            }

            _style.SkipsJargon = value;
            Raise(nameof(SkipsJargon), nameof(ShowsSkipNote));
        }
    }

    /// <summary>How to use read-aloud under the current key and activation mode, ending with the dictation fallback sentence.</summary>
    public string HowToUse
    {
        get
        {
            var key = _dictation.TriggerKey.Label();
            var activation = _dictation.Activation;
            var line = !Ask
                ? $"Select text and {activation.StartVerb()} {key} to hear it. {activation.StopHint()}"
                : activation == TriggerActivation.Hold
                    ? $"Select text and hold {key} without speaking to hear it. Press again to stop."
                    : $"Select text and tap {key} to hear it. Tap again to stop.";
            return $"{line} With nothing selected, the key dictates as usual.";
        }
    }

    /// <summary>How to ask about a selection, and where asking sends it.</summary>
    public string AskNote =>
        $"Hold {_dictation.TriggerKey.Label()} over a selection and say what you want, like “summarize this,” to hear the answer. "
        + "Asking sends the selection and what you said to AssemblyAI’s LLM Gateway.";

    /// <summary>Where skipping jargon sends the selection.</summary>
    public string SkipNote =>
        "To skip code, links, and long numbers, Blurt sends the selection to AssemblyAI’s LLM Gateway before reading it.";

    /// <summary>Whether the asking, speed, and jargon options are shown.</summary>
    public bool ShowsReadOptions => ReadAloud;

    /// <summary>Whether <see cref="AskNote"/> is shown.</summary>
    public bool ShowsAskNote => ReadAloud && Ask;

    /// <summary>Whether <see cref="SkipNote"/> is shown.</summary>
    public bool ShowsSkipNote => ReadAloud && SkipsJargon;

    /// <summary>Re-reads every store-backed value, for a window being shown again.</summary>
    public void Reload() =>
        Raise(
            nameof(ReadAloud),
            nameof(Ask),
            nameof(Speed),
            nameof(SkipsJargon),
            nameof(HowToUse),
            nameof(AskNote),
            nameof(ShowsReadOptions),
            nameof(ShowsAskNote),
            nameof(ShowsSkipNote));

    private void OnDictationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DictationSettingsViewModel.TriggerKey) or nameof(DictationSettingsViewModel.Activation))
        {
            Raise(nameof(HowToUse), nameof(AskNote));
        }
    }
}
