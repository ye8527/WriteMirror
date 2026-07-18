using WriteMirror.Core.Models;

namespace WriteMirror.Core.Feedback;

/// <summary>Always-available offline Japanese feedback with no model dependency.</summary>
public sealed class TemplateFeedbackService : IFeedbackGenerator
{
    public Task<FeedbackMessage> GenerateAsync(
        FeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<FeedbackMessage>(cancellationToken);
        }

        var message = new FeedbackMessage(
            "template",
            CreateObservation(request),
            CreateReflection(request),
            CreateNextQuestion(request),
            ["no_diagnosis", "no_causal_inference"]);
        return Task.FromResult(message);
    }

    private static string CreateObservation(FeedbackRequest request)
    {
        if (request.SubjectiveResponse == SubjectiveResponseKind.Skipped)
        {
            return "「答えない」という選択をそのまま受け取りました。";
        }

        if (request.SubjectiveResponse == SubjectiveResponseKind.None)
        {
            return "「特になし」という答えをそのまま受け取りました。";
        }

        if (request.SubjectiveResponse == SubjectiveResponseKind.WentWell)
        {
            return "「うまくいった」という答えをそのまま受け取りました。";
        }

        if (request.SecondAttemptDurationChangePercent is not null)
        {
            double change = request.SecondAttemptDurationChangePercent.Value;
            if (Math.Abs(change) < 5)
            {
                return "2回の書く時間に、はっきりしたちがいはありませんでした。";
            }

            string direction = change < 0 ? "短く" : "長く";
            return $"2回目の書く時間は、1回目より{Math.Abs(change):0.0}%{direction}、差が観測されました。これは点数ではなく、正確さ、読みやすさ、能力は評価していません。";
        }

        if (request.LongestPauseBeforeStrokeNumber is not null &&
            request.LongestPauseAfterStrokeNumber is not null)
        {
            return $"{request.LongestPauseBeforeStrokeNumber}画目を書き終えてから{request.LongestPauseAfterStrokeNumber}画目を書き始めるまでに、この試行のほかの画間と比べて少し長い間がありました。困った、まちがったという意味ではありません。";
        }

        return "今回の書き方を画面に出しました。これは点数ではありません。";
    }

    private static string CreateReflection(FeedbackRequest request)
    {
        if (request.SubjectiveResponse == SubjectiveResponseKind.Skipped)
        {
            return "場所や理由は推測せず、観測候補も自動で表示しません。";
        }

        if (request.SubjectiveResponse is SubjectiveResponseKind.None or SubjectiveResponseKind.WentWell)
        {
            return "本人の答えを優先し、観測候補は自動で表示しません。";
        }

        if (request.MatchedEventKind == ObjectiveEventKind.LongestPause)
        {
            return request.SubjectiveRelation switch
            {
                SpatialRelation.Inside or SpatialRelation.Near =>
                    "えらんだ場所の近くに、線を切り替える前後の間もありました。困った、まちがったという意味ではありません。",
                SpatialRelation.Separate =>
                    "えらんだ場所と、線を切り替える前後の間は別の場所でした。よい、わるいは決めません。",
                _ => "えらんだ場所を、今回のふり返りの目印として画面に出しました。"
            };
        }

        return request.SubjectiveRelation switch
        {
            SpatialRelation.Inside or SpatialRelation.Near =>
                "えらんだ場所の近くに、端末が観測した動きの候補もありました。困った、まちがったという意味ではありません。",
            SpatialRelation.Separate =>
                "えらんだ場所と、端末が観測した動きの候補は別の場所でした。よい、わるいは決めません。",
            _ => "えらんだ場所を、今回のふり返りの目印として画面に出しました。"
        };
    }

    private static string CreateNextQuestion(FeedbackRequest request)
    {
        if (request.SubjectiveResponse == SubjectiveResponseKind.Skipped)
        {
            return "このまま終わることができます。";
        }

        if (request.SubjectiveResponse is SubjectiveResponseKind.None or SubjectiveResponseKind.WentWell)
        {
            return "このまま終わるか、観測を見てみるかを選べます。";
        }

        if (request.HasComparableStrokeStructure == false)
        {
            return "次は、線をどこで区切るかを見てみますか？";
        }

        return "次は、線を切り替えるところを見てみますか？";
    }
}
