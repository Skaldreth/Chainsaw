using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Runs the account heuristics over what the graph has read of a chain on Ethereum's machine, seals each new
/// conclusion once under a journal entry, and builds the clusters from what is sealed: accounts joined by conclusions
/// that merge by themselves form a cluster, and proposals are left for the user (sketch KE1).
/// </summary>
internal sealed class AccountClusteringService : IAccountClustering
{
    /// <summary>How the lists of accounts and transactions are written.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);

    /// <inheritdoc/>
    public IReadOnlyList<HeuristicId> Heuristics { get; } = [DepositSweepHeuristic.Id, GasFundingHeuristic.Id];

    /// <inheritdoc/>
    public AccountClusteringRun Analyse(ICase @case, Chain chain, AccountActivity activity)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(activity);
        if (!chain.IsEvm())
        {
            throw new ArgumentException("The account heuristics read chains on Ethereum's machine.", nameof(chain));
        }

        var onChain = activity with
        {
            Transfers = [.. activity.Transfers.Where(transfer => transfer.From.Chain == chain
                && transfer.To.Chain == chain)],
            Fees = [.. activity.Fees.Where(fee => fee.Payer.Chain == chain)],
        };
        List<AccountFinding> found =
        [
            .. DepositSweepHeuristic.Analyse(onChain),
            .. GasFundingHeuristic.Analyse(onChain),
        ];
        // The graph is read often; a write is made only when there is something new to seal.
        var known = @case.Read(reader => Fingerprints(reader, chain));
        if (found.All(finding => known.Contains(finding.Fingerprint)))
        {
            return @case.Read(reader => Read(reader, chain));
        }

        return @case.Write(transaction =>
        {
            var sealedPrints = Fingerprints(transaction, chain);
            var fresh = found.Where(finding => !sealedPrints.Contains(finding.Fingerprint)).ToList();
            if (fresh.Count > 0)
            {
                Seal(transaction, chain, fresh);
            }

            return Read(transaction, chain);
        });
    }

    /// <summary>The user's own judgement, as the clustering of addresses names it.</summary>
    public static readonly HeuristicId User = new("user", 1);

    /// <inheritdoc/>
    public AccountClusteringRun Read(ICaseReader reader, Chain chain)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var decisions = Decisions(reader, chain).Where(decision => !decision.IsUndone).ToList();
        var splits = decisions.Where(decision => decision.Action == AccountDecisionAction.Split).ToList();
        bool Splits(AccountEvidence entry) => splits.Any(split => split.Accounts.All(entry.Accounts.Contains));
        var evidence = ReadEvidence(reader, chain).Where(entry => !Splits(entry)).ToList();
        var byHeuristics = evidence.Where(entry => entry.Strength == EvidenceStrength.Merge).ToList();
        var merging = byHeuristics
            .Concat(decisions.Where(decision => decision.Action == AccountDecisionAction.Merge).Select(UserEvidence))
            .Where(entry => !Splits(entry))
            .ToList();
        var apart = splits.Select(split => (split.Accounts[0], split.Accounts[1])).ToList();
        var clusters = Clusters(merging, apart);

        // A proposal the user approved stays, so that the panel shows it approved and the approval can be taken back;
        // only one the heuristics themselves already merged has nothing to add.
        var merged = Clusters(byHeuristics, apart);
        var proposals = evidence
            .Where(entry => entry.Strength == EvidenceStrength.Suggestion)
            .Where(entry => !InOneCluster(entry.Accounts, merged))
            .ToList();
        return new AccountClusteringRun(clusters, proposals);
    }

    /// <inheritdoc/>
    public AccountDecision Merge(
        ICaseTransaction transaction, Chain chain, IReadOnlyList<ChainAddress> accounts, string origin, string? reason)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        var members = Members(chain, accounts);
        var split = Decisions(transaction, chain).FirstOrDefault(decision => !decision.IsUndone
            && decision.Action == AccountDecisionAction.Split && decision.Accounts.All(members.Contains));
        if (split is not null)
        {
            throw new ArgumentException(
                "The user has split two of the accounts; undo the split first.", nameof(accounts));
        }

        var journal = transaction.AppendJournal(new JournalDraft(
            ClusteringJournalKinds.AccountsMerged,
            string.Create(CultureInfo.InvariantCulture,
                $"Slog ihop {members.Count} adresser på {chain} som din bedömning (user v1)."),
            JsonSerializer.Serialize(
                new { chain = chain.ToString(), accounts = members.Select(account => account.Value), origin }, Json)));
        return AppendDecision(transaction, AccountDecisionAction.Merge, chain, members, origin, null, reason, journal);
    }

    /// <inheritdoc/>
    public AccountDecision Split(ICase @case, ChainAddress first, ChainAddress second, string? reason)
    {
        ArgumentNullException.ThrowIfNull(second);
        return Split(@case, first, [second], reason)[0];
    }

    /// <inheritdoc/>
    public IReadOnlyList<AccountDecision> Split(
        ICase @case, ChainAddress account, IReadOnlyList<ChainAddress> others, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(others);
        var pairs = others.Where(other => other != account).Distinct()
            .Select(other => Members(account.Chain, [account, other]))
            .ToList();
        if (pairs.Count == 0)
        {
            throw new ArgumentException("A split is of two accounts on the same chain.", nameof(others));
        }

        // One transaction: the account is broken out from all the others, or from none.
        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                ClusteringJournalKinds.AccountsSplit,
                string.Create(CultureInfo.InvariantCulture,
                    $"Bröt ut {AccountFinding.Short(account)} från {pairs.Count} adresser på {account.Chain}."),
                JsonSerializer.Serialize(new
                {
                    chain = account.Chain.ToString(),
                    account = account.Value,
                    others = pairs.Select(pair => pair.First(member => member != account).Value),
                }, Json)));
            return (IReadOnlyList<AccountDecision>)
            [
                .. pairs.Select(pair => AppendDecision(
                    transaction, AccountDecisionAction.Split, account.Chain, pair, null, null, reason, journal)),
            ];
        });
    }

    /// <inheritdoc/>
    public void Undo(ICaseTransaction transaction, long decision, string? reason)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var undone = AllDecisions(transaction).FirstOrDefault(entry => entry.Sequence == decision
            && entry.Action != AccountDecisionAction.Undo)
            ?? throw new ArgumentException("No such decision about account clusters.", nameof(decision));
        if (undone.IsUndone)
        {
            return;
        }

        var summary = undone.Action == AccountDecisionAction.Merge
            ? "Ångrade en sammanslagning av adresser."
            : "Ångrade en uppbrytning av adresser.";
        var journal = transaction.AppendJournal(new JournalDraft(
            ClusteringJournalKinds.AccountDecisionUndone, summary,
            JsonSerializer.Serialize(new { undoes = decision }, Json)));
        AppendDecision(transaction, AccountDecisionAction.Undo, undone.Chain, [], null, decision, reason, journal);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AccountDecision> Decisions(ICaseReader reader, Chain chain)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return [.. AllDecisions(reader).Where(decision => decision.Chain == chain
            && decision.Action != AccountDecisionAction.Undo)];
    }

    /// <summary>Makes a user's merge into a conclusion that merges, so that the clusters are built from both.</summary>
    /// <param name="decision">The merge.</param>
    /// <returns>The conclusion.</returns>
    private static AccountEvidence UserEvidence(AccountDecision decision) => new(
        decision.Sequence, User, decision.Chain, [], decision.Accounts, EvidenceStrength.Merge,
        "Du godkände att adresserna har samma ägare; en bedömning, inte ett faktum."
            + (decision.Reason is { Length: > 0 } reason ? " Skäl: " + reason : string.Empty),
        decision.JournalSequence, decision.RecordedUtc);

    /// <summary>Checks and orders the accounts of a decision.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="accounts">The accounts.</param>
    /// <returns>Each once, by text.</returns>
    /// <exception cref="ArgumentException">Fewer than two, or one on another chain.</exception>
    private static List<ChainAddress> Members(Chain chain, IReadOnlyList<ChainAddress> accounts)
    {
        var members = accounts.Distinct().OrderBy(account => account.Value, StringComparer.Ordinal).ToList();
        return members.Count >= 2 && members.All(account => account.Chain == chain)
            ? members
            : throw new ArgumentException("A decision is about two or more accounts on its chain.", nameof(accounts));
    }

    /// <summary>Seals a decision about account clusters.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="action">What was decided.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="accounts">The accounts.</param>
    /// <param name="origin">What a merge came from.</param>
    /// <param name="undoes">The decision undone.</param>
    /// <param name="reason">Why.</param>
    /// <param name="journal">The journal entry.</param>
    /// <returns>The decision as sealed.</returns>
    private static AccountDecision AppendDecision(
        ICaseTransaction transaction,
        AccountDecisionAction action,
        Chain chain,
        IReadOnlyList<ChainAddress> accounts,
        string? origin,
        long? undoes,
        string? reason,
        JournalEntry journal)
    {
        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var record = transaction.AppendSealed(ClusteringSchema.AccountDecisionsChain,
        [
            action.ToString(), chain.ToString(),
            JsonSerializer.Serialize(accounts.Select(account => account.Value), Json), origin, undoes, note,
            journal.Sequence, journal.Utc.ToString("O", CultureInfo.InvariantCulture),
        ]);
        return new AccountDecision(
            record.Sequence, action, chain, accounts, origin, undoes, note, journal.Sequence, journal.Utc);
    }

    /// <summary>Reads every decision about account clusters, marking those a later one undid.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The decisions, in sequence order.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    private static List<AccountDecision> AllDecisions(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            """
            SELECT sequence, action, chain, accounts, origin, undoes, reason, journal_sequence, recorded_utc
            FROM clustering_account_decisions ORDER BY sequence
            """);
        using var row = command.ExecuteReader();
        var decisions = new List<AccountDecision>();
        while (row.Read())
        {
            if (!Enum.TryParse<AccountDecisionAction>(row.GetString(1), out var action) || !Enum.IsDefined(action)
                || int.TryParse(row.GetString(1), out _)
                || !Enum.TryParse<Chain>(row.GetString(2), out var chain) || !Enum.IsDefined(chain))
            {
                throw new CaseException(CaseError.Corrupt);
            }

            decisions.Add(new AccountDecision(
                row.GetInt64(0), action, chain,
                [.. EvidenceStore.Deserialize<List<string>>(row.GetString(3)).Select(text => Account(chain, text))],
                row.IsDBNull(4) ? null : row.GetString(4),
                row.IsDBNull(5) ? null : row.GetInt64(5),
                row.IsDBNull(6) ? null : row.GetString(6),
                row.GetInt64(7),
                EvidenceStore.ParseTime(row.GetString(8))));
        }

        var undone = decisions.Where(d => d.Action == AccountDecisionAction.Undo).Select(d => d.Undoes).ToHashSet();
        return [.. decisions.Select(decision => decision with { IsUndone = undone.Contains(decision.Sequence) })];
    }

    /// <summary>Seals the new conclusions under one journal entry that says what was found.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="fresh">The conclusions not sealed before.</param>
    private static void Seal(ICaseTransaction transaction, Chain chain, IReadOnlyList<AccountFinding> fresh)
    {
        var merges = fresh.Count(finding => finding.Strength == EvidenceStrength.Merge);
        var proposals = fresh.Count - merges;
        var journal = transaction.AppendJournal(new JournalDraft(
            ClusteringJournalKinds.AccountsAnalysed,
            string.Create(CultureInfo.InvariantCulture,
                $"Klustringen av konton på {chain}: {merges} sammanslagningar och {proposals} förslag."),
            JsonSerializer.Serialize(new
            {
                chain = chain.ToString(),
                heuristics = fresh.Select(finding => finding.Heuristic.ToString()).Distinct(),
                merges,
                proposals,
            }, Json)));
        var recorded = journal.Utc.ToString("O", CultureInfo.InvariantCulture);
        foreach (var finding in fresh)
        {
            transaction.AppendSealed(ClusteringSchema.AccountEvidenceChain,
            [
                finding.Heuristic.Name, (long)finding.Heuristic.Version, chain.ToString(),
                JsonSerializer.Serialize(finding.Transactions.Select(hash => hash.Value), Json),
                JsonSerializer.Serialize(finding.Accounts.Select(account => account.Value), Json),
                finding.Strength.ToString(), finding.Reason, finding.Fingerprint, journal.Sequence, recorded,
            ]);
        }
    }

    /// <summary>Reads the fingerprints of what is sealed for a chain, so that nothing is sealed twice.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The fingerprints.</returns>
    private static HashSet<string> Fingerprints(ICaseReader reader, Chain chain)
    {
        using var command = reader.CreateCommand(
            "SELECT fingerprint FROM clustering_account_evidence WHERE chain = $chain");
        command.AddParameter("$chain", chain.ToString());
        using var row = command.ExecuteReader();
        var prints = new HashSet<string>(StringComparer.Ordinal);
        while (row.Read())
        {
            prints.Add(row.GetString(0));
        }

        return prints;
    }

    /// <summary>Reads a chain's sealed conclusions in sequence order.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The conclusions.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    private static List<AccountEvidence> ReadEvidence(ICaseReader reader, Chain chain)
    {
        using var command = reader.CreateCommand(
            """
            SELECT sequence, heuristic, heuristic_version, transactions, accounts, strength, reason, journal_sequence,
                   recorded_utc
            FROM clustering_account_evidence WHERE chain = $chain ORDER BY sequence
            """);
        command.AddParameter("$chain", chain.ToString());
        using var row = command.ExecuteReader();
        var evidence = new List<AccountEvidence>();
        while (row.Read())
        {
            var accounts = EvidenceStore.Deserialize<List<string>>(row.GetString(4));
            if (accounts.Count < 2 || !Enum.TryParse<EvidenceStrength>(row.GetString(5), out var strength)
                || !Enum.IsDefined(strength) || int.TryParse(row.GetString(5), out _))
            {
                throw new CaseException(CaseError.Corrupt);
            }

            evidence.Add(new AccountEvidence(
                row.GetInt64(0),
                EvidenceStore.Heuristic(row.GetString(1), row.GetInt64(2)),
                chain,
                [.. EvidenceStore.Deserialize<List<string>>(row.GetString(3)).Select(text => Hash(chain, text))],
                [.. accounts.Select(text => Account(chain, text))],
                strength,
                row.GetString(6),
                row.GetInt64(7),
                EvidenceStore.ParseTime(row.GetString(8))));
        }

        return evidence;
    }

    /// <summary>Joins the accounts of the merging conclusions into clusters.</summary>
    /// <param name="merging">The conclusions that merge, in sequence order.</param>
    /// <param name="apart">
    /// The pairs the user split: a conclusion that would bring the two into one cluster, however indirectly, is left
    /// out, so that a split stands.
    /// </param>
    /// <returns>The clusters of two or more accounts, ordered by their first member.</returns>
    private static List<AccountCluster> Clusters(
        IReadOnlyList<AccountEvidence> merging, IReadOnlyList<(ChainAddress First, ChainAddress Second)> apart)
    {
        var parent = new Dictionary<ChainAddress, ChainAddress>();
        ChainAddress Root(ChainAddress account)
        {
            while (parent.TryGetValue(account, out var up) && up != account)
            {
                account = up;
            }

            return account;
        }

        bool Joins(ChainAddress one, ChainAddress other) => apart.Any(pair =>
            (Root(pair.First) == Root(one) && Root(pair.Second) == Root(other))
            || (Root(pair.First) == Root(other) && Root(pair.Second) == Root(one)));
        var counted = new List<AccountEvidence>();
        foreach (var entry in merging)
        {
            foreach (var account in entry.Accounts.Concat(apart.SelectMany(pair => new[] { pair.First, pair.Second })))
            {
                parent.TryAdd(account, account);
            }

            var first = Root(entry.Accounts[0]);
            if (entry.Accounts.Skip(1).Any(other => Root(other) != first && Joins(first, other)))
            {
                continue;
            }

            counted.Add(entry);
            foreach (var other in entry.Accounts.Skip(1))
            {
                var root = Root(other);
                if (root != first)
                {
                    parent[root] = first;
                }
            }
        }

        return [.. parent.Keys
            .GroupBy(Root)
            .Where(group => group.Count() > 1)
            .Select(group => Cluster([.. group], counted))
            .OrderBy(cluster => cluster.Members[0].Value, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Makes a cluster of its members, with the conclusions it rests on and the account the others empty into.
    /// </summary>
    /// <param name="members">The members.</param>
    /// <param name="merging">The conclusions that merge by themselves.</param>
    /// <returns>The cluster.</returns>
    private static AccountCluster Cluster(List<ChainAddress> members, IReadOnlyList<AccountEvidence> merging)
    {
        var inside = members.ToHashSet();
        var evidence = merging.Where(entry => inside.Contains(entry.Accounts[0])).ToList();
        var collector = evidence
            .Where(entry => entry.Heuristic.Name == AccountHeuristicNames.DepositSweep)
            .GroupBy(entry => entry.Accounts[0])
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        return new AccountCluster(
            [.. members.OrderBy(member => member.Value, StringComparer.Ordinal)], collector, evidence);
    }

    /// <summary>
    /// Tells whether all the accounts are already in one cluster, so that a proposal has nothing to add.
    /// </summary>
    /// <param name="accounts">The accounts.</param>
    /// <param name="clusters">The clusters.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    private static bool InOneCluster(IReadOnlyList<ChainAddress> accounts, IReadOnlyList<AccountCluster> clusters) =>
        clusters.Any(cluster => accounts.All(account => cluster.Members.Contains(account)));

    /// <summary>Reads a stored account.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The account as stored.</param>
    /// <returns>The account.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when it is not one.</exception>
    private static ChainAddress Account(Chain chain, string text) =>
        EthereumAddress.TryParse(chain, text, out var address)
            ? address.ToChainAddress()
            : throw new CaseException(CaseError.Corrupt);

    /// <summary>Reads a stored transaction hash.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The hash as stored.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when it is not one.</exception>
    private static EthereumTxHash Hash(Chain chain, string text) =>
        EthereumTxHash.TryParse(chain, text, out var hash) ? hash : throw new CaseException(CaseError.Corrupt);
}
