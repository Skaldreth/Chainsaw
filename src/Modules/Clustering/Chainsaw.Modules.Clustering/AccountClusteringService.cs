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
    public AccountClusteringRun Analyse(
        ICase @case,
        Chain chain,
        IReadOnlyCollection<EthereumTransfer> transfers,
        IReadOnlyCollection<EthereumFee> fees)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(fees);
        if (!chain.IsEvm())
        {
            throw new ArgumentException("The account heuristics read chains on Ethereum's machine.", nameof(chain));
        }

        var onChain = transfers.Where(transfer => transfer.From.Chain == chain && transfer.To.Chain == chain).ToList();
        var paid = fees.Where(fee => fee.Payer.Chain == chain).ToList();
        List<AccountFinding> found =
        [
            .. DepositSweepHeuristic.Analyse(onChain),
            .. GasFundingHeuristic.Analyse(onChain, paid),
        ];
        if (found.Count == 0)
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

    /// <inheritdoc/>
    public AccountClusteringRun Read(ICaseReader reader, Chain chain)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var evidence = ReadEvidence(reader, chain);
        var clusters = Clusters(evidence.Where(entry => entry.Strength == EvidenceStrength.Merge).ToList());
        var proposals = evidence
            .Where(entry => entry.Strength == EvidenceStrength.Suggestion)
            .Where(entry => !InOneCluster(entry.Accounts, clusters))
            .ToList();
        return new AccountClusteringRun(clusters, proposals);
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
            evidence.Add(new AccountEvidence(
                row.GetInt64(0),
                EvidenceStore.Heuristic(row.GetString(1), row.GetInt64(2)),
                chain,
                [.. EvidenceStore.Deserialize<List<string>>(row.GetString(3)).Select(text => Hash(chain, text))],
                [.. EvidenceStore.Deserialize<List<string>>(row.GetString(4)).Select(text => Account(chain, text))],
                Enum.TryParse<EvidenceStrength>(row.GetString(5), out var strength)
                    ? strength
                    : throw new CaseException(CaseError.Corrupt),
                row.GetString(6),
                row.GetInt64(7),
                EvidenceStore.ParseTime(row.GetString(8))));
        }

        return evidence;
    }

    /// <summary>Joins the accounts of the merging conclusions into clusters.</summary>
    /// <param name="merging">The conclusions that merge by themselves.</param>
    /// <returns>The clusters of two or more accounts, ordered by their first member.</returns>
    private static List<AccountCluster> Clusters(IReadOnlyList<AccountEvidence> merging)
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

        foreach (var entry in merging)
        {
            foreach (var account in entry.Accounts)
            {
                parent.TryAdd(account, account);
            }

            var first = Root(entry.Accounts[0]);
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
            .Select(group => Cluster([.. group], merging))
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
