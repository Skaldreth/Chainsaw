#if DEBUG
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>
/// The test case's part on Litecoin, made up (<c>/testdata</c>, Debug only): another victim pays the scammer litecoin,
/// the scammer passes most of it on to a mule with change back to an address of its own, and the mule pays Sydbörsen's
/// deposit on Litecoin. The same made-up chain as Bitcoin's (<see cref="TestChain"/>, real transactions with real
/// headers), answered in Esplora's form as litecoinspace.org does, so that Litecoin's nodes, their clustering and their
/// cash-out can be seen. Nothing in it is a real address or transaction.
/// </summary>
public static class TestLitecoinChain
{
    /// <summary>The victim's payment to the scammer: the part's start point.</summary>
    public const string VictimPayment = "ltc:offret-betalar";

    /// <summary>The scammer's payment on to the mule, with change.</summary>
    public const string ToMule = "ltc:till-mulan";

    /// <summary>The mule's payment into Sydbörsen's deposit.</summary>
    public const string CashOut = "ltc:uttaget";

    /// <summary>The first block of the made-up part.</summary>
    public const long FirstHeight = 2_950_000;

    /// <summary>The newest block.</summary>
    public const long Tip = FirstHeight + 60;

    /// <summary>One litecoin in its smallest unit.</summary>
    private const long Litecoin = 100_000_000;

    /// <summary>The first block's time.</summary>
    private static readonly DateTimeOffset FirstTime = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The chain, built once: the same every run, so that a txid named in a step is the one answered.</summary>
    private static readonly Lazy<TestChain> Built = new(Build);

    /// <summary>Gets the victim on Litecoin.</summary>
    public static TestAddress Victim { get; } = TestAddress.Wpkh("ltc:offret", BitcoinNetwork.Litecoin);

    /// <summary>Gets the scammer's address on Litecoin.</summary>
    public static TestAddress Scammer { get; } = TestAddress.Wpkh("ltc:bedragaren", BitcoinNetwork.Litecoin);

    /// <summary>Gets the scammer's change address on Litecoin, of the old type as its wallet makes it.</summary>
    public static TestAddress ScammerChange { get; } = TestAddress.Pkh("ltc:bedragarens-vaxel", BitcoinNetwork.Litecoin);

    /// <summary>Gets the mule's address on Litecoin.</summary>
    public static TestAddress Mule { get; } = TestAddress.Wpkh("ltc:mulan", BitcoinNetwork.Litecoin);

    /// <summary>Gets Sydbörsen's deposit address on Litecoin.</summary>
    public static TestAddress Exchange { get; } = TestAddress.Wpkh("ltc:sydborsen", BitcoinNetwork.Litecoin);

    /// <summary>Gets the chain.</summary>
    public static TestChain Chain => Built.Value;

    /// <summary>Gives a transaction's txid on Litecoin.</summary>
    /// <param name="label">The part's label for it.</param>
    /// <returns>The txid.</returns>
    public static Txid TxidOf(string label) => Txid.Parse(SharedKernel.Chain.Litecoin, Chain[label].Txid);

    /// <summary>Builds the chain and seals it.</summary>
    /// <returns>The chain.</returns>
    private static TestChain Build()
    {
        var chain = new TestChain(FirstHeight, FirstTime);
        var money = chain.Fund(Victim, 40 * Litecoin, FirstHeight);
        chain.Tx(VictimPayment, FirstHeight + 10).From(money.Label).Pay(Scammer, 25 * Litecoin).Rest(Victim).Done();
        chain.Tx(ToMule, FirstHeight + 20).From(VictimPayment).Pay(Mule, 20 * Litecoin).Rest(ScammerChange).Done();
        chain.Tx(CashOut, FirstHeight + 30).From(ToMule).Rest(Exchange).Done();
        chain.Seal(Tip);
        return chain;
    }
}
#endif
