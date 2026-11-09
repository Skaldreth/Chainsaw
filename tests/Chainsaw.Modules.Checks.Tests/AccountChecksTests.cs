using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The checks on the account chains against a key of synthetic transfers: a zero-value transfer from a look-alike of a
/// real counterparty is address poisoning, and a payout of a Tornado Cash pool's amount after a deposit is a candidate,
/// the depositor's own account first.
/// </summary>
public sealed class AccountChecksTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("1111aaaa", "bbbb2222");

    private static readonly EthereumAddress Real = Address("5555cafe", "beef6666");

    private static readonly EthereumAddress Mimic = Address("5555cafe", "beef6666", '9');

    private static readonly EthereumAddress Pool = EthereumAddress.Parse("0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936");

    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Harness _harness = new();

    private int _next;

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_nothing_from_a_look_alike_of_a_real_counterparty_is_poisoning()
    {
        var run = Run(Ether(Victim, Real, 1_000, 0), Ether(Mimic, Victim, 0, 1));

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == AccountPoisoningCheck.Name);
        Assert.Equal(CheckKind.Check, check.Finding.Kind);
        Assert.Equal(
            [Mimic.ToChainAddress(), Real.ToChainAddress(), Victim.ToChainAddress()], check.Finding.Accounts);
        Assert.Equal(Mimic.ToChainAddress(), check.Finding.Departure!.From);
        Assert.Contains(Real.Value, check.Finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_payment_or_an_address_that_is_not_alike_is_no_poisoning()
    {
        var other = Address("abcdabcd", "abcdabcd");

        var run = Run(Ether(Victim, Real, 1_000, 0), Ether(Mimic, Victim, 1_000, 1), Ether(other, Victim, 0, 2));

        Assert.DoesNotContain(run.Checks, check => check.Analysis.Name == AccountPoisoningCheck.Name);
    }

    [Fact]
    public void A_payout_of_the_pool_s_amount_after_a_deposit_is_a_candidate_the_own_account_first()
    {
        var stranger = Address("77777777", "77777777");
        var run = Run(
            Ether(Victim, Pool, 1_000, 0),
            Ether(Pool, stranger, 998, 2),
            Ether(Pool, Victim, 995, 30),
            Ether(Pool, Real, 890, 3),
            Ether(Pool, Mimic, 1_000, -1));

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == TornadoOutputsCheck.Name);
        Assert.Equal(CheckKind.Suggestion, check.Finding.Kind);
        Assert.Equal(
            [Victim.ToChainAddress(), stranger.ToChainAddress()],
            check.Finding.Candidates.Select(candidate => candidate.Arrival!.To));
        Assert.Contains(Pool.ToChainAddress(), check.Finding.Accounts);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Dust_is_counted_in_the_coin_s_own_decimals()
    {
        Assert.True(AccountPoisoningCheck.IsDust(new AssetAmount(Asset.Ether, BigInteger.Pow(10, 13))));
        Assert.False(AccountPoisoningCheck.IsDust(new AssetAmount(Asset.Ether, BigInteger.Pow(10, 15))));
        Assert.True(AccountPoisoningCheck.IsDust(new AssetAmount(Asset.Tronix, 10)));
        Assert.False(AccountPoisoningCheck.IsDust(new AssetAmount(Asset.Tronix, 1_000_000)));
    }

    [Fact]
    public void Hex_is_alike_without_case_and_base58_only_with_it()
    {
        Assert.True(AccountPoisoningCheck.LookAlike(
            "0xABCD" + new string('0', 32) + "1234", "0xabcd" + new string('9', 32) + "1234"));
        Assert.True(AccountPoisoningCheck.LookAlike(
            "TQ5nZAAAAAAAAAAAAAAAAAAAAAAAAAmK8e", "TQ5nZBBBBBBBBBBBBBBBBBBBBBBBBBmK8e"));
        Assert.False(AccountPoisoningCheck.LookAlike(
            "TQ5nZAAAAAAAAAAAAAAAAAAAAAAAAAmK8e", "Tq5nZBBBBBBBBBBBBBBBBBBBBBBBBBmK8e"));
        Assert.False(AccountPoisoningCheck.LookAlike("0xabcd1234", "0xabcd1234"));
    }

    [Fact]
    public void The_finding_does_not_hang_on_the_order_the_transfers_came_in()
    {
        EthereumTransfer[] transfers =
            [Ether(Victim, Real, 1_000, 0), Ether(Victim, Real, 2_000, 0), Ether(Mimic, Victim, 0, 1)];
        var first = Run(transfers);
        using var other = new Harness();
        var graph = Harness.GraphOf([]) with
        {
            Account = new AccountGraph(
                [.. transfers.Reverse()], [], new Dictionary<EthereumTxHash, EthereumTransaction>(),
                new HashSet<ChainAddress>(), []),
        };
        var second = other.Service.Run(other.Case, graph);

        Assert.Equal(
            first.Checks.Select(check => (check.Finding.Key, check.Finding.Departure)),
            second.Checks.Select(check => (check.Finding.Key, check.Finding.Departure)));
    }

    [Fact]
    public void A_deposit_through_the_router_counts_as_the_depositor_s()
    {
        var router = EthereumAddress.Parse("0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b");
        var paid = Ether(Victim, router, 1_000, 0);
        var passed = Ether(router, Pool, 1_000, 0) with { Transaction = paid.Transaction, Position = 1 };

        var run = Run(paid, passed, Ether(Pool, Victim, 995, 5));

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == TornadoOutputsCheck.Name);
        Assert.Equal(Victim.ToChainAddress(), check.Finding.Departure!.From);
        Assert.Equal(Victim.ToChainAddress(), Assert.Single(check.Finding.Candidates).Arrival!.To);
    }

    [Fact]
    public void A_pool_lists_at_most_five_candidates()
    {
        var transfers = new List<EthereumTransfer> { Ether(Victim, Pool, 1_000, 0) };
        transfers.AddRange(Enumerable.Range(1, 8)
            .Select(hour => Ether(Pool, Address("2222" + hour.ToString("x4"), "33333333"), 995, hour)));

        var run = Run([.. transfers]);

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == TornadoOutputsCheck.Name);
        Assert.Equal(5, check.Finding.Candidates.Count);
    }

    private CheckRun Run(params EthereumTransfer[] transfers)
    {
        var graph = Harness.GraphOf([]) with
        {
            Account = new AccountGraph(
                transfers, [], new Dictionary<EthereumTxHash, EthereumTransaction>(), new HashSet<ChainAddress>(), []),
        };
        return _harness.Service.Run(_harness.Case, graph);
    }

    private static EthereumAddress Address(string head, string tail, char middle = '0') =>
        EthereumAddress.Parse("0x" + head + new string(middle, 40 - head.Length - tail.Length) + tail);

    private EthereumTransfer Ether(EthereumAddress from, EthereumAddress to, long milliEther, int hour) => new(
        EthereumTxHash.Parse("0x" + (++_next).ToString("x64")), EthereumTransferKind.Native, from, to,
        new AssetAmount(Asset.Ether, new BigInteger(milliEther) * BigInteger.Pow(10, 15)), 0,
        new EthereumBlockReference(20_000_000 + hour + 10, "0x" + new string('a', 64), Start.AddHours(hour)));
}
