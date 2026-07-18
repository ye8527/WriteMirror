using WriteMirror.Core.Models;

namespace WriteMirror.Core.Comparison;

/// <summary>Compares two writing attempts without assigning a score.</summary>
public interface IAttemptComparer
{
    AttemptComparison Compare(WritingAttempt first, WritingAttempt second);
}
