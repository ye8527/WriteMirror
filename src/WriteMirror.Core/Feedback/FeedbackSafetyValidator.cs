using System.Globalization;
using System.Text.RegularExpressions;
using WriteMirror.Core.Models;

namespace WriteMirror.Core.Feedback;

/// <summary>Rejects diagnostic language, excess text, and numbers absent from input facts.</summary>
public sealed partial class FeedbackSafetyValidator
{
    private const int MaximumFieldLength = 160;
    private static readonly string[] ForbiddenTerms =
    [
        "障害", "診断", "正常", "異常", "発達遅滞", "リスク", "治療",
        "改善しました", "改善した", "悪化しました", "悪化した",
        "能力が高い", "能力が低い", "能力が上がった", "能力が下がった",
        "緊張しています", "緊張している", "疲れています", "疲れている",
        "努力していない", "努力が足りない", "握る力が強い", "握る力が弱い",
        "性別は", "学年は", "成績が"
    ];

    public bool IsValid(FeedbackRequest request, FeedbackMessage message)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(message);

        string[] fields = [message.Observation, message.Reflection, message.NextQuestion];
        if (fields.Any(field =>
            string.IsNullOrWhiteSpace(field) ||
            field.Length > MaximumFieldLength ||
            field.Contains('\n') ||
            field.Contains('\r') ||
            field.Count(character => "。！？!?".Contains(character)) > 1))
        {
            return false;
        }

        string combined = string.Join(" ", fields);
        if (ForbiddenTerms.Any(term =>
            combined.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!SubjectiveResponsePolicy.RequiresLocation(request.SubjectiveResponse) &&
            combined.Contains("えらんだ場所", StringComparison.Ordinal))
        {
            return false;
        }

        if (request.SubjectiveResponse == SubjectiveResponseKind.Skipped &&
            combined.Contains("次は", StringComparison.Ordinal))
        {
            return false;
        }

        double[] allowedNumbers = AllowedNumbers(request).ToArray();
        foreach (Match match in NumberPattern().Matches(combined))
        {
            if (!double.TryParse(
                match.Value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double value) ||
                !allowedNumbers.Any(allowed => IsEquivalentNumber(value, allowed)))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<double> AllowedNumbers(FeedbackRequest request)
    {
        yield return 1;
        yield return 2;
        if (request.LongestPauseBeforeStrokeNumber is not null)
        {
            yield return request.LongestPauseBeforeStrokeNumber.Value;
        }

        if (request.LongestPauseAfterStrokeNumber is not null)
        {
            yield return request.LongestPauseAfterStrokeNumber.Value;
        }

        if (request.LongestPauseMs is not null)
        {
            yield return request.LongestPauseMs.Value;
        }

        if (request.SecondAttemptDurationChangePercent is not null)
        {
            yield return Math.Abs(request.SecondAttemptDurationChangePercent.Value);
        }
    }

    private static bool IsEquivalentNumber(double value, double allowed) =>
        Math.Abs(Math.Abs(value) - Math.Abs(allowed)) <= Math.Max(0.051, Math.Abs(allowed) * 0.000_1);

    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
