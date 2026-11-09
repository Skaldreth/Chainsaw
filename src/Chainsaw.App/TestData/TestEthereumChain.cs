#if DEBUG
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>A made-up movement of USDT on Ethereum: a <c>Transfer</c> event in a transaction's log.</summary>
/// <param name="From">The address the tokens left.</param>
/// <param name="To">The address that received them.</param>
/// <param name="Units">The amount in USDT's smallest unit.</param>
public sealed record TestEthereumToken(EthereumAddress From, EthereumAddress To, BigInteger Units);

/// <summary>A made-up internal transaction: ether a contract sent in a transaction.</summary>
/// <param name="From">The contract that sent it.</param>
/// <param name="To">The address that received it.</param>
/// <param name="Wei">The ether, in wei.</param>
public sealed record TestEthereumCall(EthereumAddress From, EthereumAddress To, BigInteger Wei);

/// <summary>A made-up Ethereum transaction: the signer's call, with what it moved.</summary>
/// <param name="Label">The story's label.</param>
/// <param name="Hash">Its hash, <c>0x</c> and 64 hex digits made from the label.</param>
/// <param name="Height">Its block.</param>
/// <param name="Index">Its place in the block.</param>
/// <param name="From">The signer.</param>
/// <param name="To">The address it called or paid.</param>
/// <param name="Value">The ether it sent, in wei.</param>
/// <param name="GasUsed">The gas it used, paid at <see cref="TestEthereumChain.GasPrice"/>.</param>
/// <param name="Tokens">The USDT it moved, in its log's order.</param>
/// <param name="Calls">The ether contracts sent in it.</param>
public sealed record TestEthereumTransaction(
    string Label, string Hash, long Height, int Index, EthereumAddress From, EthereumAddress To, BigInteger Value,
    long GasUsed, IReadOnlyList<TestEthereumToken> Tokens, IReadOnlyList<TestEthereumCall> Calls)
{
    /// <summary>Gets the fee: the gas used at the made-up price.</summary>
    public BigInteger Fee => GasUsed * TestEthereumChain.GasPrice;
}

/// <summary>
/// The test case's part on Ethereum, made up (<c>/testdata</c>, Debug only): another victim pays the scammer USDT, the
/// scammer swaps most of it for ether through Uniswap's router (sketch SW1a with its price check, and DX1b's router in
/// the swap's pill) and pays the ether into Tornado Cash's pool, a mixer and a former sanction; the rest of the USDT
/// stays, so that the scammer's address can be frozen. The same made-up story in Blockscout's form. The addresses are
/// made up, except USDT's contract, Uniswap's router and Tornado Cash's pool, which Chainsaw knows by their real
/// address (<see cref="KnownTokens"/>, <see cref="KnownContracts"/>); no transaction is real.
/// </summary>
public sealed class TestEthereumChain : ITestBlockscoutChain
{
    /// <summary>The first block of the made-up part.</summary>
    public const long FirstHeight = 23_650_000;

    /// <summary>The newest block, deep below the swap so that its answers are final.</summary>
    public const long Tip = 23_651_000;

    /// <summary>The made-up gas price, in wei: two gwei.</summary>
    public const long GasPrice = 2_000_000_000;

    /// <summary>The swap through Uniswap's router: USDT for ether.</summary>
    public const string Swap = "eth:bytet";

    /// <summary>The time between two blocks: Ethereum's twelve seconds.</summary>
    public static readonly TimeSpan BlockTime = TimeSpan.FromSeconds(12);

    /// <summary>The first block's time.</summary>
    public static readonly DateTimeOffset FirstTime = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    /// <summary>One ether in wei.</summary>
    private static readonly BigInteger Ether = BigInteger.Pow(10, 18);

    /// <summary>One USDT in its smallest unit.</summary>
    private static readonly BigInteger Usdt = 1_000_000;

    /// <summary>The transactions, oldest first.</summary>
    private readonly List<TestEthereumTransaction> _transactions;

    /// <summary>Makes the made-up part.</summary>
    public TestEthereumChain()
    {
        _transactions =
        [
            Make("eth:offret-betalar", FirstHeight + 100, Victim, UsdtContract, 0, 60_000,
                [new TestEthereumToken(Victim, Scammer, 12_000 * Usdt)], []),
            Make(Swap, FirstHeight + 200, Scammer, Router, 0, 150_000,
                [new TestEthereumToken(Scammer, Router, 10_000 * Usdt)],
                [new TestEthereumCall(Router, Scammer, Ether * 295 / 100)]),
            Make("eth:till-tornado-1", FirstHeight + 300, Scammer, Tornado, Ether, 900_000, [], []),
            Make("eth:till-tornado-2", FirstHeight + 340, Scammer, Tornado, Ether, 900_000, [], []),
        ];
    }

    /// <summary>Gets the victim on Ethereum, who pays the scammer USDT.</summary>
    public static EthereumAddress Victim { get; } = Address("eth:offret");

    /// <summary>Gets the scammer's address on Ethereum: the start point.</summary>
    public static EthereumAddress Scammer { get; } = Address("eth:bedragaren");

    /// <summary>Gets USDT's contract on Ethereum, the real one: it is how the token is known.</summary>
    public static EthereumAddress UsdtContract { get; } = EthereumAddress.Parse(KnownTokens.EthereumUsdt.Contract!);

    /// <summary>Gets Uniswap's V2 router, the real one, as Chainsaw's list of known contracts names it.</summary>
    public static EthereumAddress Router { get; } = Known("Uniswap V2 Router02");

    /// <summary>Gets Tornado Cash's pool of one ether, the real one, as Chainsaw's list of known contracts names it.</summary>
    public static EthereumAddress Tornado { get; } = Known("Tornado Cash 1 ETH");

    /// <summary>Gets the transactions, oldest first.</summary>
    public IReadOnlyList<TestEthereumTransaction> Transactions => _transactions;

    /// <summary>Gives a block's time.</summary>
    /// <param name="height">The block.</param>
    /// <returns>Its time.</returns>
    public static DateTimeOffset TimeOf(long height) => FirstTime + ((height - FirstHeight) * BlockTime);

    /// <summary>Gives a block's made-up hash.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The hash, <c>0x</c> and 64 hex digits.</returns>
    public static string BlockHash(long height) =>
        "0x" + Convert.ToHexStringLower(TestAddress.Hash("eth-block:" + height.ToString(
            System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Makes a transaction's hash of its label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The hash.</returns>
    public static string HashOf(string label) => "0x" + Convert.ToHexStringLower(TestAddress.Hash(label));

    /// <inheritdoc/>
    Chain ITestBlockscoutChain.Chain => Chain.Ethereum;

    /// <inheritdoc/>
    Uri ITestBlockscoutChain.Root => Modules.Chains.TestData.TestDataEthereum.Address;

    /// <inheritdoc/>
    long ITestBlockscoutChain.First => FirstHeight;

    /// <inheritdoc/>
    long ITestBlockscoutChain.Newest => Tip;

    /// <inheritdoc/>
    TestBlockscoutToken ITestBlockscoutChain.Token { get; } = new(UsdtContract, 6, "USDT", "Tether USD");

    /// <inheritdoc/>
    string ITestBlockscoutChain.HashAt(long height) => BlockHash(height);

    /// <inheritdoc/>
    DateTimeOffset ITestBlockscoutChain.TimeAt(long height) => TimeOf(height);

    /// <summary>Finds a transaction by its hash.</summary>
    /// <param name="hash">The hash, in any case.</param>
    /// <returns>The transaction, or <see langword="null"/>.</returns>
    public TestEthereumTransaction? Find(string hash) => _transactions.FirstOrDefault(
        transaction => string.Equals(transaction.Hash, hash, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a transaction by its label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The transaction.</returns>
    public TestEthereumTransaction this[string label] => _transactions.Single(transaction => transaction.Label == label);

    /// <summary>Makes a made-up Ethereum address of a label: 20 bytes of its hash.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    private static EthereumAddress Address(string label) =>
        EthereumAddress.Parse("0x" + Convert.ToHexStringLower(TestAddress.Hash(label).AsSpan(0, 20)));

    /// <summary>Gives a contract on Chainsaw's list of known contracts by its name.</summary>
    /// <param name="name">The name, as the list writes it.</param>
    /// <returns>The contract's address.</returns>
    private static EthereumAddress Known(string name) =>
        KnownContracts.All.Single(contract => contract.Name == name).Address;

    /// <summary>
    /// Makes a transaction, its place in the block counted from its label so that it never changes; BSC's made-up part
    /// (<see cref="TestBscChain"/>) makes its own the same way.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="height">The block.</param>
    /// <param name="from">The signer.</param>
    /// <param name="to">The address it calls or pays.</param>
    /// <param name="value">The ether it sends, in wei.</param>
    /// <param name="gas">The gas it uses.</param>
    /// <param name="tokens">The tokens it moves.</param>
    /// <param name="calls">The ether contracts send in it.</param>
    /// <returns>The transaction.</returns>
    internal static TestEthereumTransaction Make(
        string label, long height, EthereumAddress from, EthereumAddress to, BigInteger value, long gas,
        IReadOnlyList<TestEthereumToken> tokens, IReadOnlyList<TestEthereumCall> calls) =>
        new(label, HashOf(label), height, TestAddress.Hash(label)[0] % 100, from, to, value, gas, tokens, calls);
}
#endif
