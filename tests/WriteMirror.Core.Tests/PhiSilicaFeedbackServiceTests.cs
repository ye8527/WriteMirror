using WriteMirror.Core.Feedback;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class PhiSilicaFeedbackServiceTests
{
    [TestMethod]
    public async Task UnavailableModel_UsesTemplateFallback()
    {
        var service = Service(new FakeModel(false, string.Empty));

        FeedbackMessage message = await service.GenerateAsync(Request());

        Assert.AreEqual("template", message.Generator);
    }

    [TestMethod]
    public async Task ValidJson_UsesPhiSilicaResult()
    {
        const string json = """
            {"observation":"第1画から第2画の間に停止が見られました。","reflection":"選んだ場所と近い位置でした。","nextQuestion":"次は切り替えを観察してみましょう。","safetyFlags":[]}
            """;
        var service = Service(new FakeModel(true, json));

        FeedbackMessage message = await service.GenerateAsync(Request());

        Assert.AreEqual("phi-silica", message.Generator);
    }

    [TestMethod]
    public async Task MalformedJson_UsesTemplateFallback()
    {
        var service = Service(new FakeModel(true, "not-json"));

        FeedbackMessage message = await service.GenerateAsync(Request());

        Assert.AreEqual("template", message.Generator);
    }

    [TestMethod]
    public async Task ForbiddenTerm_UsesTemplateFallback()
    {
        const string json = """
            {"observation":"異常があります。","reflection":"近い位置でした。","nextQuestion":"次も観察しましょう。","safetyFlags":[]}
            """;
        var service = Service(new FakeModel(true, json));

        FeedbackMessage message = await service.GenerateAsync(Request());

        Assert.AreEqual("template", message.Generator);
    }

    [TestMethod]
    public async Task NumberAbsentFromFacts_UsesTemplateFallback()
    {
        const string json = """
            {"observation":"停止は99msでした。","reflection":"近い位置でした。","nextQuestion":"次も観察しましょう。","safetyFlags":[]}
            """;
        var service = Service(new FakeModel(true, json));

        FeedbackMessage message = await service.GenerateAsync(Request());

        Assert.AreEqual("template", message.Generator);
    }

    [TestMethod]
    public async Task MultipleSentencesInField_UsesTemplateFallback()
    {
        const string json = """
            {"observation":"停止が見られました。別の文です。","reflection":"近い位置でした。","nextQuestion":"次も観察しましょう。","safetyFlags":[]}
            """;
        var service = Service(new FakeModel(true, json));

        FeedbackMessage message = await service.GenerateAsync(Request());

        Assert.AreEqual("template", message.Generator);
    }

    [TestMethod]
    public async Task Timeout_UsesTemplateFallback()
    {
        var service = new PhiSilicaFeedbackService(
            new SlowModel(),
            new TemplateFeedbackService(),
            timeout: TimeSpan.FromMilliseconds(20));

        FeedbackMessage message = await service.GenerateAsync(Request());

        Assert.AreEqual("template", message.Generator);
    }

    private static PhiSilicaFeedbackService Service(ILocalLanguageModel model) =>
        new(model, new TemplateFeedbackService());

    private static FeedbackRequest Request() =>
        new(
            "ja-JP",
            "木",
            1,
            2,
            810,
            SpatialRelation.Near,
            ObjectiveEventKind.LongestPause,
            -12.3,
            true,
            PressureTrend.NoClearChange,
            SubjectiveResponseKind.Hesitation);

    private sealed class FakeModel(bool available, string output) : ILocalLanguageModel
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(available);

        public Task<string> GenerateJsonAsync(
            string prompt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(output);
    }

    private sealed class SlowModel : ILocalLanguageModel
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public async Task<string> GenerateJsonAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return string.Empty;
        }
    }
}
