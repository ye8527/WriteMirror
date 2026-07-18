using WriteMirror.Core.Analysis;
using WriteMirror.Core.Matching;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class SubjectiveMatcherTests
{
    private readonly SubjectiveMatcher _matcher = new();
    private readonly WritingAnalyzer _analyzer = new();

    [TestMethod]
    public void LongGapSegmentOnCircleBoundary_IsInside()
    {
        WritingMetrics metrics = AnalyzeLongGap(10, 20);
        var mark = new SubjectiveMark(0, 0, 10, [SubjectiveLabel.Hesitation]);

        SubjectiveEventMatch pause = _matcher.Match(mark, metrics).Events.Single(
            item => item.EventKind == ObjectiveEventKind.LongestPause);

        Assert.AreEqual(SpatialRelation.Inside, pause.Relation);
        Assert.AreEqual(10d, pause.DistanceFromCenterPx, 0.000_001);
    }

    [TestMethod]
    public void LongGapSegmentWithinMargin_IsNear()
    {
        WritingMetrics metrics = AnalyzeLongGap(20, 40);
        var mark = new SubjectiveMark(0, 0, 10, [SubjectiveLabel.Hesitation]);

        SubjectiveEventMatch pause = _matcher.Match(mark, metrics).Events.Single(
            item => item.EventKind == ObjectiveEventKind.LongestPause);

        Assert.AreEqual(SpatialRelation.Near, pause.Relation);
    }

    [TestMethod]
    public void LongGapSegmentBeyondMargin_IsSeparate()
    {
        WritingMetrics metrics = AnalyzeLongGap(50, 100);
        var mark = new SubjectiveMark(0, 0, 10, [SubjectiveLabel.Hesitation]);

        SubjectiveEventMatch pause = _matcher.Match(mark, metrics).Events.Single(
            item => item.EventKind == ObjectiveEventKind.LongestPause);

        Assert.AreEqual(SpatialRelation.Separate, pause.Relation);
    }

    [TestMethod]
    public void SingleUnflaggedGap_ReturnsNoPauseCandidate()
    {
        var strokes = new[]
        {
            new Stroke(0, [Point(0, 0)]),
            new Stroke(1, [Point(20, 100_000)])
        };
        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, strokes));
        var mark = new SubjectiveMark(10, 0, 8, [SubjectiveLabel.Hesitation]);

        SubjectiveMatchResult result = _matcher.Match(mark, metrics);

        Assert.IsFalse(result.Events.Any(item => item.EventKind == ObjectiveEventKind.LongestPause));
    }

    [TestMethod]
    public void InterpolatedMovementTimestamps_NeverProduceSpeedCandidate()
    {
        var stroke = new Stroke(0, new[]
        {
            Point(0, 0),
            Point(20, 1_000_000),
            Point(30, 1_100_000)
        });
        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, [stroke]));
        var mark = new SubjectiveMark(10, 0, 4, [SubjectiveLabel.Hesitation]);

        SubjectiveMatchResult result = _matcher.Match(mark, metrics);

        Assert.IsFalse(result.Events.Any(
            item => item.EventKind == ObjectiveEventKind.SlowestMovement));
    }

    [TestMethod]
    public void OneMovementInterval_IsInsufficientForCandidate()
    {
        var stroke = new Stroke(0, [Point(0, 0), Point(20, 1_000_000)]);
        WritingMetrics metrics = _analyzer.Analyze(new WritingAttempt(1, [stroke]));
        var mark = new SubjectiveMark(10, 0, 4, [SubjectiveLabel.Hesitation]);

        Assert.AreEqual(0, _matcher.Match(mark, metrics).Events.Count);
    }

    [TestMethod]
    public void MarkBetweenPauseEndpoints_DoesNotMatchUnobservedAirPath()
    {
        WritingMetrics metrics = AnalyzeLongGap(0, 100);
        var mark = new SubjectiveMark(50, 0, 5, [SubjectiveLabel.Hesitation]);

        SubjectiveEventMatch pause = _matcher.Match(mark, metrics).Events.Single();

        Assert.AreEqual(SpatialRelation.Separate, pause.Relation);
        Assert.AreEqual(50d, pause.DistanceFromCenterPx, 0.000_001);
    }

    private WritingMetrics AnalyzeLongGap(double fromX, double toX)
    {
        var strokes = new[]
        {
            new Stroke(0, [Point(fromX, 0)]),
            new Stroke(1, [Point(fromX, 100_000)]),
            new Stroke(2, [Point(fromX, 200_000)]),
            new Stroke(3, [Point(toX, 700_000)])
        };
        return _analyzer.Analyze(new WritingAttempt(1, strokes));
    }

    private static PenPointSample Point(double x, long timestampUs) =>
        new(x, 0, timestampUs, null, null, null, true);
}
