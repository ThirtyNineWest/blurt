using System.Runtime.InteropServices;

namespace Blurt.Engine.TTS;

/// <summary>
/// Changes the playback speed of 16-bit little-endian mono PCM while holding its pitch
/// (waveform-similarity overlap-add). Each synthesis frame of <c>frameSize</c> samples is taken
/// from the input near the nominal analysis position (which advances <c>synthesisHop × rate</c>
/// per frame), at the offset within <c>±searchRange</c> whose waveform best continues the previous
/// frame, and overlap-added with a Hann crossfade over <c>synthesisHop</c> samples. Streaming:
/// input not yet consumed is kept across calls, and splitting the input into chunks yields the
/// same output as feeding it whole. Rate 1 passes bytes through unchanged.
/// </summary>
public sealed class PcmTimeStretch
{
    private readonly int _frameSize;
    private readonly int _hop;
    private readonly int _search;
    private readonly double[] _window;
    private readonly List<short> _input = [];
    private short[]? _tail;

    // Nominal analysis position of the next frame, relative to _input[0]. Trimming n samples
    // from the front of _input must subtract exactly n here.
    private double _position;

    /// <summary>Creates a stretcher at <paramref name="rate"/> (1 is natural pace, 2 is twice as fast).</summary>
    /// <param name="rate">The playback rate, a positive finite number.</param>
    /// <param name="frameSize">The analysis frame length in samples.</param>
    /// <param name="synthesisHop">The output samples produced per frame, which is also the crossfade length.</param>
    /// <param name="searchRange">How many samples either side of the nominal position the alignment search covers.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is not a positive finite number, or the geometry is not <c>0 ≤ searchRange ≤ synthesisHop</c>, <c>0 &lt; synthesisHop</c>, and <c>2 × synthesisHop ≤ frameSize</c>.</exception>
    public PcmTimeStretch(double rate, int frameSize = 480, int synthesisHop = 240, int searchRange = 120)
    {
        if (!double.IsFinite(rate) || rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "The rate must be a positive finite number.");
        }

        if (synthesisHop <= 0 || synthesisHop * 2 > frameSize || searchRange < 0 || searchRange > synthesisHop)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSize), "Expected 0 <= searchRange <= synthesisHop and 0 < 2 * synthesisHop <= frameSize.");
        }

        Rate = rate;
        _frameSize = frameSize;
        _hop = synthesisHop;
        _search = searchRange;
        _window = new double[synthesisHop];
        for (var i = 0; i < synthesisHop; i++)
        {
            _window[i] = 0.5 - (0.5 * Math.Cos(Math.PI * (i + 0.5) / synthesisHop));
        }
    }

    /// <summary>The playback rate: 1 is natural pace, 2 plays in half the time.</summary>
    public double Rate { get; }

    /// <summary>
    /// Feeds <paramref name="pcm"/> and returns every output sample that can be produced so far.
    /// At rate 1 returns <paramref name="pcm"/> itself. Otherwise a trailing odd byte is dropped,
    /// and the result may be empty while the stretcher waits for a full frame plus search window.
    /// </summary>
    /// <param name="pcm">16-bit little-endian mono PCM.</param>
    /// <returns>The newly produced 16-bit little-endian mono PCM, possibly empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pcm"/> is null.</exception>
    public byte[] Process(byte[] pcm)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        if (Rate == 1)
        {
            return pcm;
        }

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            _input.Add((short)(pcm[i] | (pcm[i + 1] << 8)));
        }

        var output = new List<short>();
        while (true)
        {
            var nominal = Nominal();
            var lowest = Math.Max(0, nominal - _search);
            var highest = nominal + _search;
            if (highest + _frameSize > _input.Count)
            {
                break;
            }

            var input = CollectionsMarshal.AsSpan(_input);
            if (_tail is null)
            {
                output.AddRange(input.Slice(nominal, _hop));
                _tail = new short[_frameSize - _hop];
                input.Slice(nominal + _hop, _tail.Length).CopyTo(_tail);
            }
            else
            {
                var start = BestOffset(input, lowest, highest);
                for (var i = 0; i < _hop; i++)
                {
                    var mixed = (_tail[i] * (1 - _window[i])) + (input[start + i] * _window[i]);
                    output.Add((short)Math.Clamp(Math.Round(mixed), short.MinValue, short.MaxValue));
                }

                input.Slice(start + _hop, _tail.Length).CopyTo(_tail);
            }

            _position += _hop * Rate;
        }

        var consumed = Math.Clamp(Nominal() - _search, 0, _input.Count);
        if (consumed > 0)
        {
            _input.RemoveRange(0, consumed);
            _position -= consumed;
        }

        return ToBytes(output);
    }

    /// <summary>
    /// Returns the overlap tail of the last frame unwindowed, then resets the stretcher so it can
    /// take a new stream. Input not yet covered by a full frame plus search window is discarded.
    /// </summary>
    /// <returns>The tail as 16-bit little-endian mono PCM; empty at rate 1 or when no frame was produced.</returns>
    public byte[] Flush()
    {
        var tail = _tail is null ? [] : ToBytes(_tail);
        _tail = null;
        _input.Clear();
        _position = 0;
        return tail;
    }

    // Rounds half up so the result shifts by exactly n when _position does, unlike banker's rounding.
    private int Nominal() => (int)Math.Floor(_position + 0.5);

    /// <summary>The start offset in <c>[lowest, highest]</c> whose first <c>hop</c> samples correlate best with the tail.</summary>
    private int BestOffset(ReadOnlySpan<short> input, int lowest, int highest)
    {
        var tail = _tail!;
        var best = lowest;
        var bestScore = double.NegativeInfinity;
        for (var offset = lowest; offset <= highest; offset++)
        {
            var candidate = input.Slice(offset, _hop);
            double score = 0;
            for (var i = 0; i < _hop; i++)
            {
                score += (double)tail[i] * candidate[i];
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = offset;
            }
        }

        return best;
    }

    private static byte[] ToBytes(IReadOnlyList<short> samples)
    {
        var bytes = new byte[samples.Count * 2];
        for (var i = 0; i < samples.Count; i++)
        {
            bytes[i * 2] = (byte)samples[i];
            bytes[(i * 2) + 1] = (byte)(samples[i] >> 8);
        }

        return bytes;
    }
}
