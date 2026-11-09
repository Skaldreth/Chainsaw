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
        Assert.Equal([Victim.ToChainAddress(), Mimic.ToChainAddress()], check.Finding.Accounts);
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
            Ether(Pool, Real, 900, 3),
            Ether(Pool, Mimic, 1_000, -1));

        var check = Assert.Single(run.Checks, check => check.Analysis.Name == TornadoOutputsCheck.Name);
        Assert.Equal(CheckKind.Suggestion, check.Finding.Kind);
        Assert.Equal(
            [Victim.ToChainAddress(), stranger.ToChainAddress()],
            check.Finding.Candidates.Select(candidate => candidate.Arrival!.To));
        Assert.Contains(Pool.ToChainAddress(), check.Finding.Accounts);
        Assert.True(_harness.Verify().IsIntact);
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
