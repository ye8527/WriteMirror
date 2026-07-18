namespace WriteMirror.Core.Policy;

/// <summary>
/// Controls whether a practice session is allowed to leave the process as stored data.
/// </summary>
public enum UsageMode
{
    IndependentPractice,
    GuidedReview
}

public static class SessionDataPolicy
{
    /// <summary>
    /// Independent practice never persists data. Guided review requires a separate,
    /// explicit opt-in for every session.
    /// </summary>
    public static bool CanPersist(UsageMode mode, bool hasExplicitSaveConsent) =>
        mode == UsageMode.GuidedReview && hasExplicitSaveConsent;
}
