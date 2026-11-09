using System.Numerics;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// The account heuristics against a key of synthetic transfers: a service's deposit addresses emptied into its hot
/// wallet are merged, a forward that is not a sweep is not, and accounts given their first gas by the same payer are
/// only proposed.
/// </summary>
public sealed class AccountClusteringTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Asset Usdt = Asset.Token(Chain.Ethereum, "0x" + new string('d', 40), "USDT", 6);

    private readonly CaseHarness _harness = new();

    private int _next;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Deposit_addresses_emptied_into_the_same_account_are_one_service_with_it()
    {
        var (victim, scammer, hot) = (Address(1), Address(2), Address(9));
        var deposits = new[] { Address(3), Address(4), Address(5) };
        var transfers = new List<EthereumTransfer> { Ether(victim, scammer, 12_000, 0) };
        foreach (var deposit in deposits)
        {
            transfers.Add(Ether(scammer, deposit, 4_000, 1));
            transfers.Add(Ether(deposit, hot, 3_990, 2));
        }

        var run = _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum, transfers, []);

        var cluster = Assert.Single(run.Clusters);
        Assert.Equal([.. deposits.Append(hot).Select(a => a.ToChainAddress()).OrderBy(a => a.Value)], cluster.Members);
        Assert.Equal(hot.ToChainAddress(), cluster.Collector);
        Assert.Equal(3, cluster.Evidence.Count);
        Assert.All(cluster.Evidence, entry => Assert.Equal(new HeuristicId("deposit-sweep", 1), entry.Heuristic));
        Assert.Null(run.ClusterOf(scammer.ToChainAddress()));
        Assert.Empty(run.Proposals);
        Assert.True(_harness.Verify().IsIntact);
        Assert.Contains(_harness.Verify().Chains, chain => chain.Chain == "clustering.account_evidence");
        Assert.Equal(ClusteringJournalKinds.AccountsAnalysed, _harness.Case.ReadJournal().Last().Kind);
    }

    [Fact]
    public void The_same_conclusions_are_sealed_once_however_often_the_graph_is_read()
    {
        var transfers = Sweeps(Address(9), Address(3), Address(4));

        _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum, transfers, []);
        var journal = _harness.Case.ReadJournal().Count;
        var again = _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum, transfers, []);

        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
        Assert.Equal(2, _harness.Count("clustering_account_evidence"));
        Assert.Single(again.Clusters);
        Assert.Single(_harness.Case.Read(reader => _harness.Accounts.Read(reader, Chain.Ethereum)).Clusters);
    }

    [Fact]
    public void One_deposit_alone_or_a_forward_that_keeps_some_is_not_merged()
    {
        var (scammer, hot) = (Address(2), Address(9));
        List<EthereumTransfer> transfers =
        [
            Ether(scammer, Address(3), 4_000, 1), Ether(Address(3), hot, 3_990, 2),
            Ether(scammer, Address(4), 4_000, 1), Ether(Address(4), hot, 2_000, 2),
            Ether(scammer, Address(5), 4_000, 1), Ether(Address(5), hot, 3_990, 200),
        ];

        var run = _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum, transfers, []);

        Assert.Empty(run.Clusters);
        Assert.Equal(0, _harness.Count("clustering_account_evidence"));
    }

    [Fact]
    public void A_deposit_that_also_sends_elsewhere_or_got_money_from_the_collector_is_not_a_deposit()
    {
        var (scammer, hot) = (Address(2), Address(9));
        var transfers = Sweeps(hot, Address(3), Address(4));
        transfers.Add(Ether(Address(4), Address(7), 1, 3));
        transfers.Add(Ether(hot, Address(3), 1, 0));

        var run = _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum, transfers, []);

        Assert.Empty(run.Clusters);
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
        List<EthereumFee> fees = [Fee(first, 1), Fee(second, 1)];

        var run = _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum, transfers, fees);

        Assert.Empty(run.Clusters);
        var proposal = Assert.Single(run.Proposals);
        Assert.Equal(EvidenceStrength.Suggestion, proposal.Strength);
        Assert.Equal(
            [.. new[] { first, second }.Select(a => a.ToChainAddress()).OrderBy(a => a.Value)], proposal.Accounts);
        Assert.Equal(2, proposal.Transactions.Count);
    }

    [Fact]
    public void Gas_that_was_not_the_first_money_or_much_coin_proposes_nothing()
    {
        var (payer, first, second) = (Address(6), Address(2), Address(8));
        List<EthereumTransfer> transfers =
        [
            Token(Address(1), first, 0), Gas(payer, first, 1),
            Ether(payer, second, 1_000_000, 0),
            Token(first, Address(9), 2), Token(second, Address(9), 1),
        ];

        var run = _harness.Accounts.Analyse(_harness.Case, Chain.Ethereum, transfers, [Fee(first, 2), Fee(second, 1)]);

        Assert.Empty(run.Proposals);
    }

    [Fact]
    public void Only_chains_on_Ethereum_s_machine_are_analysed() =>
        Assert.Throws<ArgumentException>(() => _harness.Accounts.Analyse(_harness.Case, Chain.Bitcoin, [], []));

    private List<EthereumTransfer> Sweeps(EthereumAddress hot, params EthereumAddress[] deposits) =>
        [.. deposits.SelectMany(deposit => new[]
        {
            Ether(Address(2), deposit, 4_000, 1), Ether(deposit, hot, 3_990, 2),
        })];

    private static EthereumAddress Address(int n) => EthereumAddress.Parse("0x" + new string((char)('0' + n), 40));

    private EthereumTransfer Ether(EthereumAddress from, EthereumAddress to, long milliEther, int hour) => new(
        Hash(), EthereumTransferKind.Native, from, to,
        new AssetAmount(Asset.Ether, new BigInteger(milliEther) * BigInteger.Pow(10, 15)), 0, Block(hour));

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
