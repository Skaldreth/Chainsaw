using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// One step of the way through a swap, as the router mark's box writes it (sketch DX1b): the asset and the amount
/// that stood there, and the address that paid it out, the pool, when it is not the start.
/// </summary>
/// <param name="Mark">The asset's mark, for example <c>₮</c>, <c>Ξ</c> or <c>WETH</c>.</param>
/// <param name="Amount">The amount, for example <c>29 600</c>.</param>
/// <param name="Pool">
/// Who paid the asset out in the transaction, by its name on the list of known contracts or its address shortened;
/// <see langword="null"/> for the first step, which the swapping account paid in itself.
/// </param>
public sealed record SwapRouteStep(string Mark, string Amount, string? Pool);

/// <summary>
/// The way a swap went through a known exchange's router or an aggregator (sketch DX1b): the router is no node of its
/// own, since a swap changes token and not owner, but a small mark in the corner of the swap's pill. The way is read
/// from the transaction's movements, which are facts; that the money took that way, and that the venue is the one the
/// list names, are readings, never facts (principle 2). Knows nothing of WPF.
/// </summary>
/// <param name="Venue">The known exchange or aggregator, from <see cref="KnownContracts"/>.</param>
/// <param name="Transaction">The swap's transaction.</param>
/// <param name="Steps">The way, the sold asset first and the bought one last.</param>
/// <param name="IsPartial">
/// Whether the transaction was not read whole (only the account's history shows it, or an answer left a part out),
/// so that a pool on the way may be missing.
/// </param>
public sealed record SwapRoute(
    KnownContract Venue, EthereumTxHash Transaction, IReadOnlyList<SwapRouteStep> Steps, bool IsPartial)
{
    /// <summary>The Guide's heading that explains the router's mark.</summary>
    public const string GuideHeading = "En DEX:s router";

    /// <summary>The line in the box when the traced value goes through the swap.</summary>
    public const string ThroughText = "Spåret går igenom";

    /// <summary>The line in the box when the traced value stops in the swap.</summary>
    public const string StopsText = "Spåret slutar i swappen";

    /// <summary>The hint after the status line in the box.</summary>
    public const string ClickText = "Klicka för Varför?";

    /// <summary>
    /// Gets how many exchanges the way has: one for a plain swap, more when an aggregator swapped in several steps
    /// in the same transaction.
    /// </summary>
    public int Exchanges => Math.Max(1, Steps.Count - 1);

    /// <summary>Gets whether the venue is an aggregator (1inch, 0x), whose mark shows the number of steps.</summary>
    public bool IsAggregator => Venue.Category == KnownContractCategory.Aggregator;

    /// <summary>
    /// Gets what stands in the mark: the number of steps for an aggregator, or <see langword="null"/> when the mark
    /// shows the router's icon.
    /// </summary>
    public string? MarkCount => IsAggregator ? Exchanges.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>Gets the box's title: the venue's name, with <em>aggregator</em> when it is one.</summary>
    public string Title => IsAggregator ? Venue.Name + " · aggregator" : Venue.Name;

    /// <summary>Gets the quiet line under the title: what the router does, and when the way may be partial.</summary>
    public string Sub
    {
        get
        {
            var what = Exchanges > 1 ? $"{Exchanges} byten i en tx" : "byter token, inte ägare";
            var partial = IsPartial ? " · vägen kan vara ofullständig" : string.Empty;
            return $"Känt kontrakt · {what}{partial}";
        }
    }

    /// <summary>Gets the box's foot: the router's contract and the transaction, both shortened.</summary>
    public string Foot => $"{GraphScene.Shorten(Venue.Address.Value)} · tx {GraphScene.Shorten(Transaction.Value)}";

    /// <summary>
    /// Reads the way of a swap through a known exchange from its transaction's movements: the sold asset first, then
    /// every other asset in the order the transaction paid it out, each with the address that paid it, and the bought
    /// asset last with all the account got back. Ether and wrapped ether count as one asset, since a router wraps and
    /// unwraps without swapping.
    /// </summary>
    /// <param name="swap">The swap.</param>
    /// <param name="account">The account part of the graph, with the transaction's movements.</param>
    /// <returns>The way, or <see langword="null"/> when the swap went through no known exchange.</returns>
    public static SwapRoute? Read(TokenSwap swap, AccountGraph account)
    {
        ArgumentNullException.ThrowIfNull(swap);
        ArgumentNullException.ThrowIfNull(account);
        if (swap.Venue is not { } venue)
        {
            return null;
        }

        var whole = account.Transactions.GetValueOrDefault(swap.Transaction);
        var moves = whole?.Transfers ?? account.Transfers.Where(move => move.Transaction == swap.Transaction);
        var steps = new List<(Asset Asset, AssetAmount Amount, string? Pool)> { (swap.Sold.Asset, swap.Sold, null) };
        foreach (var move in moves.OrderBy(move => move.Position).Where(move => !IsAccount(move.From, swap)))
        {
            if (!SameAsset(move.Amount.Asset, steps[^1].Asset))
            {
                steps.Add((move.Amount.Asset, move.Amount, PoolName(move.From)));
            }
        }

        if (SameAsset(steps[^1].Asset, swap.Bought.Asset) && steps.Count > 1)
        {
            steps[^1] = (swap.Bought.Asset, swap.Bought, steps[^1].Pool);
        }
        else
        {
            var payer = moves.FirstOrDefault(
                move => IsAccount(move.To, swap) && move.Amount.Asset == swap.Bought.Asset);
            steps.Add((swap.Bought.Asset, swap.Bought, payer is null ? null : PoolName(payer.From)));
        }

        var written = steps
            .Select(step => new SwapRouteStep(SwapMarks.MarkOf(step.Asset), AmountText.Number(step.Amount), step.Pool))
            .ToList();
        return new SwapRoute(venue, swap.Transaction, written, whole is not { IsComplete: true });
    }

    /// <summary>
    /// Says whether the traced value goes through the line's swap: not when the user chose that it does not, nor when
    /// its price is absurd.
    /// </summary>
    /// <param name="edge">The line with the swap.</param>
    /// <returns><see langword="true"/> when it goes through.</returns>
    public static bool GoesThrough(EdgeView edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return !edge.IsSwapStopped && !edge.IsSwapPriceAbsurd;
    }

    /// <summary>
    /// Writes <em>Varför?</em> for the router's mark, for the Guide's panel: what the account sent and through which
    /// router, the way through the pools, why the trail goes through (or that it stops), and the list the name rests
    /// on, with its source and version.
    /// </summary>
    /// <param name="edge">The line with the swap.</param>
    /// <returns>The answers, each pointing to the Guide's section on swaps.</returns>
    public IReadOnlyList<WhyLine> Why(EdgeView edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var first = Steps[0];
        var last = Steps[^1];
        var way = string.Join(" → ", Steps.Select(step => step.Pool is null
            ? $"{step.Mark} {step.Amount}"
            : $"{step.Mark} {step.Amount} ({step.Pool})"));
        var partial = IsPartial
            ? " Tx:en är inte läst hel, så en pool på vägen kan saknas; expandera tx:en för att läsa den."
            : string.Empty;
        return
        [
            new($"Adressen skickade {first.Mark} {first.Amount} genom {Venue.Name} och fick {last.Mark} {last.Amount} "
                + $"tillbaka i samma tx ({GraphScene.Shorten(Transaction.Value)}).", GuideTopic.Swaps, GuideHeading),
            new($"Vägen ur tx:ens överföringar: {way}.{partial}", GuideTopic.Swaps, GuideHeading),
            new(ThroughWhy(edge), GuideTopic.Swaps, GuideHeading),
            new($"Att kontraktet {Venue.Address.Value} är {Venue.Name} är en bedömning {Venue.Provenance()}, i "
                + $"{KnownContracts.Name} {KnownContracts.Version}.", GuideTopic.Swaps, GuideHeading),
        ];
    }

    /// <summary>Says why the trail goes through the swap, or why it stops there after all.</summary>
    /// <param name="edge">The line with the swap.</param>
    /// <returns>The answer.</returns>
    private static string ThroughWhy(EdgeView edge)
    {
        if (GoesThrough(edge))
        {
            return "Ett byte på en DEX byter token, inte ägare. Därför går spåret igenom, och det spårade följer med "
                + $"enligt {SwapValue.Analysis}.";
        }

        return edge.IsSwapPriceAbsurd
            ? "Spåret slutar ändå i swappen: priset är orimligt."
            : "Spåret slutar ändå i swappen: det är ditt val.";
    }

    /// <summary>Says whether an address is the swapping account.</summary>
    /// <param name="address">The address.</param>
    /// <param name="swap">The swap.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsAccount(EthereumAddress address, TokenSwap swap) => address.ToChainAddress() == swap.Account;

    /// <summary>Says whether two assets are the same on the way, ether and wrapped ether counted as one.</summary>
    /// <param name="first">One asset.</param>
    /// <param name="second">The other.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    private static bool SameAsset(Asset first, Asset second) => first == second || (IsEther(first) && IsEther(second));

    /// <summary>Says whether an asset is ether or wrapped ether.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsEther(Asset asset) =>
        asset == KnownTokens.EthereumWeth || asset == Asset.Ether;

    /// <summary>Names an address on the way: by the list of known contracts, or by its address shortened.</summary>
    /// <param name="address">The address that paid an asset out.</param>
    /// <returns>The name.</returns>
    private static string PoolName(EthereumAddress address) =>
        KnownContracts.Find(address)?.Name ?? "pool " + GraphScene.Shorten(address.Value);
}
