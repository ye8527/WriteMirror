namespace WriteMirror.Core.Models;

/// <summary>
/// A single canvas-coordinate sample captured from a pen pointer event.
/// Coordinates are expressed in canvas pixels and the timestamp is in microseconds.
/// </summary>
public sealed record PenPointSample(
    double X,
    double Y,
    long TimestampUs,
    float? Pressure,
    float? XTilt,
    float? YTilt,
    bool IsInContact);
