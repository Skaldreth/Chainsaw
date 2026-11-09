using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Runs the analyses, stores what they conclude with a journal entry per run, stores the user's own merges and
/// splits, and builds the clusters from the stored conclusions, merges and splits.
/// </summary>
internal sealed class ClusteringService : IClusteringService
{
    /// <summary>The longest reason a split may have.</summary>
    public const int MaximumReasonLength = 1_000;

    /// <summary>The longest line the case's journal takes (Cases refuses a longer summary).</summary>
    private const int JournalSummaryLength = 1_000;

    /// <summary>Writes a run's journal data without the parts it does not have (<c>leftOut</c>).</summary>
    private static readonly JsonSerializerOptions SkipNull = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Every registered version of the analyses for the UTXO chains, grouped by name in the order the names were first
    /// registered, highest version first; the first that a case has not drawn back runs.
    /// </summary>
    private readonly IReadOnlyList<IReadOnlyList<IUtxoHeuristic>> _versions;

    /// <summary>
    /// Takes the analyses and keeps those registered for a UTXO chain (Bitcoin, Litecoin, Zcash); two with the same
    /// name and version are refused, and so is one that does not implement the UTXO contract. Each runs only on the
    /// transactions of the chains its scope covers.
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

        var forUtxo = all.Where(heuristic => heuristic.Scope.Model == LedgerModel.Utxo).ToList();
        if (forUtxo.FirstOrDefault(heuristic => heuristic is not IUtxoHeuristic) is { } misfit)
        {
            throw new ArgumentException(
                $"{misfit.Id} is registered for a UTXO chain but does not implement the UTXO contract.",
                nameof(heuristics));
        }

        var utxo = forUtxo.Cast<IUtxoHeuristic>().ToList();
        if (utxo.Select(heuristic => heuristic.Id).Distinct().Count() != utxo.Count)
        {
            throw new ArgumentException("Two analyses have the same name and version.", nameof(heuristics));
        }

        _versions =
        [
            .. utxo.GroupBy(heuristic => heuristic.Id.Name, StringComparer.Ordinal)
                .Select(group => (IReadOnlyList<IUtxoHeuristic>)
                    [.. group.OrderByDescending(heuristic => heuristic.Id.Version)]),
        ];
        Heuristics = [.. _versions.Select(versions => versions[0].Id)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<HeuristicId> Heuristics { get; }

    /// <inheritdoc/>
    public ClusteringRun Analyse(ICase @case, IReadOnlyCollection<BitcoinTransaction> transactions) =>
        Analyse(@case, transactions, []);

    /// <inheritdoc/>
    public ClusteringRun Analyse(
        ICase @case, IReadOnlyCollection<BitcoinTransaction> transactions, IReadOnlyCollection<Txid> gone)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(gone);
        var goneTxids = gone.ToHashSet();
        var known = new KnownTransactions(
            transactions, transactions.Where(found => goneTxids.Contains(found.Txid)));
        var running = Running(@case.Read(EvidenceStore.ReadWithdrawals));
        var failed = new List<Txid>();
        var analysed = new List<(Txid Txid, List<(WeighedResult Result, string Fingerprint)> Results)>();
        foreach (var transaction in known.All)
        {
            if (AnalyseOne(running, transaction, known) is { } results
                && Fingerprinted(results) is { } fingerprinted)
            {
                analysed.Add((transaction.Txid, fingerprinted));
            }
            else
            {
                failed.Add(transaction.Txid);
            }
        }

        var txids = known.All.Select(transaction => transaction.Txid).ToHashSet();
        List<Txid> leftOut = [.. goneTxids.OrderBy(txid => txid.Stored, StringComparer.Ordinal)];

        return @case.Write(transaction =>
        {
            var (latest, holding) = EvidenceStore.ReadResults(transaction, txids.Union(goneTxids).ToHashSet());
            var counting = CountingBasis(latest);
            var resting = analysed
                .Select(entry => (entry.Txid, Missing: Missing(entry.Txid, counting, holding, txids, goneTxids)))
                .Where(entry => entry.Missing.Count > 0)
                .ToList();
            var goneOwn = GoneOwn(transaction, goneTxids, counting, txids);
            HeldBackStore.Replace(transaction, [.. resting, .. goneOwn]);
            List<Txid> heldBack = [.. resting.Select(entry => entry.Txid), .. goneOwn.Select(entry => entry.Txid)];
            // A transaction whose new results would rest on a gone transaction is not redone this run, none of its
            // results: they are weighed together, so storing some would seal a weighing that points at nothing.
            List<Txid> notDrawn =
            [
                .. analysed.Where(entry => resting.All(held => held.Txid != entry.Txid)
                        && entry.Results.Any(result => result.Result.Result.Basis.Any(goneTxids.Contains)))
                    .Select(entry => entry.Txid),
            ];
            var notRedone = resting.Select(entry => entry.Txid).Concat(notDrawn).ToHashSet();
            var changed = analysed.Where(entry => !notRedone.Contains(entry.Txid))
                .SelectMany(entry => entry.Results)
                .Where(entry => Replaces(entry.Result, entry.Fingerprint, latest))
                .ToList();
            // A run that only holds something back stores nothing, so it writes nothing in the journal either; an app
            // that analyses often with few transactions would otherwise fill the journal with such lines. What it held
            // back is still in the run's result.
            if (changed.Count == 0 && failed.Count == 0)
            {
                return new ClusteringRun(null, known.Count, 0, 0, 0, 0, [], failed, heldBack)
                {
                    LeftOut = leftOut,
                    NotDrawn = notDrawn,
                };
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
                    heuristics = running.Select(heuristic => heuristic.Id.ToString()),
                    merges,
                    suggestions,
                    stopped,
                    coinJoins = coinJoins.Select(txid => txid.Value),
                    failed = failed.Select(txid => txid.Value),
                    heldBack = heldBack.Select(txid => txid.Value),
                    // Only when something was left out, so that a run told of nothing writes what it always did.
                    leftOut = leftOut.Count > 0 ? leftOut.Select(txid => txid.Value) : null,
                    notDrawn = notDrawn.Count > 0 ? notDrawn.Select(txid => txid.Value) : null,
                },
                SkipNull)));
            foreach (var (result, fingerprint) in changed)
            {
                EvidenceStore.Append(transaction, result, fingerprint, journal);
            }

            return new ClusteringRun(
                journal, known.Count, changed.Count, merges, suggestions, stopped, coinJoins, failed, heldBack)
            {
                LeftOut = leftOut,
                NotDrawn = notDrawn,
            };
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
    /// The addresses are the same, the heuristic is not one that runs, or the reason is too long.
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

        if (request.Heuristic != UserMerges.Name && !Heuristics.Any(heuristic => heuristic.Name == request.Heuristic))
        {
            throw new ArgumentException(
                "A split names one of the analyses that run, or the user's merges.", nameof(request));
        }

        // The reason is the user's choice (the user's decision 2026-11-07); only its length is checked.
        if (request.Reason is null || request.Reason.Length > MaximumReasonLength)
        {
            throw new ArgumentException("A split's reason is at most 1 000 characters.", nameof(request));
        }
    }

    /// <inheritdoc/>
    public int UndoSplits(ICase @case, IReadOnlyCollection<long> splitSequences, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(splitSequences);
        var checkedReason = OptionalReason(reason);
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
        var (evidence, merges, history) = @case.Read(reader => (
            EvidenceStore.ReadLatest(reader, withFindings: false).Evidence,
            MergeStore.ReadInForce(reader),
            EvidenceStore.ReadSplitHistory(reader)));
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
        var before = new ClusterGraph(evidence, merges, inForce);
        var after = new ClusterGraph(evidence, merges, [.. inForce.Where(split => !wanted.Contains(split.Sequence))]);
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
    public IReadOnlyList<SplitHistoryEntry> GetSplitHistory(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(EvidenceStore.ReadSplitHistory);
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

    /// <inheritdoc/>
    public IReadOnlyDictionary<Txid, TransactionConclusions> GetConclusions(
        ICase @case, IReadOnlyCollection<Txid> txids)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txids);
        var wanted = txids.ToHashSet();
        return @case.Read(reader => EvidenceStore.ReadLatest(reader, wanted));
    }

    /// <inheritdoc/>
    public VersionWithdrawal Withdraw(ICase @case, HeuristicId heuristic, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(heuristic);
        if (heuristic.Name == UserMerges.Name)
        {
            throw new ArgumentException(
                "The user's own merges are undone one by one, not drawn back.", nameof(heuristic));
        }

        var versions = _versions.FirstOrDefault(group => group[0].Id.Name == heuristic.Name);
        if (versions is null || versions.All(registered => registered.Id != heuristic))
        {
            throw new ArgumentException($"{heuristic} is not a registered analysis.", nameof(heuristic));
        }

        var checkedReason = OptionalReason(reason);
        return @case.Write(transaction =>
        {
            var inForce = EvidenceStore.ReadWithdrawals(transaction).Where(known => known.IsInForce).ToList();
            if (inForce.FirstOrDefault(known => known.Heuristic == heuristic) is { } earlier)
            {
                return earlier;
            }

            if (versions.All(registered => registered.Id == heuristic
                || inForce.Any(known => known.Heuristic == registered.Id)))
            {
                throw new ArgumentException(
                    $"{heuristic} is the only version of {heuristic.Name} left; it cannot be drawn back.",
                    nameof(heuristic));
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.VersionWithdrawn,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} dragen tillbaka: dess slutsatser räknas inte längre, men står kvar förseglade.",
                    heuristic),
                JsonSerializer.Serialize(new { heuristic = heuristic.ToString(), reason = checkedReason })));
            return EvidenceStore.AppendWithdrawal(transaction, heuristic, checkedReason, journal);
        });
    }

    /// <inheritdoc/>
    public VersionWithdrawalUndo? TakeBackWithdrawal(ICase @case, long withdrawalSequence, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedReason = OptionalReason(reason);
        return @case.Write(transaction =>
        {
            if (EvidenceStore.ReadWithdrawals(transaction)
                    .FirstOrDefault(known => known.Sequence == withdrawalSequence && known.IsInForce)
                is not { } withdrawal)
            {
                return null;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.WithdrawalTakenBack,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Tillbakadragandet av {0} är taget tillbaka: versionen körs och räknas igen.",
                    withdrawal.Heuristic),
                JsonSerializer.Serialize(new { withdrawal = withdrawal.Sequence, reason = checkedReason })));
            return EvidenceStore.AppendWithdrawalUndo(transaction, withdrawal, checkedReason, journal);
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<VersionWithdrawal> GetWithdrawals(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(EvidenceStore.ReadWithdrawals);
    }

    /// <inheritdoc/>
    public WithdrawPreview? PreviewWithdraw(ICase @case, HeuristicId heuristic)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(heuristic);
        var versions = _versions.FirstOrDefault(group => group[0].Id.Name == heuristic.Name);
        if (heuristic.Name == UserMerges.Name || versions is null || versions.All(known => known.Id != heuristic))
        {
            return null;
        }

        return @case.Read(reader =>
        {
            var inForce = EvidenceStore.ReadWithdrawals(reader).Where(known => known.IsInForce).ToList();
            var instead = versions.FirstOrDefault(registered => registered.Id != heuristic
                && inForce.All(known => known.Heuristic != registered.Id));
            if (inForce.Any(known => known.Heuristic == heuristic) || instead is null)
            {
                return null;
            }

            var merges = MergeStore.ReadInForce(reader);
            var splits = EvidenceStore.ReadSplits(reader);
            var before = EvidenceStore.ReadLatest(reader, withFindings: false).Evidence;
            var after = EvidenceStore.ReadLatestWithout(reader, heuristic);
            var changes = ClusterChanges.Between(
                new ClusterGraph(before, merges, splits), new ClusterGraph(after, merges, splits), before, after);
            return new WithdrawPreview(heuristic, instead.Id, changes);
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<HeldBackConclusion> GetHeldBack(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(reader =>
        {
            var held = HeldBackStore.Read(reader);
            var decisions = HeldBackStore.Latest(HeldBackStore.ReadDecisions(reader))
                .ToDictionary(decision => decision.Txid);
            var conclusions = EvidenceStore.ReadLatest(reader, held.Select(entry => entry.Txid).ToHashSet());
            return (IReadOnlyList<HeldBackConclusion>)
            [
                .. held.Select(entry => new HeldBackConclusion(
                        entry.Txid,
                        conclusions[entry.Txid].Evidence,
                        entry.Missing,
                        decisions.GetValueOrDefault(entry.Txid))
                    {
                        Findings = conclusions[entry.Txid].Findings,
                    })
                    // A conclusion let go no longer counts, so there is nothing left to hold back.
                    .Where(conclusion => conclusion.Evidence.Count > 0 || conclusion.Findings.Count > 0),
            ];
        });
    }

    /// <inheritdoc/>
    public HeldBackDecision? DecideHeldBack(ICase @case, Txid txid, ConclusionChoice choice, string? reason) =>
        DecideHeldBack(@case, txid, choice, reason, []);

    /// <inheritdoc/>
    public HeldBackDecision? DecideHeldBack(
        ICase @case, Txid txid, ConclusionChoice choice, string? reason, IReadOnlyCollection<Txid> gone)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        ArgumentNullException.ThrowIfNull(gone);
        if (choice == ConclusionChoice.Reopen)
        {
            throw new ArgumentException("A decision is taken back with ReopenHeldBack.", nameof(choice));
        }

        var checkedReason = OptionalReason(reason);
        return @case.Write(transaction =>
        {
            var held = HeldBackStore.Read(transaction).FirstOrDefault(entry => entry.Txid == txid);
            var (evidence, findings) = EvidenceStore.ReadLatest(transaction, txid);

            // The kept row is working state that is not sealed, so what it says was missing is checked against the
            // sealed conclusions' own holding basis before anything is sealed from it. A conclusion also rests on its
            // own transaction, which may be what is missing, but only when the caller says that it is gone.
            var basis = EvidenceStore.ReadResults(transaction, new HashSet<Txid> { txid }).Holding
                .GetValueOrDefault(txid, []);
            if (gone.Contains(txid))
            {
                basis.Add(txid);
            }

            var missing = held.Txid is null ? [] : held.Missing.Where(basis.Contains).ToList();
            if (missing.Count == 0 || (evidence.Count == 0 && findings.Count == 0))
            {
                return null;
            }

            var addresses = evidence.SelectMany(proposal => proposal.Addresses).Distinct().ToList();
            var summary = choice == ConclusionChoice.Keep
                ? "Slutsatsen om {0} står kvar fast den bygger på en tx som inte fanns i senaste körningen."
                : "Slutsatsen om {0} är släppt: den räknas inte längre, inte heller dess fynd, men står kvar "
                    + "förseglad.";
            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.HeldBackDecided,
                string.Format(CultureInfo.InvariantCulture, summary, txid.Value),
                JsonSerializer.Serialize(new
                {
                    txid = txid.Value,
                    missing = missing.Select(item => item.Value),
                    choice = choice.ToString(),
                    reason = checkedReason,
                })));
            return HeldBackStore.AppendDecision(transaction, txid, missing, addresses, choice, checkedReason, journal);
        });
    }

    /// <inheritdoc/>
    public HeldBackDecision? ReopenHeldBack(ICase @case, long decisionSequence, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedReason = OptionalReason(reason);
        return @case.Write(transaction =>
        {
            var decisions = HeldBackStore.ReadDecisions(transaction);
            if (decisions.FirstOrDefault(known => known.Sequence == decisionSequence) is not { } decision
                || decision.Choice == ConclusionChoice.Reopen
                || HeldBackStore.Latest(decisions).All(latest => latest.Sequence != decisionSequence))
            {
                return null;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.HeldBackDecided,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Beslutet om slutsatsen om {0} är taget tillbaka.",
                    decision.Txid.Value),
                JsonSerializer.Serialize(new
                {
                    txid = decision.Txid.Value,
                    undoes = decision.Sequence,
                    choice = nameof(ConclusionChoice.Reopen),
                    reason = checkedReason,
                })));
            return HeldBackStore.AppendDecision(
                transaction, decision.Txid, decision.Missing, decision.Addresses, ConclusionChoice.Reopen,
                checkedReason, journal, decision.Sequence);
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<HeldBackDecision> GetHeldBackDecisions(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(HeldBackStore.ReadDecisions);
    }

    /// <inheritdoc/>
    public TransactionConclusions GetConclusions(ICaseReader reader, Txid txid)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(txid);
        var (evidence, findings) = EvidenceStore.ReadLatest(reader, txid);
        return new TransactionConclusions(txid, evidence, findings);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ClusterMerge> Merge(
        ICaseTransaction transaction, IReadOnlyList<MergeRequest> requests, string? reason, MergeOrigin? origin)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            throw new ArgumentException("A merge has at least one part.", nameof(requests));
        }

        foreach (var request in requests)
        {
            CheckMerge(request);
        }

        var checkedReason = OptionalReason(reason);
        if (origin is not null && string.IsNullOrWhiteSpace(origin.Chain))
        {
            throw new ArgumentException("A merge's origin names its chain.", nameof(origin));
        }

        // The clusters as they stand, before the merges: what holds now must hold after them too (see below).
        var before = Graph(transaction).MergedKeys();
        var journal = transaction.AppendJournal(new JournalDraft(
            ClusteringJournalKinds.Merged,
            MergeSummary(requests),
            JsonSerializer.Serialize(new
            {
                merges = requests.Select(request => new
                {
                    addresses = request.Addresses.Select(address => address.Value),
                    txid = request.Txid.Value,
                    proposal = request.Proposal.ToString(),
                }),
                reason = checkedReason,
                origin = origin is null ? null : new { chain = origin.Chain, sequence = origin.Sequence },
            })));
        var merges = requests
            .Select(request => MergeStore.Append(transaction, request, checkedReason, origin, journal))
            .ToList();

        // Read in the same transaction, with the new merges. One that a split in force would refuse merges nothing,
        // and one that makes another link stop merging would break up a cluster the user never broke up (the user's
        // merges come first): either is refused here, and the whole transaction rolls back.
        var after = Graph(transaction);
        var refused = after.RefusedMerges();
        if (merges.Any(merge => refused.Contains(merge.Sequence)))
        {
            throw new MergeRefusedException(MergeRefusal.AcrossSplit);
        }

        if (!before.IsSubsetOf(after.MergedKeys()))
        {
            throw new MergeRefusedException(MergeRefusal.BreaksMerge);
        }

        return merges;
    }

    /// <inheritdoc/>
    public int UndoMerges(ICaseTransaction transaction, IReadOnlyCollection<long> mergeSequences, string? reason)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(mergeSequences);
        var checkedReason = OptionalReason(reason);
        var wanted = mergeSequences.ToHashSet();
        var merges = MergeStore.ReadInForce(transaction).Where(merge => wanted.Contains(merge.Sequence)).ToList();
        if (merges.Count == 0)
        {
            return 0;
        }

        var journal = transaction.AppendJournal(new JournalDraft(
            ClusteringJournalKinds.MergeUndone,
            string.Format(
                CultureInfo.InvariantCulture,
                "Sammanslagning av användaren ångrad: {0}.",
                string.Join(
                    "; ", merges.Select(merge => string.Join(", ", merge.Addresses.Select(address => address.Value))))),
            JsonSerializer.Serialize(new { merges = merges.Select(merge => merge.Sequence), reason = checkedReason })));
        foreach (var merge in merges)
        {
            MergeStore.AppendUndo(transaction, merge, checkedReason, journal);
        }

        return merges.Count;
    }

    /// <inheritdoc/>
    public UndoMergePreview? PreviewUndoMerges(ICase @case, IReadOnlyCollection<long> mergeSequences)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(mergeSequences);
        var wanted = mergeSequences.ToHashSet();
        var (evidence, history, splits) = @case.Read(reader => (
            EvidenceStore.ReadLatest(reader, withFindings: false).Evidence,
            MergeStore.ReadHistory(reader),
            EvidenceStore.ReadSplits(reader)));
        var named = history.Where(entry => wanted.Contains(entry.Merge.Sequence)).ToList();
        if (named.Count == 0)
        {
            return null;
        }

        var undone = named.Where(entry => !entry.IsUndone).Select(entry => entry.Merge).ToList();
        if (undone.Count == 0)
        {
            return new UndoMergePreview([], [], []);
        }

        var inForce = history.Where(entry => !entry.IsUndone).Select(entry => entry.Merge).ToList();
        var now = new ClusterGraph(evidence, inForce, splits).MergedKeys();
        var later = new ClusterGraph(evidence, [.. inForce.Where(merge => !wanted.Contains(merge.Sequence))], splits)
            .MergedLinks()
            .Where(link => !now.Contains(ClusterGraph.KeyOf(link.Evidence)))
            .ToList();
        var rejoined = later
            .SelectMany(link => link.Evidence.Addresses.Distinct()
                .Select(address => new ClusterMember(address, link, null)))
            .DistinctBy(member => member.Address)
            .ToList();
        var heuristics = later.Select(link => link.Evidence.Heuristic).Distinct().ToList();
        return new UndoMergePreview(undone, rejoined, heuristics);
    }

    /// <inheritdoc/>
    public IReadOnlyList<MergeHistoryEntry> GetMergeHistory(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(MergeStore.ReadHistory);
    }

    /// <inheritdoc/>
    public IReadOnlyList<MergeHistoryEntry> GetMergeHistory(ICaseReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return MergeStore.ReadHistory(reader);
    }

    /// <summary>Checks a merge before anything is stored.</summary>
    /// <param name="request">The merge.</param>
    /// <exception cref="ArgumentException">
    /// It has fewer than two different addresses, too many, or grounds that are empty or too long.
    /// </exception>
    private static void CheckMerge(MergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Txid);
        ArgumentNullException.ThrowIfNull(request.Proposal);
        if (request.Addresses is null || request.Addresses.Any(address => address is null)
            || request.Addresses.Count < 2 || request.Addresses.Count > UserMerges.MaximumAddresses
            || request.Addresses.Distinct().Count() != request.Addresses.Count)
        {
            throw new ArgumentException("A merge has at least two different addresses.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Grounds) || request.Grounds.Length > UserMerges.MaximumTextLength)
        {
            throw new ArgumentException(
                "A merge says what it rests on, in at most 1 000 characters.", nameof(request));
        }
    }

    /// <summary>
    /// Gives the journal's line for the user's merges: every part with its addresses, proposal and transaction when it
    /// fits on the line, and otherwise how many parts and addresses, since a wallet of many proposals (a peel chain's
    /// change) would not fit. The journal row's details always hold every part.
    /// </summary>
    /// <param name="requests">The merges.</param>
    /// <returns>The line.</returns>
    private static string MergeSummary(IReadOnlyList<MergeRequest> requests)
    {
        var full = string.Format(
            CultureInfo.InvariantCulture,
            "Sammanslagning av användaren ({0}): {1}.",
            UserMerges.Id,
            string.Join(
                "; ",
                requests.Select(request => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} har samma ägare ({1}, tx {2})",
                    string.Join(", ", request.Addresses.Select(address => address.Value)),
                    request.Proposal,
                    request.Txid.Value))));
        if (full.Length <= JournalSummaryLength)
        {
            return full;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "Sammanslagning av användaren ({0}): {1} adresser i {2} delar har samma ägare; varje del står i raden.",
            UserMerges.Id,
            requests.SelectMany(request => request.Addresses).Distinct().Count(),
            requests.Count);
    }

    /// <summary>Checks an optional reason: empty is none, and it may be at most 1 000 characters.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The reason trimmed, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    private static string? OptionalReason(string? reason)
    {
        var checkedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return checkedReason?.Length > MaximumReasonLength
            ? throw new ArgumentException("A reason is at most 1 000 characters.", nameof(reason))
            : checkedReason;
    }

    /// <summary>
    /// Gives the analyses that run in a case: of each name the highest registered version the case has not drawn
    /// back, in the order the names were registered; a name whose every version is drawn back runs none.
    /// </summary>
    /// <param name="withdrawals">The case's withdrawals.</param>
    /// <returns>The analyses to run.</returns>
    private List<IUtxoHeuristic> Running(IReadOnlyList<VersionWithdrawal> withdrawals)
    {
        var withdrawn = withdrawals.Where(withdrawal => withdrawal.IsInForce)
            .Select(withdrawal => withdrawal.Heuristic)
            .ToHashSet();
        return
        [
            .. _versions.Select(versions => versions.FirstOrDefault(heuristic => !withdrawn.Contains(heuristic.Id)))
                .OfType<IUtxoHeuristic>(),
        ];
    }

    /// <summary>
    /// Runs every analysis whose scope covers the transaction's chain over it. When one of them fails, nothing about the transaction is used: a
    /// failed CoinJoin analysis must never let the other analyses merge a CoinJoin's inputs.
    /// </summary>
    /// <param name="running">The analyses that run in the case (<see cref="Running"/>).</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="known">What the case knows around it.</param>
    /// <returns>Every analysis's result, or <see langword="null"/> when one failed.</returns>
    private static List<HeuristicResult>? AnalyseOne(
        IReadOnlyList<IUtxoHeuristic> running, BitcoinTransaction transaction, KnownTransactions known)
    {
        var results = new List<HeuristicResult>(running.Count);
        foreach (var heuristic in running.Where(heuristic => heuristic.Scope.Covers(transaction.Txid.Chain)))
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
        // A gone transaction can be a basis: where an address was seen stays as the case saw it.
        var basis = result.Basis.All(txid => txid is not null
            && (known.Find(txid) is not null || known.IsGone(txid)));
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
    /// Gives the gone transactions that still have counting stored conclusions, each with what is missing: the
    /// transaction itself, on which its own conclusions rest, and whatever else they rest on that the run does not
    /// know (sketch HB1a). A gone transaction with nothing counting (none concluded, or let go) is not held back.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="gone">The transactions the source no longer knows.</param>
    /// <param name="counting">The counting basis by transaction (<see cref="CountingBasis"/>).</param>
    /// <param name="known">The transactions the run knows.</param>
    /// <returns>The gone transactions held back, with what is missing.</returns>
    private static List<(Txid Txid, IReadOnlyList<Txid> Missing)> GoneOwn(
        ICaseReader reader, HashSet<Txid> gone, Dictionary<Txid, List<Txid>> counting, HashSet<Txid> known) =>
    [
        .. gone.OrderBy(txid => txid.Stored, StringComparer.Ordinal)
            .Where(txid => EvidenceStore.ReadLatest(reader, txid) is var (evidence, findings)
                && (evidence.Count > 0 || findings.Count > 0))
            .Select(txid => (txid, (IReadOnlyList<Txid>)
            [
                txid,
                .. counting.GetValueOrDefault(txid, []).Where(basis => basis != txid && !known.Contains(basis)),
            ])),
    ];

    /// <summary>
    /// Gives, per transaction, what its counting stored results rest on: the highest version of each analysis. Worked
    /// out once per run rather than once per transaction.
    /// </summary>
    /// <param name="latest">What is stored.</param>
    /// <returns>The basis by transaction.</returns>
    private static Dictionary<Txid, List<Txid>> CountingBasis(
        Dictionary<(Txid Txid, HeuristicId Heuristic), StoredResult> latest) =>
        latest.GroupBy(entry => entry.Key.Txid)
            .ToDictionary(
                byTxid => byTxid.Key,
                byTxid => byTxid.GroupBy(entry => entry.Key.Heuristic.Name)
                    .Select(group => group.MaxBy(entry => entry.Key.Heuristic.Version))
                    .SelectMany(entry => entry.Value.Basis)
                    .Distinct()
                    .ToList());

    /// <summary>
    /// Gives what a transaction's stored conclusions rest on that the run does not know, so that the run holds them
    /// back and the user can see what they were built on (sketch HB1a); empty when the run may redo them. The results
    /// of a transaction are weighed together, so they are replaced together or not at all: a narrower run never takes
    /// back what a wider one concluded. A gone transaction in the holding basis also holds them back, so that a
    /// withdrawal (KV1a), which re-weighs what the withdrawn version had weighed, cannot redo them without it.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <param name="counting">The counting basis by transaction (<see cref="CountingBasis"/>).</param>
    /// <param name="holding">The holding basis by transaction (<see cref="EvidenceStore.ReadResults"/>).</param>
    /// <param name="known">The transactions the run knows.</param>
    /// <param name="gone">The transactions the source no longer knows.</param>
    /// <returns>The missing transactions.</returns>
    private static IReadOnlyList<Txid> Missing(
        Txid txid,
        Dictionary<Txid, List<Txid>> counting,
        Dictionary<Txid, HashSet<Txid>> holding,
        HashSet<Txid> known,
        HashSet<Txid> gone) =>
    [
        .. counting.GetValueOrDefault(txid, []).Where(basis => !known.Contains(basis))
            .Concat(holding.GetValueOrDefault(txid, []).Where(gone.Contains))
            .Distinct(),
    ];

    /// <summary>
    /// Decides whether a new result is stored: when nothing is stored for its version it must conclude something, or
    /// replace what an older version concluded (which would otherwise still count); when something is stored it must
    /// differ.
    /// </summary>
    /// <param name="result">The new result.</param>
    /// <param name="fingerprint">Its fingerprint.</param>
    /// <param name="latest">What is stored.</param>
    /// <returns><see langword="true"/> when it is stored.</returns>
    private static bool Replaces(
        WeighedResult result, string fingerprint, Dictionary<(Txid Txid, HeuristicId Heuristic), StoredResult> latest)
    {
        var (txid, heuristic) = (result.Result.Txid, result.Result.Heuristic);
        if (!latest.TryGetValue((txid, heuristic), out var stored))
        {
            return result.Proposals.Count > 0 || result.Result.Findings.Count > 0
                || latest.Keys.Any(key => key.Txid == txid && key.Heuristic.Name == heuristic.Name
                    && key.Heuristic.Version < heuristic.Version);
        }

        return stored.Fingerprint != fingerprint;
    }

    /// <summary>Builds the clusters from what the case has stored.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The clusters.</returns>
    private static ClusterGraph Graph(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(Graph);
    }

    /// <summary>Builds the clusters from what the case has stored, in a transaction the caller holds.</summary>
    /// <param name="reader">The case's reader or write transaction.</param>
    /// <returns>The clusters.</returns>
    private static ClusterGraph Graph(ICaseReader reader) => new(
        EvidenceStore.ReadLatest(reader, withFindings: false).Evidence,
        MergeStore.ReadInForce(reader),
        EvidenceStore.ReadSplits(reader));

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
