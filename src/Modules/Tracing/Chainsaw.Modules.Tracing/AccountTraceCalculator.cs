using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Traces money through the account part of the graph, one asset at a time. Each account whose history the graph has
/// read carries the traced money with the chosen method (<see cref="AccountChecks.Run"/> checks every answer); money
/// that reaches an account whose history is not read, or one where the trail ends (an exchange, a mixer), stops
/// there. Since an account's outgoing share depends on what came in, and money can come back, the accounts are worked
/// through again until nothing changes.
/// </summary>
internal static class AccountTraceCalculator
{
    /// <summary>Traces the seeds.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="method">The method for accounts.</param>
    /// <param name="seeds">The movements the money starts in.</param>
    /// <param name="cancellationToken">Stops the trace between rounds.</param>
    /// <returns>The result per asset.</returns>
    /// <exception cref="ArgumentException">
    /// There are no seeds, or a seed is not in the graph or is given twice.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A method broke its contract, the accounts do not settle, or the seeds lie in each other's flow.
    /// </exception>
    public static AccountTraceResult Trace(
        TraceGraph graph,
        IAccountTracingMethod method,
        IReadOnlyList<AccountSeed> seeds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(seeds);
        if (seeds.Count == 0 || seeds.Distinct().Count() != seeds.Count)
        {
            throw new ArgumentException("A trace needs seeds, each once.", nameof(seeds));
        }

        var transfers = graph.Account.Transfers;
        var seeded = seeds.Select(seed => transfers.FirstOrDefault(seed.Is)
                ?? throw new ArgumentException($"The seed {seed.Transaction} is not a movement in the graph.",
                    nameof(seeds)))
            .ToList();
        if (seeded.FirstOrDefault(transfer => transfer.From == transfer.To) is { } own)
        {
            throw new ArgumentException(
                $"The seed {own.Transaction} moves money from an account to itself; nothing leaves to be traced.",
                nameof(seeds));
        }
        var assets = seeded.Select(transfer => transfer.Amount.Asset).Distinct().ToList();
        var traces = assets.Select(asset =>
                new AssetTracer(graph, method, asset, [.. seeded.Where(transfer => transfer.Amount.Asset == asset)])
                    .Run(cancellationToken))
            .ToList();
        return new AccountTraceResult(method.Id, seeds, traces);
    }

    /// <summary>Traces one asset.</summary>
    private sealed class AssetTracer
    {
        /// <summary>The graph.</summary>
        private readonly TraceGraph _graph;

        /// <summary>The method.</summary>
        private readonly IAccountTracingMethod _method;

        /// <summary>The asset.</summary>
        private readonly Asset _asset;

        /// <summary>The seed movements.</summary>
        private readonly IReadOnlyList<EthereumTransfer> _seeds;

        /// <summary>The movements of the asset between two accounts, in chain order.</summary>
        private readonly List<EthereumTransfer> _transfers;

        /// <summary>The traced part of each movement.</summary>
        private readonly Dictionary<EthereumTransfer, BigInteger> _traced = [];

        /// <summary>
        /// The movements the user linked to another chain, with their links: their traced money continues there and
        /// never enters the receiver's ledger.
        /// </summary>
        private readonly Dictionary<EthereumTransfer, List<CrossChainLink>> _linked = [];

        /// <summary>The traced money each account that carries it sent to fees and keeps, by the last round.</summary>
        private readonly Dictionary<ChainAddress, AccountTaint> _taints = [];

        /// <summary>Creates a tracer.</summary>
        /// <param name="graph">The graph.</param>
        /// <param name="method">The method.</param>
        /// <param name="asset">The asset.</param>
        /// <param name="seeds">The seed movements, all of the asset.</param>
        public AssetTracer(
            TraceGraph graph, IAccountTracingMethod method, Asset asset, IReadOnlyList<EthereumTransfer> seeds)
        {
            (_graph, _method, _asset, _seeds) = (graph, method, asset, seeds);
            _transfers =
            [
                .. graph.Account.Transfers.Where(transfer =>
                    transfer.Amount.Asset == asset && transfer.From != transfer.To),
            ];
            foreach (var seed in seeds)
            {
                _traced[seed] = seed.Amount.Units;
            }

            foreach (var link in graph.CrossChainLinks)
            {
                if (link.FromTransfer is { } movement && _transfers.FirstOrDefault(movement.Is) is { } transfer)
                {
                    if (!_linked.TryGetValue(transfer, out var links))
                    {
                        links = [];
                        _linked.Add(transfer, links);
                    }

                    links.Add(link);
                }
            }
        }

        /// <summary>Carries the money through the accounts until nothing changes, and gives the result.</summary>
        /// <param name="cancellationToken">Stops the trace between rounds.</param>
        /// <returns>The asset's trace.</returns>
        public AssetTrace Run(CancellationToken cancellationToken)
        {
            var carriers = Carriers();
            var rounds = _transfers.Count + 2;
            for (var round = 0; ; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (round == rounds)
                {
                    throw new InvalidOperationException("The traced money did not settle between the accounts.");
                }

                if (!carriers.Aggregate(false, (changed, account) => Carry(account) | changed))
                {
                    break;
                }
            }

            var ends = Ends(carriers);
            var traced = _seeds.Aggregate(BigInteger.Zero, (sum, seed) => sum + seed.Amount.Units);
            if (ends.Aggregate(BigInteger.Zero, (sum, end) => sum + end.Amount.Amount.Units) != traced)
            {
                throw new InvalidOperationException(
                    "The seeds lie in each other's flow, so money would be counted twice; trace from one of them.");
            }

            return new AssetTrace(new AssetAmount(_asset, traced), Edges(), ends);
        }

        /// <summary>
        /// Gives the accounts that carry money on: those whose history the graph has read and where the trail does not
        /// end.
        /// </summary>
        /// <returns>The accounts, in a stable order.</returns>
        private List<ChainAddress> Carriers() =>
        [
            .. _graph.Account.ReadAccounts
                .Where(account => _graph.NodeOf(account) is not { Ends.Count: > 0 })
                .OrderBy(account => account.Value, StringComparer.Ordinal),
        ];

        /// <summary>Runs the method on one account's ledger and records its outgoing shares.</summary>
        /// <param name="account">The account.</param>
        /// <returns>Whether a share changed.</returns>
        private bool Carry(ChainAddress account)
        {
            var entries = new List<(EthereumTransfer? Transfer, AccountEntry Entry)>();
            foreach (var transfer in _transfers.Where(transfer => Touches(transfer, account)
                && !(transfer.To.Value == account.Value && _linked.ContainsKey(transfer))))
            {
                var incoming = transfer.To.Value == account.Value;
                var traced = incoming ? _traced.GetValueOrDefault(transfer) : BigInteger.Zero;
                entries.Add((transfer, new AccountEntry(
                    AssetTransfer.OfEthereum(transfer, null),
                    incoming ? TransferDirection.In : TransferDirection.Out,
                    new AssetAmount(_asset, traced))));
            }

            if (_asset.Kind == AssetKind.Native)
            {
                foreach (var fee in _graph.Account.Fees.Where(fee => fee.Payer.Value == account.Value))
                {
                    entries.Add((null, new AccountEntry(
                        new AssetTransfer(new ChainTransactionRef(fee.Transaction.Chain, fee.Transaction.Value), fee.Amount,
                            null, null),
                        TransferDirection.Fee,
                        AssetAmount.Zero(_asset))));
                }
            }

            if (entries.All(entry => entry.Entry.Traced.IsZero))
            {
                return Record(account, entries, new AccountTaint([], AssetAmount.Zero(_asset), AssetAmount.Zero(_asset)));
            }

            var ordered = entries.OrderBy(entry => Order(entry.Transfer, entry.Entry)).ToList();
            var taint = AccountChecks.Run(_method, new AccountLedger(
                account, _asset, AssetAmount.Zero(_asset), [.. ordered.Select(entry => entry.Entry)]));
            return Record(account, ordered, taint);
        }

        /// <summary>Records an account's answer and says whether an outgoing share changed.</summary>
        /// <param name="account">The account.</param>
        /// <param name="entries">The ledger's entries, in the order the method saw them.</param>
        /// <param name="taint">The method's answer.</param>
        /// <returns>Whether a share changed.</returns>
        private bool Record(
            ChainAddress account, List<(EthereumTransfer? Transfer, AccountEntry Entry)> entries, AccountTaint taint)
        {
            _taints[account] = taint;
            var changed = false;
            var outgoing = entries.Where(entry => entry.Entry.Direction == TransferDirection.Out).ToList();
            for (var index = 0; index < outgoing.Count; index++)
            {
                var transfer = outgoing[index].Transfer!;
                if (_seeds.Contains(transfer))
                {
                    // A seed is traced whole by the user's choice; money from upstream that would land in it is
                    // found by the check that the ends add up.
                    continue;
                }

                var share = index < taint.Outgoing.Count ? taint.Outgoing[index].Units : BigInteger.Zero;
                if (_traced.GetValueOrDefault(transfer) != share)
                {
                    _traced[transfer] = share;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>Gives where the traced money stops.</summary>
        /// <param name="carriers">The accounts that carry money on.</param>
        /// <returns>The ends.</returns>
        private List<TraceEnd> Ends(List<ChainAddress> carriers)
        {
            var ends = new List<TraceEnd>();
            var carrying = carriers.ToHashSet();
            foreach (var (transfer, links) in _linked.Where(pair => _traced.GetValueOrDefault(pair.Key) > 0))
            {
                var node = _graph.NodeOf(transfer.To.ToChainAddress());
                ends.Add(new TraceEnd(
                    TrailEndReason.OtherChain,
                    Amount(_traced[transfer], [transfer.Transaction]),
                    null,
                    null,
                    node?.Key,
                    $"Fortsätter på {links[0].To.Chain} enligt användarens länk till {links[0].To.Id}.",
                    [],
                    links,
                    transfer.Block is null));
            }

            var arrived = _transfers.Where(transfer => _traced.GetValueOrDefault(transfer) > 0
                    && !_linked.ContainsKey(transfer))
                .GroupBy(transfer => transfer.To.ToChainAddress());
            foreach (var group in arrived.OrderBy(group => group.Key.Value, StringComparer.Ordinal))
            {
                if (carrying.Contains(group.Key))
                {
                    continue;
                }

                var node = _graph.NodeOf(group.Key);
                var amount = group.Aggregate(BigInteger.Zero, (sum, transfer) => sum + _traced[transfer]);
                var through = group.Select(transfer => transfer.Transaction).ToList();
                var outdated = group.Any(transfer => transfer.Block is null);
                if (node is { Ends.Count: > 0 })
                {
                    var hint = node.Ends[0];
                    ends.Add(End(hint.Reason, amount, node.Key, through, hint.Explanation, node.Ends, outdated));
                }
                else
                {
                    var text = _graph.Account.PartlyRead.Contains(group.Key)
                        ? "Adressens historik är bara delvis läst (gränsen nåddes eller ett uppslag var ofullständigt), "
                            + "så pengarna förs inte vidare genom den; det kan saknas rörelser däremellan."
                        : "Adressens historik är inte expanderad; expandera den för att spåra vidare.";
                    ends.Add(End(TrailEndReason.NotExpanded, amount, node?.Key, through, text, [], outdated));
                }
            }

            foreach (var account in carriers)
            {
                var taint = _taints.GetValueOrDefault(account);
                var key = _graph.NodeOf(account)?.Key;
                var outdated = _graph.Account.MayBeOutdated.Contains(account);
                if (taint is { Remaining.Units.Sign: > 0 })
                {
                    ends.Add(End(TrailEndReason.Unspent, taint.Remaining.Units, key, [],
                        "Ligger kvar på adressen enligt historiken i ärendet; det kan ha flyttats sedan.", [], outdated));
                }

                if (taint is { Fees.Units.Sign: > 0 })
                {
                    ends.Add(End(TrailEndReason.MinerFee, taint.Fees.Units, key, [],
                        "Gick till avgifter som adressen betalade (gas på Ethereum, energi och bandbredd på TRON).",
                        [], outdated));
                }
            }

            return ends;
        }

        /// <summary>Makes an end.</summary>
        /// <param name="reason">Why it stops.</param>
        /// <param name="units">How much.</param>
        /// <param name="nodeKey">The node, if it is in the graph.</param>
        /// <param name="through">The transactions it came in.</param>
        /// <param name="explanation">Why, in Swedish.</param>
        /// <param name="basis">The hints it rests on.</param>
        /// <param name="outdated">Whether it may have changed.</param>
        /// <returns>The end.</returns>
        private TraceEnd End(
            TrailEndReason reason,
            BigInteger units,
            string? nodeKey,
            IEnumerable<EthereumTxHash> through,
            string explanation,
            IReadOnlyList<TrailEndHint> basis,
            bool outdated) =>
            new(reason, Amount(units, through), null, null, nodeKey, explanation, basis, [], outdated);

        /// <summary>Gives the traced money per edge of the graph.</summary>
        /// <returns>The edges that carry some.</returns>
        private List<TracedEdge> Edges()
        {
            var edges = new List<TracedEdge>();
            foreach (var edge in _graph.Edges.Where(edge => edge.Amount.Asset == _asset))
            {
                var carried = _transfers.Where(transfer => _traced.GetValueOrDefault(transfer) > 0
                        && edge.Transfers.Any(on => Same(on, transfer)))
                    .ToList();
                if (carried.Count == 0)
                {
                    continue;
                }

                var units = carried.Aggregate(BigInteger.Zero, (sum, transfer) => sum + _traced[transfer]);
                var outdated = carried.Any(transfer => transfer.Block is null)
                    || _graph.Account.MayBeOutdated.Contains(carried[0].From.ToChainAddress());
                edges.Add(new TracedEdge(edge.Key, Amount(units, carried.Select(t => t.Transaction)), outdated));
            }

            return edges;
        }

        /// <summary>Makes a traced amount of the asset.</summary>
        /// <param name="units">How much.</param>
        /// <param name="through">The transactions it was counted through.</param>
        /// <returns>The amount.</returns>
        private TracedAmount Amount(BigInteger units, IEnumerable<EthereumTxHash> through) =>
            new(new AssetAmount(_asset, units), _method.Id, [])
            {
                ThroughOther =
                [
                    .. through.Distinct().OrderBy(hash => hash.Value, StringComparer.Ordinal)
                        .Select(hash => new ChainTransactionRef(hash.Chain, hash.Value)),
                ],
            };

        /// <summary>Says whether an edge's transfer is a movement.</summary>
        /// <param name="transfer">The edge's transfer.</param>
        /// <param name="movement">The movement.</param>
        /// <returns>Whether it is.</returns>
        private static bool Same(AssetTransfer transfer, EthereumTransfer movement) =>
            transfer.Movement == movement.Kind && transfer.Position == movement.Position
            && transfer.Transaction.Id == movement.Transaction.Value;

        /// <summary>Says whether a movement comes to or leaves an account.</summary>
        /// <param name="transfer">The movement.</param>
        /// <param name="account">The account.</param>
        /// <returns>Whether it does.</returns>
        private static bool Touches(EthereumTransfer transfer, ChainAddress account) =>
            transfer.From.Value == account.Value || transfer.To.Value == account.Value;

        /// <summary>
        /// Gives the order of an entry in a ledger: chain order, and in one transaction the fee after its movements.
        /// </summary>
        /// <param name="transfer">The movement, or <see langword="null"/> for a fee.</param>
        /// <param name="entry">The entry.</param>
        /// <returns>The key.</returns>
        private (long Block, long Index, string Transaction, long Position, int Kind) Order(
            EthereumTransfer? transfer, AccountEntry entry)
        {
            if (transfer is not null)
            {
                return AccountGraph.ChainOrder(transfer);
            }

            var fee = _graph.Account.Fees.First(fee => fee.Transaction.Value == entry.Transfer.Transaction.Id);
            return (fee.Block?.Height ?? long.MaxValue, fee.Block?.TransactionIndex ?? long.MaxValue,
                fee.Transaction.Value, long.MaxValue, int.MaxValue);
        }
    }
}
