using WriteMirror.Core.Models;

namespace WriteMirror.Core.Analysis;

/// <summary>
/// Calculates time in microseconds, distance in canvas pixels, speed in pixels per
/// second, and population pressure statistics. Zero-duration segments contribute
/// to path length but are excluded from speed to prevent division by zero. A long
/// inter-stroke pause is greater than median + 1.5 × MAD and at least 250 ms; this
/// is a within-attempt visualization rule, not a clinical threshold.
/// </summary>
public sealed class WritingAnalyzer : IWritingAnalyzer
{
    private const long MinimumLongPauseUs = 250_000;
    private const double MadMultiplier = 1.5;

    public WritingMetrics Analyze(WritingAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        Stroke[] contactStrokes = attempt.Strokes
            .Select(stroke => new
            {
                stroke.StrokeIndex,
                Points = stroke.Points.Where(point => point.IsInContact).ToArray()
            })
            .Where(stroke => stroke.Points.Length > 0)
            .Select(stroke => new Stroke(stroke.StrokeIndex, stroke.Points))
            .ToArray();

        if (contactStrokes.Length == 0)
        {
            return new WritingMetrics(0, [], [], 0, null, null, null, null);
        }

        var strokeMetrics = new List<StrokeMetrics>(contactStrokes.Length);
        var pauses = new List<PauseMetrics>(Math.Max(0, contactStrokes.Length - 1));
        var allPressureValues = new List<double>();
        double totalPathLength = 0;
        double timedPathLength = 0;
        long totalMotionDurationUs = 0;
        Stroke? previousStroke = null;

        foreach (Stroke stroke in contactStrokes)
        {
            ValidateStroke(stroke, previousStroke);

            StrokeMeasurements measurements = MeasureStroke(stroke);
            strokeMetrics.Add(new StrokeMetrics(
                stroke.StrokeIndex,
                stroke.EndTimestampUs - stroke.StartTimestampUs,
                measurements.PathLengthPx,
                CalculateSpeed(measurements.TimedPathLengthPx, measurements.MotionDurationUs),
                CalculatePressure(measurements.PressureValues),
                measurements.Movements));

            totalPathLength += measurements.PathLengthPx;
            timedPathLength += measurements.TimedPathLengthPx;
            totalMotionDurationUs += measurements.MotionDurationUs;
            allPressureValues.AddRange(measurements.PressureValues);

            if (previousStroke is not null)
            {
                pauses.Add(new PauseMetrics(
                    previousStroke.StrokeIndex,
                    stroke.StrokeIndex,
                    stroke.StartTimestampUs - previousStroke.EndTimestampUs,
                    previousStroke.Points[^1],
                    stroke.Points[0],
                    false));
            }

            previousStroke = stroke;
        }

        long? longPauseThresholdUs = CalculateLongPauseThreshold(pauses);
        if (longPauseThresholdUs is not null)
        {
            double relativeThreshold = CalculateRelativePauseThreshold(pauses);
            for (int index = 0; index < pauses.Count; index++)
            {
                PauseMetrics pause = pauses[index];
                pauses[index] = pause with
                {
                    IsLongPause = pause.DurationUs >= MinimumLongPauseUs &&
                        pause.DurationUs > relativeThreshold
                };
            }
        }

        return new WritingMetrics(
            contactStrokes[^1].EndTimestampUs - contactStrokes[0].StartTimestampUs,
            strokeMetrics,
            pauses,
            totalPathLength,
            CalculateSpeed(timedPathLength, totalMotionDurationUs),
            CalculatePressure(allPressureValues),
            pauses.Count == 0 ? null : pauses.Max(pause => pause.DurationUs),
            longPauseThresholdUs);
    }

    private static StrokeMeasurements MeasureStroke(Stroke stroke)
    {
        double pathLength = 0;
        double timedPathLength = 0;
        long motionDurationUs = 0;
        var pressureValues = new List<double>();
        var movements = new List<MovementMetrics>(Math.Max(0, stroke.Points.Count - 1));

        for (int index = 0; index < stroke.Points.Count; index++)
        {
            PenPointSample point = stroke.Points[index];
            ValidatePoint(point);
            if (point.IsInContact && point.Pressure is not null)
            {
                pressureValues.Add(point.Pressure.Value);
            }

            if (index == 0)
            {
                continue;
            }

            PenPointSample previous = stroke.Points[index - 1];
            double distance = Math.Sqrt(
                Math.Pow(point.X - previous.X, 2) +
                Math.Pow(point.Y - previous.Y, 2));
            long durationUs = point.TimestampUs - previous.TimestampUs;
            pathLength += distance;
            movements.Add(new MovementMetrics(
                previous,
                point,
                durationUs,
                distance,
                CalculateSpeed(distance, durationUs)));

            if (durationUs > 0)
            {
                timedPathLength += distance;
                motionDurationUs += durationUs;
            }
        }

        return new StrokeMeasurements(
            pathLength,
            timedPathLength,
            motionDurationUs,
            pressureValues,
            movements);
    }

    private static void ValidateStroke(Stroke stroke, Stroke? previousStroke)
    {
        for (int index = 1; index < stroke.Points.Count; index++)
        {
            if (stroke.Points[index].TimestampUs < stroke.Points[index - 1].TimestampUs)
            {
                throw new ArgumentException("Point timestamps must be chronological.", nameof(stroke));
            }
        }

        if (previousStroke is not null &&
            stroke.StartTimestampUs < previousStroke.EndTimestampUs)
        {
            throw new ArgumentException("Strokes must be chronological.", nameof(stroke));
        }
    }

    private static void ValidatePoint(PenPointSample point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentException("Point coordinates must be finite.", nameof(point));
        }

        if (point.Pressure is not null &&
            (!float.IsFinite(point.Pressure.Value) ||
             point.Pressure.Value < 0 ||
             point.Pressure.Value > 1))
        {
            throw new ArgumentException("Pressure must be between zero and one.", nameof(point));
        }
    }

    private static double? CalculateSpeed(double distancePx, long durationUs) =>
        durationUs == 0 ? null : distancePx * 1_000_000d / durationUs;

    private static PressureMetrics? CalculatePressure(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        double mean = values.Average();
        double variance = values.Average(value => Math.Pow(value - mean, 2));
        double standardDeviation = Math.Sqrt(variance);
        return new PressureMetrics(
            values.Count,
            mean,
            standardDeviation,
            mean == 0 ? null : standardDeviation / mean);
    }

    private static long? CalculateLongPauseThreshold(IReadOnlyList<PauseMetrics> pauses)
    {
        if (pauses.Count == 0)
        {
            return null;
        }

        return checked((long)Math.Ceiling(Math.Max(
            MinimumLongPauseUs,
            CalculateRelativePauseThreshold(pauses))));
    }

    private static double CalculateRelativePauseThreshold(IReadOnlyList<PauseMetrics> pauses)
    {
        double median = Median(pauses.Select(pause => (double)pause.DurationUs));
        double mad = Median(pauses.Select(pause => Math.Abs(pause.DurationUs - median)));
        return median + MadMultiplier * mad;
    }

    private static double Median(IEnumerable<double> source)
    {
        double[] values = source.Order().ToArray();
        int middle = values.Length / 2;
        return values.Length % 2 == 1
            ? values[middle]
            : values[middle - 1] / 2 + values[middle] / 2;
    }

    private sealed record StrokeMeasurements(
        double PathLengthPx,
        double TimedPathLengthPx,
        long MotionDurationUs,
        IReadOnlyList<double> PressureValues,
        IReadOnlyList<MovementMetrics> Movements);
}
