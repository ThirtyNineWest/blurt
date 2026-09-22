namespace Blurt.Engine.Pipeline;

/// <summary>
/// Shape of the overlay pill's live level meter — the bar row that tracks the current mic level,
/// plus the breathing curve the REC dot and "Transcribing…" label share. Pure math, owned here for
/// the same reason as <see cref="OverlayPlacement"/>: the rules are worth pinning, and a view body
/// is not somewhere a test can reach them.
/// </summary>
public static class MeterBarGeometry
{
    /// <summary>Width of a single bar. The view lays the row out with it, so this and <see cref="BarCount"/> can't disagree about the pitch.</summary>
    public const double BarWidth = 3;

    /// <summary>The gap between two adjacent bars.</summary>
    public const double BarSpacing = 3;

    /// <summary>Insets taken off the frame's width before bars are placed.</summary>
    private const double HorizontalInset = 4;

    /// <summary>Insets taken off the frame's height before bars are placed.</summary>
    public const double VerticalInset = 3;

    /// <summary>Floor on every bar, so a silent pill reads as a row of dots rather than nothing at all.</summary>
    public const double MinBarHeightFraction = 0.12;

    /// <summary>Compresses the 0...1 level into bar height. Lowish so normal speech already fills much of the pill height.</summary>
    internal const double LevelGamma = 1.3;

    /// <summary>Height fraction at the far ends of the symmetric envelope; the center is 1.</summary>
    public const double EnvelopeEdge = 0.45;

    /// <summary>How far a bar travels while idle-breathing.</summary>
    internal const double BreathDepth = 0.12;

    /// <summary>Idle wave: one full sweep every this many seconds.</summary>
    public const double BreathPeriod = 2.0;

    /// <summary>Radians each bar trails its left neighbour by, so a soft crest travels left to right.</summary>
    internal const double WavePhaseStep = 0.45;

    /// <summary>Voice level at which the idle breathing has faded out completely.</summary>
    internal const double BreathFadeCeiling = 0.25;

    /// <summary>Never fewer than this many bars, however narrow the frame.</summary>
    public const int MinBarCount = 3;

    /// <summary>How many bars fit <paramref name="availableWidth"/>. Derived rather than fixed so the row fills whatever width the pill gives it.</summary>
    public static int BarCount(double availableWidth)
    {
        var usable = Math.Max(1, availableWidth - HorizontalInset * 2);
        return Math.Max(MinBarCount, (int)((usable + BarSpacing) / (BarWidth + BarSpacing)));
    }

    /// <summary>The tallest a bar may be inside <paramref name="availableHeight"/>.</summary>
    public static double MaxBarHeight(double availableHeight) => Math.Max(1, availableHeight - VerticalInset * 2);

    /// <summary>
    /// Symmetric raised-sine window: <see cref="EnvelopeEdge"/> at the ends rising to 1 at the
    /// center, so the bars form a single voice hump filling the pill width.
    /// </summary>
    public static double EnvelopeWeight(int index, int count)
    {
        if (count <= 1)
        {
            return 1;
        }

        var position = (double)index / (count - 1);
        return EnvelopeEdge + (1 - EnvelopeEdge) * Math.Sin(Math.PI * position);
    }

    /// <summary>
    /// Raised-cosine opacity breathing over <paramref name="period"/>: 1 → <paramref name="minOpacity"/>
    /// → 1, so a view eases through the dim point instead of bouncing off it. Shared by the REC dot
    /// and the "Transcribing…" label so the pill's two heartbeats are one curve rather than two that
    /// look alike.
    /// </summary>
    public static double BreathingOpacity(double time, double period, double minOpacity)
    {
        var osc = (Math.Cos(time / period * 2 * Math.PI) + 1) / 2;
        return minOpacity + (1 - minOpacity) * osc;
    }

    /// <summary>
    /// Angle in degrees of a continuous rotation of <paramref name="period"/> seconds per turn —
    /// wrapped into 0..&lt;360 rather than growing without bound, since the value feeds a rotation
    /// effect and an ever-increasing angle loses precision after a long session.
    /// </summary>
    public static double RotationDegrees(double time, double period)
    {
        if (period <= 0)
        {
            return 0;
        }

        var turns = time / period;
        return (turns - Math.Floor(turns)) * 360;
    }

    /// <summary>
    /// Height fraction (before the row's max height scales it) for a bar at <paramref name="index"/>
    /// carrying <paramref name="weight"/>. <paramref name="level"/> is the 0...1 mic level;
    /// <paramref name="time"/> advances the idle wave and is ignored unless <paramref name="animated"/> —
    /// pass <see langword="false"/> to honor Reduce Motion, which drops the wave entirely.
    /// </summary>
    internal static double BarHeightFraction(int index, double weight, float level, double time, bool animated)
    {
        // Voice-driven height: the current level, gamma-shaped, scaled by this bar's envelope weight
        // so the middle leads.
        var voice = Math.Pow(level, LevelGamma) * weight;
        var breath = 0.0;
        // Full breathing when quiet, fading to none once the voice is moderate. Once it's faded out
        // the per-bar sine would multiply to ~0, so skip it entirely on louder frames rather than
        // computing-then-zeroing.
        var idleStrength = animated ? Math.Max(0, 1 - voice / BreathFadeCeiling) : 0;
        if (idleStrength > 0)
        {
            // A gentle wave travelling left-to-right across the row while quiet; the phase advances
            // with time and steps back per bar, sending a soft crest sweeping across.
            var phase = (time / BreathPeriod * 2 * Math.PI) - (index * WavePhaseStep);
            var osc = (Math.Sin(phase) + 1) / 2;
            breath = BreathDepth * weight * osc * idleStrength;
        }

        return Math.Max(MinBarHeightFraction, voice + breath);
    }
}

/// <summary>
/// One laid-out row of meter bars: how many fit the frame and how tall each may be, resolved once
/// per render so the count used to lay the row out and the count used to shape it can't disagree.
/// </summary>
public sealed class MeterBarRow
{
    private readonly IReadOnlyList<double> _weights;

    /// <summary>Resolves the row for the frame the meter has been given.</summary>
    public MeterBarRow(Size availableSize)
    {
        var count = MeterBarGeometry.BarCount(availableSize.Width);
        Count = count;
        MaxBarHeight = MeterBarGeometry.MaxBarHeight(availableSize.Height);
        _weights = Enumerable.Range(0, count).Select(index => MeterBarGeometry.EnvelopeWeight(index, count)).ToList();
    }

    /// <summary>Bars that fit the width, never below <see cref="MeterBarGeometry.MinBarCount"/>.</summary>
    public int Count { get; }

    /// <summary>The tallest a bar in this row may be.</summary>
    public double MaxBarHeight { get; }

    /// <summary>
    /// Height of the bar at <paramref name="index"/> for the current <paramref name="level"/>. An
    /// index outside the row falls back to the unweighted envelope of 1 rather than throwing,
    /// matching <see cref="MeterBarGeometry.EnvelopeWeight"/>'s own count-below-two fallback.
    /// </summary>
    public double Height(int index, float level, double time, bool animated)
    {
        var weight = index >= 0 && index < _weights.Count ? _weights[index] : 1;
        var fraction = MeterBarGeometry.BarHeightFraction(index, weight, level, time, animated);
        return MaxBarHeight * Math.Min(1, fraction);
    }
}
