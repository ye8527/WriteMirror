using WriteMirror.Core.Analysis;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Comparison;

/// <summary>
/// Compares total measurements for all attempts and corresponding stroke order only
/// when stroke counts match. No shape alignment or quality score is inferred.
/// </summary>
public sealed class AttemptComparer : IAttemptComparer
{
    private readonly IWritingAnalyzer _analyzer;

    public AttemptComparer(IWritingAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        _analyzer = analyzer;
    }

    public AttemptComparison Compare(WritingAttempt first, WritingAttempt second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        WritingMetrics firstMetrics = first.Metrics ?? _analyzer.Analyze(first);
        WritingMetrics secondMetrics = second.Metrics ?? _analyzer.Analyze(second);
        bool comparableStructure = first.Strokes.Count == second.Strokes.Count;
        var strokeComparisons = new List<StrokeComparison>();

        if (comparableStructure)
        {
            for (int index = 0; index < firstMetrics.Strokes.Count; index++)
            {
                StrokeMetrics firstStroke = firstMetrics.Strokes[index];
                StrokeMetrics secondStroke = secondMetrics.Strokes[index];
                strokeComparisons.Add(new StrokeComparison(
                    index,
                    Delta(firstStroke.DurationUs, secondStroke.DurationUs),
                    Delta(firstStroke.PathLengthPx, secondStroke.PathLengthPx),
                    OptionalDelta(firstStroke.Pressure?.Mean, secondStroke.Pressure?.Mean)));
            }
        }

        return new AttemptComparison(
            first.Strokes.Count,
            second.Strokes.Count,
            Delta(firstMetrics.TotalDurationUs, secondMetrics.TotalDurationUs),
            OptionalDelta(firstMetrics.LongestPauseUs, secondMetrics.LongestPauseUs),
            OptionalDelta(
                firstMetrics.Pressure?.PopulationStandardDeviation,
                secondMetrics.Pressure?.PopulationStandardDeviation),
            strokeComparisons);
    }

    private static MetricDelta? OptionalDelta(double? first, double? second) =>
        first is null || second is null ? null : Delta(first.Value, second.Value);

    private static MetricDelta? OptionalDelta(long? first, long? second) =>
        first is null || second is null ? null : Delta(first.Value, second.Value);

    private static MetricDelta Delta(double first, double second)
    {
        double change = second - first;
        return new MetricDelta(
            first,
            second,
            change,
            first == 0 ? null : change / first * 100);
    }
}
