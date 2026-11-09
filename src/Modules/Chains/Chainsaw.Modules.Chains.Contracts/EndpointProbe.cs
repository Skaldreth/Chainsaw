namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What an endpoint answered when it was tried.</summary>
public enum ProbeOutcome
{
    /// <summary>It answered with the chain's height, as high as the highest any endpoint answered.</summary>
    Answered,

    /// <summary>
    /// It answered, but with a lower height than another endpoint: it lags behind, and Snabbast asks it only when no
    /// other answers.
    /// </summary>
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
/// <param name="BlockHash">The hash of the block at that height when asked for, or <see langword="null"/>.</param>
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
    string? Problem)
{
    /// <summary>
    /// Gets how many blocks lower its height was than the highest answer of the round, when it is
    /// <see cref="ProbeOutcome.Behind"/>.
    /// </summary>
    public long? BlocksBehind { get; init; }

    /// <summary>
    /// Gets whether its height was measured against no endpoint asked through the exit: the user's own node tried in a
    /// round when the exit allowed no lookups (it never leaves the own network, so it is tried all the same). Its
    /// height is then the own node's word alone, and a node that has fallen behind the chain would look in step with
    /// itself; until a round with the exit has measured it, it answers lookups but is never <see cref="IsInStep"/>,
    /// so that the monitoring never counts from it.
    /// </summary>
    public bool Unreferenced { get; init; }

    /// <summary>
    /// Gets whether it is known to be in step with the chain: it answered with the highest height of its round, not
    /// even one block behind, measured against the endpoints asked through the exit. The monitoring asks only such an
    /// endpoint (<see cref="LookupMode.InStep"/>).
    /// </summary>
    public bool IsInStep => Outcome == ProbeOutcome.Answered && (BlocksBehind ?? 0) == 0 && !Unreferenced;
}
