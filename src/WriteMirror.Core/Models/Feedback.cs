using System.Collections.ObjectModel;

namespace WriteMirror.Core.Models;

public enum PressureTrend
{
    Unavailable,
    Decreased,
    Increased,
    NoClearChange
}

/// <summary>Validated facts supplied to a feedback generator; it contains no raw points.</summary>
public sealed record FeedbackRequest(
    string Locale,
    string TaskDisplayName,
    int? LongestPauseBeforeStrokeNumber,
    int? LongestPauseAfterStrokeNumber,
    long? LongestPauseMs,
    SpatialRelation? SubjectiveRelation,
    ObjectiveEventKind? MatchedEventKind,
    double? SecondAttemptDurationChangePercent,
    bool? HasComparableStrokeStructure,
    PressureTrend PressureTrend,
    SubjectiveResponseKind SubjectiveResponse);

/// <summary>Three-part neutral reflection text.</summary>
public sealed record FeedbackMessage
{
    public FeedbackMessage(
        string generator,
        string observation,
        string reflection,
        string nextQuestion,
        IEnumerable<string>? safetyFlags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(reflection);
        ArgumentException.ThrowIfNullOrWhiteSpace(nextQuestion);
        Generator = generator;
        Observation = observation;
        Reflection = reflection;
        NextQuestion = nextQuestion;
        SafetyFlags = new ReadOnlyCollection<string>((safetyFlags ?? []).ToArray());
    }

    public string Generator { get; }

    public string Observation { get; }

    public string Reflection { get; }

    public string NextQuestion { get; }

    public IReadOnlyList<string> SafetyFlags { get; }
}
