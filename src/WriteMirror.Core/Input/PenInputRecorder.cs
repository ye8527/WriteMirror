using System.Collections.ObjectModel;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Input;

/// <summary>
/// Default in-memory recorder. Only in-contact samples are retained. Synthetic
/// release, cancellation, and capture-lost samples therefore cannot extend a
/// stroke's distance, duration, or speed.
/// </summary>
public sealed class PenInputRecorder : IPenInputRecorder
{
    private readonly List<Stroke> _strokes = [];
    private List<PenPointSample>? _activePoints;

    public bool IsRecording => _activePoints is not null;

    public IReadOnlyList<Stroke> Strokes =>
        new ReadOnlyCollection<Stroke>(_strokes.ToArray());

    public void StartStroke()
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("A stroke is already being recorded.");
        }

        _activePoints = [];
    }

    public void AddPoint(PenPointSample point)
    {
        ArgumentNullException.ThrowIfNull(point);

        List<PenPointSample> activePoints = _activePoints
            ?? throw new InvalidOperationException("StartStroke must be called before adding points.");

        PenPointSample? previous = activePoints.Count == 0 ? null : activePoints[^1];
        long? previousTimestampUs = previous?.TimestampUs
            ?? (_strokes.Count == 0 ? null : _strokes[^1].EndTimestampUs);
        if (previousTimestampUs is not null && point.TimestampUs < previousTimestampUs.Value)
        {
            throw new ArgumentException(
                "Point timestamps cannot move backwards within a recording.",
                nameof(point));
        }

        if (!point.IsInContact)
        {
            return;
        }

        if (point == previous)
        {
            return;
        }

        activePoints.Add(point);
    }

    public Stroke? EndStroke()
    {
        List<PenPointSample> activePoints = _activePoints
            ?? throw new InvalidOperationException("No stroke is being recorded.");

        _activePoints = null;
        if (activePoints.Count == 0)
        {
            return null;
        }

        var stroke = new Stroke(_strokes.Count, activePoints);
        _strokes.Add(stroke);
        return stroke;
    }

    public void Reset()
    {
        _activePoints = null;
        _strokes.Clear();
    }
}
