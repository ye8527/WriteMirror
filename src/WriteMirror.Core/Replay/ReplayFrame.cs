using WriteMirror.Core.Models;

namespace WriteMirror.Core.Replay;

/// <summary>
/// One point in a replay timeline. Delay is measured from the preceding frame.
/// </summary>
public sealed record ReplayFrame(
    PenPointSample Point,
    long DelayUs,
    bool StartsStroke);
