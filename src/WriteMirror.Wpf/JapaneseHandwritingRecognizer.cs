using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Ink;

namespace WriteMirror.Wpf;

internal sealed record HandwritingRecognition(
    string Text,
    IReadOnlyList<string> Candidates,
    string RecognizerName);

internal sealed class JapaneseHandwritingRecognizer
{
    public async Task<HandwritingRecognition> RecognizeAsync(StrokeCollection source)
    {
        string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "arm64"
            : "x64";
        string helperPath = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "Recognizer",
            architecture,
            "WriteMirror.Recognizer.exe");
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException("日本語手書き認識ヘルパーがありません", helperPath);
        }

        var input = new RecognizerInput(source
            .Select(stroke => stroke.StylusPoints
                .Select(point => new RecognizerPoint(
                    (float)point.X,
                    (float)point.Y,
                    point.PressureFactor))
                .ToList())
            .ToList());
        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("日本語手書き認識を開始できません");
        await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, input);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        string output;
        string error;
        try
        {
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            output = await outputTask;
            error = await errorTask;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            throw new TimeoutException("日本語文字候補の取得が12秒で終了しませんでした");
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "認識結果がありません" : error.Trim());
        }

        RecognizerOutput result = JsonSerializer.Deserialize<RecognizerOutput>(output)
            ?? throw new InvalidOperationException("認識結果を解析できません");
        return new HandwritingRecognition(result.Text, result.Candidates, result.RecognizerName);
    }

    private sealed record RecognizerInput(List<List<RecognizerPoint>> Strokes);
    private sealed record RecognizerPoint(float X, float Y, float Pressure);
    private sealed record RecognizerOutput(string Text, string[] Candidates, string RecognizerName);
}
