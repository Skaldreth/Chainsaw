using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>The checks and suggestions, sealed in the case (see <see cref="ICheckService"/>).</summary>
internal sealed class CheckService : ICheckService
{
    /// <summary>What a run says about an analysis that failed.</summary>
    public const string FailedText = "Analysen {0} misslyckades och gav inget den här gången.";

    /// <summary>The analyses that run on Bitcoin, in order.</summary>
    private readonly IReadOnlyList<ICheckAnalysis> _analyses;

    /// <summary>The clustering, whose conclusions the analyses read.</summary>
    private readonly IClusteringService _clustering;

    /// <summary>The clock, for when a run was made.</summary>
    private readonly IClock _clock;

    /// <summary>The labels, for the victims; <see langword="null"/> when the module is not registered.</summary>
    private readonly IAttributionService? _attribution;

    /// <summary>
    /// What the case has of the other chains for the cross-chain analysis; <see langword="null"/> when not registered.
    /// </summary>
    private readonly ICrossChainLookup? _crossChain;

    /// <summary>
    /// Where an approved continuation on another chain is linked; <see langword="null"/> when Tracing is not
    /// registered, and then such an approval is refused.
    /// </summary>
    private readonly ICrossChainLinks? _links;

    /// <summary>Creates the service.</summary>
    /// <param name="analyses">Every registered analysis; those for Bitcoin run, in the order registered.</param>
    /// <param name="clustering">The clustering.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="attribution">
    /// The labels, so that the victims are known (<see cref="CheckInput.Protected"/>); <see langword="null"/> when
    /// Attribution is not registered, and then only the start points are.
    /// </param>
    /// <param name="crossChain">
    /// What the case has of the other chains (read only, sending nothing), or <see langword="null"/>.
    /// </param>
    /// <param name="links">Where an approved continuation on another chain is linked, or <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">Two analyses have the same name.</exception>
    public CheckService(
        IEnumerable<ICheckAnalysis> analyses,
        IClusteringService clustering,
        IClock clock,
        IAttributionService? attribution = null,
        ICrossChainLookup? crossChain = null,
        ICrossChainLinks? links = null)
    {
        ArgumentNullException.ThrowIfNull(analyses);
        var all = analyses.ToList();
        if (all.FirstOrDefault(analysis => analysis.Scope is null) is { } unscoped)
        {
            throw new InvalidOperationException($"The analysis {unscoped.Id} says no chains it covers.");
        }

        _analyses = [.. all.Where(analysis => analysis.Scope.Covers(Chain.Bitcoin))];
        if (_analyses.GroupBy(analysis => analysis.Id.Name).FirstOrDefault(group => group.Count() > 1) is { } twice)
        {
            throw new InvalidOperationException($"Two analyses are named {twice.Key}.");
        }

        _clustering = clustering;
        _clock = clock;
        _attribution = attribution;
        _crossChain = crossChain;
        _links = links;
    }

    /// <inheritdoc/>
    public IReadOnlyList<HeuristicId> Analyses => [.. _analyses.Select(analysis => analysis.Id)];

    /// <inheritdoc/>
    public CheckRun Run(ICase @case, TraceGraph graph)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(graph);
        var (results, decisions) = @case.Read(
            reader => (CheckStore.ReadResults(reader), CheckStore.ReadDecisions(reader)));
        var (approved, dismissed) = DecisionsOf(results, decisions);
        var problems = new List<string>();
        var input = InputOf(@case, graph) with
        {
            Approved = approved,
            Dismissed = dismissed,
            ApprovedWallets = ApprovedWalletsOf(@case, decisions),
            Graph = graph,
            CrossChain = CrossChainOf(@case, graph, problems),
        };
        var raw = new List<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding)>();
        var found = new List<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding, string Content)>();
        foreach (var analysis in _analyses)
        {
            try
            {
                var mine = analysis.Run(input)
                    .DistinctBy(finding => finding.Key, StringComparer.Ordinal)
                    .Select(finding => (analysis, new ResultKey(analysis.Id.Name, analysis.Id.Version, finding.Key),
                        finding))
                    .ToList();

                // Kept and encoded per analysis, so that a finding that cannot be sealed fails its analysis alone.
                var kept = Keep(mine, results, decisions);
                raw.AddRange(mine);
                found.AddRange(kept);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One analysis that fails, or gives a finding that cannot be sealed, must not keep the others'
                // findings from being sealed and shown.
                problems.Add(string.Format(CultureInfo.InvariantCulture, FailedText, analysis.Id));
            }
        }

        var sealedCount = 0;
        if (found.Any(item => !Same(results, item.Key, item.Content)))
        {
            var stamp = StampOf(results, decisions);
            (results, decisions, found, sealedCount) = @case.Write(
                transaction => SealNew(transaction, raw, found, stamp, input));
        }

        var checks = found.Select(item =>
        {
            var stored = results[item.Key];
            return new StoredCheck(
                stored.Sequence,
                item.Analysis.Id,
                item.Analysis.DisplayName,
                item.Finding,
                stored.JournalSequence,
                stored.RecordedUtc,
                decisions.TryGetValue(item.Key, out var made) ? made : []);
        }).ToList();
        return new CheckRun(checks, sealedCount, input.Transactions.Count, _clock.UtcNow, problems);
    }

    /// <summary>
    /// Reads what the case has of the other chains for the cross-chain analysis, sending nothing; a failure leaves it
    /// empty and is said, so that the other analyses still run.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="problems">Where a failure is said.</param>
    /// <returns>What was read.</returns>
    private CrossChainArrivals CrossChainOf(ICase @case, TraceGraph graph, List<string> problems)
    {
        if (_crossChain is null)
        {
            return CrossChainArrivals.Empty;
        }

        try
        {
            return _crossChain.FindAsync(@case, graph, LookupMode.CaseOnly).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            problems.Add(string.Format(CultureInfo.InvariantCulture, FailedText, CrossChainCheck.Identity));
            return CrossChainArrivals.Empty;
        }
    }

    /// <inheritdoc/>
    public CheckDecision Decide(ICase @case, DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAll(@case, [request])[0];
    }

    /// <inheritdoc/>
    public IReadOnlyList<CheckDecision> DecideAll(ICase @case, IReadOnlyList<DecisionRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(requests);
        var reasons = requests.Select(request => ReasonOf(request)).ToList();
        if (requests.Count == 0)
        {
            return [];
        }

        return @case.Write(transaction => DecideIn(transaction, requests, reasons));
    }

    /// <inheritdoc/>
    public int TakeBackMerges(ICase @case, IReadOnlyCollection<long> mergeSequences, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(mergeSequences);
        var checkedReason = Optional(reason, CheckRules.MaximumTextLength, nameof(reason));
        var wanted = mergeSequences.ToHashSet();
        return @case.Write(transaction =>
        {
            var merges = _clustering.GetMergeHistory(transaction)
                .Where(entry => !entry.IsUndone && wanted.Contains(entry.Merge.Sequence))
                .Select(entry => entry.Merge)
                .ToList();
            if (merges.Count == 0)
            {
                return 0;
            }

            var before = InForceCount(transaction);

            // A merge that an approval made is undone by taking the approval back, so that the suggestion waits
            // again and the panel and the clusters say the same; the take-back undoes the approval's merges.
            var approvals = merges.Select(merge => merge.Origin)
                .OfType<MergeOrigin>()
                .Where(origin => origin.Chain == CheckSchema.DecisionsChain)
                .Select(origin => origin.Sequence)
                .ToHashSet();
            var inForce = CheckStore.ReadDecisions(transaction).Values
                .Select(made => made.GroupBy(decision => decision.CandidateKey ?? string.Empty, StringComparer.Ordinal)
                    .Select(group => group.Last()))
                .SelectMany(latest => latest)
                .Where(decision => decision.Verdict == CheckVerdict.Approved && approvals.Contains(decision.Sequence))
                .OrderBy(decision => decision.Sequence)
                .ToList();
            var requests = inForce
                .Select(decision => DecisionRequest.TakeBack(decision) with { Reason = checkedReason })
                .ToList();
            if (requests.Count > 0)
            {
                DecideIn(transaction, requests, [.. requests.Select(_ => checkedReason)]);
            }

            // A merge whose approval is no longer in force (or that came from elsewhere) is undone on its own.
            var left = _clustering.GetMergeHistory(transaction)
                .Where(entry => !entry.IsUndone && wanted.Contains(entry.Merge.Sequence))
                .Select(entry => entry.Merge.Sequence)
                .ToList();
            if (left.Count > 0)
            {
                _clustering.UndoMerges(transaction, left, checkedReason);
            }

            return before - InForceCount(transaction);
        });
    }

    /// <summary>Counts the user's merges in force, to tell how many a taking back undid.</summary>
    /// <param name="reader">The case's transaction.</param>
    /// <returns>How many hold.</returns>
    private int InForceCount(ICaseReader reader) =>
        _clustering.GetMergeHistory(reader).Count(entry => !entry.IsUndone);

    /// <summary>
    /// Seals decisions in a transaction the caller holds, in order, each checked against what the ones before it
    /// sealed, and follows each in the clustering when it is about a wallet.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="requests">The decisions.</param>
    /// <param name="reasons">The checked reasons, one per decision.</param>
    /// <returns>The sealed decisions, in order.</returns>
    /// <exception cref="ArgumentException">One of the decisions is refused.</exception>
    private IReadOnlyList<CheckDecision> DecideIn(
        ICaseTransaction transaction, IReadOnlyList<DecisionRequest> requests, IReadOnlyList<string?> reasons)
    {
        var results = CheckStore.ReadResults(transaction);
        var decisions = CheckStore.ReadDecisions(transaction);
        var sealedNow = new List<CheckDecision>();
        for (var index = 0; index < requests.Count; index++)
        {
            var decision = SealOne(transaction, requests[index], reasons[index], results, decisions);
            FollowInClustering(transaction, requests[index], decision, results);
            FollowAcrossChains(transaction, requests[index], decision, results);
            var key = KeyOf(requests[index]);
            if (!decisions.TryGetValue(key, out var list))
            {
                decisions[key] = list = [];
            }

            list.Add(decision);
            sealedNow.Add(decision);
        }

        return sealedNow;
    }

    /// <summary>
    /// Follows a decision about a wallet in the clustering, in the same transaction, so that the two are sealed
    /// together or not at all: an approval merges the wallet's proposals as the user's judgement (<c>user v1</c>), and
    /// taking an approval back undoes the merges it made. Any other decision is left alone.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="request">The decision as asked for.</param>
    /// <param name="decision">The decision as sealed.</param>
    /// <param name="results">The sealed findings.</param>
    /// <exception cref="SuggestionChangedException">The wallet's proposals are no longer stored.</exception>
    /// <exception cref="ArgumentException">
    /// The clustering refuses the merge (for example because the user has split the addresses).
    /// </exception>
    private void FollowInClustering(
        ICaseTransaction transaction,
        DecisionRequest request,
        CheckDecision decision,
        Dictionary<ResultKey, SealedResult> results)
    {
        if (request.Analysis.Name != WalletCheck.Name)
        {
            return;
        }

        if (request.Verdict == CheckVerdict.Approved)
        {
            var finding = FindingCodec.Decode(results[KeyOf(request)].Content);
            var merges = WalletMerges(transaction, finding);
            if (merges.Count == 0)
            {
                throw new SuggestionChangedException(
                    "The clustering no longer proposes the wallet; run the checks again.");
            }

            _clustering.Merge(
                transaction, merges, decision.Reason, new MergeOrigin(CheckSchema.DecisionsChain, decision.Sequence));
        }
        else if (request.Verdict == CheckVerdict.Withdrawn && request.Undoes is { } undone)
        {
            var made = _clustering.GetMergeHistory(transaction)
                .Where(entry => !entry.IsUndone
                    && entry.Merge.Origin == new MergeOrigin(CheckSchema.DecisionsChain, undone))
                .Select(entry => entry.Merge.Sequence)
                .ToList();
            if (made.Count > 0)
            {
                _clustering.UndoMerges(transaction, made, decision.Reason);
            }
        }
    }

    /// <summary>
    /// Follows a decision about a continuation on another chain in the same transaction, so that the two are sealed
    /// together or not at all: approving a candidate links the payment into the bridge or swap to the candidate's
    /// transaction, as the user's judgement with the decision as its origin, and taking the approval back takes that
    /// link back. Any other decision is left alone.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="request">The decision as asked for.</param>
    /// <param name="decision">The decision as sealed.</param>
    /// <param name="results">The sealed findings.</param>
    /// <exception cref="InvalidOperationException">No links can be made here (Tracing is not registered).</exception>
    /// <exception cref="ArgumentException">
    /// The payment already continues on another chain (for example by another candidate's approval).
    /// </exception>
    private void FollowAcrossChains(
        ICaseTransaction transaction,
        DecisionRequest request,
        CheckDecision decision,
        Dictionary<ResultKey, SealedResult> results)
    {
        if (request.Analysis.Name != CrossChainCheck.Name || request.Verdict == CheckVerdict.Dismissed)
        {
            return;
        }

        var links = _links ?? throw new InvalidOperationException("Links to other chains cannot be made here.");
        if (request.Verdict == CheckVerdict.Withdrawn)
        {
            if (request.Undoes is { } undone)
            {
                links.UnlinkMadeBy(transaction, request.Analysis.ToString(), undone, decision.Reason);
            }

            return;
        }

        var finding = FindingCodec.Decode(results[KeyOf(request)].Content);
        var arrival = finding.Candidates.FirstOrDefault(candidate => candidate.Key == request.CandidateKey)?.Arrival;
        if (finding.Departure is not { } departure || arrival is null)
        {
            throw new SuggestionChangedException("The suggestion does not name both payments; run the checks again.");
        }

        links.Link(transaction, new CrossChainLinkDraft(
            departure.Coin,
            departure.Movement,
            arrival.Transaction,
            decision.Reason,
            new CrossChainOrigin(request.Analysis.ToString(), decision.Sequence)));
    }

    /// <inheritdoc/>
    public bool TakeBackLink(ICase @case, CrossChainLink link, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(link);
        if (link.Origin is not { } origin)
        {
            throw new ArgumentException("A link made by hand is taken back in Tracing.", nameof(link));
        }

        var checkedReason = Optional(reason, CheckRules.MaximumTextLength, nameof(reason));
        return @case.Write(transaction =>
        {
            if (ApprovalInForce(transaction, origin) is { } approval)
            {
                // Taking the approval back takes its link back too, so that the suggestion waits again.
                var request = DecisionRequest.TakeBack(approval) with { Reason = checkedReason };
                DecideIn(transaction, [request], [checkedReason]);
                return true;
            }

            var links = _links
                ?? throw new InvalidOperationException("Links to other chains cannot be taken back here.");
            return links.UnlinkMadeBy(transaction, origin.Analysis, origin.Decision, checkedReason) > 0;
        });
    }

    /// <summary>Gives the approval a link names as its origin, when it is still the decision in force.</summary>
    /// <param name="reader">The case's transaction.</param>
    /// <param name="origin">The link's origin.</param>
    /// <returns>The approval, or <see langword="null"/> when it was taken back or is not sealed.</returns>
    private static CheckDecision? ApprovalInForce(ICaseReader reader, CrossChainOrigin origin)
    {
        foreach (var made in CheckStore.ReadDecisions(reader).Values)
        {
            if (made.FirstOrDefault(decision => decision.Sequence == origin.Decision
                && decision.Analysis.ToString() == origin.Analysis) is not { } approval)
            {
                continue;
            }

            var latest = made.LastOrDefault(decision => decision.CandidateKey == approval.CandidateKey);
            return latest?.Sequence == approval.Sequence && approval.Verdict == CheckVerdict.Approved ? approval : null;
        }

        return null;
    }

    /// <summary>
    /// Gives the merges an approved wallet makes: one per proposal the clustering still has stored in the wallet's
    /// transactions whose addresses all lie in the wallet as the user saw it, each resting on its own transaction and
    /// saying what the proposal said (principle 3). Together they must join every address the user saw, and none
    /// other, so that an approval never stands for more or less than what was approved.
    /// </summary>
    /// <param name="reader">The case's transaction.</param>
    /// <param name="finding">The wallet, as sealed.</param>
    /// <returns>The merges.</returns>
    /// <exception cref="SuggestionChangedException">
    /// The proposals no longer join the wallet the user saw; the checks are run again first.
    /// </exception>
    private List<MergeRequest> WalletMerges(ICaseReader reader, CheckFinding finding)
    {
        var members = finding.Addresses.ToHashSet();
        var proposals = finding.Basis
            .Select(basis => basis.Txid)
            .Distinct()
            .SelectMany(txid => WalletCheck.ProposalsOf(_clustering.GetConclusions(reader, txid)))
            .Where(evidence => evidence.Addresses.All(members.Contains))
            .ToList();
        var joined = new UnionFind<BitcoinAddress>();
        foreach (var evidence in proposals)
        {
            foreach (var address in evidence.Addresses.Skip(1))
            {
                joined.Union(evidence.Addresses[0], address);
            }
        }

        if (joined.Groups() is not [var only] || !only.ToHashSet().SetEquals(members))
        {
            throw new SuggestionChangedException(
                "The clustering's proposals no longer join the wallet as it was shown; run the checks again.");
        }

        return
        [
            .. proposals.Select(evidence => new MergeRequest(
                [.. evidence.Addresses.Distinct()], evidence.Txid, evidence.Heuristic, GroundsOf(evidence))),
        ];
    }

    /// <summary>
    /// Gives what an approved proposal said, with what lowered it to a suggestion (for example a possible CoinJoin),
    /// so that the merge's <em>Varför?</em> says what spoke against it, as the wallet did when it was approved
    /// (principle 3); only the proposal's reason when both would not fit in a merge's grounds.
    /// </summary>
    /// <param name="evidence">The proposal.</param>
    /// <returns>The grounds.</returns>
    private static string GroundsOf(ClusterEvidence evidence)
    {
        var grounds = evidence.Adjustment is { } against
            ? $"{evidence.Reason} Det som talade emot: {against}"
            : evidence.Reason;
        return grounds.Length <= UserMerges.MaximumTextLength ? grounds : evidence.Reason;
    }

    /// <summary>
    /// Gives the wallets whose approval is in force with the merges it made that hold in the clustering, so that the
    /// wallet analysis shows them as approved and suggests only what is new.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="decisions">The decisions, by finding, oldest first.</param>
    /// <returns>The approved wallets, in the order they were approved.</returns>
    private IReadOnlyList<ApprovedWallet> ApprovedWalletsOf(
        ICase @case, Dictionary<ResultKey, List<CheckDecision>> decisions)
    {
        var approvals = decisions
            .Where(entry => entry.Key.Analysis == WalletCheck.Name)
            .Select(entry => entry.Value.LastOrDefault(decision => decision.CandidateKey is null))
            .OfType<CheckDecision>()
            .Where(decision => decision.Verdict == CheckVerdict.Approved)
            .OrderBy(decision => decision.Sequence)
            .ToList();
        if (approvals.Count == 0)
        {
            return [];
        }

        var merges = _clustering.GetMergeHistory(@case)
            .Where(entry => !entry.IsUndone && entry.Merge.Origin?.Chain == CheckSchema.DecisionsChain)
            .Select(entry => entry.Merge)
            .ToList();
        var wallets = new List<ApprovedWallet>();
        foreach (var approval in approvals)
        {
            var made = merges.Where(merge => merge.Origin!.Sequence == approval.Sequence).ToList();
            if (made.Count == 0)
            {
                continue;
            }

            IReadOnlyList<BitcoinAddress> addresses = [.. made.SelectMany(merge => merge.Addresses).Distinct()];
            var clusters = _clustering.GetClusters(@case, addresses);
            var broken = addresses.Any(address => !clusters[addresses[0]].Contains(address));
            wallets.Add(new ApprovedWallet(
                approval.Analysis,
                approval.FindingKey,
                addresses,
                [.. made.Select(merge => merge.Txid).Distinct()],
                broken));
        }

        return wallets;
    }

    /// <summary>Checks a decision's verdict and reason before anything is written.</summary>
    /// <param name="request">The decision.</param>
    /// <returns>The checked reason, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">The verdict is not known or the reason is too long.</exception>
    private static string? ReasonOf(DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Verdict))
        {
            throw new ArgumentException("The decision's verdict is not known.", nameof(request));
        }

        return Optional(request.Reason, CheckRules.MaximumTextLength, nameof(request));
    }

    /// <summary>Gives the key of the finding a decision is about.</summary>
    /// <param name="request">The decision.</param>
    /// <returns>The key.</returns>
    private static ResultKey KeyOf(DecisionRequest request) =>
        new(request.Analysis.Name, request.Analysis.Version, request.FindingKey);

    /// <summary>
    /// Checks one decision against the finding and the decisions sealed so far in the transaction, and seals it with
    /// its line in the journal.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="request">The decision.</param>
    /// <param name="reason">The checked reason.</param>
    /// <param name="results">The sealed findings.</param>
    /// <param name="decisions">The decisions sealed so far, by finding.</param>
    /// <returns>The sealed decision.</returns>
    /// <exception cref="ArgumentException">The decision is refused (see <see
    /// cref="ICheckService.Decide"/>).</exception>
    private static CheckDecision SealOne(
        ICaseTransaction transaction,
        DecisionRequest request,
        string? reason,
        Dictionary<ResultKey, SealedResult> results,
        Dictionary<ResultKey, List<CheckDecision>> decisions)
    {
        var key = KeyOf(request);
        if (!results.TryGetValue(key, out var result))
        {
            throw new ArgumentException("The suggestion is not sealed in the case.", nameof(request));
        }

        var finding = FindingCodec.Decode(result.Content);
        var candidate = finding.Candidates.FirstOrDefault(c => c.Key == request.CandidateKey);
        if (finding.Kind != CheckKind.Suggestion
            || (request.CandidateKey is not null && candidate is null)
            || (request.CandidateKey is null && finding.Candidates.Count > 0
                && request.Verdict == CheckVerdict.Approved))
        {
            throw new ArgumentException(
                "Only a suggestion is decided: a candidate of it, or the whole when it has none; a suggestion "
                + "with candidates is dismissed as a whole but approved through one of them.",
                nameof(request));
        }

        var made = decisions.GetValueOrDefault(key) ?? [];
        var stored = new StoredCheck(
            result.Sequence, request.Analysis, string.Empty, finding, result.JournalSequence, result.RecordedUtc, made);
        Refuse(request, stored);
        var verb = VerbOf(request.Verdict);
        var what = candidate is null
            ? finding.Title
            : $"{finding.Title}, kandidat {candidate.Address?.ToString() ?? candidate.Arrival?.To.ToString()}";
        var journal = transaction.AppendJournal(new JournalDraft(
            CheckJournalKinds.Decided,
            $"{verb}: {what} ({request.Analysis})",
            JsonSerializer.Serialize(new
            {
                analysis = request.Analysis.ToString(),
                finding = request.FindingKey,
                candidate = request.CandidateKey,
                verdict = request.Verdict.ToString(),
                undoes = request.Undoes,
                reason,
            })));
        return CheckStore.SealDecision(transaction, request, reason, result, journal);
    }

    /// <summary>
    /// Refuses a decision the suggestion's state does not allow: an approve or a dismiss on a settled suggestion or on
    /// a candidate with a decision in force, a dismissal of the whole once a candidate is approved, and a take-back
    /// that does not name the decision in force.
    /// </summary>
    /// <param name="request">The decision.</param>
    /// <param name="stored">The suggestion with the decisions sealed so far.</param>
    /// <exception cref="ArgumentException">The decision is refused.</exception>
    private static void Refuse(DecisionRequest request, StoredCheck stored)
    {
        var inForce = stored.DecisionFor(request.CandidateKey);
        if (request.Verdict == CheckVerdict.Withdrawn)
        {
            if (inForce is null || request.Undoes != inForce.Sequence)
            {
                throw new ArgumentException(
                    "The decision to take back is not the one in force about it.", nameof(request));
            }

            return;
        }

        if (request.Undoes is not null || stored.IsSettled || inForce is not null)
        {
            throw new ArgumentException(
                "The suggestion is settled already, or the candidate has a decision in force.", nameof(request));
        }

        if (request.CandidateKey is null
            && stored.Finding.Candidates.Any(c => stored.DecisionFor(c.Key)?.Verdict == CheckVerdict.Approved))
        {
            // Dismissing the whole would contradict an approved candidate; the rest is dismissed one by one.
            throw new ArgumentException(
                "A suggestion with an approved candidate is not dismissed as a whole.", nameof(request));
        }
    }

    /// <summary>Gives the journal's words for a verdict.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>For example <c>Förslag godkänt</c>.</returns>
    private static string VerbOf(CheckVerdict verdict) => verdict switch
    {
        CheckVerdict.Approved => "Förslag godkänt",
        CheckVerdict.Dismissed => "Förslag avfärdat",
        _ => "Beslut om förslag ångrat",
    };

    /// <summary>Says whether a decision is in force about a candidate or the whole: made and not taken back.</summary>
    /// <param name="decisions">The decisions about the finding, oldest first.</param>
    /// <param name="candidateKey">The candidate's key, or <see langword="null"/> for the whole.</param>
    /// <returns><see langword="true"/> when the latest is an approval or a dismissal.</returns>
    private static bool InForce(IReadOnlyList<CheckDecision> decisions, string? candidateKey) =>
        decisions.LastOrDefault(decision => decision.CandidateKey == candidateKey) is { } latest
        && latest.Verdict != CheckVerdict.Withdrawn;

    /// <inheritdoc/>
    public IReadOnlyList<OwnArea> GetAreas(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(AreaStore.Read);
    }

    /// <inheritdoc/>
    public OwnArea AddArea(ICase @case, OwnAreaDraft draft)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        var name = Required(draft.Name, CheckRules.MaximumNameLength, nameof(draft));
        var note = Optional(draft.Note, CheckRules.MaximumTextLength, nameof(draft));
        var members = Members(draft.Members, nameof(draft));
        if (!Enum.IsDefined(draft.Sort) || !Enum.IsDefined(draft.Tint))
        {
            throw new ArgumentException("The area's sort or colour is not known.", nameof(draft));
        }

        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                CheckJournalKinds.AreaAdded,
                $"Egen yta: {name} ({members.Count.ToString(CultureInfo.InvariantCulture)} aktörer)",
                JsonSerializer.Serialize(new { name, sort = draft.Sort.ToString(), note, members })));
            var area = new OwnArea(
                Guid.NewGuid().ToString("N"),
                name,
                draft.Sort,
                draft.Tint,
                draft.SameActor,
                note,
                members,
                journal.Operator,
                journal.Utc,
                0);
            var record = AreaStore.Seal(transaction, AreaStore.AddAction, area, null, journal);
            return area with { Sequence = record.Sequence };
        });
    }

    /// <inheritdoc/>
    public OwnArea? RenameArea(ICase @case, string id, string name)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedName = Required(name, CheckRules.MaximumNameLength, nameof(name));
        return @case.Write(transaction =>
        {
            if (AreaStore.Read(transaction).FirstOrDefault(area => area.Id == id) is not { } area)
            {
                return null;
            }

            if (area.Name == checkedName)
            {
                return area;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                CheckJournalKinds.AreaRenamed,
                $"Egen yta bytte namn: {area.Name} till {checkedName}",
                JsonSerializer.Serialize(new { area = id, from = area.Name, to = checkedName })));
            var renamed = area with { Name = checkedName };
            AreaStore.Seal(transaction, AreaStore.RenameAction, renamed, null, journal);
            return renamed;
        });
    }

    /// <inheritdoc/>
    public bool RemoveArea(ICase @case, string id, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedReason = Optional(reason, CheckRules.MaximumTextLength, nameof(reason));
        return @case.Write(transaction =>
        {
            if (AreaStore.Read(transaction).FirstOrDefault(area => area.Id == id) is not { } area)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                CheckJournalKinds.AreaRemoved,
                $"Egen yta togs bort: {area.Name}",
                JsonSerializer.Serialize(new { area = id, name = area.Name, reason = checkedReason })));
            AreaStore.Seal(transaction, AreaStore.RemoveAction, area, checkedReason, journal);
            return true;
        });
    }

    /// <summary>
    /// Builds what the analyses run on: the graph's transactions in block order, the actors the trail ends at as
    /// mixers, and the clustering's conclusions about each transaction.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <returns>The input.</returns>
    private CheckInput InputOf(ICase @case, TraceGraph graph)
    {
        var transactions = graph.Transactions.Values
            .Select(known => known.Transaction)
            .OrderBy(transaction => transaction.Block?.Height ?? long.MaxValue)
            .ThenBy(transaction => transaction.Txid.Value, StringComparer.Ordinal)
            .ToList();
        var mixers = graph.Nodes
            .Where(node => node.GraphAddresses.Count > 0 && node.Ends.Any(end => end.Reason == TrailEndReason.Mixer))
            .Select(node => new KnownMixer(node.GraphAddresses, ServiceOf(node)))
            .ToList();
        var services = graph.Nodes
            .Where(node => node.GraphAddresses.Count > 0
                && node.Ends.All(end => end.Reason != TrailEndReason.Mixer)
                && node.Ends.Any(end => end.Reason is TrailEndReason.Exchange or TrailEndReason.Service))
            .Select(node => new KnownService(
                node.GraphAddresses,
                node.Ends.Any(end => end.Reason == TrailEndReason.Exchange),
                node.Ends.First(end => end.Reason is TrailEndReason.Exchange or TrailEndReason.Service).Explanation))
            .ToList();

        // One read for every transaction, so that a large graph is not read transaction by transaction.
        var conclusions = _clustering.GetConclusions(@case, [.. transactions.Select(transaction => transaction.Txid)])
            .Where(entry => entry.Value.Evidence.Count > 0 || entry.Value.Findings.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        return new CheckInput(transactions, mixers, conclusions)
        {
            Services = services,
            Owners = OwnersOf(@case, transactions),
            Protected = ProtectedOf(@case, graph),
        };
    }

    /// <summary>
    /// Gives the addresses an analysis must never count as a service's business: the start points' (and the payers of
    /// a start transaction) and the victims' that labels name.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <returns>The addresses.</returns>
    private HashSet<BitcoinAddress> ProtectedOf(ICase @case, TraceGraph graph)
    {
        var starts = graph.Nodes.Where(node => node.IsStartPoint).ToList();
        var startPayers = starts.Select(node => node.Transaction)
            .OfType<Txid>()
            .Select(txid => graph.Transactions.TryGetValue(txid, out var known) ? known.Transaction : null)
            .OfType<BitcoinTransaction>()
            .SelectMany(transaction => transaction.Inputs.Select(input => input.Address))
            .OfType<BitcoinAddress>();
        var victims = _attribution?.GetLabels(@case)
            .Where(label => label.Kind == LabelKind.Victim)
            .Select(label => label.Subject.Address)
            .OfType<BitcoinAddress>() ?? [];
        return [.. starts.SelectMany(node => node.GraphAddresses).Concat(startPayers).Concat(victims)];
    }

    /// <summary>
    /// Gives the owner of every address in the transactions as the clustering counts it: the smallest address of
    /// its cluster, built once for all of them.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The owner of each address that the clustering links to another.</returns>
    private Dictionary<BitcoinAddress, BitcoinAddress> OwnersOf(
        ICase @case, IReadOnlyList<BitcoinTransaction> transactions)
    {
        var addresses = transactions
            .SelectMany(transaction => transaction.Inputs.Select(input => input.Address)
                .Concat(transaction.Outputs.Select(output => output.Address)))
            .OfType<BitcoinAddress>()
            .Distinct()
            .ToList();
        if (addresses.Count == 0)
        {
            return [];
        }

        return _clustering.GetClusters(@case, addresses)
            .Where(entry => entry.Value.Members.Count > 1)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Addresses.MinBy(address => address.Value, StringComparer.Ordinal)!);
    }

    /// <summary>
    /// Gives the decisions in force about candidates and suggestions: the approved candidates with their coins, and
    /// the dismissals of a candidate or of a whole suggestion; each ordered by analysis, version, suggestion and then
    /// coin or candidate, so that an analysis that follows the user's judgements (<c>mixer-chain</c>,
    /// <c>mixer-parts</c>) runs on the same input every time.
    /// </summary>
    /// <param name="results">The sealed findings.</param>
    /// <param name="decisions">The decisions, by finding, oldest first.</param>
    /// <returns>The approvals and the dismissals in force.</returns>
    internal static (IReadOnlyList<ApprovedCandidate> Approved, IReadOnlyList<DismissedDecision> Dismissed)
        DecisionsOf(Dictionary<ResultKey, SealedResult> results, Dictionary<ResultKey, List<CheckDecision>> decisions)
    {
        var approved = new List<ApprovedCandidate>();
        var dismissed = new List<DismissedDecision>();
        foreach (var (key, made) in decisions)
        {
            var analysis = new HeuristicId(key.Analysis, (int)key.Version);
            var latest = made.GroupBy(decision => decision.CandidateKey ?? string.Empty, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToList();
            dismissed.AddRange(latest
                .Where(decision => decision.Verdict == CheckVerdict.Dismissed)
                .Select(decision => new DismissedDecision(analysis, key.FindingKey, decision.CandidateKey)));
            var inForce = latest
                .Where(decision => decision.CandidateKey is not null && decision.Verdict == CheckVerdict.Approved)
                .Select(decision => decision.CandidateKey!)
                .ToHashSet(StringComparer.Ordinal);
            if (inForce.Count > 0 && results.TryGetValue(key, out var result))
            {
                // A candidate on another chain has no coin; the analyses that build on approvals read Bitcoin's.
                approved.AddRange(FindingCodec.Decode(result.Content).Candidates
                    .Where(candidate => inForce.Contains(candidate.Key) && candidate.Txid is not null)
                    .Select(c => new ApprovedCandidate(analysis, key.FindingKey, c.Txid!, c.Output)));
            }
        }

        return (
            [
                .. approved
                    .OrderBy(a => a.Analysis.Name, StringComparer.Ordinal)
                    .ThenBy(a => a.Analysis.Version)
                    .ThenBy(a => a.FindingKey, StringComparer.Ordinal)
                    .ThenBy(a => a.Txid.Value, StringComparer.Ordinal)
                    .ThenBy(a => a.Output),
            ],
            [
                .. dismissed
                    .OrderBy(d => d.Analysis.Name, StringComparer.Ordinal)
                    .ThenBy(d => d.Analysis.Version)
                    .ThenBy(d => d.FindingKey, StringComparer.Ordinal)
                    .ThenBy(d => d.CandidateKey ?? string.Empty, StringComparer.Ordinal),
            ]);
    }

    /// <summary>
    /// Gives what a mixer's label says it is, so that two actors with the same service are known as the same mixer.
    /// </summary>
    /// <param name="node">The mixer's actor.</param>
    /// <returns>The first mixer end's explanation, which names the label.</returns>
    private static string ServiceOf(GraphNode node) =>
        node.Ends.First(end => end.Reason == TrailEndReason.Mixer).Explanation;

    /// <summary>
    /// Gives the newest sealed record and decision, so that a write can tell whether anything was sealed since they
    /// were read.
    /// </summary>
    /// <param name="results">The latest records.</param>
    /// <param name="decisions">The decisions.</param>
    /// <returns>The highest sequence of each, 0 when none.</returns>
    private static (long Results, long Decisions) StampOf(
        Dictionary<ResultKey, SealedResult> results, Dictionary<ResultKey, List<CheckDecision>> decisions) =>
        (results.Values.Select(result => result.Sequence).DefaultIfEmpty().Max(),
            decisions.Values.SelectMany(made => made).Select(decision => decision.Sequence).DefaultIfEmpty().Max());

    /// <summary>
    /// Keeps each finding's candidates the way the list shows them (<see cref="KeepCandidates"/>), with the decided
    /// candidates of the latest sealed record that the run no longer gives, and encodes it.
    /// </summary>
    /// <param name="raw">What the analyses found, with all their candidates.</param>
    /// <param name="results">The latest sealed records.</param>
    /// <param name="decisions">The decisions about each finding.</param>
    /// <returns>The findings as they are shown and sealed, with their JSON.</returns>
    private static List<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding, string Content)> Keep(
        IReadOnlyList<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding)> raw,
        Dictionary<ResultKey, SealedResult> results,
        Dictionary<ResultKey, List<CheckDecision>> decisions)
    {
        return
        [
            .. raw.Select(item =>
            {
                // The last sealed record is read only for a finding with decisions, whose decided candidates may
                // need to be carried over.
                var made = decisions.GetValueOrDefault(item.Key) ?? [];
                var before = made.Count > 0 && results.TryGetValue(item.Key, out var sealedBefore)
                    ? FindingCodec.Decode(sealedBefore.Content)
                    : null;
                var kept = KeepCandidates(item.Finding, made, before);
                return (item.Analysis, item.Key, kept, FindingCodec.Encode(kept));
            }),
        ];
    }

    /// <summary>
    /// Seals every finding that is new or changed, with one line in the journal. The records and the decisions are
    /// read again inside the write, so that a run made meanwhile is not sealed twice and a decision sealed meanwhile
    /// keeps its candidate in the list that is sealed.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="raw">What the analyses found, with all their candidates.</param>
    /// <param name="kept">The findings as kept from what was read before the write.</param>
    /// <param name="stamp">The newest record and decision read before the write (<see cref="StampOf"/>).</param>
    /// <param name="input">What the run ran on.</param>
    /// <returns>The latest records, the decisions, the findings as kept, and how many were sealed now.</returns>
    private (
        Dictionary<ResultKey, SealedResult> Results,
        Dictionary<ResultKey, List<CheckDecision>> Decisions,
        List<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding, string Content)> Found,
        int Sealed) SealNew(
        ICaseTransaction transaction,
        IReadOnlyList<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding)> raw,
        List<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding, string Content)> kept,
        (long Results, long Decisions) stamp,
        CheckInput input)
    {
        var results = CheckStore.ReadResults(transaction);
        var decisions = CheckStore.ReadDecisions(transaction);

        // The candidates are kept again only when something was sealed since they were read.
        var found = StampOf(results, decisions) == stamp ? kept : Keep(raw, results, decisions);
        var fresh = found.Where(item => !Same(results, item.Key, item.Content)).ToList();
        if (fresh.Count == 0)
        {
            return (results, decisions, found, 0);
        }

        var analyses = string.Join(", ", fresh.Select(item => item.Analysis.Id.ToString()).Distinct());
        var journal = transaction.AppendJournal(new JournalDraft(
            CheckJournalKinds.Ran,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Kontroller: {fresh.Count} nya eller ändrade resultat på {input.Transactions.Count} tx ({analyses})"),
            JsonSerializer.Serialize(new
            {
                analyses = fresh.Select(item => item.Analysis.Id.ToString()).Distinct(),
                findings = fresh.Select(item => new { analysis = item.Key.Analysis, key = item.Key.FindingKey }),
            })));
        foreach (var (_, key, finding, content) in fresh)
        {
            results[key] = CheckStore.Seal(transaction, key, finding, content, journal);
        }

        return (results, decisions, found, fresh.Count);
    }

    /// <summary>
    /// Keeps a suggestion's candidates the way the list shows them: every candidate the user has decided, so that a
    /// decision never disappears with a better candidate, and the best of the rest up to
    /// <see cref="CheckRules.MostWaitingCandidates"/>, so that a dismissed candidate frees its place. What only the
    /// dropped candidates rested on leaves the area and the basis.
    /// </summary>
    /// <param name="finding">What the analysis found, with all its candidates.</param>
    /// <param name="decisions">The decisions about it in this version.</param>
    /// <param name="sealedBefore">
    /// The finding as last sealed, or <see langword="null"/>: a decided candidate in it that the analysis no longer
    /// gives (for example because its transaction left the graph) is carried over, so that the decision stays shown.
    /// </param>
    /// <returns>
    /// The finding with the candidates kept, best first; equal scores stay in the analysis's order (for example the
    /// parts of a combination soonest first).
    /// </returns>
    internal static CheckFinding KeepCandidates(
        CheckFinding finding, IReadOnlyList<CheckDecision> decisions, CheckFinding? sealedBefore = null)
    {
        var decided = decisions
            .Select(decision => decision.CandidateKey)
            .OfType<string>()
            .Where(key => InForce(decisions, key))
            .ToHashSet(StringComparer.Ordinal);
        finding = CarryDecided(finding, sealedBefore, decided);
        if (finding.Candidates.Count == 0)
        {
            return finding;
        }

        var order = finding.Candidates.Select((c, index) => (c.Key, Index: index))
            .DistinctBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Index, StringComparer.Ordinal);
        var waiting = finding.Candidates.Where(c => !decided.Contains(c.Key)).Take(CheckRules.MostWaitingCandidates);
        var kept = finding.Candidates
            .Where(c => decided.Contains(c.Key))
            .Concat(waiting)
            .Select(c => (Candidate: c, Order: order[c.Key]))
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Order)
            .Select(item => item.Candidate)
            .ToList();
        var dropped = finding.Candidates.Except(kept).ToList();
        var droppedAddresses = dropped.Select(c => c.Address).OfType<BitcoinAddress>()
            .Except(kept.Select(c => c.Address).OfType<BitcoinAddress>())
            .ToHashSet();
        var droppedTxids = dropped.Select(c => c.Txid).OfType<Txid>()
            .Except(kept.Select(c => c.Txid).OfType<Txid>())
            .ToHashSet();
        return finding with
        {
            Addresses = [.. finding.Addresses.Where((a, index) => index == 0 || !droppedAddresses.Contains(a))],
            Basis = [.. finding.Basis.Where((b, index) => index == 0 || !droppedTxids.Contains(b.Txid))],
            Candidates = kept,
        };
    }

    /// <summary>
    /// Adds to a finding the decided candidates of its last sealed record that it no longer gives, with their
    /// addresses and basis.
    /// </summary>
    /// <param name="finding">What the analysis found now.</param>
    /// <param name="sealedBefore">The finding as last sealed, or <see langword="null"/>.</param>
    /// <param name="decided">The keys of the candidates with a decision in force.</param>
    /// <returns>The finding, with the carried candidates last.</returns>
    private static CheckFinding CarryDecided(
        CheckFinding finding, CheckFinding? sealedBefore, IReadOnlySet<string> decided)
    {
        var given = finding.Candidates.Select(candidate => candidate.Key).ToHashSet(StringComparer.Ordinal);
        var carried = (sealedBefore?.Candidates ?? [])
            .Where(candidate => decided.Contains(candidate.Key) && !given.Contains(candidate.Key))
            .ToList();
        if (carried.Count == 0)
        {
            return finding;
        }

        var txids = carried.Select(candidate => candidate.Txid).OfType<Txid>().ToHashSet();
        // The first basis entry is the anchor's (the deposit's transaction); only the candidates' entries are carried.
        var basis = sealedBefore!.Basis.Skip(1).Where(entry => txids.Contains(entry.Txid));
        return finding with
        {
            Addresses =
            [
                .. finding.Addresses.Concat(carried.Select(candidate => candidate.Address).OfType<BitcoinAddress>())
                    .Distinct(),
            ],
            Basis = [.. finding.Basis.Concat(basis).DistinctBy(entry => entry.Txid)],
            Candidates = [.. finding.Candidates.Concat(carried)],
        };
    }

    /// <summary>Says whether a finding is sealed as it is now.</summary>
    /// <param name="results">The latest records.</param>
    /// <param name="key">The finding's key.</param>
    /// <param name="content">The finding's JSON now.</param>
    /// <returns><see langword="true"/> when the latest record has the same fingerprint.</returns>
    private static bool Same(Dictionary<ResultKey, SealedResult> results, ResultKey key, string content) =>
        results.TryGetValue(key, out var stored) && stored.Fingerprint == FindingCodec.Fingerprint(content);

    /// <summary>Checks the members of an own area.</summary>
    /// <param name="members">The members.</param>
    /// <param name="parameter">The parameter's name, for the exception.</param>
    /// <returns>The members, without repeats, in order.</returns>
    /// <exception cref="ArgumentException">None, too many, or one that is not an address or a txid.</exception>
    private static IReadOnlyList<string> Members(IReadOnlyList<string>? members, string parameter)
    {
        var distinct = (members ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0 || distinct.Count > CheckRules.MaximumMembers)
        {
            throw new ArgumentException(
                $"En egen yta har minst en och högst {CheckRules.MaximumMembers} adresser.", parameter);
        }

        foreach (var member in distinct)
        {
            var valid = member.StartsWith(CheckRules.AddressMember, StringComparison.Ordinal)
                ? CheckRules.AddressOf(member) is not null
                : member.StartsWith(CheckRules.TransactionMember, StringComparison.Ordinal)
                    && Txid.TryParse(member[CheckRules.TransactionMember.Length..], out _);
            if (!valid)
            {
                throw new ArgumentException("En egen yta håller adresser och tx, inget annat.", parameter);
            }
        }

        return distinct;
    }

    /// <summary>Checks a required text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maximum">The longest allowed.</param>
    /// <param name="parameter">The parameter's name, for the exception.</param>
    /// <returns>The text, trimmed.</returns>
    /// <exception cref="ArgumentException">It is empty, too long or has control characters.</exception>
    private static string Required(string? text, int maximum, string parameter) =>
        Optional(text, maximum, parameter) ?? throw new ArgumentException("Texten behövs.", parameter);

    /// <summary>Checks an optional text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maximum">The longest allowed.</param>
    /// <param name="parameter">The parameter's name, for the exception.</param>
    /// <returns>The text, trimmed, or <see langword="null"/> when it is empty.</returns>
    /// <exception cref="ArgumentException">It is too long or has control characters other than new lines.</exception>
    private static string? Optional(string? text, int maximum, string parameter)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maximum || trimmed.Any(c => char.IsControl(c) && c is not '\n' and not '\r'))
        {
            throw new ArgumentException(
                $"Texten är för lång (högst {maximum} tecken) eller har otillåtna tecken.", parameter);
        }

        return trimmed;
    }
}
