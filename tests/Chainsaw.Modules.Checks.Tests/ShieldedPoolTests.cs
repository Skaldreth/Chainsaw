using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// <c>shielded-pool v1</c> against a synthetic facit: money into Zcash's pool and into Litecoin's MWEB, and the ways
/// out of the same pool in the graph that are the same amount minus a fee soon after, as candidates; a way out larger
/// than the deposit, one before it, one too long after and one on another chain are not.
/// </summary>
public sealed class ShieldedPoolTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ShieldedPoolCheck _check = new();

    [Fact]
    public void A_way_out_of_zcashs_pool_with_the_same_amount_minus_a_fee_soon_after_is_a_candidate()
    {
        var deposit = Zcash("shield", 100, inputs: [(Address("payer"), 1_000_000)], outputs: [], net: 990_000);
        var exact = Zcash("unshield-exact", 110, [], [(Address("cash-out"), 989_000)], net: -990_000);
        var larger = Zcash("unshield-larger", 120, [], [(Address("other"), 2_000_000)], net: -2_010_000);
        var before = Zcash("unshield-before", 90, [], [(Address("earlier"), 989_000)], net: -990_000);
        var late = Zcash("unshield-late", 100 + (8 * 24 * 48), [], [(Address("late"), 989_000)], net: -990_000);

        var finding = Assert.Single(_check.Run(Input(deposit, exact, larger, before, late)));

        Assert.Equal(CheckKind.Suggestion, finding.Kind);
        Assert.Equal(deposit.Txid.Value + ":pool", finding.Key);
        var candidate = Assert.Single(finding.Candidates);
        Assert.Equal(exact.Txid, candidate.Txid);
        Assert.Equal(Address("cash-out"), candidate.Address);
        Assert.InRange(candidate.Score, 0.9, 1.0);
        Assert.Contains("shielded-pool v1", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_peg_out_from_mweb_is_a_candidate_for_a_peg_in()
    {
        var pegIn = BitcoinAddress.FromWitness(9, Enumerable.Repeat((byte)0x66, 32).ToArray(), BitcoinNetwork.Litecoin);
        var hog = BitcoinAddress.FromWitness(8, Enumerable.Repeat((byte)0x77, 32).ToArray(), BitcoinNetwork.Litecoin);
        var payout = BitcoinAddress.FromWitness(0, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Litecoin);
        var deposit = Litecoin("peg-in", 3_000_000, [(Litecoin("ltc-payer"), 5_000_000)], [(pegIn, 4_990_000)]);
        var integration = Litecoin(
            "hogex", 3_000_004, [(hog, 50_000_000)], [(hog, 45_015_000), (payout, 4_985_000)]);

        var finding = Assert.Single(_check.Run(Input(deposit, integration)));

        var candidate = Assert.Single(finding.Candidates);
        Assert.Equal(payout, candidate.Address);
        Assert.Contains("MWEB", finding.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Bitcoins_transactions_are_never_weighed_and_the_patterns_never_see_the_pools()
    {
        Assert.False(_check.Scope.Covers(Chain.Bitcoin));
        Assert.True(_check.Scope.Covers(Chain.Zcash));
        Assert.True(_check.Scope.Covers(Chain.Litecoin));
        Assert.False(new MixerOutputsCheck().Scope.Covers(Chain.Zcash));
    }

    [Fact]
    public void A_sealed_finding_on_zcash_is_read_back_on_its_chain()
    {
        var deposit = Zcash("shield", 100, inputs: [(Address("payer"), 1_000_000)], outputs: [], net: 990_000);
        var exact = Zcash("unshield-exact", 110, [], [(Address("cash-out"), 989_000)], net: -990_000);
        var finding = Assert.Single(_check.Run(Input(deposit, exact)));

        var read = FindingCodec.Decode(FindingCodec.Encode(finding));

        Assert.Equal(Chain.Zcash, read.Candidates[0].Txid!.Chain);
        Assert.Equal(exact.Txid, read.Candidates[0].Txid);
        Assert.Equal(Chain.Zcash, read.Candidates[0].Address!.Chain);
        Assert.All(read.Basis, basis => Assert.Equal(Chain.Zcash, basis.Txid.Chain));
        Assert.Equal(FindingCodec.Encode(finding), FindingCodec.Encode(read));
    }

    [Theory]
    [InlineData(0, 100_000, 1.0)]
    [InlineData(50_000, 100_000, 0.5)]
    [InlineData(100_000, 100_000, 0.0)]
    [InlineData(-1, 100_000, 0.0)]
    public void The_amount_counts_from_the_same_to_what_the_deposit_may_lack(long lacking, long allowance, double score) =>
        Assert.Equal(score, ShieldedPoolCheck.AmountScore(lacking, allowance), 3);

    [Fact]
    public void A_small_deposit_has_room_for_a_fee_and_a_large_one_a_per_cent()
    {
        Assert.Equal(ShieldedPoolCheck.MostFee, ShieldedPoolCheck.Allowance(1_000_000));
        Assert.Equal(10_000_000, ShieldedPoolCheck.Allowance(1_000_000_000));
    }

    [Fact]
    public void A_round_amount_and_ways_out_of_the_same_amount_rank_below_a_distinct_one()
    {
        var deposit = Zcash("shield-round", 100, [(Address("payer"), 101_000_000)], [], net: 100_050_000);
        var distinct = Zcash("out-distinct", 104, [], [(Address("distinct"), 100_037_000)], net: -100_047_000);
        var roundA = Zcash("out-round-a", 102, [], [(Address("round-a"), 100_000_000)], net: -100_010_000);
        var roundB = Zcash("out-round-b", 103, [], [(Address("round-b"), 100_000_000)], net: -100_010_000);

        var finding = Assert.Single(_check.Run(Input(deposit, distinct, roundA, roundB)));

        Assert.Equal(distinct.Txid, finding.Candidates[0].Txid);
        Assert.Equal(3, finding.Candidates.Count);
        Assert.True(finding.Candidates.Skip(1).All(candidate => candidate.Score < finding.Candidates[0].Score));
        Assert.True(finding.Candidates.Skip(1).All(candidate => candidate.Score < ShieldedPoolCheck.LowestScore));
        Assert.Contains("jämnt belopp", finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_closer_amount_ranks_first_at_the_same_time()
    {
        var deposit = Zcash("shield-close", 100, [(Address("payer"), 5_100_000)], [], net: 5_050_000);
        var far = Zcash("out-far", 101, [], [(Address("far"), 4_980_000)], net: -4_990_000);
        var near = Zcash("out-near", 101, [], [(Address("near"), 5_040_000)], net: -5_050_000);

        var finding = Assert.Single(_check.Run(Input(deposit, far, near)));

        Assert.Equal(near.Txid, finding.Candidates[0].Txid);
    }

    /// <summary>Gives the input of some transactions.</summary>
    private static CheckInput Input(params BitcoinTransaction[] transactions) =>
        new(transactions, [], new Dictionary<Txid, TransactionConclusions>());

    /// <summary>A synthetic transparent Zcash address.</summary>
    private static BitcoinAddress Address(string label) => BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Hash(label).AsSpan(0, 20), BitcoinNetwork.Zcash);

    /// <summary>A synthetic Litecoin address.</summary>
    private static BitcoinAddress Litecoin(string label) => BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Hash(label).AsSpan(0, 20), BitcoinNetwork.Litecoin);

    /// <summary>A synthetic Zcash transaction, 75 seconds a block from <see cref="Start"/>.</summary>
    private static BitcoinTransaction Zcash(
        string label, long height, (BitcoinAddress Address, long Amount)[] inputs,
        (BitcoinAddress Address, long Amount)[] outputs, long net)
    {
        var transaction = Build(Chain.Zcash, label, height, TimeSpan.FromSeconds(75), inputs, outputs);
        return transaction with
        {
            Fee = new Satoshi(inputs.Sum(i => i.Amount) - outputs.Sum(o => o.Amount) - net),
            Shielded = ShieldedFlow.FromNet(net, [ShieldedPool.Sapling]),
        };
    }

    /// <summary>A synthetic Litecoin transaction, 150 seconds a block from <see cref="Start"/>.</summary>
    private static BitcoinTransaction Litecoin(
        string label, long height, (BitcoinAddress Address, long Amount)[] inputs,
        (BitcoinAddress Address, long Amount)[] outputs) =>
        Build(Chain.Litecoin, label, height, TimeSpan.FromSeconds(150), inputs, outputs);

    /// <summary>Builds a confirmed transaction on a chain.</summary>
    private static BitcoinTransaction Build(
        Chain chain, string label, long height, TimeSpan pace, (BitcoinAddress Address, long Amount)[] inputs,
        (BitcoinAddress Address, long Amount)[] outputs)
    {
        var block = new BlockReference(
            height, BlockHash.Parse(Convert.ToHexStringLower(Hash("block:" + label))), Start + (pace * height));
        return new BitcoinTransaction(
            Txid.Parse(chain, Convert.ToHexStringLower(Hash("tx:" + label))),
            block,
            [
                .. inputs.Select((input, index) => new BitcoinInput(
                    index, false, Txid.Parse(chain, Convert.ToHexStringLower(Hash($"{label}:in:{index}"))), 0,
                    input.Address, input.Address.Type, new Satoshi(input.Amount), uint.MaxValue)),
            ],
            [
                .. outputs.Select((output, index) => new BitcoinOutput(
                    index, output.Address.Type, output.Address, new Satoshi(output.Amount), string.Empty, null)),
            ],
            new Satoshi(Math.Max(0, inputs.Sum(i => i.Amount) - outputs.Sum(o => o.Amount))),
            250,
            1000,
            2,
            0);
    }

    /// <summary>Hashes a label.</summary>
    private static byte[] Hash(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(label));
}
