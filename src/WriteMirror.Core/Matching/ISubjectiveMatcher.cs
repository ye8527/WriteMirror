using WriteMirror.Core.Models;

namespace WriteMirror.Core.Matching;

/// <summary>Compares a user-selected region with deterministic writing events.</summary>
public interface ISubjectiveMatcher
{
    SubjectiveMatchResult Match(SubjectiveMark mark, WritingMetrics metrics);
}
