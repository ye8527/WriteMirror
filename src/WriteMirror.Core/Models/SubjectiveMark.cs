using System.Collections.ObjectModel;

namespace WriteMirror.Core.Models;

public enum SubjectiveLabel
{
    Hesitation,
    Difficult,
    Dissatisfied
}

public enum SubjectiveResponseKind
{
    Hesitation,
    Difficult,
    Dissatisfied,
    None,
    WentWell,
    Skipped
}

public static class SubjectiveResponsePolicy
{
    public static bool RequiresLocation(SubjectiveResponseKind response) =>
        response is SubjectiveResponseKind.Hesitation or
            SubjectiveResponseKind.Difficult or
            SubjectiveResponseKind.Dissatisfied;

    public static bool ShowsObservationCandidatesAutomatically(SubjectiveResponseKind response) =>
        RequiresLocation(response);
}

/// <summary>A user-selected circular region captured before analysis is shown.</summary>
public sealed record SubjectiveMark
{
    public SubjectiveMark(
        double centerX,
        double centerY,
        double radiusPx,
        IEnumerable<SubjectiveLabel> labels,
        string? note = null)
    {
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY))
        {
            throw new ArgumentException("Mark coordinates must be finite.");
        }

        if (!double.IsFinite(radiusPx) || radiusPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusPx), "Radius must be positive.");
        }

        ArgumentNullException.ThrowIfNull(labels);
        CenterX = centerX;
        CenterY = centerY;
        RadiusPx = radiusPx;
        Labels = new ReadOnlyCollection<SubjectiveLabel>(labels.Distinct().ToArray());
        Note = note;
    }

    public double CenterX { get; }

    public double CenterY { get; }

    public double RadiusPx { get; }

    public IReadOnlyList<SubjectiveLabel> Labels { get; }

    public string? Note { get; }
}

public enum ObjectiveEventKind
{
    LongestPause,
    SlowestMovement
}

public enum SpatialRelation
{
    Inside,
    Near,
    Separate
}

/// <summary>Spatial relationship between one objective event and the user's mark.</summary>
public sealed record SubjectiveEventMatch(
    ObjectiveEventKind EventKind,
    double EventX,
    double EventY,
    double DistanceFromCenterPx,
    SpatialRelation Relation);

/// <summary>All objective events available for comparison with a subjective mark.</summary>
public sealed record SubjectiveMatchResult
{
    public SubjectiveMatchResult(IEnumerable<SubjectiveEventMatch> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        Events = new ReadOnlyCollection<SubjectiveEventMatch>(events.ToArray());
    }

    public IReadOnlyList<SubjectiveEventMatch> Events { get; }

    public SubjectiveEventMatch? Closest =>
        Events.OrderBy(item => item.DistanceFromCenterPx).FirstOrDefault();
}
