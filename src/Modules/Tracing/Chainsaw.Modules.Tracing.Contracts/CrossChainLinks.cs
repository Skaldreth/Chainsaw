using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// Where a link to another chain came from when it was not made by hand: the suggestion of an analysis that the user
/// approved (for example <c>cross-chain v1</c>), and the sealed decision that approved it. The link is still the
/// user's judgement (principle 2); the origin says what it rests on (principle 3).
/// </summary>
/// <param name="Analysis">The analysis with its version as the user reads it, like <c>cross-chain v1</c>.</param>
/// <param name="Decision">The sealed decision's number in the chain of the checks' decisions.</param>
public sealed record CrossChainOrigin(string Analysis, long Decision)
{
    /// <summary>Gets the analysis and version; never empty.</summary>
    public string Analysis { get; } = string.IsNullOrWhiteSpace(Analysis)
        ? throw new ArgumentException("An origin names its analysis.", nameof(Analysis))
        : Analysis.Trim();

    /// <summary>Writes the origin as it is sealed, for example <c>cross-chain v1#12</c>.</summary>
    /// <returns>The analysis, a hash sign and the decision's number.</returns>
    public override string ToString() =>
        Analysis + "#" + Decision.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Reads an origin as it is sealed.</summary>
    /// <param name="text">The text, or <see langword="null"/> for a link made by hand.</param>
    /// <returns>The origin, or <see langword="null"/>.</returns>
    /// <exception cref="FormatException">The text is not an origin.</exception>
    public static CrossChainOrigin? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var hash = text.LastIndexOf('#');
        return hash > 0 && long.TryParse(
            text.AsSpan(hash + 1), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var decision)
            ? new CrossChainOrigin(text[..hash], decision)
            : throw new FormatException("Not a link's origin.");
    }
}

/// <summary>
/// A link to another chain that an approved suggestion makes: where the money left (a Bitcoin coin or a movement on
/// an account chain, exactly one), the other chain's transaction, the user's optional reason and the origin.
/// </summary>
/// <param name="From">The Bitcoin coin that went into the bridge or swap, or <see langword="null"/>.</param>
/// <param name="FromTransfer">The account chain's movement that went in, or <see langword="null"/>.</param>
/// <param name="To">The transaction on the other chain.</param>
/// <param name="Reason">The user's reason, or <see langword="null"/>; it is optional.</param>
/// <param name="Origin">The suggestion and decision the link comes from.</param>
public sealed record CrossChainLinkDraft(
    Outpoint? From, AccountSeed? FromTransfer, ChainTransactionRef To, string? Reason, CrossChainOrigin Origin);

/// <summary>
/// Seals links to other chains inside a transaction the caller holds, so that a decision and the link it makes are
/// sealed together or not at all (as an approved wallet and its merge are). The links are the same as those made by
/// hand (<see cref="ITracingService.LinkCrossChain(ICase, Outpoint, ChainTransactionRef, string)"/>), with an origin.
/// </summary>
public interface ICrossChainLinks
{
    /// <summary>Seals a link, with a line in the journal, in the caller's transaction.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="draft">The link.</param>
    /// <returns>The sealed link.</returns>
    /// <exception cref="ArgumentException">
    /// Not exactly one of the coin and the movement is given, the target is on the money's own chain, the money
    /// already continues on another chain, or the reason is too long.
    /// </exception>
    CrossChainLink Link(ICaseTransaction transaction, CrossChainLinkDraft draft);

    /// <summary>
    /// Takes back the links in force that an origin's decision made, with a line in the journal each, in the caller's
    /// transaction; the links stay sealed (principle 1).
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="analysis">The analysis and version of the origin.</param>
    /// <param name="decision">The decision's number.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns>How many links were taken back.</returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    int UnlinkMadeBy(ICaseTransaction transaction, string analysis, long decision, string? reason);
}
