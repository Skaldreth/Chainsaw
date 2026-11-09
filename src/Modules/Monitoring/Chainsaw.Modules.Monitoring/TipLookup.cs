using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>What Chainsaw's own lookup of a tip's transaction says about the tip.</summary>
internal enum TipVerdict
{
    /// <summary>It cannot be said now (offline, no endpoint in step, the source failed, not yet in a block).</summary>
    Wait,

    /// <summary>The watch's check, run on Chainsaw's own answer, gives the hit.</summary>
    Confirmed,

    /// <summary>The transaction is not on the chain, or the check on Chainsaw's answer does not give the hit.</summary>
    Rejected,
}

/// <summary>What Chainsaw's own lookup of a tip's transaction found.</summary>
/// <param name="Verdict">What it says about the tip.</param>
/// <param name="Match">The hit as Chainsaw's own answer gives it, when confirmed.</param>
/// <param name="Origins">Chainsaw's own sealed responses the answer rests on.</param>
/// <param name="Problem">Why it could not be said now, in Swedish, or <see langword="null"/>.</param>
internal sealed record TipFinding(
    TipVerdict Verdict, WatchMatch? Match, IReadOnlyList<long> Origins, string? Problem);

/// <summary>
/// Checks a tip from the background against the chain (decided 2026-11-08): its transaction is looked up through the
/// case's own source with <see cref="LookupMode.InStep"/>, as the open check reads, and the watch's check
/// (<see cref="IWatchCheck.Match"/>) runs on that answer. The background's own responses never answer: they are sealed
/// without an index, so the lookups never take them as answers.
/// </summary>
internal static class TipLookup
{
    /// <summary>Looks a tip's transaction up and runs the watch's check on the answer.</summary>
    /// <param name="check">The check.</param>
    /// <param name="bitcoin">Bitcoin's source.</param>
    /// <param name="ethereum">Ethereum's source.</param>
    /// <param name="tron">TRON's source, or <see langword="null"/> where it is not registered.</param>
    /// <param name="case">The open case the answer is sealed in.</param>
    /// <param name="tip">The tip.</param>
    /// <param name="addresses">The watch's addresses now.</param>
    /// <param name="filter">The watch's filter.</param>
    /// <param name="after">The watch's start: only money above it is a hit.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What was found.</returns>
    public static async Task<TipFinding> ReadAsync(
        IWatchCheck check,
        IBitcoinSource bitcoin,
        IEthereumSource ethereum,
        ITronSource? tron,
        ICase @case,
        WatchTip tip,
        IReadOnlySet<ChainAddress> addresses,
        WatchFilter filter,
        long after,
        CancellationToken cancellationToken)
    {
        IAccountSource? account = tip.Chain switch
        {
            Chain.Ethereum => ethereum,
            Chain.Tron => tron,
            _ => null,
        };
        var (status, transaction, origins, problem, complete) = account is not null
            ? await EthereumAsync(account, @case, tip.Match, cancellationToken).ConfigureAwait(false)
            : await BitcoinAsync(bitcoin, @case, tip.Match.Transaction, cancellationToken).ConfigureAwait(false);
        if (status == LookupStatus.NotFound)
        {
            return new TipFinding(TipVerdict.Rejected, null, origins, null);
        }

        if (status is not (LookupStatus.Fetched or LookupStatus.FromCase) || transaction is null)
        {
            return new TipFinding(TipVerdict.Wait, null, [], problem ?? "Transaktionen kunde inte slås upp nu.");
        }

        if (transaction.Block is null)
        {
            // Not in a block (yet, or again): whether it gives the hit is not known until it is.
            return new TipFinding(TipVerdict.Wait, null, [], "Transaktionen ligger inte i ett block än.");
        }

        var key = WatchSchedule.Key(tip.WatchId, tip.Match);
        var match = check.Match(addresses, filter, transaction)
            .FirstOrDefault(each => each.Block.Height > after && WatchSchedule.Key(tip.WatchId, each) == key);
        if (match is not null)
        {
            return new TipFinding(TipVerdict.Confirmed, match, origins, null);
        }

        // An answer that lacks a part (an internal transaction, a token's decimals) may lack the very movement.
        return complete
            ? new TipFinding(TipVerdict.Rejected, null, origins, null)
            : new TipFinding(TipVerdict.Wait, null, [], problem ?? "Svaret på transaktionen var inte komplett.");
    }

    /// <summary>Looks a Bitcoin transaction up.</summary>
    /// <param name="source">Bitcoin's source.</param>
    /// <param name="case">The open case.</param>
    /// <param name="id">The txid as the tip gave it.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The answer in the check's form.</returns>
    private static async Task<(LookupStatus, WatchTransaction?, IReadOnlyList<long>, string?, bool)> BitcoinAsync(
        IBitcoinSource source, ICase @case, string id, CancellationToken cancellationToken)
    {
        if (!Txid.TryParse(id, out var txid))
        {
            return (LookupStatus.NotFound, null, [], null, true);
        }

        var lookup = await source.GetTransactionAsync(@case, txid, LookupMode.InStep, cancellationToken)
            .ConfigureAwait(false);
        var transaction = lookup.Value is { } value ? BitcoinCaseFeed.Of(value) : null;
        return (lookup.Status, transaction, Sequences(lookup.Origins), lookup.Problem, true);
    }

    /// <summary>
    /// Looks a transaction on an account chain up, with every movement of value in it. On TRON a whole transaction
    /// counts TRX and every token Chainsaw knows, and leaves out only the others, which the watch never takes as hits;
    /// so an answer that lacks only them is complete for a hit in TRX or a known token, and a hit it does not give is
    /// rejected instead of waiting for ever.
    /// </summary>
    /// <param name="source">The chain's source.</param>
    /// <param name="case">The open case.</param>
    /// <param name="match">The hit the tip was about.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The answer in the check's form; a block without its hash counts as not in a block.</returns>
    private static async Task<(LookupStatus, WatchTransaction?, IReadOnlyList<long>, string?, bool)> EthereumAsync(
        IAccountSource source, ICase @case, WatchMatch match, CancellationToken cancellationToken)
    {
        if (!EthereumTxHash.TryParse(source.Chain, match.Transaction, out var hash))
        {
            return (LookupStatus.NotFound, null, [], null, true);
        }

        var lookup = await source.GetTransactionAsync(@case, hash, LookupMode.InStep, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is not { } value)
        {
            return (lookup.Status, null, Sequences(lookup.Origins), lookup.Problem, true);
        }

        var block = value.Block is { Hash: { } blockHash } at ? new WatchBlock(at.Height, blockHash, at.Time) : null;
        var flows = value.Transfers.Select(transfer => new WatchFlow(
            [new WatchLeg(transfer.From.ToChainAddress(), transfer.Amount)],
            [new WatchLeg(transfer.To.ToChainAddress(), transfer.Amount)]));
        var transaction = new WatchTransaction(source.Chain, value.Hash.Value, block, [.. flows]);

        var asset = match.Amount.Asset;
        var countedWhole = source.Chain == Chain.Tron && value.Outcome != EthereumOutcome.Pending
            && (asset.Kind == AssetKind.Native || KnownTokens.IssuerOf(asset) is not null);
        return (lookup.Status, transaction, Sequences(lookup.Origins), lookup.Problem,
            value.IsComplete || countedWhole);

    }

    /// <summary>Gives the sealed responses' numbers.</summary>
    /// <param name="origins">The responses.</param>
    /// <returns>Their numbers.</returns>
    private static IReadOnlyList<long> Sequences(IReadOnlyList<ResponseOrigin> origins) =>
        [.. origins.Select(origin => origin.Sequence)];
}
