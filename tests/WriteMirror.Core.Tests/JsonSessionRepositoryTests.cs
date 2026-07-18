using WriteMirror.Core.Models;
using WriteMirror.Infrastructure.Storage;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class JsonSessionRepositoryTests
{
    [TestMethod]
    public async Task SaveLoadDelete_RoundTripsPrivacyMinimizedSession()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WriteMirror.Tests",
            Guid.NewGuid().ToString("N"));
        var repository = new JsonSessionRepository(directory);
        Guid sessionId = Guid.NewGuid();
        var point = new PenPointSample(10, 20, 1_000, 0.5f, 1, -1, true);
        var mark = new SubjectiveMark(
            10,
            20,
            12,
            new[] { SubjectiveLabel.Hesitation });
        var session = new WritingSession(
            sessionId,
            "kanji_ki",
            DateTimeOffset.Parse("2026-07-18T00:00:00+09:00"),
            new[]
            {
                new WritingAttempt(
                    1,
                    new[] { new Stroke(0, new[] { point }) },
                    mark,
                    subjectiveResponse: SubjectiveResponseKind.Hesitation)
            },
            Handedness.Left);

        try
        {
            await repository.SaveAsync(session);
            WritingSession? loaded = await repository.LoadAsync(sessionId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual("kanji_ki", loaded.TaskId);
            Assert.AreEqual(Handedness.Left, loaded.Handedness);
            Assert.AreEqual(1, loaded.Attempts.Count);
            Assert.AreEqual(point, loaded.Attempts[0].Strokes[0].Points[0]);
            Assert.AreEqual(SubjectiveLabel.Hesitation, loaded.Attempts[0].SubjectiveMark!.Labels[0]);
            Assert.AreEqual(SubjectiveResponseKind.Hesitation, loaded.Attempts[0].SubjectiveResponse);

            await repository.DeleteAsync(sessionId);
            Assert.IsNull(await repository.LoadAsync(sessionId));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Save_IsAtomicAndFiltersNonContactRelease()
    {
        string directory = NewDirectory();
        var repository = new JsonSessionRepository(directory);
        Guid id = Guid.NewGuid();
        var contact = new PenPointSample(1, 2, 100, 0.5f, null, null, true);
        var release = new PenPointSample(100, 200, 200, null, null, null, false);
        var session = Session(id, [contact, release]);

        try
        {
            await repository.SaveAsync(session);
            WritingSession? loaded = await repository.LoadAsync(id);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Attempts[0].Strokes[0].Points.Count);
            Assert.IsTrue(loaded.Attempts[0].Strokes[0].Points[^1].IsInContact);
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CorruptedJson_ReturnsUnavailableWithoutThrowing()
    {
        string directory = NewDirectory();
        Directory.CreateDirectory(directory);
        Guid id = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(directory, $"{id:N}.json"), "{broken");
        var repository = new JsonSessionRepository(directory);

        try
        {
            Assert.IsNull(await repository.LoadAsync(id));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DeleteAll_RemovesEverySessionAndTemporaryFile()
    {
        string directory = NewDirectory();
        var repository = new JsonSessionRepository(directory);
        try
        {
            await repository.SaveAsync(Session(Guid.NewGuid(), [Point()]));
            await repository.SaveAsync(Session(Guid.NewGuid(), [Point()]));
            await File.WriteAllTextAsync(Path.Combine(directory, ".orphan.tmp"), "partial");

            await repository.DeleteAllAsync();

            Assert.AreEqual(0, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewDirectory() => Path.Combine(
        Path.GetTempPath(),
        "WriteMirror.Tests",
        Guid.NewGuid().ToString("N"));

    private static PenPointSample Point() =>
        new(1, 2, 100, 0.5f, null, null, true);

    private static WritingSession Session(Guid id, IReadOnlyList<PenPointSample> points) =>
        new(
            id,
            "japanese:test",
            DateTimeOffset.Parse("2026-07-18T00:00:00+09:00"),
            [new WritingAttempt(1, [new Stroke(0, points)])],
            Handedness.Right);
}
