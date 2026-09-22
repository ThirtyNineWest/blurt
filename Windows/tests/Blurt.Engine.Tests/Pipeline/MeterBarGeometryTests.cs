using Blurt.Engine.Pipeline;
using FluentAssertions;

namespace Blurt.Engine.Tests.Pipeline;

/// <summary>
/// The overlay meter's shape rules: how many bars fit a frame, how tall each is for the current
/// level, and the breathing/rotation curves the pill's other motion shares.
/// </summary>
public sealed class MeterBarGeometryTests
{
    private const double RealisticWidth = 100;
    private const double RealisticHeight = 28;

    /// <summary>A row laid out for the pill's realistic frame: 15 bars, 22 pt tall.</summary>
    private static readonly MeterBarRow Row = new(new Size(RealisticWidth, RealisticHeight));

    [Test]
    public void BarCountFillsTheWidthAtTheBarPitch()
    {
        // 100 - 2*4 inset = 92 usable; (92 + 3) / (3 + 3) = 15 bars.
        MeterBarGeometry.BarCount(RealisticWidth).Should().Be(15);
        MeterBarGeometry.BarCount(200).Should().BeGreaterThan(MeterBarGeometry.BarCount(100));
    }

    [Test]
    public void BarCountNeverDropsBelowTheFloorHoweverNarrowTheFrame()
    {
        foreach (var width in new[] { 0, 1, 4, 8, 12, -50 })
        {
            MeterBarGeometry.BarCount(width).Should().Be(MeterBarGeometry.MinBarCount);
        }
    }

    [Test]
    public void MaxBarHeightInsetsFrameAndStaysPositive()
    {
        MeterBarGeometry.MaxBarHeight(RealisticHeight).Should().Be(RealisticHeight - MeterBarGeometry.VerticalInset * 2);
        MeterBarGeometry.MaxBarHeight(0).Should().Be(1);
        MeterBarGeometry.MaxBarHeight(-10).Should().Be(1);
    }

    [Test]
    public void ARowResolvesItsCountAndHeightFromTheFrameItIsGiven()
    {
        Row.Count.Should().Be(MeterBarGeometry.BarCount(RealisticWidth));
        Row.MaxBarHeight.Should().Be(MeterBarGeometry.MaxBarHeight(RealisticHeight));

        var empty = new MeterBarRow(Size.Zero);

        empty.Count.Should().Be(MeterBarGeometry.MinBarCount);
        empty.MaxBarHeight.Should().Be(1);
    }

    [Test]
    public void TheEnvelopeIsSymmetricEdgeValuedAtTheEndsAndOneAtTheCenter()
    {
        const int count = 15;
        var weights = Enumerable.Range(0, count).Select(i => MeterBarGeometry.EnvelopeWeight(i, count)).ToList();

        Math.Abs(weights[0] - MeterBarGeometry.EnvelopeEdge).Should().BeLessThan(0.0001);
        Math.Abs(weights[^1] - MeterBarGeometry.EnvelopeEdge).Should().BeLessThan(0.0001);
        Math.Abs(weights[count / 2] - 1).Should().BeLessThan(0.0001);

        for (var i = 0; i < count; i++)
        {
            Math.Abs(weights[i] - weights[count - 1 - i]).Should().BeLessThan(0.0001);
        }

        weights.Should().OnlyContain(w => w >= MeterBarGeometry.EnvelopeEdge && w <= 1);
    }

    [Test]
    public void ASingleBarGetsFullWeightRatherThanDividingByZero()
    {
        MeterBarGeometry.EnvelopeWeight(0, 1).Should().Be(1);
        MeterBarGeometry.EnvelopeWeight(0, 0).Should().Be(1);
    }

    [Test]
    public void EveryBarStaysWithinTheFloorAndTheFrameAcrossTheLevelRange()
    {
        var floor = Row.MaxBarHeight * MeterBarGeometry.MinBarHeightFraction;

        for (var level = 0f; level <= 1f; level += 0.05f)
        {
            foreach (var animated in new[] { true, false })
            {
                for (var index = 0; index < Row.Count; index++)
                {
                    var h = Row.Height(index, level, 3.7, animated);

                    h.Should().BeGreaterThanOrEqualTo(floor);
                    h.Should().BeLessThanOrEqualTo(Row.MaxBarHeight);
                }
            }
        }
    }

    [Test]
    public void ALouderLevelNeverShortensABar()
    {
        var center = Row.Count / 2;
        var previous = 0.0;

        for (var level = 0f; level <= 1f; level += 0.05f)
        {
            var h = Row.Height(center, level, 0, animated: false);

            h.Should().BeGreaterThanOrEqualTo(previous);
            previous = h;
        }

        Row.Height(center, 1, 0, animated: false).Should().Be(Row.MaxBarHeight);
    }

    [Test]
    public void WithMotionOffTheHeightDependsOnlyOnTheLevelNeverOnTime()
    {
        const float quiet = 0.02f;
        var heights = new[] { 0.0, 0.5, 1.0, 37.25, 1_000.5 }
            .Select(time => Row.Height(4, quiet, time, animated: false))
            .Distinct()
            .ToList();

        heights.Should().HaveCount(1);
    }

    [Test]
    public void TheIdleWaveMovesBetweenWordsAndIsGoneOnceTheVoiceComesIn()
    {
        var quietHeights = Times(0, MeterBarGeometry.BreathPeriod, 0.1)
            .Select(time => Row.Height(4, 0.2f, time, animated: true))
            .Distinct()
            .ToList();

        quietHeights.Should().HaveCountGreaterThan(1);

        var loudHeights = Times(0, MeterBarGeometry.BreathPeriod, 0.1)
            .Select(time => Row.Height(4, 0.9f, time, animated: true))
            .Distinct()
            .ToList();

        loudHeights.Should().HaveCount(1);
    }

    [Test]
    public void ABarIndexOutsideTheRowFallsBackToFullWeightInsteadOfTrapping()
    {
        Row.Height(Row.Count, 1, 0, animated: false).Should().Be(Row.MaxBarHeight);
        Row.Height(-1, 1, 0, animated: false).Should().Be(Row.MaxBarHeight);

        const float quiet = 0.5f;

        Row.Height(Row.Count, quiet, 0, animated: false).Should().BeGreaterThan(Row.Height(0, quiet, 0, animated: false));
    }

    [Test]
    public void TheWaveIsPhaseShiftedPerBarSoACrestTravelsAcrossTheRow()
    {
        double Gap(bool animated) => Row.Height(5, 0.2f, 0.4, animated) - Row.Height(4, 0.2f, 0.4, animated);

        Math.Abs(Gap(true) - Gap(false)).Should().BeGreaterThan(0.001);
    }

    [Test]
    public void BreathingOpacityStartsFullDipsToTheFloorAndReturns()
    {
        const double period = 1.8;
        const double floor = 0.55;

        Math.Abs(MeterBarGeometry.BreathingOpacity(0, period, floor) - 1).Should().BeLessThan(0.0001);
        Math.Abs(MeterBarGeometry.BreathingOpacity(period / 2, period, floor) - floor).Should().BeLessThan(0.0001);
        Math.Abs(MeterBarGeometry.BreathingOpacity(period, period, floor) - 1).Should().BeLessThan(0.0001);
    }

    [Test]
    public void BreathingOpacityNeverLeavesTheFloorToOneBand()
    {
        foreach (var (period, floor) in new[] { (1.2, 0.4), (1.8, 0.55) })
        {
            foreach (var time in Times(0, 5, 0.05))
            {
                var o = MeterBarGeometry.BreathingOpacity(time, period, floor);

                o.Should().BeGreaterThanOrEqualTo(floor - 0.0001);
                o.Should().BeLessThanOrEqualTo(1 + 0.0001);
            }
        }
    }

    [Test]
    public void RotationSweepsOneFullTurnPerPeriodAndWraps()
    {
        const double period = 1.6;

        Math.Abs(MeterBarGeometry.RotationDegrees(0, period)).Should().BeLessThan(0.0001);
        Math.Abs(MeterBarGeometry.RotationDegrees(period / 4, period) - 90).Should().BeLessThan(0.0001);
        Math.Abs(MeterBarGeometry.RotationDegrees(period / 2, period) - 180).Should().BeLessThan(0.0001);
        Math.Abs(MeterBarGeometry.RotationDegrees(period, period)).Should().BeLessThan(0.0001);
    }

    [Test]
    public void RotationStaysInBandAndNeverDividesByAZeroPeriod()
    {
        foreach (var time in Times(0, 600, 0.37))
        {
            var d = MeterBarGeometry.RotationDegrees(time, 1.6);

            d.Should().BeGreaterThanOrEqualTo(0);
            d.Should().BeLessThan(360);
        }

        MeterBarGeometry.RotationDegrees(5, 0).Should().Be(0);
    }

    private static IEnumerable<double> Times(double from, double through, double step)
    {
        for (var t = from; t <= through; t += step)
        {
            yield return t;
        }
    }
}
