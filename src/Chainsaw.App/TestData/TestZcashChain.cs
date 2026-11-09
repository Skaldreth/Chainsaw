#if DEBUG
using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>A coin a made-up Zcash transaction spends: the earlier transaction's output, its address and amount.</summary>
/// <param name="Previous">The earlier transaction's label.</param>
/// <param name="Index">The output.</param>
/// <param name="From">The output's address.</param>
/// <param name="Zatoshi">The output's amount.</param>
public sealed record TestZcashInput(string Previous, int Index, TestAddress From, long Zatoshi);

/// <summary>
/// A made-up Zcash transaction: its transparent part, and what it moves into or out of the Sapling pool.
/// </summary>
/// <param name="Label">The story's label.</param>
/// <param name="Height">Its block.</param>
/// <param name="Inputs">The transparent coins it spends; none for a coinbase or a payment out of the pool alone.</param>
/// <param name="Outputs">The transparent outputs.</param>
/// <param name="SaplingBalance">
/// Sapling's value balance, as zcashd writes it: negative for money into the pool, positive for money out of it.
/// </param>
public sealed record TestZcashTransaction(
    string Label,
    long Height,
    IReadOnlyList<TestZcashInput> Inputs,
    IReadOnlyList<TestOutput> Outputs,
    long SaplingBalance)
{
    /// <summary>Gets its txid: made from the label, since Blockbook's answers are not checked against bytes.</summary>
    public string Txid { get; } = TestZcashChain.Id(Label);

    /// <summary>Gets whether it is a coinbase: no inputs and nothing out of the pool.</summary>
    public bool IsCoinbase => Inputs.Count == 0 && SaplingBalance == 0;
}

/// <summary>
/// The test case's part on Zcash's transparent chain, made up (<c>/testdata</c>, Debug only): another victim pays the
/// scammer zec, the scammer puts it all into the shielded Sapling pool (t→z), and half a day later almost the same
/// amount comes out of the pool (z→t) to an address that pays Sydbörsen's deposit on Zcash. Inside the pool nothing
/// ties the two together, so <c>shielded-pool v1</c> suggests the way out. Answered in Blockbook's form, with zcashd's
/// own answer for the pools' balances. Nothing in it is a real address or transaction.
/// </summary>
public static class TestZcashChain
{
    /// <summary>The victim's money, mined to it before the part.</summary>
    public const string VictimMoney = "zec:offrets-pengar";

    /// <summary>The victim's payment to the scammer: the part's start point.</summary>
    public const string VictimPayment = "zec:offret-betalar";

    /// <summary>The scammer's payment into the Sapling pool (t→z).</summary>
    public const string IntoPool = "zec:in-i-poolen";

    /// <summary>The payment out of the Sapling pool (z→t), to an address that cannot be tied to the scammer.</summary>
    public const string OutOfPool = "zec:ut-ur-poolen";

    /// <summary>The way out's payment into Sydbörsen's deposit.</summary>
    public const string CashOut = "zec:uttaget";

    /// <summary>The first block of the made-up part.</summary>
    public const long FirstHeight = 3_100_000;

    /// <summary>The newest block.</summary>
    public const long Tip = FirstHeight + 900;

    /// <summary>The time between blocks: Zcash's 75 seconds.</summary>
    public static readonly TimeSpan BlockTime = TimeSpan.FromSeconds(75);

    /// <summary>One zec in zatoshi.</summary>
    private const long Zec = 100_000_000;

    /// <summary>The fee each made-up transaction pays, in zatoshi (ZIP 317's least for two actions).</summary>
    private const long Fee = 10_000;

    /// <summary>The first block's time.</summary>
    private static readonly DateTimeOffset FirstTime = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Gets the victim on Zcash.</summary>
    public static TestAddress Victim { get; } = TestAddress.Pkh("zec:offret", BitcoinNetwork.Zcash);

    /// <summary>Gets the scammer's transparent address on Zcash.</summary>
    public static TestAddress Scammer { get; } = TestAddress.Pkh("zec:bedragaren", BitcoinNetwork.Zcash);

    /// <summary>Gets the transparent address the money comes out of the pool to.</summary>
    public static TestAddress WayOut { get; } = TestAddress.Pkh("zec:ut-ur-poolen", BitcoinNetwork.Zcash);

    /// <summary>Gets Sydbörsen's deposit address on Zcash.</summary>
    public static TestAddress Exchange { get; } = TestAddress.Pkh("zec:sydborsen", BitcoinNetwork.Zcash);

    /// <summary>Gets the transactions, oldest first.</summary>
    public static IReadOnlyList<TestZcashTransaction> Transactions { get; } = Build();

    /// <summary>Gives a block's time.</summary>
    /// <param name="height">The block.</param>
    /// <returns>Its time.</returns>
    public static DateTimeOffset TimeOf(long height) => FirstTime + ((height - FirstHeight) * BlockTime);

    /// <summary>Gives a block's made-up hash.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The hash, 64 hex digits.</returns>
    public static string BlockHash(long height) =>
        Convert.ToHexStringLower(TestAddress.Hash("zec-block:" + height.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Makes a transaction's id of its label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The id, 64 hex digits.</returns>
    public static string Id(string label) => Convert.ToHexStringLower(TestAddress.Hash(label));

    /// <summary>Gives a transaction's txid on Zcash.</summary>
    /// <param name="label">The part's label for it.</param>
    /// <returns>The txid.</returns>
    public static Txid TxidOf(string label) => Txid.Parse(Chain.Zcash, Id(label));

    /// <summary>Finds a transaction by its txid.</summary>
    /// <param name="txid">The txid.</param>
    /// <returns>The transaction, or <see langword="null"/>.</returns>
    public static TestZcashTransaction? Find(string txid) =>
        Transactions.FirstOrDefault(transaction => transaction.Txid == txid);

    /// <summary>Gives an address's transactions, newest first, as Blockbook orders them.</summary>
    /// <param name="address">The address, as written.</param>
    /// <returns>The transactions.</returns>
    public static IEnumerable<TestZcashTransaction> History(string address) =>
        Transactions.Where(transaction => transaction.Outputs.Any(output => output.To.Address?.Value == address)
                || transaction.Inputs.Any(input => input.From.Address?.Value == address))
            .OrderByDescending(transaction => transaction.Height);

    /// <summary>Builds the part.</summary>
    /// <returns>The transactions, oldest first.</returns>
    private static List<TestZcashTransaction> Build()
    {
        const long paid = 15 * Zec;
        const long change = 5 * Zec;
        const long intoPool = paid - Fee;
        const long outOfPool = intoPool - (2 * Fee);
        return
        [
            new(VictimMoney, FirstHeight, [], [new TestOutput(Victim, paid + change + Fee)], 0),
            new(VictimPayment, FirstHeight + 10,
                [new TestZcashInput(VictimMoney, 0, Victim, paid + change + Fee)],
                [new TestOutput(Scammer, paid), new TestOutput(Victim, change)], 0),
            new(IntoPool, FirstHeight + 20, [new TestZcashInput(VictimPayment, 0, Scammer, paid)], [], -intoPool),
            new(OutOfPool, FirstHeight + 620, [], [new TestOutput(WayOut, outOfPool)], outOfPool + Fee),
            new(CashOut, FirstHeight + 700, [new TestZcashInput(OutOfPool, 0, WayOut, outOfPool)],
                [new TestOutput(Exchange, outOfPool - Fee)], 0),
        ];
    }
}
#endif
