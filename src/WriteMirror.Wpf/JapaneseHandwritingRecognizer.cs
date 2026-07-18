using System.Numerics;
using System.Windows.Ink;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace WriteMirror.Wpf;

internal sealed record HandwritingRecognition(
    string Text,
    IReadOnlyList<string> Candidates,
    string RecognizerName);

internal sealed class JapaneseHandwritingRecognizer
{
    private readonly InkRecognizerContainer _recognizer = new();
    private readonly InkRecognizer _japanese;

    public JapaneseHandwritingRecognizer()
    {
        _japanese = _recognizer.GetRecognizers().FirstOrDefault(candidate =>
            candidate.Name.Contains("日本", StringComparison.OrdinalIgnoreCase) ||
            candidate.Name.Contains("Japanese", StringComparison.OrdinalIgnoreCase) ||
            candidate.Name.Contains("ja-", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Windowsの日本語手書き認識を利用できません");
        _recognizer.SetDefaultRecognizer(_japanese);
    }

    public async Task<HandwritingRecognition> RecognizeAsync(StrokeCollection source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0)
        {
            return new HandwritingRecognition(string.Empty, [], _japanese.Name);
        }

        var container = new InkStrokeContainer();
        var builder = new InkStrokeBuilder();
        foreach (System.Windows.Ink.Stroke sourceStroke in source)
        {
            InkPoint[] points = sourceStroke.StylusPoints
                .Select(point => new InkPoint(
                    new Point(point.X, point.Y),
                    Math.Clamp(point.PressureFactor, 0f, 1f)))
                .ToArray();
            if (points.Length > 0)
            {
                container.AddStroke(builder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity));
            }
        }

        IReadOnlyList<InkRecognitionResult> results = await _recognizer.RecognizeAsync(
            container,
            InkRecognitionTarget.All);
        string text = string.Concat(results.Select(result =>
            result.GetTextCandidates().FirstOrDefault() ?? string.Empty));
        string[] candidates = results
            .SelectMany(result => result.GetTextCandidates().Take(5))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct()
            .Take(10)
            .ToArray();
        return new HandwritingRecognition(text, candidates, _japanese.Name);
    }
}
