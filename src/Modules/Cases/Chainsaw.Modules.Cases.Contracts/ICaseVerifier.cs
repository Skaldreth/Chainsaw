namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// Checks that nothing in a case's sealed tables has been changed, removed or reordered (what <c>/verify</c> runs).
/// </summary>
public interface ICaseVerifier
{
    /// <summary>Goes through every sealed chain in the case, record by record, and says where a chain breaks.</summary>
    /// <param name="case">An open case from this module's <see cref="ICaseStore"/>.</param>
    /// <returns>One result per sealed chain.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Closed"/>.</exception>
    CaseVerification Verify(ICase @case);
}
