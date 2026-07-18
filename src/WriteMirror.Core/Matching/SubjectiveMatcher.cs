using WriteMirror.Core.Models;

namespace WriteMirror.Core.Matching;

/// <summary>
/// Matches independently defined observation candidates against a circular mark.
/// Only statistically flagged inter-stroke gaps are eligible. WPF point timestamps
/// are interpolated, so local movement speed is never used as an observation event.
/// These are UI spatial rules, not evidence of difficulty or ability.
/// </summary>
public sealed class SubjectiveMatcher : ISubjectiveMatcher
{
    private const double DefaultNearMarginPx = 24;
    private readonly double _nearMarginPx;

    public SubjectiveMatcher(double nearMarginPx = DefaultNearMarginPx)
    {
        if (!double.IsFinite(nearMarginPx) || nearMarginPx < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nearMarginPx));
        }

        _nearMarginPx = nearMarginPx;
    }

    public SubjectiveMatchResult Match(SubjectiveMark mark, WritingMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ArgumentNullException.ThrowIfNull(metrics);

        var matches = new List<SubjectiveEventMatch>();
        PauseMetrics? longestPause = metrics.Pauses
            .Where(pause => pause.IsLongPause)
            .OrderByDescending(pause => pause.DurationUs)
            .FirstOrDefault();
        if (longestPause is not null)
        {
            AddEndpointPairMatch(
                matches,
                mark,
                ObjectiveEventKind.LongestPause,
                longestPause.BeforePoint,
                longestPause.AfterPoint);
        }

        return new SubjectiveMatchResult(matches);
    }

    private void AddEndpointPairMatch(
        ICollection<SubjectiveEventMatch> matches,
        SubjectiveMark mark,
        ObjectiveEventKind eventKind,
        PenPointSample from,
        PenPointSample to)
    {
        double fromDistanceSquared = Math.Pow(from.X - mark.CenterX, 2) +
            Math.Pow(from.Y - mark.CenterY, 2);
        double toDistanceSquared = Math.Pow(to.X - mark.CenterX, 2) +
            Math.Pow(to.Y - mark.CenterY, 2);
        PenPointSample closestEndpoint = fromDistanceSquared <= toDistanceSquared ? from : to;
        double eventX = closestEndpoint.X;
        double eventY = closestEndpoint.Y;
        double distance = Math.Sqrt(
            Math.Pow(eventX - mark.CenterX, 2) +
            Math.Pow(eventY - mark.CenterY, 2));
        SpatialRelation relation = distance <= mark.RadiusPx
            ? SpatialRelation.Inside
            : distance <= mark.RadiusPx + _nearMarginPx
                ? SpatialRelation.Near
                : SpatialRelation.Separate;

        matches.Add(new SubjectiveEventMatch(
            eventKind,
            eventX,
            eventY,
            distance,
            relation));
    }
}
