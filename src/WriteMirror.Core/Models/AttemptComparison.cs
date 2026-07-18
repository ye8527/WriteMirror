using System.Collections.ObjectModel;

namespace WriteMirror.Core.Models;

/// <summary>A before/after value pair. Percent change is unavailable at a zero baseline.</summary>
public sealed record MetricDelta(
    double FirstValue,
    double SecondValue,
    double AbsoluteChange,
    double? PercentChange);

/// <summary>Comparison of corresponding strokes when both attempts have equal structure.</summary>
public sealed record StrokeComparison(
    int StrokeIndex,
    MetricDelta DurationUs,
    MetricDelta PathLengthPx,
    MetricDelta? MeanPressure);

/// <summary>Deterministic measurements comparing two attempts by the same user.</summary>
public sealed record AttemptComparison
{
    public AttemptComparison(
        int firstStrokeCount,
        int secondStrokeCount,
        MetricDelta totalDurationUs,
        MetricDelta? longestPauseUs,
        MetricDelta? pressureVariability,
        IEnumerable<StrokeComparison> strokes)
    {
        ArgumentNullException.ThrowIfNull(totalDurationUs);
        ArgumentNullException.ThrowIfNull(strokes);
        FirstStrokeCount = firstStrokeCount;
        SecondStrokeCount = secondStrokeCount;
        TotalDurationUs = totalDurationUs;
        LongestPauseUs = longestPauseUs;
        PressureVariability = pressureVariability;
        Strokes = new ReadOnlyCollection<StrokeComparison>(strokes.ToArray());
    }

    public int FirstStrokeCount { get; }

    public int SecondStrokeCount { get; }

    public bool HasComparableStrokeStructure => FirstStrokeCount == SecondStrokeCount;

    public MetricDelta TotalDurationUs { get; }

    public MetricDelta? LongestPauseUs { get; }

    public MetricDelta? PressureVariability { get; }

    public IReadOnlyList<StrokeComparison> Strokes { get; }
}
