using System.Globalization;
using System.Text.Json;
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

    /// <summary>Creates the service.</summary>
    /// <param name="analyses">Every registered analysis; those for Bitcoin run, in the order registered.</param>
    /// <param name="clustering">The clustering.</param>
    /// <param name="clock">The clock.</param>
    /// <exception cref="InvalidOperationException">Two analyses have the same name.</exception>
    public CheckService(IEnumerable<ICheckAnalysis> analyses, IClusteringService clustering, IClock clock)
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
    }

    /// <inheritdoc/>
    public IReadOnlyList<HeuristicId> Analyses => [.. _analyses.Select(analysis => analysis.Id)];

    /// <inheritdoc/>
    public CheckRun Run(ICase @case, TraceGraph graph)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(graph);
        var input = InputOf(@case, graph);
        var problems = new List<string>();
        var found = new List<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding, string Content)>();
        var (results, decisions) = @case.Read(
            reader => (CheckStore.ReadResults(reader), CheckStore.ReadDecisions(reader)));
        foreach (var analysis in _analyses)
        {
            try
            {
                var mine = new List<(ICheckAnalysis, ResultKey, CheckFinding, string)>();
                foreach (var finding in analysis.Run(input).DistinctBy(finding => finding.Key, StringComparer.Ordinal))
                {
                    var key = new ResultKey(analysis.Id.Name, analysis.Id.Version, finding.Key);
                    var kept = KeepCandidates(finding, decisions.GetValueOrDefault(key) ?? []);
                    mine.Add((analysis, key, kept, FindingCodec.Encode(kept)));
                }

                found.AddRange(mine);
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
            (results, sealedCount) = @case.Write(transaction => SealNew(transaction, found, input));
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

    /// <inheritdoc/>
    public CheckDecision Decide(ICase @case, DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Verdict))
        {
            throw new ArgumentException("The decision's verdict is not known.", nameof(request));
        }

        var reason = Optional(request.Reason, CheckRules.MaximumTextLength, nameof(request));
        return @case.Write(transaction =>
        {
            var key = new ResultKey(request.Analysis.Name, request.Analysis.Version, request.FindingKey);
            if (!CheckStore.ReadResults(transaction).TryGetValue(key, out var result))
            {
                throw new ArgumentException("The suggestion is not sealed in the case.", nameof(request));
            }

            var finding = FindingCodec.Decode(result.Content);
            var candidate = finding.Candidates.FirstOrDefault(c => c.Key == request.CandidateKey);
            if (finding.Kind != CheckKind.Suggestion
                || (request.CandidateKey is not null && candidate is null)
                || (request.CandidateKey is null && finding.Candidates.Count > 0))
            {
                throw new ArgumentException(
                    "Only a suggestion is decided: a candidate of it, or the whole when it has none.", nameof(request));
            }

            var verb = request.Verdict == CheckVerdict.Approved ? "godkänt" : "avfärdat";
            var what = candidate is null ? finding.Title : $"{finding.Title}, kandidat {candidate.Address}";
            var journal = transaction.AppendJournal(new JournalDraft(
                CheckJournalKinds.Decided,
                $"Förslag {verb}: {what} ({request.Analysis})",
                JsonSerializer.Serialize(new
                {
                    analysis = request.Analysis.ToString(),
                    finding = request.FindingKey,
                    candidate = request.CandidateKey,
                    verdict = request.Verdict.ToString(),
                    reason,
                })));
            return CheckStore.SealDecision(transaction, request, reason, result, journal);
        });
    }

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
            .Select(node => new KnownMixer(node.GraphAddresses))
            .ToList();
        var conclusions = new Dictionary<Txid, TransactionConclusions>();
        foreach (var transaction in transactions)
        {
            var found = _clustering.GetConclusions(@case, transaction.Txid);
            if (found.Evidence.Count > 0 || found.Findings.Count > 0)
            {
                conclusions[transaction.Txid] = found;
            }
        }

        return new CheckInput(transactions, mixers, conclusions);
    }

    /// <summary>Seals every finding that is new or changed, with one line in the journal.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="found">The run's findings.</param>
    /// <param name="input">What the run ran on.</param>
    /// <returns>The latest records, and how many were sealed now.</returns>
    private (Dictionary<ResultKey, SealedResult> Results, int Sealed) SealNew(
        ICaseTransaction transaction,
        IReadOnlyList<(ICheckAnalysis Analysis, ResultKey Key, CheckFinding Finding, string Content)> found,
        CheckInput input)
    {
        // Read again inside the write, so that a run made meanwhile is not sealed twice.
        var results = CheckStore.ReadResults(transaction);
        var fresh = found.Where(item => !Same(results, item.Key, item.Content)).ToList();
        if (fresh.Count == 0)
        {
            return (results, 0);
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

        return (results, fresh.Count);
    }

    /// <summary>
    /// Keeps a suggestion's candidates the way the list shows them: every candidate the user has decided, so that a
    /// decision never disappears with a better candidate, and the best of the rest up to
    /// <see cref="CheckRules.MostWaitingCandidates"/>, so that a dismissed candidate frees its place. What only the
    /// dropped candidates rested on leaves the area and the basis.
    /// </summary>
    /// <param name="finding">What the analysis found, with all its candidates.</param>
    /// <param name="decisions">The decisions about it in this version.</param>
    /// <returns>The finding with the candidates kept, best first.</returns>
    internal static CheckFinding KeepCandidates(CheckFinding finding, IReadOnlyList<CheckDecision> decisions)
    {
        if (finding.Candidates.Count == 0)
        {
            return finding;
        }

        var decided = decisions
            .Select(decision => decision.CandidateKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var waiting = finding.Candidates.Where(c => !decided.Contains(c.Key)).Take(CheckRules.MostWaitingCandidates);
        var kept = finding.Candidates
            .Where(c => decided.Contains(c.Key))
            .Concat(waiting)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
        var dropped = finding.Candidates.Except(kept).ToList();
        var droppedAddresses = dropped.Select(c => c.Address).Except(kept.Select(c => c.Address)).ToHashSet();
        var droppedTxids = dropped.Select(c => c.Txid).Except(kept.Select(c => c.Txid)).ToHashSet();
        return finding with
        {
            Addresses = [.. finding.Addresses.Where((a, index) => index == 0 || !droppedAddresses.Contains(a))],
            Basis = [.. finding.Basis.Where((b, index) => index == 0 || !droppedTxids.Contains(b.Txid))],
            Candidates = kept,
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
                ? BitcoinAddress.TryParse(member[CheckRules.AddressMember.Length..], out _)
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
