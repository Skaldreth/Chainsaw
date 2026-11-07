using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Contracts;

/// <summary>Which money a watch looks at (CLAUDE.md, <em>Bevaka</em>): coming in, going out or both.</summary>
public enum WatchDirection
{
    /// <summary>Money that comes in to the watched node.</summary>
    In,

    /// <summary>Money that goes out from the watched node.</summary>
    Out,

    /// <summary>Money both ways.</summary>
    Both,
}

/// <summary>What a watch watches: one address, or a cluster that follows the clustering.</summary>
public enum WatchSubjectKind
{
    /// <summary>One address.</summary>
    Address,

    /// <summary>
    /// A cluster: the addresses the clustering puts with the anchor at each check, so that an address that joins the
    /// cluster later is watched too (Bitcoin only, where clustering exists).
    /// </summary>
    Cluster,
}

/// <summary>
/// What a watch looks for, without the node it watches: the part a check needs besides the addresses.
/// </summary>
/// <param name="Chain">The chain.</param>
/// <param name="Direction">In, out or both.</param>
/// <param name="Receivers">
/// For money going out: the receivers it must go to, any of them (1:n); empty means any receiver. Empty when the
/// direction is <see cref="WatchDirection.In"/>.
/// </param>
/// <param name="Senders">
/// For money coming in: the senders it must come from, any of them (n:1); empty means any sender. Empty when the
/// direction is <see cref="WatchDirection.Out"/>.
/// </param>
/// <param name="Asset">
/// The one asset watched, named by its contract on Ethereum; <see langword="null"/> for every asset. On Bitcoin it is
/// always <see langword="null"/>, since BTC is the only one.
/// </param>
/// <param name="Minimum">
/// The smallest amount a hit has, in the watched asset (on Bitcoin in BTC), or <see langword="null"/> for any amount.
/// </param>
public sealed record WatchFilter(
    Chain Chain,
    WatchDirection Direction,
    IReadOnlyList<ChainAddress> Receivers,
    IReadOnlyList<ChainAddress> Senders,
    Asset? Asset,
    AssetAmount? Minimum)
{
    /// <summary>Gets whether money going out is watched.</summary>
    public bool WatchesOut => Direction != WatchDirection.In;

    /// <summary>Gets whether money coming in is watched.</summary>
    public bool WatchesIn => Direction != WatchDirection.Out;

    /// <summary>Gets whether the watch has no counterparty to point at (every receiver and sender).</summary>
    public bool HasNoCounterparties => Receivers.Count == 0 && Senders.Count == 0;

    /// <summary>Gets the asset the minimum is counted in: the watched one, or BTC on Bitcoin.</summary>
    public Asset? CountedAsset => Asset ?? (Chain == Chain.Bitcoin ? SharedKernel.Asset.Bitcoin : null);

    /// <summary>Checks the filter and gives it in its one form, or says what is wrong.</summary>
    /// <returns>The filter with its lists in order and without doubles.</returns>
    /// <exception cref="ArgumentException">The filter does not hold together; the message says why.</exception>
    public WatchFilter Checked()
    {
        if (!Enum.IsDefined(Chain) || !Enum.IsDefined(Direction))
        {
            throw new ArgumentException("The watch's chain or direction is not known.");
        }

        if ((Direction == WatchDirection.In && Receivers.Count > 0)
            || (Direction == WatchDirection.Out && Senders.Count > 0))
        {
            throw new ArgumentException("Receivers are only for money going out, and senders for money coming in.");
        }

        if (Receivers.Concat(Senders).Any(party => party.Chain != Chain))
        {
            throw new ArgumentException("A counterparty is on another chain than the watch.");
        }

        if (Receivers.Count > WatchRules.MaximumCounterparties || Senders.Count > WatchRules.MaximumCounterparties)
        {
            throw new ArgumentException("The watch has too many counterparties.");
        }

        if (Asset is { } asset && (asset.Chain != Chain || Chain == Chain.Bitcoin))
        {
            throw new ArgumentException("The watched asset is not one of the chain's own to choose.");
        }

        if (Minimum is { } minimum
            && (CountedAsset is not { } counted || minimum.Asset != counted
                || minimum.Asset.Decimals != counted.Decimals
                || minimum.Units.Sign <= 0))
        {
            throw new ArgumentException("A minimum is a positive amount of the watched asset.");
        }

        return this with
        {
            Receivers = Distinct(Receivers),
            Senders = Distinct(Senders),
        };
    }

    /// <summary>Gives a list of addresses in order without doubles, so that equal filters are stored alike.</summary>
    /// <param name="parties">The addresses.</param>
    /// <returns>The same addresses, each once, ordered by their text.</returns>
    private static IReadOnlyList<ChainAddress> Distinct(IReadOnlyList<ChainAddress> parties) =>
        [.. parties.Distinct().OrderBy(party => party.Value, StringComparer.Ordinal)];
}

/// <summary>The node a watch watches.</summary>
/// <param name="Kind">An address or a cluster.</param>
/// <param name="Anchor">The address, or for a cluster the address the cluster is found from.</param>
/// <param name="Name">What the user saw the node called when the watch was made, for example <c>Kluster 7</c>.</param>
public sealed record WatchSubject(WatchSubjectKind Kind, ChainAddress Anchor, string Name);

/// <summary>The rules for watches: lengths and limits.</summary>
public static class WatchRules
{
    /// <summary>The longest name of a watched node, or reason.</summary>
    public const int MaximumTextLength = 1_000;

    /// <summary>The most receivers, or senders, a watch has.</summary>
    public const int MaximumCounterparties = 100;

    /// <summary>
    /// The most addresses of a cluster that one check reads; the rest are said to be left out, so that a large cluster
    /// never floods the endpoints (the pace is four lookups a second).
    /// </summary>
    public const int MaximumAddresses = 50;

    /// <summary>The most pages of one address's history that one check reads back to where it was.</summary>
    public const int MaximumPages = 5;

    /// <summary>
    /// How many blocks back from the last check a check reads again, so that a transaction an endpoint had not yet
    /// indexed when its block came is still found.
    /// </summary>
    public const int Lookback = 6;
}
