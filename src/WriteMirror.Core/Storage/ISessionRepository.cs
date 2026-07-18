using WriteMirror.Core.Models;

namespace WriteMirror.Core.Storage;

/// <summary>Stores privacy-minimized writing sessions on the local device.</summary>
public interface ISessionRepository
{
    Task SaveAsync(WritingSession session, CancellationToken cancellationToken = default);

    Task<WritingSession?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
