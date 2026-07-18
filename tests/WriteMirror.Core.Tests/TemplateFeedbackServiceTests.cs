using WriteMirror.Core.Feedback;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class TemplateFeedbackServiceTests
{
    private readonly TemplateFeedbackService _service = new();

    [TestMethod]
    public async Task FirstAttempt_DescribesPauseAndSpatialRelation()
    {
        FeedbackMessage message = await _service.GenerateAsync(Request(
            beforeStroke: 3,
            afterStroke: 4,
            relation: SpatialRelation.Near));

        StringAssert.Contains(message.Observation, "3画目を書き終えてから4画目を書き始めるまで");
        StringAssert.Contains(message.Reflection, "えらんだ場所の近く");
        StringAssert.Contains(message.Reflection, "線を切り替える前後の間");
        Assert.IsFalse(message.Reflection.Contains("ゆっくりになった", StringComparison.Ordinal));
        Assert.AreEqual("template", message.Generator);
    }

    [TestMethod]
    public async Task SecondAttempt_DescribesChangeWithoutClaimingAbility()
    {
        FeedbackMessage message = await _service.GenerateAsync(Request(
            durationChangePercent: -12.25));

        StringAssert.Contains(message.Observation, "12.3%短く");
        StringAssert.Contains(message.Observation, "能力は評価していません");
        Assert.IsFalse(message.Observation.Contains("改善", StringComparison.Ordinal));
        Assert.IsFalse(message.Observation.Contains("上達", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow(0.1)]
    [DataRow(1.0)]
    [DataRow(4.9)]
    public async Task SmallDifference_IsReportedAsNoClearDifference(double percent)
    {
        FeedbackMessage message = await _service.GenerateAsync(Request(
            durationChangePercent: percent));

        StringAssert.Contains(message.Observation, "はっきりしたちがいはありませんでした");
        Assert.IsFalse(message.Observation.Contains("改善", StringComparison.Ordinal));
        Assert.IsFalse(message.Observation.Contains("上達", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TenPercentDifference_DoesNotJudgeQuality()
    {
        FeedbackMessage message = await _service.GenerateAsync(Request(
            durationChangePercent: 10));

        StringAssert.Contains(message.Observation, "差が観測されました");
        StringAssert.Contains(message.Observation, "正確さ、読みやすさ、能力は評価していません");
    }

    [TestMethod]
    public async Task MissingEvents_UsesAvailableFallbackText()
    {
        FeedbackMessage message = await _service.GenerateAsync(Request());

        Assert.IsFalse(string.IsNullOrWhiteSpace(message.Observation));
        Assert.IsFalse(string.IsNullOrWhiteSpace(message.Reflection));
        Assert.IsFalse(string.IsNullOrWhiteSpace(message.NextQuestion));
    }

    [DataTestMethod]
    [DataRow(SubjectiveResponseKind.None, "特になし")]
    [DataRow(SubjectiveResponseKind.WentWell, "うまくいった")]
    public async Task NonProblemResponse_IsRespectedWithoutAutomaticObservation(
        SubjectiveResponseKind response,
        string expectedAnswer)
    {
        FeedbackMessage message = await _service.GenerateAsync(Request(
            beforeStroke: 1,
            afterStroke: 2,
            relation: SpatialRelation.Near,
            response: response));

        StringAssert.Contains(message.Observation, expectedAnswer);
        StringAssert.Contains(message.Reflection, "自動で表示しません");
        StringAssert.Contains(message.NextQuestion, "このまま終わる");
        Assert.IsFalse(message.Reflection.Contains("えらんだ場所", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SkippedResponse_DoesNotInferLocationReasonOrNextTask()
    {
        FeedbackMessage message = await _service.GenerateAsync(Request(
            beforeStroke: 1,
            afterStroke: 2,
            relation: SpatialRelation.Near,
            response: SubjectiveResponseKind.Skipped));
        string output = string.Join(" ", message.Observation, message.Reflection, message.NextQuestion);

        StringAssert.Contains(message.Observation, "答えない");
        StringAssert.Contains(message.Reflection, "場所や理由は推測せず");
        Assert.AreEqual("このまま終わることができます。", message.NextQuestion);
        Assert.IsFalse(output.Contains("えらんだ場所", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("次は", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PressureChange_DoesNotInferPenForce()
    {
        FeedbackRequest request = Request() with { PressureTrend = PressureTrend.Increased };

        FeedbackMessage message = await _service.GenerateAsync(request);

        Assert.AreEqual("次は、線を切り替えるところを見てみますか？", message.NextQuestion);
        Assert.IsFalse(message.NextQuestion.Contains("ペンの力", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Cancellation_IsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => _service.GenerateAsync(Request(), cancellation.Token));
    }

    [TestMethod]
    public async Task Output_DoesNotContainForbiddenDiagnosticTerms()
    {
        FeedbackMessage message = await _service.GenerateAsync(Request(
            relation: SpatialRelation.Separate,
            durationChangePercent: 10));
        string output = string.Join(
            " ",
            message.Observation,
            message.Reflection,
            message.NextQuestion);

        foreach (string term in new[] { "障害", "診断", "正常", "異常", "能力が低い", "発達遅滞" })
        {
            Assert.IsFalse(output.Contains(term, StringComparison.Ordinal));
        }
    }

    private static FeedbackRequest Request(
        int? beforeStroke = null,
        int? afterStroke = null,
        SpatialRelation? relation = null,
        double? durationChangePercent = null,
        SubjectiveResponseKind response = SubjectiveResponseKind.Hesitation) =>
        new(
            "ja-JP",
            "木",
            beforeStroke,
            afterStroke,
            beforeStroke is null ? null : 810,
            relation,
            ObjectiveEventKind.LongestPause,
            durationChangePercent,
            true,
            PressureTrend.Unavailable,
            response);
}
