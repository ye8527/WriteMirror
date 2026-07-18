using System.Collections.ObjectModel;

namespace WriteMirror.Core.Models;

/// <summary>
/// One complete writing attempt in a reflection session.
/// </summary>
public sealed record WritingAttempt
{
    public WritingAttempt(
        int attemptNo,
        IEnumerable<Stroke> strokes,
        SubjectiveMark? subjectiveMark = null,
        WritingMetrics? metrics = null,
        SubjectiveResponseKind? subjectiveResponse = null)
    {
        if (attemptNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNo), "Attempt numbers start at one.");
        }

        ArgumentNullException.ThrowIfNull(strokes);
        AttemptNo = attemptNo;
        Strokes = new ReadOnlyCollection<Stroke>(strokes.ToArray());
        SubjectiveMark = subjectiveMark;
        Metrics = metrics;
        SubjectiveResponse = subjectiveResponse;
    }

    public int AttemptNo { get; }

    public IReadOnlyList<Stroke> Strokes { get; }

    public SubjectiveMark? SubjectiveMark { get; }

    public WritingMetrics? Metrics { get; }

    public SubjectiveResponseKind? SubjectiveResponse { get; }
}
