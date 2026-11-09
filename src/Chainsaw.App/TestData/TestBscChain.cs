#if DEBUG
using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Chains.TestData;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// The test case's part on BSC (BNB Smart Chain), made up (<c>/testdata</c>, Debug only): another victim pays the
/// scammer BNB and USDT (Binance's bridged BSC-USD, which no one can freeze), and the scammer pays most of the BNB into
/// Sydbörsen's deposit on BSC and keeps the USDT. The same made-up Blockscout answers as Ethereum's
/// (<see cref="TestEthereumHttp"/>), on BSC's own addresses, so that BSC's nodes, BNB and its token can be seen. The
/// addresses are made up, except BSC-USD's contract, which Chainsaw knows by its real address
/// (<see cref="KnownTokens.BscUsdt"/>); no transaction is real.
/// </summary>
public sealed class TestBscChain : ITestBlockscoutChain
{
    /// <summary>The victim's payment of BNB to the scammer.</summary>
    public const string VictimPayment = "bsc:offret-betalar";

    /// <summary>The victim's payment of USDT to the scammer.</summary>
    public const string VictimTokens = "bsc:offret-betalar-usdt";

    /// <summary>The scammer's payment of BNB into Sydbörsen's deposit.</summary>
    public const string CashOut = "bsc:uttaget";

    /// <summary>The first block of the made-up part.</summary>
    public const long FirstHeight = 64_000_000;

    /// <summary>The newest block, deep below the payments so that their answers are final.</summary>
    public const long Tip = FirstHeight + 1_000;

    /// <summary>The time between two blocks: BSC's three seconds.</summary>
    public static readonly TimeSpan BlockTime = TimeSpan.FromSeconds(3);

    /// <summary>The first block's time.</summary>
    private static readonly DateTimeOffset FirstTime = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    /// <summary>One BNB in its smallest unit, and one USDT on BSC (18 decimals both).</summary>
    private static readonly BigInteger Whole = BigInteger.Pow(10, 18);

    /// <summary>Gets the victim on BSC.</summary>
    public static EthereumAddress Victim { get; } = Address("bsc:offret");

    /// <summary>Gets the scammer's address on BSC.</summary>
    public static EthereumAddress Scammer { get; } = Address("bsc:bedragaren");

    /// <summary>Gets Sydbörsen's deposit address on BSC.</summary>
    public static EthereumAddress Exchange { get; } = Address("bsc:sydborsen");

    /// <summary>Gets BSC-USD's contract, the real one: it is how the token is known.</summary>
    public static EthereumAddress UsdtContract { get; } = EthereumAddress.Parse(Chain.Bsc, KnownTokens.BscUsdt.Contract!);

    /// <inheritdoc/>
    public IReadOnlyList<TestEthereumTransaction> Transactions { get; } =
    [
        TestEthereumChain.Make(VictimPayment, FirstHeight + 100, Victim, Scammer, Whole * 3, 21_000, [], []),
        TestEthereumChain.Make(VictimTokens, FirstHeight + 120, Victim, UsdtContract, 0, 60_000,
            [new TestEthereumToken(Victim, Scammer, Whole * 4_000)], []),
        TestEthereumChain.Make(CashOut, FirstHeight + 400, Scammer, Exchange, Whole * 29 / 10, 21_000, [], []),
    ];

    /// <inheritdoc/>
    Chain ITestBlockscoutChain.Chain => Chain.Bsc;

    /// <inheritdoc/>
    public Uri Root => TestDataChains.BscAddress;

    /// <inheritdoc/>
    public long First => FirstHeight;

    /// <inheritdoc/>
    public long Newest => Tip;

    /// <inheritdoc/>
    public TestBlockscoutToken Token { get; } = new(UsdtContract, 18, "USDT", "Binance-Peg BSC-USD");

    /// <inheritdoc/>
    public string HashAt(long height) =>
        "0x" + Convert.ToHexStringLower(TestAddress.Hash("bsc-block:" + height.ToString(CultureInfo.InvariantCulture)));

    /// <inheritdoc/>
    public DateTimeOffset TimeAt(long height) => FirstTime + ((height - FirstHeight) * BlockTime);

    /// <summary>Makes a made-up BSC address of a label: 20 bytes of its hash.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    private static EthereumAddress Address(string label) =>
        EthereumAddress.Parse(Chain.Bsc, "0x" + Convert.ToHexStringLower(TestAddress.Hash(label).AsSpan(0, 20)));
}
#endif
