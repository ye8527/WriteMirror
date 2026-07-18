using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WriteMirror.Core.Models;
using WriteMirror.Core.Storage;

namespace WriteMirror.Infrastructure.Storage;

/// <summary>
/// Stores session JSON in an application-owned directory. DTO mapping deliberately
/// excludes free-form logs, prompts, identities, and generated feedback.
/// </summary>
public sealed class JsonSessionRepository : ISessionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _directoryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSessionRepository(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
    }

    public async Task SaveAsync(
        WritingSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directoryPath);
            string path = GetPath(session.SessionId);
            string temporaryPath = Path.Combine(
                _directoryPath,
                $".{session.SessionId:N}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        SessionDto.FromModel(session),
                        JsonOptions,
                        cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WritingSession?> LoadAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = GetPath(sessionId);
            if (!File.Exists(path))
            {
                return null;
            }

            await using FileStream stream = File.OpenRead(path);
            try
            {
                SessionDto? dto = await JsonSerializer.DeserializeAsync<SessionDto>(
                    stream,
                    JsonOptions,
                    cancellationToken);
                return dto?.ToModel();
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_directoryPath))
            {
                return;
            }

            foreach (string pattern in new[] { "*.json", ".*.tmp" })
            {
                foreach (string path in Directory.EnumerateFiles(
                    _directoryPath,
                    pattern,
                    SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(path);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string path = GetPath(sessionId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        return Path.Combine(_directoryPath, $"{sessionId:N}.json");
    }

    private sealed record SessionDto(
        Guid SessionId,
        string TaskId,
        DateTimeOffset StartedAt,
        IReadOnlyList<AttemptDto> Attempts,
        Handedness Handedness)
    {
        public static SessionDto FromModel(WritingSession session) =>
            new(
                session.SessionId,
                session.TaskId,
                session.StartedAt,
                session.Attempts.Select(AttemptDto.FromModel).ToArray(),
                session.Handedness);

        public WritingSession ToModel() =>
            new(
                SessionId,
                TaskId,
                StartedAt,
                Attempts.Select(attempt => attempt.ToModel()),
                Handedness);
    }

    private sealed record AttemptDto(
        int AttemptNo,
        IReadOnlyList<StrokeDto> Strokes,
        SubjectiveMarkDto? SubjectiveMark,
        SubjectiveResponseKind? SubjectiveResponse)
    {
        public static AttemptDto FromModel(WritingAttempt attempt) =>
            new(
                attempt.AttemptNo,
                attempt.Strokes
                    .Where(stroke => stroke.Points.Any(point => point.IsInContact))
                    .Select(StrokeDto.FromModel)
                    .ToArray(),
                attempt.SubjectiveMark is null
                    ? null
                    : SubjectiveMarkDto.FromModel(attempt.SubjectiveMark),
                attempt.SubjectiveResponse);

        public WritingAttempt ToModel() =>
            new(
                AttemptNo,
                Strokes.Select(stroke => stroke.ToModel()),
                SubjectiveMark?.ToModel(),
                subjectiveResponse: SubjectiveResponse);
    }

    private sealed record StrokeDto(
        int StrokeIndex,
        IReadOnlyList<PenPointSample> Points)
    {
        public static StrokeDto FromModel(Stroke stroke) =>
            new(stroke.StrokeIndex, stroke.Points.Where(point => point.IsInContact).ToArray());

        public Stroke ToModel() => new(StrokeIndex, Points);
    }

    private sealed record SubjectiveMarkDto(
        double CenterX,
        double CenterY,
        double RadiusPx,
        IReadOnlyList<SubjectiveLabel> Labels)
    {
        public static SubjectiveMarkDto FromModel(SubjectiveMark mark) =>
            new(mark.CenterX, mark.CenterY, mark.RadiusPx, mark.Labels);

        public SubjectiveMark ToModel() =>
            new(CenterX, CenterY, RadiusPx, Labels);
    }
}
