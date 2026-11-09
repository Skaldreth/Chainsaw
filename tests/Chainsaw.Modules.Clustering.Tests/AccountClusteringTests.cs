using System.Numerics;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// The account heuristics against a key of synthetic transfers: deposit addresses emptied into a labelled service are
/// merged with it, the same into an unlabelled account (victims paying a fraudster) or a contract is not, each rule
/// on its own keeps one deposit out, and accounts given their first gas by the same payer are only proposed.
/// </summary>
public sealed class AccountClusteringTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Asset Usdt = Asset.Token(Chain.Ethereum, "0x" + new string('d', 40), "USDT", 6);

    private static readonly EthereumAddress Scammer = Address(2);

    private static readonly EthereumAddress Hot = Address(9);

    private readonly CaseHarness _harness = new();

    private int _next;

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Deposit_addresses_emptied_into_a_labelled_service_are_merged_with_it()
    {
        var deposits = new[] { Address(3), Address(4), Address(5) };
        var transfers = Sweeps(deposits);

        var run = Analyse(transfers, services: [Hot]);

        var cluster = Assert.Single(run.Clusters);
        Assert.Equal(Members(deposits.Append(Hot)), cluster.Members);
        Assert.Equal(Hot.ToChainAddress(), cluster.Collector);
        Assert.Equal(3, cluster.Evidence.Count);
        Assert.All(cluster.Evidence, entry => Assert.Equal(new HeuristicId("deposit-sweep", 1), entry.Heuristic));
        Assert.Null(run.ClusterOf(Scammer.ToChainAddress()));
        Assert.Empty(run.Proposals);
        Assert.True(_harness.Verify().IsIntact);
        Assert.Contains(_harness.Verify().Chains, chain => chain.Chain == "clustering.account_evidence");
        Assert.Equal(ClusteringJournalKinds.AccountsAnalysed, _harness.Case.ReadJournal().Last().Kind);
    }

    [Fact]
    public void Victims_paying_the_same_unlabelled_account_are_only_proposals()
    {
        var (exchange, victims) = (Address(1), new[] { Address(3), Address(4) });
        List<EthereumTransfer> transfers =
        [
            .. victims.SelectMany(victim => new[]
            {
                Ether(exchange, victim, 4_000, 0), Ether(victim, Scammer, 3_990, 5),
            }),
        ];

        var run = Analyse(transfers);

        Assert.Empty(run.Clusters);
        Assert.Equal(2, run.Proposals.Count);
        Assert.All(run.Proposals, proposal => Assert.Equal(EvidenceStrength.Suggestion, proposal.Strength));
        Assert.All(run.Proposals, proposal => Assert.Equal(Scammer.ToChainAddress(), proposal.Accounts[0]));
    }

    [Fact]
    public void A_contract_is_never_a_collector()
    {
        var transfers = Sweeps(Address(3), Address(4));

        var known = Analyse(transfers, services: [Hot], contracts: [Hot]);
        transfers.Add(Internal(Hot, Address(7), 6));
        var shown = Analyse(transfers, services: [Hot]);

        Assert.Empty(known.Clusters);
        Assert.Empty(shown.Clusters);
        Assert.Empty(shown.Proposals);
    }

    [Theory]
    [InlineData("keeps-some")]
    [InlineData("too-late")]
    [InlineData("sends-elsewhere")]
    [InlineData("paid-by-collector")]
    [InlineData("not-fully-read")]
    [InlineData("pending")]
    public void Each_rule_on_its_own_keeps_one_deposit_out(string broken)
    {
        var (kept, odd) = (new[] { Address(3), Address(4) }, Address(5));
        var transfers = Sweeps(kept);
        transfers.Add(Ether(Scammer, odd, 4_000, 1));
        transfers.Add(broken switch
        {
            "keeps-some" => Ether(odd, Hot, 2_000, 2),
            "too-late" => Ether(odd, Hot, 3_990, 100),
            "pending" => Ether(odd, Hot, 3_990, 2) with { Block = null },
            _ => Ether(odd, Hot, 3_990, 2),
        });
        if (broken == "sends-elsewhere")
        {
            transfers.Add(Ether(odd, Address(7), 1, 3));
        }

        if (broken == "paid-by-collector")
        {
            transfers.Add(Ether(Hot, odd, 1, 0));
        }

        var fullyRead = broken == "not-fully-read" ? kept : kept.Append(odd);

        var run = Analyse(transfers, services: [Hot], fullyRead: fullyRead);

        Assert.Equal(Members(kept.Append(Hot)), Assert.Single(run.Clusters).Members);
    }

    [Fact]
    public void A_deposit_used_again_is_swept_after_each_deposit()
    {
        var transfers = Sweeps(Address(3), Address(4));
        transfers.Add(Ether(Scammer, Address(3), 1_000, 40));
        transfers.Add(Ether(Address(3), Hot, 1_000, 41));

        var run = Analyse(transfers, services: [Hot]);

        Assert.Equal(3, Assert.Single(run.Clusters).Members.Count);
    }

    [Fact]
    public void The_same_conclusions_are_sealed_once_also_when_more_history_is_read_or_in_another_order()
    {
        var transfers = Sweeps(Address(3), Address(4));
        Analyse(transfers, services: [Hot]);
        var journal = _harness.Case.ReadJournal().Count;

        transfers.Add(Ether(Scammer, Address(3), 1_000, 40));
        transfers.Add(Ether(Address(3), Hot, 1_000, 41));
        transfers.Reverse();
        var again = Analyse(transfers, services: [Hot]);

        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
        Assert.Equal(2, _harness.Count("clustering_account_evidence"));
        Assert.Single(again.Clusters);
        _harness.Reopen();
        Assert.Single(_harness.Case.Read(reader => _harness.Accounts.Read(reader, Chain.Ethereum)).Clusters);
    }

    [Fact]
    public void Accounts_given_their_first_gas_by_the_same_payer_are_proposed_not_merged()
    {
        var (payer, first, second) = (Address(6), Address(2), Address(8));
        List<EthereumTransfer> transfers =
        [
            Gas(payer, first, 0), Gas(payer, second, 0),
            Token(Address(1), first, 0), Token(Address(1), second, 0),
            Token(first, Address(9), 1), Token(second, Address(7), 1),
        ];

        var run = Analyse(transfers, [Fee(first, 1), Fee(second, 1)]);

        Assert.Empty(run.Clusters);
        var proposal = Assert.Single(run.Proposals);
        Assert.Equal(EvidenceStrength.Suggestion, proposal.Strength);
        Assert.Equal(Members([first, second]), proposal.Accounts.OrderBy(address => address.Value));
        Assert.Equal(2, proposal.Transactions.Count);
    }

    [Theory]
    [InlineData("not-first")]
    [InlineData("much-coin")]
    [InlineData("sent-coin-first")]
    [InlineData("no-gas-paid")]
    public void Each_gas_rule_on_its_own_keeps_one_account_out(string broken)
    {
        var (payer, kept, odd) = (Address(6), new[] { Address(2), Address(8) }, Address(5));
        List<EthereumTransfer> transfers =
        [
            .. kept.SelectMany(account => new[] { Gas(payer, account, 0), Token(account, Address(7), 1) }),
        ];
        List<EthereumFee> fees = [.. kept.Select(account => Fee(account, 1))];
        transfers.Add(broken switch
        {
            "not-first" => Token(Address(1), odd, 0) with { Block = Block(0) with { Height = 19_999_999 } },
            "much-coin" => Ether(payer, odd, 1_000, 0),
            _ => Gas(payer, odd, 0),
        });
        transfers.Add(broken == "sent-coin-first" ? Ether(odd, Address(7), 0, 1) : Token(odd, Address(7), 1));
        if (broken == "not-first")
        {
            transfers.Add(Gas(payer, odd, 0));
        }

        if (broken != "no-gas-paid")
        {
            fees.Add(Fee(odd, 1));
        }

        var run = Analyse(transfers, fees, fullyRead: kept.Append(odd));

        Assert.Equal(Members(kept), Assert.Single(run.Proposals).Accounts.OrderBy(address => address.Value));
    }

    [Fact]
    public void An_approved_proposal_merges_as_the_user_s_judgement_and_its_undoing_takes_it_back()
    {
        var victims = new[] { Address(3), Address(4) };
        var proposed = Analyse([.. victims.SelectMany(victim => new[]
        {
            Ether(Address(1), victim, 4_000, 0), Ether(victim, Scammer, 3_990, 5),
        })]);
        var accounts = proposed.Proposals[0].Accounts;

        var merge = _harness.Case.Write(transaction =>
            _harness.Accounts.Merge(transaction, Chain.Ethereum, accounts, "checks.decisions:1", "Samma person."));
        var merged = Read();

        var cluster = Assert.Single(merged.Clusters);
        Assert.Equal(accounts.OrderBy(account => account.Value), cluster.Members);
        Assert.Contains(cluster.Evidence, evidence => evidence.Heuristic == new HeuristicId("user", 1));
        Assert.DoesNotContain(merged.Proposals, proposal => proposal.Accounts.All(cluster.Members.Contains));
        Assert.Equal(ClusteringJournalKinds.AccountsMerged, _harness.Case.ReadJournal().Last().Kind);

        _harness.Case.Write(transaction =>
        {
            _harness.Accounts.Undo(transaction, merge.Sequence, null);
            return 0;
        });

        Assert.Empty(Read().Clusters);
        Assert.True(Assert.Single(_harness.Case.Read(reader =>
            _harness.Accounts.Decisions(reader, Chain.Ethereum))).IsUndone);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_split_keeps_two_accounts_apart_until_it_is_undone()
    {
        var transfers = Sweeps(Address(3), Address(4), Address(5));
        Analyse(transfers, services: [Hot]);

        var split = _harness.Accounts.Split(_harness.Case, Hot.ToChainAddress(), Address(3).ToChainAddress(), null);

        Assert.Equal(Members([Address(4), Address(5), Hot]), Assert.Single(Read().Clusters).Members);
        Assert.Equal(ClusteringJournalKinds.AccountsSplit, _harness.Case.ReadJournal().Last().Kind);
        Assert.Throws<ArgumentException>(() => _harness.Case.Write(transaction => _harness.Accounts.Merge(
            transaction, Chain.Ethereum, [Hot.ToChainAddress(), Address(3).ToChainAddress()], "checks.decisions:2",
            null)));

        _harness.Case.Write(transaction =>
        {
            _harness.Accounts.Undo(transaction, split.Sequence, "Fel.");
            return 0;
        });

        Assert.Equal(4, Assert.Single(Read().Clusters).Members.Count);
    }

    [Fact]
    public void A_split_needs_two_accounts_on_one_chain() =>
        Assert.Throws<ArgumentException>(() => _harness.Accounts.Split(
            _harness.Case, Hot.ToChainAddress(), Hot.ToChainAddress(), null));

    [Fact]
    public void Only_chains_on_Ethereum_s_machine_are_analysed() =>
        Assert.Throws<ArgumentException>(() => _harness.Accounts.Analyse(
            _harness.Case, Chain.Bitcoin, Activity([], [], [], [], [])));

    private AccountClusteringRun Read() =>
        _harness.Case.Read(reader => _harness.Accounts.Read(reader, Chain.Ethereum));

    private AccountClusteringRun Analyse(
        List<EthereumTransfer> transfers,
        List<EthereumFee>? fees = null,
        IEnumerable<EthereumAddress>? services = null,
        IEnumerable<EthereumAddress>? contracts = null,
        IEnumerable<EthereumAddress>? fullyRead = null)
    {
        var read = fullyRead ?? transfers.SelectMany(transfer => new[] { transfer.From, transfer.To });
        return _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum,
            Activity(transfers, fees ?? [], read, services ?? [], contracts ?? []));
    }

    private static AccountActivity Activity(
        List<EthereumTransfer> transfers,
        List<EthereumFee> fees,
        IEnumerable<EthereumAddress> fullyRead,
        IEnumerable<EthereumAddress> services,
        IEnumerable<EthereumAddress> contracts) => new(
        transfers, fees, Set(fullyRead), Set(services), Set(contracts));

    private static HashSet<ChainAddress> Set(IEnumerable<EthereumAddress> addresses) =>
        [.. addresses.Select(address => address.ToChainAddress())];

    private static List<ChainAddress> Members(IEnumerable<EthereumAddress> addresses) =>
        [.. addresses.Select(address => address.ToChainAddress()).OrderBy(address => address.Value)];

    private List<EthereumTransfer> Sweeps(params EthereumAddress[] deposits) =>
    [
        .. deposits.SelectMany(deposit => new[]
        {
            Ether(Scammer, deposit, 4_000, 1), Ether(deposit, Hot, 3_990, 2),
        }),
    ];

    private static EthereumAddress Address(int n) => EthereumAddress.Parse("0x" + new string((char)('0' + n), 40));

    private EthereumTransfer Ether(EthereumAddress from, EthereumAddress to, long milliEther, int hour) => new(
        Hash(), EthereumTransferKind.Native, from, to,
        new AssetAmount(Asset.Ether, new BigInteger(milliEther) * BigInteger.Pow(10, 15)), 0, Block(hour));

    private EthereumTransfer Internal(EthereumAddress from, EthereumAddress to, int hour) =>
        Ether(from, to, 1, hour) with { Kind = EthereumTransferKind.Internal };

    private EthereumTransfer Gas(EthereumAddress from, EthereumAddress to, int hour) => new(
        Hash(), EthereumTransferKind.Native, from, to, new AssetAmount(Asset.Ether, BigInteger.Pow(10, 15)), 0,
        Block(hour));

    private EthereumTransfer Token(EthereumAddress from, EthereumAddress to, int hour) => new(
        Hash(), EthereumTransferKind.Token, from, to, new AssetAmount(Usdt, 1_000_000), 1, Block(hour));

    private EthereumFee Fee(EthereumAddress payer, int hour) =>
        new(Hash(), payer, new AssetAmount(Asset.Ether, 21_000), true, Block(hour));

    private EthereumTxHash Hash() => EthereumTxHash.Parse("0x" + (++_next).ToString("x64"));

    private static EthereumBlockReference Block(int hour) =>
        new(20_000_000 + hour, "0x" + new string('a', 64), Start.AddHours(hour));
}
