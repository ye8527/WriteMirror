using System.Collections.ObjectModel;

namespace WriteMirror.Core.Models;

public enum Handedness
{
    Unspecified,
    Right,
    Left
}

/// <summary>
/// A local reflection session for one writing task.
/// </summary>
public sealed record WritingSession
{
    public WritingSession(
        Guid sessionId,
        string taskId,
        DateTimeOffset startedAt,
        IEnumerable<WritingAttempt> attempts,
        Handedness handedness = Handedness.Unspecified)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(attempts);

        SessionId = sessionId;
        TaskId = taskId;
        StartedAt = startedAt;
        Attempts = new ReadOnlyCollection<WritingAttempt>(attempts.ToArray());
        Handedness = handedness;
    }

    public Guid SessionId { get; }

    public string TaskId { get; }

    public DateTimeOffset StartedAt { get; }

    public IReadOnlyList<WritingAttempt> Attempts { get; }

    /// <summary>
    /// User-declared writing hand. This is context metadata and is never a score.
    /// </summary>
    public Handedness Handedness { get; }
}
