namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>How a lookup was answered; never silent about what is missing (principle 9).</summary>
public enum LookupStatus
{
    /// <summary>Fetched from the source now and sealed in the case.</summary>
    Fetched,

    /// <summary>
    /// Answered from the case without asking the source, because it cannot have changed (for example a transaction
    /// that is deep enough in the chain, or a block).
    /// </summary>
    FromCase,

    /// <summary>
    /// Answered from the case although it may have changed since, because the source could not be asked (no exit is
    /// connected, or the lookup failed); <see cref="BitcoinLookup{T}.Problem"/> says why.
    /// </summary>
    FromCaseMayBeOutdated,

    /// <summary>No exit is connected and the case has no answer; nothing was sent.</summary>
    Offline,

    /// <summary>The source answered that it does not exist (or that the request was invalid).</summary>
    NotFound,

    /// <summary>The lookup failed and the case has no answer; <see cref="BitcoinLookup{T}.Problem"/> says why.</summary>
    Failed,
}
