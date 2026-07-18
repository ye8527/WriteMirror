using WriteMirror.Core.Analysis;
using WriteMirror.Core.Comparison;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class AttemptComparerTests
{
    private readonly AttemptComparer _comparer = new(new WritingAnalyzer());

    [TestMethod]
    public void EqualStrokeCounts_CompareCorrespondingStrokes()
    {
        WritingAttempt first = Attempt(1, Stroke(0, 0, 1_000_000, 0.2f));
        WritingAttempt second = Attempt(2, Stroke(0, 0, 800_000, 0.4f));

        AttemptComparison comparison = _comparer.Compare(first, second);

        Assert.IsTrue(comparison.HasComparableStrokeStructure);
        Assert.AreEqual(1, comparison.Strokes.Count);
        Assert.AreEqual(-200_000d, comparison.TotalDurationUs.AbsoluteChange);
        Assert.AreEqual(-20d, comparison.TotalDurationUs.PercentChange!.Value, 0.000_001);
        Assert.AreEqual(0.2d, comparison.Strokes[0].MeanPressure!.AbsoluteChange, 0.000_001);
    }

    [TestMethod]
    public void DifferentStrokeCounts_CompareTotalsOnly()
    {
        WritingAttempt first = Attempt(1, Stroke(0, 0, 1_000));
        WritingAttempt second = Attempt(
            2,
            Stroke(0, 0, 1_000),
            Stroke(1, 2_000, 3_000));

        AttemptComparison comparison = _comparer.Compare(first, second);

        Assert.IsFalse(comparison.HasComparableStrokeStructure);
        Assert.AreEqual(0, comparison.Strokes.Count);
        Assert.AreEqual(1, comparison.FirstStrokeCount);
        Assert.AreEqual(2, comparison.SecondStrokeCount);
    }

    [TestMethod]
    public void ZeroBaseline_MakesPercentChangeUnavailable()
    {
        WritingAttempt first = Attempt(1, SinglePointStroke(0, 0));
        WritingAttempt second = Attempt(2, Stroke(0, 0, 1_000));

        AttemptComparison comparison = _comparer.Compare(first, second);

        Assert.IsNull(comparison.TotalDurationUs.PercentChange);
    }

    [TestMethod]
    public void MissingPressure_DoesNotCreateSyntheticComparison()
    {
        AttemptComparison comparison = _comparer.Compare(
            Attempt(1, Stroke(0, 0, 1_000)),
            Attempt(2, Stroke(0, 0, 900)));

        Assert.IsNull(comparison.PressureVariability);
        Assert.IsNull(comparison.Strokes[0].MeanPressure);
    }

    private static WritingAttempt Attempt(int attemptNo, params Stroke[] strokes) =>
        new(attemptNo, strokes);

    private static Stroke Stroke(
        int index,
        long startUs,
        long endUs,
        float? pressure = null) =>
        new(index, new[]
        {
            new PenPointSample(0, 0, startUs, pressure, null, null, true),
            new PenPointSample(1, 0, endUs, pressure, null, null, true)
        });

    private static Stroke SinglePointStroke(int index, long timestampUs) =>
        new(index, new[]
        {
            new PenPointSample(0, 0, timestampUs, null, null, null, true)
        });
}
