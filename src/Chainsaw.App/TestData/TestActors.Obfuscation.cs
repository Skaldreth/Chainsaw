#if DEBUG
namespace Chainsaw.App.TestData;

/// <summary>
/// The actors of the fifth round's patterns (<see cref="TestStoryChain"/>, <c>Obfuscation</c>): a central mixer without
/// a label and its customers, a chain from Blandaren through it and Virveln, a split spread over several transactions,
/// a row of hops that pay a layering service's fee, and a nested service with its account at Nordbörsen.
/// </summary>
public sealed partial class TestActors
{
    /// <summary>How many customers use the central mixer without a label, besides the chain's branch.</summary>
    public const int PoolCustomers = 9;

    /// <summary>How many parts the spread split pays, two per transaction.</summary>
    public const int SpreadParts = 8;

    /// <summary>How many hops the layering row has; every other one pays the layering service's fee.</summary>
    public const int FeeHopCount = 5;

    /// <summary>How many customers and payees the nested service has.</summary>
    public const int NestedCustomers = 9;

    /// <summary>Gets the central mixer without a label: one pool address its customers pay and it pays out from.</summary>
    public TestAddress Pool { get; } = TestAddress.Wsh("malstrommen");

    /// <summary>Gets the central mixer's customers, each paying the pool once.</summary>
    public IReadOnlyList<TestAddress> PoolDepositors { get; } =
        [.. Enumerable.Range(1, PoolCustomers).Select(index => TestAddress.Wpkh($"malstrommen-kund-{index}"))];

    /// <summary>
    /// Gets the fresh addresses the central mixer pays its customers' money out to, one each, of both address types,
    /// so that the type says little.
    /// </summary>
    public IReadOnlyList<TestAddress> PoolRecipients { get; } =
    [
        .. Enumerable.Range(1, PoolCustomers).Select(index => index % 2 == 0
            ? TestAddress.Pkh($"malstrommen-ut-{index}")
            : TestAddress.Wpkh($"malstrommen-ut-{index}")),
    ];

    /// <summary>Gets the payer whose deposit into Blandaren starts the chain of mixers.</summary>
    public TestAddress ChainPayer { get; } = TestAddress.Wpkh("kedjebetalaren");

    /// <summary>Gets Blandaren's first part of the chain's deposit, which goes into Virveln.</summary>
    public TestAddress ChainBranchA { get; } = TestAddress.Wpkh("kedja-gren-a");

    /// <summary>Gets Blandaren's second part of the chain's deposit, which goes into the central mixer.</summary>
    public TestAddress ChainBranchB { get; } = TestAddress.Wpkh("kedja-gren-b");

    /// <summary>
    /// Gets the central mixer's way out for the second part, of another address type than the part, which goes into
    /// Virveln.
    /// </summary>
    public TestAddress ChainOut { get; } = TestAddress.Pkh("kedja-ut");

    /// <summary>Gets the payer that splits its money into equal parts over several transactions.</summary>
    public TestAddress Spreader { get; } = TestAddress.Wpkh("spridaren");

    /// <summary>Gets the spread split's parts, each a recipient of its own.</summary>
    public IReadOnlyList<TestAddress> SpreadPartsTo { get; } =
        [.. Enumerable.Range(1, SpreadParts).Select(index => TestAddress.Wpkh($"spridd-del-{index}"))];

    /// <summary>Gets the address the fourth spread part passes through before it meets the first.</summary>
    public TestAddress SpreadPartHop { get; } = TestAddress.Wpkh("spridd-del-4-hopp");

    /// <summary>Gets the address where two of the spread parts meet again.</summary>
    public TestAddress SpreadJoined { get; } = TestAddress.Wpkh("spridda-mots");

    /// <summary>Gets the holders the layering row passes the money through, one more than the hops.</summary>
    public IReadOnlyList<TestAddress> FeeHolders { get; } =
        [.. Enumerable.Range(0, FeeHopCount + 1).Select(index => TestAddress.Wpkh($"skiktning-{index}"))];

    /// <summary>Gets the layering service's fee address, which every other hop pays; no label, so it is found.</summary>
    public TestAddress LayeringFee { get; } = TestAddress.Wpkh("skiktarens-avgift");

    /// <summary>
    /// Gets the nested service: a small exchange without a label that runs its business through Nordbörsen.
    /// </summary>
    public TestAddress NestedKiosk { get; } = TestAddress.Wpkh("vaxelkiosken");

    /// <summary>
    /// Gets the nested service's deposit address at Nordbörsen, which Nordbörsen sweeps into its hot wallet.
    /// </summary>
    public TestAddress NestedAccount { get; } = TestAddress.Wpkh("nordborsen-kioskens-konto");

    /// <summary>Gets the nested service's customers, each paying it once.</summary>
    public IReadOnlyList<TestAddress> NestedCustomersFrom { get; } =
        [.. Enumerable.Range(1, NestedCustomers).Select(index => TestAddress.Wpkh($"vaxelkiosken-kund-{index}"))];

    /// <summary>Gets the nested service's payees, each paid once from a withdrawal from Nordbörsen.</summary>
    public IReadOnlyList<TestAddress> NestedPayees { get; } =
        [.. Enumerable.Range(1, NestedCustomers).Select(index => TestAddress.Wpkh($"vaxelkiosken-mottagare-{index}"))];
}
#endif
