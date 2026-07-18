using WriteMirror.Core.Models;

namespace WriteMirror.Core.Analysis;

/// <summary>
/// Calculates deterministic measurements from a writing attempt.
/// </summary>
public interface IWritingAnalyzer
{
    WritingMetrics Analyze(WritingAttempt attempt);
}
