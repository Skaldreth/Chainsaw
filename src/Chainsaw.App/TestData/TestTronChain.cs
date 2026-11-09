#if DEBUG
using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>What a made-up TRON transaction moves: TRX of its own, or USDT by a call to Tether's contract.</summary>
public enum TestTronMove
{
    /// <summary>TRX sent by the transaction itself (<c>TransferContract</c>).</summary>
    Trx,

    /// <summary>USDT moved by a call to its contract (<c>TriggerSmartContract</c> and a <c>Transfer</c> event).</summary>
    Usdt,
}

/// <summary>A made-up TRON transaction: one movement from its signer, with the fee it burned.</summary>
/// <param name="Label">The story's label.</param>
/// <param name="Id">Its id, 64 hex digits made from the label.</param>
/// <param name="Height">Its block.</param>
/// <param name="Move">What it moves.</param>
/// <param name="From">The signer, who sends.</param>
/// <param name="To">Who receives.</param>
/// <param name="Units">The amount in sun (TRX) or in USDT's smallest unit.</param>
/// <param name="Fee">The TRX it burned, in sun.</param>
public sealed record TestTronTransaction(
    string Label, string Id, long Height, TestTronMove Move, EthereumAddress From, EthereumAddress To,
    BigInteger Units, long Fee);

/// <summary>
/// The test case's part on TRON, made up (<c>/testdata</c>, Debug only): the swap service pays the scammer USDT on
/// TRON for the bitcoin it took (the link to another chain), and a little TRX for the fees; the scammer sends the USDT
/// on to a mule, who pays most of it into Sydbörsen's deposit and keeps the rest. The same made-up story in TronGrid's
/// form, so that USDT on TRON, its snowflake (the mule's rest) and its fees can be seen. Nothing in it is a real
/// address or transaction.
/// </summary>
public sealed class TestTronChain
{
    /// <summary>The swap service's payout of USDT to the scammer on TRON, where the bitcoin continues.</summary>
    public const string SwapPayout = "tron:bytarens-utbetalning";

    /// <summary>The first block of the made-up part.</summary>
    public const long FirstHeight = 80_000_000;

    /// <summary>The newest block.</summary>
    public const long Tip = 80_001_000;

    /// <summary>The time between two blocks: TRON's three seconds.</summary>
    public static readonly TimeSpan BlockTime = TimeSpan.FromSeconds(3);

    /// <summary>The first block's time.</summary>
    public static readonly DateTimeOffset FirstTime = new(2025, 10, 30, 8, 0, 0, TimeSpan.Zero);

    /// <summary>USDT's contract on TRON in TRON's hex, the only real address here: it is how the token is known.</summary>
    private static readonly string UsdtHex = EthereumAddress.Parse(Chain.Tron, KnownTokens.TronUsdt.Contract!).TronHex!;

    /// <summary>The transactions, oldest first.</summary>
    private readonly List<TestTronTransaction> _transactions;

    /// <summary>Makes the made-up part.</summary>
    public TestTronChain()
    {
        _transactions =
        [
            Usdt(SwapPayout, FirstHeight + 100, Swap, Scammer, 9_800, 13_400_000),
            Trx("tron:avgiftspengar", FirstHeight + 101, Swap, Scammer, 30, 0),
            Usdt("tron:till-mulan", FirstHeight + 400, Scammer, Mule, 9_800, 13_400_000),
            Usdt("tron:uttaget", FirstHeight + 900, Mule, Exchange, 9_500, 27_255_000),
        ];
    }

    /// <summary>Gets the swap service's hot wallet on TRON, which pays out.</summary>
    public static EthereumAddress Swap { get; } = Address("tron:bytaren");

    /// <summary>Gets the scammer's address on TRON.</summary>
    public static EthereumAddress Scammer { get; } = Address("tron:bedragaren");

    /// <summary>Gets the mule's address on TRON.</summary>
    public static EthereumAddress Mule { get; } = Address("tron:mulan");

    /// <summary>Gets Sydbörsen's deposit address on TRON.</summary>
    public static EthereumAddress Exchange { get; } = Address("tron:sydborsen");

    /// <summary>Gets USDT's contract in TRON's hex.</summary>
    public static string UsdtContractHex => UsdtHex;

    /// <summary>Gets the transactions, oldest first.</summary>
    public IReadOnlyList<TestTronTransaction> Transactions => _transactions;

    /// <summary>Gives a block's time.</summary>
    /// <param name="height">The block.</param>
    /// <returns>Its time.</returns>
    public static DateTimeOffset TimeOf(long height) => FirstTime + ((height - FirstHeight) * BlockTime);

    /// <summary>Gives a block's id as TRON writes it: its number in eight bytes, then 24 made-up bytes.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The id, 64 hex digits.</returns>
    public static string BlockId(long height)
    {
        var number = height.ToString(CultureInfo.InvariantCulture);
        return height.ToString("x16", CultureInfo.InvariantCulture)
            + Convert.ToHexStringLower(TestAddress.Hash("tron-block:" + number))[..48];
    }

    /// <summary>Finds a transaction by its id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The transaction, or <see langword="null"/>.</returns>
    public TestTronTransaction? Find(string id) =>
        _transactions.FirstOrDefault(transaction => transaction.Id == id);

    /// <summary>Finds a transaction by its label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The transaction.</returns>
    public TestTronTransaction this[string label] => _transactions.Single(transaction => transaction.Label == label);

    /// <summary>Makes a made-up TRON address of a label: the version byte and 20 bytes of its hash.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    private static EthereumAddress Address(string label) =>
        EthereumAddress.Parse(Chain.Tron, "41" + Convert.ToHexStringLower(TestAddress.Hash(label).AsSpan(0, 20)));

    /// <summary>Makes a transfer of USDT.</summary>
    /// <param name="label">The label.</param>
    /// <param name="height">The block.</param>
    /// <param name="from">The signer.</param>
    /// <param name="to">The receiver.</param>
    /// <param name="whole">Whole USDT.</param>
    /// <param name="fee">The fee in sun.</param>
    /// <returns>The transaction.</returns>
    private static TestTronTransaction Usdt(
        string label, long height, EthereumAddress from, EthereumAddress to, long whole, long fee) =>
        new(label, Id(label), height, TestTronMove.Usdt, from, to, new BigInteger(whole) * 1_000_000, fee);

    /// <summary>Makes a transfer of TRX.</summary>
    /// <param name="label">The label.</param>
    /// <param name="height">The block.</param>
    /// <param name="from">The signer.</param>
    /// <param name="to">The receiver.</param>
    /// <param name="whole">Whole TRX.</param>
    /// <param name="fee">The fee in sun.</param>
    /// <returns>The transaction.</returns>
    private static TestTronTransaction Trx(
        string label, long height, EthereumAddress from, EthereumAddress to, long whole, long fee) =>
        new(label, Id(label), height, TestTronMove.Trx, from, to, new BigInteger(whole) * 1_000_000, fee);

    /// <summary>Makes a transaction's id of its label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The id.</returns>
    public static string Id(string label) => Convert.ToHexStringLower(TestAddress.Hash(label));
}
#endif
