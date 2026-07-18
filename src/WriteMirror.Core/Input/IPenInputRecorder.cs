using WriteMirror.Core.Models;

namespace WriteMirror.Core.Input;

/// <summary>
/// Builds immutable strokes from pen-down, point, and pen-up events.
/// </summary>
public interface IPenInputRecorder
{
    bool IsRecording { get; }

    IReadOnlyList<Stroke> Strokes { get; }

    /// <summary>Begins a new pen-down sequence.</summary>
    void StartStroke();

    /// <summary>Adds a point to the active stroke.</summary>
    void AddPoint(PenPointSample point);

    /// <summary>
    /// Completes the active stroke, or returns <see langword="null"/> when no points were captured.
    /// </summary>
    Stroke? EndStroke();

    /// <summary>Clears completed strokes and abandons any active stroke.</summary>
    void Reset();
}
