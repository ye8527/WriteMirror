using System.Collections.ObjectModel;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Replay;

/// <summary>
/// Produces a deterministic, chronological replay sequence from recorded strokes.
/// </summary>
public static class StrokeReplayTimeline
{
    public static IReadOnlyList<ReplayFrame> Create(IEnumerable<Stroke> strokes)
    {
        ArgumentNullException.ThrowIfNull(strokes);

        var frames = new List<ReplayFrame>();
        long? previousTimestampUs = null;

        foreach (Stroke stroke in strokes)
        {
            for (int pointIndex = 0; pointIndex < stroke.Points.Count; pointIndex++)
            {
                PenPointSample point = stroke.Points[pointIndex];
                if (previousTimestampUs is not null && point.TimestampUs < previousTimestampUs.Value)
                {
                    throw new ArgumentException(
                        "Stroke timestamps must be chronological.",
                        nameof(strokes));
                }

                long delayUs = previousTimestampUs is null
                    ? 0
                    : point.TimestampUs - previousTimestampUs.Value;

                frames.Add(new ReplayFrame(point, delayUs, pointIndex == 0));
                previousTimestampUs = point.TimestampUs;
            }
        }

        return new ReadOnlyCollection<ReplayFrame>(frames);
    }
}
