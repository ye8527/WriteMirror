using System.Collections.ObjectModel;

namespace WriteMirror.Core.Models;

/// <summary>Pressure statistics over available in-contact samples.</summary>
public sealed record PressureMetrics(
    int SampleCount,
    double Mean,
    double PopulationStandardDeviation,
    double? CoefficientOfVariation);

/// <summary>Measurements for one movement between adjacent point samples.</summary>
public sealed record MovementMetrics(
    PenPointSample FromPoint,
    PenPointSample ToPoint,
    long DurationUs,
    double DistancePx,
    double? SpeedPxPerSecond);

/// <summary>Measurements for one pen-down to pen-up stroke.</summary>
public sealed record StrokeMetrics
{
    public StrokeMetrics(
        int strokeIndex,
        long durationUs,
        double pathLengthPx,
        double? meanSpeedPxPerSecond,
        PressureMetrics? pressure,
        IEnumerable<MovementMetrics> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);
        StrokeIndex = strokeIndex;
        DurationUs = durationUs;
        PathLengthPx = pathLengthPx;
        MeanSpeedPxPerSecond = meanSpeedPxPerSecond;
        Pressure = pressure;
        Movements = new ReadOnlyCollection<MovementMetrics>(movements.ToArray());
    }

    public int StrokeIndex { get; }

    public long DurationUs { get; }

    public double PathLengthPx { get; }

    public double? MeanSpeedPxPerSecond { get; }

    public PressureMetrics? Pressure { get; }

    public IReadOnlyList<MovementMetrics> Movements { get; }
}

/// <summary>A pen-up interval between two consecutive strokes.</summary>
public sealed record PauseMetrics(
    int BeforeStrokeIndex,
    int AfterStrokeIndex,
    long DurationUs,
    PenPointSample BeforePoint,
    PenPointSample AfterPoint,
    bool IsLongPause);

/// <summary>
/// Aggregate measurements for one writing attempt. Missing sensor values remain
/// unavailable rather than being replaced with synthetic values.
/// </summary>
public sealed record WritingMetrics
{
    public WritingMetrics(
        long totalDurationUs,
        IEnumerable<StrokeMetrics> strokes,
        IEnumerable<PauseMetrics> pauses,
        double totalPathLengthPx,
        double? meanSpeedPxPerSecond,
        PressureMetrics? pressure,
        long? longestPauseUs,
        long? longPauseThresholdUs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalDurationUs);
        ArgumentOutOfRangeException.ThrowIfNegative(totalPathLengthPx);
        ArgumentNullException.ThrowIfNull(strokes);
        ArgumentNullException.ThrowIfNull(pauses);

        TotalDurationUs = totalDurationUs;
        Strokes = new ReadOnlyCollection<StrokeMetrics>(strokes.ToArray());
        Pauses = new ReadOnlyCollection<PauseMetrics>(pauses.ToArray());
        TotalPathLengthPx = totalPathLengthPx;
        MeanSpeedPxPerSecond = meanSpeedPxPerSecond;
        Pressure = pressure;
        LongestPauseUs = longestPauseUs;
        LongPauseThresholdUs = longPauseThresholdUs;
    }

    public long TotalDurationUs { get; }

    public IReadOnlyList<StrokeMetrics> Strokes { get; }

    public IReadOnlyList<PauseMetrics> Pauses { get; }

    public double TotalPathLengthPx { get; }

    public double? MeanSpeedPxPerSecond { get; }

    public PressureMetrics? Pressure { get; }

    public long? LongestPauseUs { get; }

    public long? LongPauseThresholdUs { get; }
}
