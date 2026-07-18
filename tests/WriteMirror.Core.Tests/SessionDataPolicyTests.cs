using WriteMirror.Core.Policy;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class SessionDataPolicyTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IndependentPractice_NeverPersists(bool hasExplicitSaveConsent)
    {
        Assert.IsFalse(SessionDataPolicy.CanPersist(
            UsageMode.IndependentPractice,
            hasExplicitSaveConsent));
    }

    [TestMethod]
    public void GuidedReview_WithoutExplicitConsent_DoesNotPersist()
    {
        Assert.IsFalse(SessionDataPolicy.CanPersist(UsageMode.GuidedReview, false));
    }

    [TestMethod]
    public void GuidedReview_WithExplicitConsent_CanPersist()
    {
        Assert.IsTrue(SessionDataPolicy.CanPersist(UsageMode.GuidedReview, true));
    }
}
