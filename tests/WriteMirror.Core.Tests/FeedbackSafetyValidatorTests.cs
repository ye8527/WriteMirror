using WriteMirror.Core.Feedback;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class FeedbackSafetyValidatorTests
{
    private readonly FeedbackSafetyValidator _validator = new();

    [DataTestMethod]
    [DataRow("改善しました")]
    [DataRow("能力が高い")]
    [DataRow("緊張しています")]
    [DataRow("疲れている")]
    [DataRow("努力が足りない")]
    [DataRow("握る力が強い")]
    [DataRow("性別は女の子です")]
    [DataRow("学年は3年生です")]
    public void InferenceOrEvaluationClaim_IsRejected(string claim)
    {
        var message = new FeedbackMessage(
            "test",
            claim,
            "えらんだ場所を画面に出しました。",
            "次も見てみますか？",
            []);

        Assert.IsFalse(_validator.IsValid(Request(), message));
    }

    [TestMethod]
    public void NeutralObservation_IsAccepted()
    {
        var message = new FeedbackMessage(
            "test",
            "1画目と2画目の間を端末が観測しました。",
            "えらんだ場所を画面に出しました。",
            "次も見てみますか？",
            []);

        Assert.IsTrue(_validator.IsValid(Request(), message));
    }

    [TestMethod]
    public void SkippedResponse_WithLocationOrNextTask_IsRejected()
    {
        FeedbackRequest request = Request() with
        {
            SubjectiveResponse = SubjectiveResponseKind.Skipped
        };
        var message = new FeedbackMessage(
            "test",
            "今回の記録を受け取りました。",
            "えらんだ場所を表示しました。",
            "次は線を見てみますか？",
            []);

        Assert.IsFalse(_validator.IsValid(request, message));
    }

    [TestMethod]
    public void SkippedResponse_WithoutInference_IsAccepted()
    {
        FeedbackRequest request = Request() with
        {
            SubjectiveResponse = SubjectiveResponseKind.Skipped,
            SubjectiveRelation = null,
            MatchedEventKind = null
        };
        var message = new FeedbackMessage(
            "test",
            "回答しない選択を受け取りました。",
            "場所や理由は推測しません。",
            "このまま終わることができます。",
            []);

        Assert.IsTrue(_validator.IsValid(request, message));
    }

    private static FeedbackRequest Request() =>
        new(
            "ja-JP",
            "木",
            1,
            2,
            810,
            SpatialRelation.Near,
            ObjectiveEventKind.LongestPause,
            null,
            true,
            PressureTrend.Unavailable,
            SubjectiveResponseKind.Hesitation);
}
