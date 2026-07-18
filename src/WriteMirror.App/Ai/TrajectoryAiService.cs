using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WriteMirror.Core.Models;

namespace WriteMirror.Ai;

internal sealed class TrajectoryAiService : IDisposable
{
    private const int PointCount = 128;
    private const int FeatureCount = 3;
    private const int InputSize = PointCount * FeatureCount;
    private readonly InferenceSession _session;

    private TrajectoryAiService(InferenceSession session, string executionProvider)
    {
        _session = session;
        ExecutionProvider = executionProvider;
    }

    public string ExecutionProvider { get; }

    public static TrajectoryAiService CreateNpu(string modelPath)
    {
        OrtEnv environment = OrtEnv.Instance();
        OrtEpDevice npu = environment.GetEpDevices()
            .First(device =>
                device.EpName == "QNNExecutionProvider" &&
                device.HardwareDevice.Type == OrtHardwareDeviceType.NPU);
        using var options = new SessionOptions();
        options.AppendExecutionProvider(
            environment,
            [npu],
            new Dictionary<string, string>());
        options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
        return new TrajectoryAiService(
            new InferenceSession(modelPath, options),
            "QNNExecutionProvider / Qualcomm Hexagon NPU");
    }

    public static TrajectoryAiService CreateCpu(string modelPath)
    {
        using var options = new SessionOptions();
        return new TrajectoryAiService(
            new InferenceSession(modelPath, options),
            "CPUExecutionProvider");
    }

    public TrajectoryAiResult Analyze(IReadOnlyList<Stroke> strokes)
    {
        float[] input = Resample(strokes);
        var tensor = new DenseTensor<float>(input, [1, InputSize]);
        NamedOnnxValue value = NamedOnnxValue.CreateFromTensor("trajectory", tensor);
        Stopwatch stopwatch = Stopwatch.StartNew();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> output =
            _session.Run([value]);
        stopwatch.Stop();
        float[] reconstruction = output
            .First(item => item.Name == "reconstruction")
            .AsEnumerable<float>()
            .ToArray();
        double squaredError = 0;
        for (int index = 0; index < input.Length; index++)
        {
            double difference = reconstruction[index] - input[index];
            squaredError += difference * difference;
        }

        return new TrajectoryAiResult(
            squaredError / input.Length,
            stopwatch.Elapsed.TotalMilliseconds,
            ExecutionProvider,
            ToPoints(input, input),
            ToPoints(reconstruction, input));
    }

    public void Dispose() => _session.Dispose();

    private static float[] Resample(IReadOnlyList<Stroke> strokes)
    {
        var points = new List<(float X, float Y, float StrokeEnd)>();
        foreach (Stroke stroke in strokes)
        {
            for (int index = 0; index < stroke.Points.Count; index++)
            {
                PenPointSample point = stroke.Points[index];
                points.Add(((float)point.X, (float)point.Y, index == stroke.Points.Count - 1 ? 1f : 0f));
            }
        }

        if (points.Count == 0)
        {
            throw new ArgumentException("At least one stroke point is required.", nameof(strokes));
        }

        float minimumX = points.Min(point => point.X);
        float maximumX = points.Max(point => point.X);
        float minimumY = points.Min(point => point.Y);
        float maximumY = points.Max(point => point.Y);
        float width = Math.Max(1f, maximumX - minimumX);
        float height = Math.Max(1f, maximumY - minimumY);
        var result = new float[InputSize];
        for (int targetIndex = 0; targetIndex < PointCount; targetIndex++)
        {
            double sourcePosition = points.Count == 1
                ? 0
                : targetIndex * (points.Count - 1d) / (PointCount - 1d);
            int left = (int)Math.Floor(sourcePosition);
            int right = Math.Min(points.Count - 1, left + 1);
            float fraction = (float)(sourcePosition - left);
            int offset = targetIndex * FeatureCount;
            result[offset] = Lerp(
                (points[left].X - minimumX) / width,
                (points[right].X - minimumX) / width,
                fraction);
            result[offset + 1] = Lerp(
                (points[left].Y - minimumY) / height,
                (points[right].Y - minimumY) / height,
                fraction);
            int nearest = (int)Math.Round(sourcePosition);
            result[offset + 2] = points[nearest].StrokeEnd;
        }

        return result;
    }

    private static float Lerp(float first, float second, float amount) =>
        first + (second - first) * amount;

    private static IReadOnlyList<TrajectoryAiPoint> ToPoints(
        IReadOnlyList<float> coordinates,
        IReadOnlyList<float> strokeBoundaries)
    {
        var points = new TrajectoryAiPoint[PointCount];
        for (int index = 0; index < PointCount; index++)
        {
            int offset = index * FeatureCount;
            points[index] = new TrajectoryAiPoint(
                Math.Clamp(coordinates[offset], 0f, 1f),
                Math.Clamp(coordinates[offset + 1], 0f, 1f),
                strokeBoundaries[offset + 2] >= 0.5f);
        }

        return points;
    }
}

internal sealed record TrajectoryAiResult(
    double ReconstructionDifference,
    double InferenceMilliseconds,
    string ExecutionProvider,
    IReadOnlyList<TrajectoryAiPoint> OriginalPoints,
    IReadOnlyList<TrajectoryAiPoint> ReconstructedPoints);

internal sealed record TrajectoryAiPoint(
    double X,
    double Y,
    bool EndsStroke);
