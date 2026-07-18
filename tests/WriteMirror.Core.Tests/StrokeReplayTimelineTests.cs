using WriteMirror.Core.Models;
using WriteMirror.Core.Replay;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class StrokeReplayTimelineTests
{
    [TestMethod]
    public void Create_PreservesStrokeAndPointOrder()
    {
        PenPointSample first = Point(0, 0, 1_000);
        PenPointSample second = Point(1, 1, 1_400);
        PenPointSample third = Point(2, 2, 2_000);
        var strokes = new[]
        {
            new Stroke(0, new[] { first, second }),
            new Stroke(1, new[] { third })
        };

        IReadOnlyList<ReplayFrame> frames = StrokeReplayTimeline.Create(strokes);

        CollectionAssert.AreEqual(
            new[] { first, second, third },
            frames.Select(frame => frame.Point).ToArray());
        CollectionAssert.AreEqual(
            new long[] { 0, 400, 600 },
            frames.Select(frame => frame.DelayUs).ToArray());
        CollectionAssert.AreEqual(
            new[] { true, false, true },
            frames.Select(frame => frame.StartsStroke).ToArray());
    }

    [TestMethod]
    public void Create_RejectsTimeMovingBackwardsAcrossStrokes()
    {
        var strokes = new[]
        {
            new Stroke(0, new[] { Point(0, 0, 2_000) }),
            new Stroke(1, new[] { Point(1, 1, 1_999) })
        };

        Assert.ThrowsException<ArgumentException>(
            () => StrokeReplayTimeline.Create(strokes));
    }

    private static PenPointSample Point(double x, double y, long timestampUs) =>
        new(x, y, timestampUs, 0.5f, 0f, 0f, true);
}
