using System.Numerics;
using System.Text.Json;
using Windows.Foundation;
using Windows.UI.Input.Inking;

RecognizerInput? input = await JsonSerializer.DeserializeAsync<RecognizerInput>(Console.OpenStandardInput());
if (input is null || input.Strokes.Count == 0)
{
    await WriteResultAsync(new RecognizerOutput(string.Empty, [], "筆跡なし"));
    return;
}

var recognizer = new InkRecognizerContainer();
InkRecognizer? japanese = recognizer.GetRecognizers().FirstOrDefault(candidate =>
    candidate.Name.Contains("日本", StringComparison.OrdinalIgnoreCase) ||
    candidate.Name.Contains("Japanese", StringComparison.OrdinalIgnoreCase) ||
    candidate.Name.Contains("ja-", StringComparison.OrdinalIgnoreCase));
if (japanese is not null)
{
    recognizer.SetDefaultRecognizer(japanese);
}
else
{
    await WriteResultAsync(new RecognizerOutput(
        string.Empty,
        [],
        "日本語手書き認識を利用できません"));
    return;
}

var container = new InkStrokeContainer();
var builder = new InkStrokeBuilder();
foreach (List<RecognizerPoint> sourceStroke in input.Strokes)
{
    InkPoint[] points = sourceStroke
        .Select(point => new InkPoint(
            new Point(point.X, point.Y),
            Math.Clamp(point.Pressure, 0f, 1f)))
        .ToArray();
    if (points.Length > 0)
    {
        container.AddStroke(builder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity));
    }
}

IReadOnlyList<InkRecognitionResult> results = await recognizer.RecognizeAsync(
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
await WriteResultAsync(new RecognizerOutput(
    text,
    candidates,
    japanese.Name));

static async Task WriteResultAsync(RecognizerOutput result)
{
    await JsonSerializer.SerializeAsync(Console.OpenStandardOutput(), result);
    await Console.Out.FlushAsync();
}

internal sealed record RecognizerInput(List<List<RecognizerPoint>> Strokes);
internal sealed record RecognizerPoint(float X, float Y, float Pressure);
internal sealed record RecognizerOutput(string Text, string[] Candidates, string RecognizerName);
