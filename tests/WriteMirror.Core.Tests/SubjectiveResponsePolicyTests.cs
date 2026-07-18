using WriteMirror.Core.Models;

namespace WriteMirror.Core.Tests;

[TestClass]
public sealed class SubjectiveResponsePolicyTests
{
    [DataTestMethod]
    [DataRow(SubjectiveResponseKind.None)]
    [DataRow(SubjectiveResponseKind.WentWell)]
    [DataRow(SubjectiveResponseKind.Skipped)]
    public void NonProblemOrSkippedResponse_NeverRequiresLocationOrAutoCandidates(
        SubjectiveResponseKind response)
    {
        Assert.IsFalse(SubjectiveResponsePolicy.RequiresLocation(response));
        Assert.IsFalse(SubjectiveResponsePolicy.ShowsObservationCandidatesAutomatically(response));
    }

    [DataTestMethod]
    [DataRow(SubjectiveResponseKind.Hesitation)]
    [DataRow(SubjectiveResponseKind.Difficult)]
    [DataRow(SubjectiveResponseKind.Dissatisfied)]
    public void NegativeReflection_RequiresLocationAndMayShowCandidates(
        SubjectiveResponseKind response)
    {
        Assert.IsTrue(SubjectiveResponsePolicy.RequiresLocation(response));
        Assert.IsTrue(SubjectiveResponsePolicy.ShowsObservationCandidatesAutomatically(response));
    }
}
