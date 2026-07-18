using WriteMirror.Core.Input;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class PenInputRecorderTests
{
    [TestMethod]
    public void NormalInput_CreatesChronologicalStroke()
    {
        var recorder = new PenInputRecorder();
        PenPointSample first = Point(10, 20, 1_000);
        PenPointSample second = Point(12, 23, 2_000);

        recorder.StartStroke();
        recorder.AddPoint(first);
        recorder.AddPoint(second);
        Stroke? stroke = recorder.EndStroke();

        Assert.IsNotNull(stroke);
        Assert.AreEqual(0, stroke.StrokeIndex);
        CollectionAssert.AreEqual(new[] { first, second }, stroke.Points.ToArray());
        Assert.AreEqual(1_000, stroke.StartTimestampUs);
        Assert.AreEqual(2_000, stroke.EndTimestampUs);
        Assert.AreEqual(1, recorder.Strokes.Count);
        Assert.IsFalse(recorder.IsRecording);
    }

    [TestMethod]
    public void EmptyStroke_IsDiscarded()
    {
        var recorder = new PenInputRecorder();

        recorder.StartStroke();
        Stroke? stroke = recorder.EndStroke();

        Assert.IsNull(stroke);
        Assert.AreEqual(0, recorder.Strokes.Count);
        Assert.IsFalse(recorder.IsRecording);
    }

    [TestMethod]
    public void TimestampMovingBackwards_IsRejected()
    {
        var recorder = new PenInputRecorder();
        recorder.StartStroke();
        recorder.AddPoint(Point(10, 20, 2_000));

        Assert.ThrowsException<ArgumentException>(
            () => recorder.AddPoint(Point(11, 21, 1_999)));
    }

    [TestMethod]
    public void ConsecutiveDuplicatePoint_IsIgnored()
    {
        var recorder = new PenInputRecorder();
        PenPointSample point = Point(10, 20, 1_000);

        recorder.StartStroke();
        recorder.AddPoint(point);
        recorder.AddPoint(point);
        Stroke? stroke = recorder.EndStroke();

        Assert.IsNotNull(stroke);
        Assert.AreEqual(1, stroke.Points.Count);
    }

    [TestMethod]
    public void TimestampMovingBackwardsAcrossStrokes_IsRejected()
    {
        var recorder = new PenInputRecorder();
        recorder.StartStroke();
        recorder.AddPoint(Point(10, 20, 2_000));
        recorder.EndStroke();
        recorder.StartStroke();

        Assert.ThrowsException<ArgumentException>(
            () => recorder.AddPoint(Point(11, 21, 1_999)));
    }

    [TestMethod]
    public void SyntheticReleasePoint_IsNotStoredOrMeasuredAsStrokeEnd()
    {
        var recorder = new PenInputRecorder();
        PenPointSample contact = Point(10, 20, 1_000);
        var release = new PenPointSample(100, 200, 2_000, null, null, null, false);

        recorder.StartStroke();
        recorder.AddPoint(contact);
        recorder.AddPoint(release);
        Stroke? stroke = recorder.EndStroke();

        Assert.IsNotNull(stroke);
        Assert.AreEqual(1, stroke.Points.Count);
        Assert.AreEqual(contact, stroke.Points[^1]);
        Assert.IsTrue(stroke.Points[^1].IsInContact);
    }

    [TestMethod]
    public void StrokeWithOnlyNonContactSamples_IsDiscarded()
    {
        var recorder = new PenInputRecorder();
        recorder.StartStroke();
        recorder.AddPoint(new PenPointSample(10, 20, 1_000, null, null, null, false));

        Assert.IsNull(recorder.EndStroke());
        Assert.AreEqual(0, recorder.Strokes.Count);
    }

    private static PenPointSample Point(double x, double y, long timestampUs) =>
        new(x, y, timestampUs, 0.5f, 2f, -3f, true);
}
