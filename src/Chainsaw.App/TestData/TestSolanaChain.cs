#if DEBUG
using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>A made-up Solana transaction: one transfer of SOL by the System program, signed by the sender.</summary>
/// <param name="Label">The story's label.</param>
/// <param name="Slot">Its slot.</param>
/// <param name="From">The signer, who sends and pays the fee.</param>
/// <param name="To">Who receives.</param>
/// <param name="Lamports">The SOL it moves, in lamports.</param>
public sealed record TestSolanaTransaction(
    string Label, long Slot, EthereumAddress From, EthereumAddress To, long Lamports)
{
    /// <summary>Gets its signature: 64 bytes made from the label, in Base58.</summary>
    public string Signature { get; } = TestSolanaChain.SignatureOf(Label);
}

/// <summary>
/// The test case's part on Solana, made up (<c>/testdata</c>, Debug only): another victim pays the scammer SOL, the
/// scammer passes most of it on to a mule, and the mule pays Sydbörsen's deposit on Solana. Answered in Solana's
/// JSON-RPC form, so that Solana's nodes and SOL can be seen. Nothing in it is a real address or transaction; only the
/// System program's address is real, since it is how a transfer of SOL is known.
/// </summary>
public static class TestSolanaChain
{
    /// <summary>The victim's payment to the scammer.</summary>
    public const string VictimPayment = "sol:offret-betalar";

    /// <summary>The scammer's payment on to the mule.</summary>
    public const string ToMule = "sol:till-mulan";

    /// <summary>The mule's payment into Sydbörsen's deposit.</summary>
    public const string CashOut = "sol:uttaget";

    /// <summary>The System program, which moves SOL: the only real address here.</summary>
    public const string SystemProgram = "11111111111111111111111111111111";

    /// <summary>The fee every made-up transaction pays, in lamports: Solana's base fee for one signature.</summary>
    public const long Fee = 5_000;

    /// <summary>The first slot of the made-up part.</summary>
    public const long FirstSlot = 410_000_000;

    /// <summary>The newest slot.</summary>
    public const long Tip = FirstSlot + 5_000;

    /// <summary>One SOL in lamports.</summary>
    private const long Sol = 1_000_000_000;

    /// <summary>The time between two slots: about 400 milliseconds.</summary>
    private static readonly TimeSpan SlotTime = TimeSpan.FromMilliseconds(400);

    /// <summary>The first slot's time.</summary>
    private static readonly DateTimeOffset FirstTime = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Gets the victim on Solana, who held 100 SOL before the part.</summary>
    public static EthereumAddress Victim { get; } = Address("sol:offret");

    /// <summary>Gets the scammer's address on Solana.</summary>
    public static EthereumAddress Scammer { get; } = Address("sol:bedragaren");

    /// <summary>Gets the mule's address on Solana.</summary>
    public static EthereumAddress Mule { get; } = Address("sol:mulan");

    /// <summary>Gets Sydbörsen's deposit address on Solana.</summary>
    public static EthereumAddress Exchange { get; } = Address("sol:sydborsen");

    /// <summary>Gets what the victim held before the part, in lamports.</summary>
    public static long VictimHeld => 100 * Sol;

    /// <summary>Gets the transactions, oldest first.</summary>
    public static IReadOnlyList<TestSolanaTransaction> Transactions { get; } =
    [
        new(VictimPayment, FirstSlot + 100, Victim, Scammer, 40 * Sol),
        new(ToMule, FirstSlot + 1_600, Scammer, Mule, 30 * Sol),
        new(CashOut, FirstSlot + 3_200, Mule, Exchange, (29 * Sol) + (Sol / 2)),
    ];

    /// <summary>Gives a slot's time.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>Its time.</returns>
    public static DateTimeOffset TimeOf(long slot) => FirstTime + ((slot - FirstSlot) * SlotTime);

    /// <summary>Gives a slot's made-up block hash: 32 bytes in Base58.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The hash.</returns>
    public static string BlockHash(long slot) =>
        Base58.Encode(TestAddress.Hash("sol-block:" + slot.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Makes a signature of a label: 64 bytes, two hashes of it, in Base58.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The signature.</returns>
    public static string SignatureOf(string label) =>
        Base58.Encode([.. TestAddress.Hash(label), .. TestAddress.Hash(label + ":2")]);

    /// <summary>Gives a transaction's hash on Solana, its signature.</summary>
    /// <param name="label">The part's label for it.</param>
    /// <returns>The hash.</returns>
    public static EthereumTxHash HashOf(string label) =>
        EthereumTxHash.TryParse(Chain.Solana, SignatureOf(label), out var hash)
            ? hash
            : throw new InvalidOperationException("A made-up signature is not a Solana signature.");

    /// <summary>Finds a transaction by its signature.</summary>
    /// <param name="signature">The signature.</param>
    /// <returns>The transaction, or <see langword="null"/>.</returns>
    public static TestSolanaTransaction? Find(string signature) =>
        Transactions.FirstOrDefault(transaction => transaction.Signature == signature);

    /// <summary>Gives what an address holds before a slot, in lamports, after the made-up transactions before it.</summary>
    /// <param name="address">The address.</param>
    /// <param name="slot">The slot; transactions at it or later are not counted.</param>
    /// <returns>The lamports.</returns>
    public static long HeldBefore(EthereumAddress address, long slot) =>
        (address == Victim ? VictimHeld : 0) + Transactions.Where(transaction => transaction.Slot < slot).Sum(each =>
            (each.To == address ? each.Lamports : 0) - (each.From == address ? each.Lamports + Fee : 0));

    /// <summary>Makes a made-up Solana address of a label: 32 bytes of its hash, in Base58.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    private static EthereumAddress Address(string label) =>
        EthereumAddress.Parse(Chain.Solana, Base58.Encode(TestAddress.Hash(label)));
}
#endif
