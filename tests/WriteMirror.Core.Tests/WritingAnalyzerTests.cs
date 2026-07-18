using WriteMirror.Core.Analysis;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class WritingAnalyzerTests
{
    private readonly WritingAnalyzer _analyzer = new();

    [TestMethod]
    public void EmptyAttempt_ReturnsAvailableZeroCounts()
    {
        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, []));

        Assert.AreEqual(0, metrics.TotalDurationUs);
        Assert.AreEqual(0, metrics.Strokes.Count);
        Assert.AreEqual(0, metrics.Pauses.Count);
        Assert.AreEqual(0d, metrics.TotalPathLengthPx);
        Assert.IsNull(metrics.MeanSpeedPxPerSecond);
        Assert.IsNull(metrics.Pressure);
    }

    [TestMethod]
    public void KnownStroke_CalculatesDurationDistanceSpeedAndPressure()
    {
        var stroke = new Stroke(0, new[]
        {
            Point(0, 0, 0, 0.2f),
            Point(3, 4, 1_000_000, 0.6f)
        });

        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, new[] { stroke }));

        Assert.AreEqual(1_000_000, metrics.TotalDurationUs);
        Assert.AreEqual(5d, metrics.TotalPathLengthPx, 0.000_001);
        Assert.AreEqual(5d, metrics.MeanSpeedPxPerSecond!.Value, 0.000_001);
        Assert.AreEqual(5d, metrics.Strokes[0].Movements[0].SpeedPxPerSecond!.Value, 0.000_001);
        Assert.AreEqual(0.4d, metrics.Pressure!.Mean, 0.000_001);
        Assert.AreEqual(0.2d, metrics.Pressure.PopulationStandardDeviation, 0.000_001);
        Assert.AreEqual(0.5d, metrics.Pressure.CoefficientOfVariation!.Value, 0.000_001);
    }

    [TestMethod]
    public void MultipleStrokes_CalculatesDurationsAndPause()
    {
        var strokes = new[]
        {
            new Stroke(0, new[] { Point(0, 0, 0), Point(1, 0, 100_000) }),
            new Stroke(1, new[] { Point(2, 0, 400_000), Point(3, 0, 600_000) })
        };

        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, strokes));

        Assert.AreEqual(600_000, metrics.TotalDurationUs);
        Assert.AreEqual(100_000, metrics.Strokes[0].DurationUs);
        Assert.AreEqual(200_000, metrics.Strokes[1].DurationUs);
        Assert.AreEqual(300_000, metrics.Pauses[0].DurationUs);
        Assert.AreEqual(300_000, metrics.LongestPauseUs);
    }

    [TestMethod]
    public void ZeroTimeDifference_DoesNotDivideByZero()
    {
        var stroke = new Stroke(0, new[]
        {
            Point(0, 0, 1_000),
            Point(1, 0, 1_000)
        });

        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, new[] { stroke }));

        Assert.AreEqual(1d, metrics.TotalPathLengthPx);
        Assert.IsNull(metrics.MeanSpeedPxPerSecond);
        Assert.IsNull(metrics.Strokes[0].MeanSpeedPxPerSecond);
        Assert.IsNull(metrics.Strokes[0].Movements[0].SpeedPxPerSecond);
    }

    [TestMethod]
    public void MissingPressure_RemainsUnavailable()
    {
        var stroke = new Stroke(0, new[]
        {
            Point(0, 0, 0),
            Point(1, 0, 1_000)
        });

        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, new[] { stroke }));

        Assert.IsNull(metrics.Pressure);
        Assert.IsNull(metrics.Strokes[0].Pressure);
    }

    [TestMethod]
    public void RelativePauseRule_FlagsOnlyLongPause()
    {
        var strokes = new[]
        {
            SinglePointStroke(0, 0),
            SinglePointStroke(1, 100_000),
            SinglePointStroke(2, 210_000),
            SinglePointStroke(3, 710_000)
        };

        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, strokes));

        CollectionAssert.AreEqual(
            new[] { false, false, true },
            metrics.Pauses.Select(pause => pause.IsLongPause).ToArray());
        Assert.AreEqual(250_000, metrics.LongPauseThresholdUs!.Value);
    }

    [TestMethod]
    public void OutOfOrderPoints_AreRejected()
    {
        var stroke = new Stroke(0, new[]
        {
            Point(0, 0, 2_000),
            Point(1, 0, 1_999)
        });

        Assert.ThrowsException<ArgumentException>(
            () => _analyzer.Analyze(new WritingAttempt(1, new[] { stroke })));
    }

    [TestMethod]
    public void NonContactRelease_DoesNotIncreaseDistanceDurationOrSpeed()
    {
        var stroke = new Stroke(0, new[]
        {
            Point(0, 0, 0),
            Point(3, 4, 1_000_000),
            new PenPointSample(300, 400, 2_000_000, null, null, null, false)
        });

        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, new[] { stroke }));

        Assert.AreEqual(1_000_000, metrics.TotalDurationUs);
        Assert.AreEqual(5d, metrics.TotalPathLengthPx, 0.000_001);
        Assert.AreEqual(5d, metrics.MeanSpeedPxPerSecond!.Value, 0.000_001);
        Assert.IsTrue(metrics.Strokes.SelectMany(item => item.Movements)
            .All(item => item.FromPoint.IsInContact && item.ToPoint.IsInContact));
    }

    private static Stroke SinglePointStroke(int index, long timestampUs) =>
        new(index, new[] { Point(index, 0, timestampUs) });

    private static PenPointSample Point(
        double x,
        double y,
        long timestampUs,
        float? pressure = null) =>
        new(x, y, timestampUs, pressure, null, null, true);
}
