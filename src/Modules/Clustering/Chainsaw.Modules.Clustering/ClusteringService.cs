using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Runs the analyses, stores what they conclude with a journal entry per run, and builds the clusters from the
/// stored conclusions and the user's splits.
/// </summary>
internal sealed class ClusteringService : IClusteringService
{
    /// <summary>The longest reason a split may have.</summary>
    public const int MaximumReasonLength = 1_000;

    /// <summary>The analyses that run on Bitcoin's transactions, in the order they run.</summary>
    private readonly IReadOnlyList<IUtxoHeuristic> _heuristics;

    /// <summary>
    /// Takes the analyses and keeps those registered for Bitcoin; two for Bitcoin with the same name and version are
    /// refused, and so is one for Bitcoin that does not implement the UTXO contract.
    /// </summary>
    /// <param name="heuristics">The analyses for every model and chain, in the order they run.</param>
    /// <exception cref="ArgumentException">An analysis for Bitcoin is not valid, or two are the same.</exception>
    public ClusteringService(IEnumerable<IClusteringHeuristic> heuristics)
    {
        ArgumentNullException.ThrowIfNull(heuristics);
        var all = heuristics.ToList();
        if (all.FirstOrDefault(heuristic => heuristic.Scope is null) is { } unscoped)
        {
            throw new ArgumentException($"{unscoped.Id} is registered without a scope.", nameof(heuristics));
        }

        var forBitcoin = all.Where(heuristic => heuristic.Scope.Covers(Chain.Bitcoin)).ToList();
        if (forBitcoin.FirstOrDefault(heuristic => heuristic is not IUtxoHeuristic) is { } misfit)
        {
            throw new ArgumentException(
                $"{misfit.Id} is registered for Bitcoin but does not implement the UTXO contract.", nameof(heuristics));
        }

        _heuristics = [.. forBitcoin.Cast<IUtxoHeuristic>()];
        if (_heuristics.Select(heuristic => heuristic.Id).Distinct().Count() != _heuristics.Count)
        {
            throw new ArgumentException("Two analyses have the same name and version.", nameof(heuristics));
        }

        Heuristics = [.. _heuristics.Select(heuristic => heuristic.Id)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<HeuristicId> Heuristics { get; }

    /// <inheritdoc/>
    public ClusteringRun Analyse(ICase @case, IReadOnlyCollection<BitcoinTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(transactions);
        var known = new KnownTransactions(transactions);
        var failed = new List<Txid>();
        var analysed = new List<(Txid Txid, List<(WeighedResult Result, string Fingerprint)> Results)>();
        foreach (var transaction in known.All)
        {
            if (AnalyseOne(transaction, known) is { } results && Fingerprinted(results) is { } fingerprinted)
            {
                analysed.Add((transaction.Txid, fingerprinted));
            }
            else
            {
                failed.Add(transaction.Txid);
            }
        }

        var txids = known.All.Select(transaction => transaction.Txid).ToHashSet();

        return @case.Write(transaction =>
        {
            var latest = EvidenceStore.LatestResults(transaction, txids);
            var heldBack = analysed.Where(entry => !KnowsBasis(entry.Txid, latest, txids))
                .Select(entry => entry.Txid)
                .ToList();
            var changed = analysed.Where(entry => !heldBack.Contains(entry.Txid))
                .SelectMany(entry => entry.Results)
                .Where(entry => Replaces(entry.Result, entry.Fingerprint, latest))
                .ToList();
            if (changed.Count == 0 && failed.Count == 0 && heldBack.Count == 0)
            {
                return new ClusteringRun(null, known.Count, 0, 0, 0, 0, [], failed, heldBack);
            }

            var proposals = changed.SelectMany(entry => entry.Result.Proposals).ToList();
            var merges = proposals.Count(proposal => proposal.Strength == EvidenceStrength.Merge);
            var suggestions = proposals.Count(proposal => proposal.Strength == EvidenceStrength.Suggestion);
            var stopped = proposals.Count(proposal => proposal.Strength == EvidenceStrength.Stopped);
            var coinJoins = changed
                .Where(entry => entry.Result.Result.Findings.OfType<CoinJoinFinding>().Any(found => found.EndsTrail))
                .Select(entry => entry.Result.Result.Txid)
                .Distinct()
                .ToList();
            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.Analysed,
                Summary(known.Count, merges, suggestions, stopped, coinJoins.Count),
                JsonSerializer.Serialize(new
                {
                    transactions = known.Count,
                    results = changed.Count,
                    heuristics = Heuristics.Select(heuristic => heuristic.ToString()),
                    merges,
                    suggestions,
                    stopped,
                    coinJoins = coinJoins.Select(txid => txid.Value),
                    failed = failed.Select(txid => txid.Value),
                    heldBack = heldBack.Select(txid => txid.Value),
                })));
            foreach (var (result, fingerprint) in changed)
            {
                EvidenceStore.Append(transaction, result, fingerprint, journal);
            }

            return new ClusteringRun(
                journal, known.Count, changed.Count, merges, suggestions, stopped, coinJoins, failed, heldBack);
        });
    }

    /// <inheritdoc/>
    public ClusterSplit Split(ICase @case, SplitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Split(@case, [request])[0];
    }

    /// <inheritdoc/>
    public IReadOnlyList<ClusterSplit> Split(ICase @case, IReadOnlyList<SplitRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            throw new ArgumentException("A breaking out has at least one split.", nameof(requests));
        }

        foreach (var request in requests)
        {
            CheckRequest(request);
        }

        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.Split,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Uppbrytning: {0}.",
                    string.Join(
                        "; ",
                        requests.Select(request => string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} och {1} har inte samma ägare ({2})",
                            request.First.Value,
                            request.Second.Value,
                            request.Heuristic)))),
                JsonSerializer.Serialize(requests.Select(request => new
                {
                    first = request.First.Value,
                    second = request.Second.Value,
                    heuristic = request.Heuristic,
                    reason = request.Reason,
                }))));
            return (IReadOnlyList<ClusterSplit>)
            [
                .. requests.Select(request => EvidenceStore.AppendSplit(transaction, request, journal)),
            ];
        });
    }

    /// <summary>Checks a split before anything is stored.</summary>
    /// <param name="request">The split.</param>
    /// <exception cref="ArgumentException">
    /// The addresses are the same, the heuristic is not one that runs, or the reason is empty or too long.
    /// </exception>
    private void CheckRequest(SplitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.First);
        ArgumentNullException.ThrowIfNull(request.Second);
        if (request.First == request.Second)
        {
            throw new ArgumentException("An address cannot be split from itself.", nameof(request));
        }

        if (!Heuristics.Any(heuristic => heuristic.Name == request.Heuristic))
        {
            throw new ArgumentException("A split names one of the analyses that run.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > MaximumReasonLength)
        {
            throw new ArgumentException("A split needs a reason of at most 1 000 characters.", nameof(request));
        }
    }

    /// <inheritdoc/>
    public int UndoSplits(ICase @case, IReadOnlyCollection<long> splitSequences, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(splitSequences);
        var checkedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (checkedReason?.Length > MaximumReasonLength)
        {
            throw new ArgumentException("A reason is at most 1 000 characters.", nameof(reason));
        }

        var wanted = splitSequences.ToHashSet();
        return @case.Write(transaction =>
        {
            // The stored splits are what is undone, read in the same transaction, so that none is undone twice.
            var splits = EvidenceStore.ReadSplitHistory(transaction)
                .Where(entry => !entry.IsUndone && wanted.Contains(entry.Split.Sequence))
                .Select(entry => entry.Split)
                .ToList();
            if (splits.Count == 0)
            {
                return 0;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.SplitUndone,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Uppbrytning ångrad, kan slås ihop igen: {0}.",
                    string.Join(
                        "; ",
                        splits.Select(split => $"{split.First.Value} och {split.Second.Value} ({split.Heuristic})"))),
                JsonSerializer.Serialize(
                    new { splits = splits.Select(split => split.Sequence), reason = checkedReason })));
            foreach (var split in splits)
            {
                EvidenceStore.AppendUndo(transaction, split, checkedReason, journal);
            }

            return splits.Count;
        });
    }

    /// <inheritdoc/>
    public UndoSplitPreview? PreviewUndoSplits(
        ICase @case, IReadOnlyCollection<long> splitSequences, BitcoinAddress? from)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(splitSequences);
        var wanted = splitSequences.ToHashSet();
        var (evidence, history) = @case.Read(reader =>
            (EvidenceStore.ReadLatest(reader, withFindings: false).Evidence, EvidenceStore.ReadSplitHistory(reader)));
        var named = history.Where(entry => wanted.Contains(entry.Split.Sequence)).ToList();
        if (named.Count == 0)
        {
            return null;
        }

        var undone = named.Where(entry => !entry.IsUndone).Select(entry => entry.Split).ToList();
        if (undone.Count == 0)
        {
            return new UndoSplitPreview([], [], []);
        }

        var inForce = history.Where(entry => !entry.IsUndone).Select(entry => entry.Split).ToList();
        var before = new ClusterGraph(evidence, inForce);
        var after = new ClusterGraph(evidence, [.. inForce.Where(split => !wanted.Contains(split.Sequence))]);
        var sides = undone.SelectMany(split => new[] { split.First, split.Second }).ToList();
        var anchor = from is not null && sides.Any(side => before.ClusterOf(side).Contains(from)) ? from : sides[0];
        var now = before.ClusterOf(anchor).Addresses.ToHashSet();
        var rejoined = after.ClusterOf(anchor).Members.Where(member => !now.Contains(member.Address)).ToList();
        var heuristics = rejoined.Select(member => member.JoinedBy?.Evidence.Heuristic)
            .OfType<HeuristicId>()
            .Distinct()
            .ToList();
        return new UndoSplitPreview(undone, rejoined, heuristics);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SplitHistoryEntry> GetSplitHistory(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        var wanted = addresses.ToHashSet();
        if (wanted.Count == 0)
        {
            return [];
        }

        return
        [
            .. @case.Read(EvidenceStore.ReadSplitHistory)
                .Where(entry => wanted.Contains(entry.Split.First) || wanted.Contains(entry.Split.Second)),
        ];
    }

    /// <inheritdoc/>
    public Cluster GetCluster(ICase @case, BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Graph(@case).ClusterOf(address);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<BitcoinAddress, Cluster> GetClusters(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var graph = Graph(@case);
        var clusters = new Dictionary<BitcoinAddress, Cluster>();
        foreach (var address in addresses.Where(address => address is not null))
        {
            if (clusters.ContainsKey(address))
            {
                continue;
            }

            var cluster = graph.ClusterOf(address);
            foreach (var member in cluster.Addresses)
            {
                clusters.TryAdd(member, cluster);
            }
        }

        return clusters;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ClusterLink>? ExplainConnection(ICase @case, BitcoinAddress from, BitcoinAddress to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return Graph(@case).Path(from, to);
    }

    /// <inheritdoc/>
    public TransactionConclusions GetConclusions(ICase @case, Txid txid)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        var (evidence, findings) = @case.Read(reader => EvidenceStore.ReadLatest(reader, txid));
        return new TransactionConclusions(txid, evidence, findings);
    }

    /// <summary>
    /// Runs every analysis over one transaction. When one of them fails, nothing about the transaction is used: a
    /// failed CoinJoin analysis must never let the other analyses merge a CoinJoin's inputs.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="known">What the case knows around it.</param>
    /// <returns>Every analysis's result, or <see langword="null"/> when one failed.</returns>
    private List<HeuristicResult>? AnalyseOne(BitcoinTransaction transaction, KnownTransactions known)
    {
        var results = new List<HeuristicResult>(_heuristics.Count);
        foreach (var heuristic in _heuristics)
        {
            try
            {
                var result = heuristic.Analyse(transaction, known);
                if (!IsWellFormed(result, heuristic.Id, transaction, known))
                {
                    return null;
                }

                results.Add(result);
            }
#pragma warning disable CA1031 // An analysis is a step that fails on its own (CLAUDE.md, Arkitektur).
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                return null;
            }
        }

        return results;
    }

    /// <summary>
    /// Checks what an analysis gave before anything is sealed, since a sealed record can never be taken back: the
    /// right analysis and transaction, proposals of two or more different addresses that all appear in the
    /// transaction, change outputs that exist, reasons that are not empty, a basis the case knows of, and a veto of a
    /// known kind with a reason.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="heuristic">The analysis that gave it.</param>
    /// <param name="transaction">The transaction it is about.</param>
    /// <param name="known">What the case knows.</param>
    /// <returns><see langword="true"/> when it can be stored.</returns>
    private static bool IsWellFormed(
        HeuristicResult? result, HeuristicId heuristic, BitcoinTransaction transaction, KnownTransactions known)
    {
        if (result is null || result.Heuristic != heuristic || result.Txid != transaction.Txid
            || result.Proposals is null || result.Findings is null || result.Basis is null)
        {
            return false;
        }

        var addresses = transaction.Inputs.Select(input => input.Address)
            .Concat(transaction.Outputs.Select(output => output.Address))
            .OfType<BitcoinAddress>()
            .ToHashSet();
        bool ValidOutput(int? index) => index is null || (index >= 0 && index < transaction.Outputs.Count);
        var proposals = result.Proposals.All(proposal => proposal is not null
            && proposal.Addresses is not null
            && proposal.Addresses.Distinct().Count() == proposal.Addresses.Count
            && proposal.Addresses.Count >= 2
            && proposal.Addresses.All(address => address is not null && addresses.Contains(address))
            && Enum.IsDefined(proposal.Strength)
            && !string.IsNullOrWhiteSpace(proposal.Reason)
            && ValidOutput(proposal.ChangeOutput));
        var findings = result.Findings.All(finding => finding is not null
            && !string.IsNullOrWhiteSpace(finding.Reason)
            && ValidOutput(finding.ChangeOutput));
        var basis = result.Basis.All(txid => txid is not null && known.Find(txid) is not null);
        var veto = result.Veto is null
            || (Enum.IsDefined(result.Veto.Kind) && !string.IsNullOrWhiteSpace(result.Veto.Reason));
        return proposals && findings && basis && veto;
    }

    /// <summary>
    /// Weighs the results of one transaction and fingerprints them. A finding that cannot be stored (a kind not
    /// declared on <see cref="HeuristicFinding"/>, or one whose JSON fails) fails the transaction, not the run.
    /// </summary>
    /// <param name="results">Every analysis's result of the transaction.</param>
    /// <returns>The weighed results with their fingerprints, or <see langword="null"/> when one cannot be stored.</returns>
    private static List<(WeighedResult Result, string Fingerprint)>? Fingerprinted(List<HeuristicResult> results)
    {
        try
        {
            return [.. ResultWeighing.Weigh(results).Select(result => (result, EvidenceStore.Fingerprint(result)))];
        }
#pragma warning disable CA1031 // A finding that cannot be written fails its own transaction (CLAUDE.md, Arkitektur).
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// Says whether the run knows every transaction that any counting stored result of a transaction (the highest
    /// version of each analysis) rests on. The results of
    /// a transaction are weighed together, so they are replaced together or not at all: a narrower run never takes
    /// back what a wider one concluded.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <param name="latest">What is stored.</param>
    /// <param name="known">The transactions the run knows.</param>
    /// <returns><see langword="true"/> when the run may replace the transaction's results.</returns>
    private static bool KnowsBasis(
        Txid txid, Dictionary<(Txid Txid, HeuristicId Heuristic), StoredResult> latest, HashSet<Txid> known) =>
        latest.Where(entry => entry.Key.Txid == txid)
            .GroupBy(entry => entry.Key.Heuristic.Name)
            .Select(group => group.MaxBy(entry => entry.Key.Heuristic.Version))
            .SelectMany(entry => entry.Value.Basis)
            .All(known.Contains);

    /// <summary>
    /// Decides whether a new result is stored: when nothing is stored it must conclude something; when something is
    /// stored it must differ.
    /// </summary>
    /// <param name="result">The new result.</param>
    /// <param name="fingerprint">Its fingerprint.</param>
    /// <param name="latest">What is stored.</param>
    /// <returns><see langword="true"/> when it is stored.</returns>
    private static bool Replaces(
        WeighedResult result, string fingerprint, Dictionary<(Txid Txid, HeuristicId Heuristic), StoredResult> latest)
    {
        if (!latest.TryGetValue((result.Result.Txid, result.Result.Heuristic), out var stored))
        {
            return result.Proposals.Count > 0 || result.Result.Findings.Count > 0;
        }

        return stored.Fingerprint != fingerprint;
    }

    /// <summary>Builds the clusters from what the case has stored.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The clusters.</returns>
    private static ClusterGraph Graph(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var (evidence, splits) = @case.Read(reader =>
            (EvidenceStore.ReadLatest(reader, withFindings: false).Evidence, EvidenceStore.ReadSplits(reader)));
        return new ClusterGraph(evidence, splits);
    }

    /// <summary>Writes the journal's line for a run, in Swedish.</summary>
    /// <param name="transactions">How many transactions were analysed.</param>
    /// <param name="merges">New merges.</param>
    /// <param name="suggestions">New suggestions.</param>
    /// <param name="stopped">New stopped proposals.</param>
    /// <param name="coinJoins">New CoinJoins.</param>
    /// <returns>The line.</returns>
    private static string Summary(int transactions, int merges, int suggestions, int stopped, int coinJoins) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Klustring av {0} tx: {1} sammanslagningar, {2} förslag, {3} stoppade, {4} CoinJoin.",
            transactions,
            merges,
            suggestions,
            stopped,
            coinJoins);
}
