using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Runs the analyses, stores what they conclude with a journal entry per run, and builds the clusters from the
/// stored conclusions and the user's splits.
/// </summary>
internal sealed class ClusteringService : IClusteringService
{
    /// <summary>The longest reason a split may have.</summary>
    public const int MaximumReasonLength = 1_000;

    /// <summary>The analyses, in the order they run.</summary>
    private readonly IReadOnlyList<IClusteringHeuristic> _heuristics;

    /// <summary>Takes the analyses; two with the same name and version are refused.</summary>
    /// <param name="heuristics">The analyses, in the order they run.</param>
    /// <exception cref="ArgumentException">Two analyses have the same name and version.</exception>
    public ClusteringService(IEnumerable<IClusteringHeuristic> heuristics)
    {
        ArgumentNullException.ThrowIfNull(heuristics);
        _heuristics = [.. heuristics];
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
        var weighed = new List<(WeighedResult Result, string Fingerprint)>();
        foreach (var transaction in known.All)
        {
            if (AnalyseOne(transaction, known) is { } results)
            {
                weighed.AddRange(ResultWeighing.Weigh(results)
                    .Select(result => (result, EvidenceStore.Fingerprint(result))));
            }
            else
            {
                failed.Add(transaction.Txid);
            }
        }

        var txids = known.All.Select(transaction => transaction.Txid).ToHashSet();

        return @case.Write(transaction =>
        {
            var latest = EvidenceStore.LatestFingerprints(transaction, txids);
            var changed = weighed.Where(entry =>
                !(latest.TryGetValue((entry.Result.Result.Txid, entry.Result.Result.Heuristic), out var stored)
                    && stored == entry.Fingerprint)).ToList();
            if (changed.Count == 0)
            {
                return new ClusteringRun(null, known.Count, 0, 0, 0, 0, [], failed);
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
                })));
            foreach (var (result, fingerprint) in changed)
            {
                EvidenceStore.Append(transaction, result, fingerprint, journal);
            }

            return new ClusteringRun(
                journal, known.Count, changed.Count, merges, suggestions, stopped, coinJoins, failed);
        });
    }

    /// <inheritdoc/>
    public ClusterSplit Split(ICase @case, SplitRequest request)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.First);
        ArgumentNullException.ThrowIfNull(request.Second);
        if (request.First == request.Second)
        {
            throw new ArgumentException("An address cannot be split from itself.", nameof(request));
        }

        _ = new HeuristicId(request.Heuristic, 1);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > MaximumReasonLength)
        {
            throw new ArgumentException("A split needs a reason of at most 1 000 characters.", nameof(request));
        }

        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.Split,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Uppbrytning: {0} och {1} har inte samma ägare ({2}).",
                    request.First.Value,
                    request.Second.Value,
                    request.Heuristic),
                JsonSerializer.Serialize(new
                {
                    first = request.First.Value,
                    second = request.Second.Value,
                    heuristic = request.Heuristic,
                    reason = request.Reason,
                })));
            return EvidenceStore.AppendSplit(transaction, request, journal);
        });
    }

    /// <inheritdoc/>
    public Cluster GetCluster(ICase @case, BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Graph(@case).ClusterOf(address);
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
                if (result.Heuristic != heuristic.Id || result.Txid != transaction.Txid)
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

    /// <summary>Builds the clusters from what the case has stored.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The clusters.</returns>
    private static ClusterGraph Graph(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var (evidence, splits) = @case.Read(reader =>
            (EvidenceStore.ReadLatest(reader).Evidence, EvidenceStore.ReadSplits(reader)));
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
