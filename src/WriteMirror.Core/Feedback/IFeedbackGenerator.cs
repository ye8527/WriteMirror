using WriteMirror.Core.Models;

namespace WriteMirror.Core.Feedback;

/// <summary>Generates neutral text from precomputed facts only.</summary>
public interface IFeedbackGenerator
{
    Task<FeedbackMessage> GenerateAsync(
        FeedbackRequest request,
        CancellationToken cancellationToken = default);
}
