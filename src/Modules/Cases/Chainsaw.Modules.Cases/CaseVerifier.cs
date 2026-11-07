using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Cases;

/// <summary>Walks every sealed chain of an open case and reports where each one breaks.</summary>
/// <param name="clock">The clock, for when the verification ran.</param>
internal sealed class CaseVerifier(IClock clock) : ICaseVerifier
{
    /// <inheritdoc />
    public CaseVerification Verify(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        if (@case is not Case opened)
        {
            throw new ArgumentException("The case must come from this module's case store.", nameof(@case));
        }

        var (databaseIntact, chains) = opened.VerifyChains();
        return new CaseVerification(clock.UtcNow.ToUniversalTime(), databaseIntact, chains);
    }
}
