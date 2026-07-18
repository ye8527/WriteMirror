using System.Collections.ObjectModel;

namespace WriteMirror.Core.Models;

/// <summary>
/// An immutable pen-down to pen-up sequence.
/// </summary>
public sealed record Stroke
{
    public Stroke(int strokeIndex, IEnumerable<PenPointSample> points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(strokeIndex);
        ArgumentNullException.ThrowIfNull(points);

        PenPointSample[] snapshot = points.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("A stroke must contain at least one point.", nameof(points));
        }

        StrokeIndex = strokeIndex;
        Points = new ReadOnlyCollection<PenPointSample>(snapshot);
    }

    /// <summary>Zero-based index within an attempt.</summary>
    public int StrokeIndex { get; }

    /// <summary>Chronologically ordered point samples.</summary>
    public IReadOnlyList<PenPointSample> Points { get; }

    public long StartTimestampUs => Points[0].TimestampUs;

    public long EndTimestampUs => Points[^1].TimestampUs;
}
