using System.IO;
using System.Windows.Media;
using Blurt.Engine.Audio;
using Blurt.Engine.Diagnostics;
using Blurt.Engine.Pipeline;

namespace Blurt.App.Cues;

/// <summary>
/// Plays the record start/stop chimes of the selected sound pack. Every member must be called on
/// the UI thread that created the instance, since <see cref="MediaPlayer"/> is thread-affine.
/// A silent pack or a missing cue file leaves that cue unloaded, which playing skips.
/// </summary>
public sealed class CueSoundPlayer : IDisposable
{
    private const string LogCategory = "CueSoundPlayer";

    // The cues are normalized to a hot peak; well below full scale they read as a soft chime.
    private const double CueVolume = 0.35;

    private static readonly TimeSpan PreviewGap = TimeSpan.FromMilliseconds(380);

    private readonly SoundPackStore _store;
    private readonly string _soundsDirectory;
    private readonly MediaPlayer _start = new() { Volume = CueVolume };
    private readonly MediaPlayer _stop = new() { Volume = CueVolume };
    private RecordingCueGate _gate;
    private SoundPack? _loadedPack;
    private bool _startLoaded;
    private bool _stopLoaded;

    /// <summary>Creates a player reading the selection from <paramref name="store"/> and the cue files from <c>Resources\Sounds</c> beside the executable.</summary>
    public CueSoundPlayer(SoundPackStore store)
    {
        _store = store;
        _soundsDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "Sounds");
        _start.MediaFailed += (_, e) => BlurtLog.Error(LogCategory, $"start cue failed to load: {e.ErrorException?.Message}");
        _stop.MediaFailed += (_, e) => BlurtLog.Error(LogCategory, $"stop cue failed to load: {e.ErrorException?.Message}");
    }

    /// <summary>Opens the selected pack's cue files unless that pack is already loaded, so the first chime does not wait on a file open.</summary>
    public void Prime() => LoadCurrentPack();

    /// <summary>Plays the cue <see cref="RecordingCueGate"/> resolves for <paramref name="phase"/>. Call once per rendered phase.</summary>
    public void Transition(PipelinePhase phase)
    {
        switch (_gate.Cue(phase))
        {
            case RecordingCue.Start:
                Play(_start, _startLoaded);
                break;
            case RecordingCue.Stop:
                Play(_stop, _stopLoaded);
                break;
            default:
                break;
        }
    }

    /// <summary>Loads the newly selected pack and previews it: the start cue, then the stop cue a beat later.</summary>
    public void PackChanged()
    {
        LoadCurrentPack();
        Play(_start, _startLoaded);
        _ = PlayStopAfterGapAsync();
    }

    /// <summary>Closes both players.</summary>
    public void Dispose()
    {
        _start.Close();
        _stop.Close();
    }

    private async Task PlayStopAfterGapAsync()
    {
        await Task.Delay(PreviewGap);
        Play(_stop, _stopLoaded);
    }

    private void LoadCurrentPack()
    {
        var pack = _store.SoundPack;
        if (pack == _loadedPack)
        {
            return;
        }

        _loadedPack = pack;
        _startLoaded = Open(_start, pack.StartFileName);
        _stopLoaded = Open(_stop, pack.StopFileName);
    }

    private bool Open(MediaPlayer player, string? stem)
    {
        if (stem is null)
        {
            player.Close();
            return false;
        }

        var path = Path.Combine(_soundsDirectory, stem + ".m4a");
        if (!File.Exists(path))
        {
            BlurtLog.Warn(LogCategory, $"cue file missing: {path}");
            player.Close();
            return false;
        }

        player.Open(new Uri(path, UriKind.Absolute));
        return true;
    }

    private static void Play(MediaPlayer player, bool loaded)
    {
        if (!loaded)
        {
            return;
        }

        player.Stop();
        player.Play();
    }
}
