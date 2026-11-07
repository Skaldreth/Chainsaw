namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What an endpoint answered when it was tried.</summary>
public enum ProbeOutcome
{
    /// <summary>It answered with the chain's height, as high as the highest any endpoint answered.</summary>
    Answered,

    /// <summary>It answered, but with a lower height than another endpoint: it lags behind and is not used.</summary>
    Behind,

    /// <summary>It answered wrongly, with an error or not in time.</summary>
    Failed,

    /// <summary>
    /// Nothing was sent: the exit does not allow lookups now, the vault is locked or the key is missing.
    /// </summary>
    NotSent,
}

/// <summary>
/// An endpoint tried by asking only for the chain's height (and, in the form that adds one, the hash of the block at
/// that height). Nothing about a case is asked, and nothing is sealed in one.
/// </summary>
/// <param name="EndpointId">The endpoint tried, or <see cref="Guid.Empty"/> for an address not yet added.</param>
/// <param name="Outcome">What it answered.</param>
/// <param name="Height">The height it answered, or <see langword="null"/>.</param>
/// <param name="BlockHash">The hash of the block at that height when it was asked for, or <see langword="null"/>.</param>
/// <param name="Duration">How long the height took to come, or <see langword="null"/> when it did not.</param>
/// <param name="At">When it was tried, in UTC.</param>
/// <param name="Problem">Why it failed or was not sent, in Swedish, or <see langword="null"/>.</param>
public sealed record EndpointProbe(
    Guid EndpointId,
    ProbeOutcome Outcome,
    long? Height,
    string? BlockHash,
    TimeSpan? Duration,
    DateTimeOffset At,
    string? Problem);
