namespace WriteMirror.Core.Feedback;

/// <summary>
/// Bridge implemented by a platform project when an on-device language model is
/// available. Core never depends directly on an experimental Windows AI package.
/// </summary>
public interface ILocalLanguageModel
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateJsonAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}

/// <summary>Explicit capability result used until a platform model bridge is configured.</summary>
public sealed class UnavailableLocalLanguageModel : ILocalLanguageModel
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<string> GenerateJsonAsync(
        string prompt,
        CancellationToken cancellationToken = default) =>
        Task.FromException<string>(
            new NotSupportedException("No on-device language model bridge is configured."));
}
