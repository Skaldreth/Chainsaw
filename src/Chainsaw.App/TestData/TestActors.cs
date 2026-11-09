#if DEBUG
namespace Chainsaw.App.TestData;

/// <summary>
/// Everyone in the test case's story, as made-up addresses. The labels are the addresses' seeds, so they never change
/// between runs; a new part of the story adds its actors here.
/// </summary>
public sealed partial class TestActors
{
    /// <summary>How many senders pay the hub, so that it has more than the expansion's limit of transactions.</summary>
    public const int HubSenders = 40;

    /// <summary>How many of the hub's coins it pays on, each to a counterparty of its own.</summary>
    public const int HubReceivers = 24;

    /// <summary>How many deposits of Sydbörsen's customers are swept into its hot wallet.</summary>
    public const int SouthDeposits = 10;

    /// <summary>How many hops a CoinJoin output of the scammer's passes through.</summary>
    public const int HopCount = 5;

    /// <summary>How many parts the CoinJoin output is split into after the hops.</summary>
    public const int PartCount = 6;

    /// <summary>How many ways go alike through the mixer to Sydbörsen.</summary>
    public const int PatternWays = 2;

    /// <summary>How many mules each of the alike ways passes through.</summary>
    public const int PatternMulesPerWay = 2;

    /// <summary>Gets the exchange's hot wallet that paid the victim out.</summary>
    public TestAddress ExchangeHot { get; } = TestAddress.Wsh("nordborsen-hot");

    /// <summary>Gets the victim.</summary>
    public TestAddress Victim { get; } = TestAddress.Wpkh("offret");

    /// <summary>Gets the scammer's address the victim paid.</summary>
    public TestAddress Scammer { get; } = TestAddress.Wpkh("bedragaren-1");

    /// <summary>Gets the scammer's second address, paid by another victim.</summary>
    public TestAddress Scammer2 { get; } = TestAddress.Wpkh("bedragaren-2");

    /// <summary>Gets the scammer's third address, paid by a third victim.</summary>
    public TestAddress Scammer3 { get; } = TestAddress.Wpkh("bedragaren-3");

    /// <summary>Gets the second victim.</summary>
    public TestAddress Victim2 { get; } = TestAddress.Wpkh("offer-2");

    /// <summary>Gets the third victim.</summary>
    public TestAddress Victim3 { get; } = TestAddress.Wpkh("offer-3");

    /// <summary>Gets the address the consolidation pays everything to.</summary>
    public TestAddress Consolidated { get; } = TestAddress.Wpkh("samlad");

    /// <summary>Gets the address that tops the consolidated coin up for the next payment.</summary>
    public TestAddress TopUp { get; } = TestAddress.Wpkh("pafyllnad");

    /// <summary>Gets the change address of the payment after the consolidation.</summary>
    public TestAddress Change { get; } = TestAddress.Wpkh("vaxel");

    /// <summary>Gets the miner's coin without an address (pay to public key).</summary>
    public TestAddress Miner { get; } = TestAddress.PublicKey("gruvarbetaren");

    /// <summary>Gets the coinbase's OP_RETURN (the witness commitment), which nothing can spend.</summary>
    public TestAddress Commitment { get; } = TestAddress.OpReturn("vittnesatagande");

    /// <summary>Gets the owner who spends together with the miner's coin.</summary>
    public TestAddress CoOwner { get; } = TestAddress.Wpkh("delagaren");

    /// <summary>Gets the distributor, paid with an old-style address.</summary>
    public TestAddress Distributor { get; } = TestAddress.Pkh("fordelaren");

    /// <summary>Gets the mixer's address.</summary>
    public TestAddress Mixer { get; } = TestAddress.Wsh("blandaren");

    /// <summary>Gets someone else who deposited to the mixer earlier, so that it has coins to pay out from.</summary>
    public TestAddress MixerDepositor { get; } = TestAddress.Wpkh("blandarkund");

    /// <summary>Gets the mixer's likely way out: the same type as the deposit and about 2 % less.</summary>
    public TestAddress MixerOut1 { get; } = TestAddress.Pkh("blandat-ut-1");

    /// <summary>Gets the mixer's less likely way out: another type and 5 % less.</summary>
    public TestAddress MixerOut2 { get; } = TestAddress.Wpkh("blandat-ut-2");

    /// <summary>Gets the second mixer the less likely way out goes into: mixer after mixer.</summary>
    public TestAddress SecondMixer { get; } = TestAddress.Wsh("virveln");

    /// <summary>
    /// Gets the exchange the mixer's way out goes on to, for the user to find. Its seed was searched for together with
    /// the poisoner's (<see cref="LookAlike"/>), so that the two look alike.
    /// </summary>
    public TestAddress SouthExchange { get; } = TestAddress.Wpkh("sydborsen-insattning-1849900");

    /// <summary>Gets the exchange's second deposit address, paid the same amount at the same time.</summary>
    public TestAddress SouthExchange2 { get; } = TestAddress.Wpkh("sydborsen-insattning-2");

    /// <summary>Gets a coin of the mixer's way out's own, spent with it: proposed as the same wallet.</summary>
    public TestAddress MixerOut1Reserve { get; } = TestAddress.Pkh("blandat-ut-1-reserv");

    /// <summary>Gets the scammer's coin into the CoinJoin.</summary>
    public TestAddress CoinJoinIn { get; } = TestAddress.Wpkh("whirlpool-in");

    /// <summary>Gets the CoinJoin's other participants.</summary>
    public IReadOnlyList<TestAddress> CoinJoinOthers { get; } =
        [.. Enumerable.Range(1, 4).Select(index => TestAddress.Wpkh($"whirlpool-annan-{index}"))];

    /// <summary>Gets the CoinJoin's equal outputs.</summary>
    public IReadOnlyList<TestAddress> CoinJoinOut { get; } =
        [.. Enumerable.Range(1, 5).Select(index => TestAddress.Wpkh($"whirlpool-ut-{index}"))];

    /// <summary>Gets the peel chain's addresses, each the next hop's.</summary>
    public IReadOnlyList<TestAddress> Peel { get; } =
        [.. Enumerable.Range(1, 6).Select(index => TestAddress.Wpkh($"skalning-{index}"))];

    /// <summary>Gets the gambling site the peel chain pays first.</summary>
    public TestAddress Gambling { get; } = TestAddress.Wpkh("lyckohjulet");

    /// <summary>Gets the shop.</summary>
    public TestAddress Merchant { get; } = TestAddress.Wpkh("elbutiken");

    /// <summary>Gets the gift card seller.</summary>
    public TestAddress GiftCard { get; } = TestAddress.Wpkh("kortkungen");

    /// <summary>Gets the crypto ATM.</summary>
    public TestAddress Atm { get; } = TestAddress.Wpkh("automaten");

    /// <summary>Gets the crypto card.</summary>
    public TestAddress Card { get; } = TestAddress.Wpkh("kryptokortet");

    /// <summary>Gets the exchange deposit where the peel chain ends: the cash-out.</summary>
    public TestAddress Exchange { get; } = TestAddress.Wpkh("nordborsen-insattning");

    /// <summary>Gets the exchange without customer checks.</summary>
    public TestAddress NoKycExchange { get; } = TestAddress.Wpkh("snabbvaxlaren");

    /// <summary>
    /// Gets the exchange without customer checks' new deposit address, which the gone spend paid first and a later
    /// payment pays again.
    /// </summary>
    public TestAddress NoKycDeposit { get; } = TestAddress.Wpkh("snabbvaxlaren-ny-insattning");

    /// <summary>Gets the change of the later payment to <see cref="NoKycDeposit"/>.</summary>
    public TestAddress ReusedDepositChange { get; } = TestAddress.Wpkh("insattningen-igen-vaxel");

    /// <summary>Gets the hub: an address with many transactions and many counterparties.</summary>
    public TestAddress Hub { get; } = TestAddress.Wpkh("navet");

    /// <summary>Gets the darknet market, also on a sanctions list.</summary>
    public TestAddress Darknet { get; } = TestAddress.Wpkh("skuggtorget");

    /// <summary>Gets the escrow market, labelled only in a colleague's case.</summary>
    public TestAddress Escrow { get; } = TestAddress.Wpkh("garantitorget");

    /// <summary>Gets the OTC desk.</summary>
    public TestAddress Otc { get; } = TestAddress.Wpkh("torgvaxlaren");

    /// <summary>Gets the nested service.</summary>
    public TestAddress Nested { get; } = TestAddress.Wpkh("inbaddade");

    /// <summary>Gets the swap service that takes the money to another chain.</summary>
    public TestAddress Swap { get; } = TestAddress.Wpkh("bytaren");

    /// <summary>Gets the sanctioned actor.</summary>
    public TestAddress Sanctioned { get; } = TestAddress.Wpkh("sanktionerad");

    /// <summary>Gets the hub's senders.</summary>
    public IReadOnlyList<TestAddress> HubIn { get; } =
        [.. Enumerable.Range(1, HubSenders).Select(index => TestAddress.Wpkh($"navet-in-{index}"))];

    /// <summary>Gets the hub's counterparties it pays.</summary>
    public IReadOnlyList<TestAddress> HubOut { get; } =
        [.. Enumerable.Range(1, HubReceivers).Select(index => TestAddress.Wpkh($"navet-ut-{index}"))];

    /// <summary>Gets the new victim whose payment the watch catches.</summary>
    public TestAddress NewVictim { get; } = TestAddress.Wpkh("nytt-offer");

    /// <summary>Gets the duster, who sends tiny amounts to many addresses in the graph.</summary>
    public TestAddress Duster { get; } = TestAddress.Wpkh("dammaren");

    /// <summary>
    /// Gets the poisoner's address that looks like Sydbörsen's deposit (<see cref="SouthExchange"/>) in its first and
    /// last four characters: both seeds were searched for until their addresses looked alike.
    /// </summary>
    public TestAddress LookAlike { get; } = TestAddress.Wpkh("forgiftaren-754626");

    /// <summary>Gets Sydbörsen's hot wallet, which its deposits are swept into; no label, so that it is to be found.</summary>
    public TestAddress SouthHot { get; } = TestAddress.Wsh("sydborsen-het");

    /// <summary>Gets Sydbörsen's customers, each paying a deposit address of its own.</summary>
    public IReadOnlyList<TestAddress> SouthCustomers { get; } =
        [.. Enumerable.Range(1, SouthDeposits).Select(index => TestAddress.Wpkh($"sydborsen-kund-{index}"))];

    /// <summary>Gets Sydbörsen's deposit addresses for its customers.</summary>
    public IReadOnlyList<TestAddress> SouthCustomerDeposits { get; } =
        [.. Enumerable.Range(1, SouthDeposits).Select(index => TestAddress.Wpkh($"sydborsen-kundinsattning-{index}"))];

    /// <summary>Gets the addresses a CoinJoin output of the scammer's passes through, one per hop.</summary>
    public IReadOnlyList<TestAddress> Hops { get; } =
        [.. Enumerable.Range(1, HopCount).Select(index => TestAddress.Wpkh($"hopp-{index}"))];

    /// <summary>Gets the parts the CoinJoin output is split into after the hops.</summary>
    public IReadOnlyList<TestAddress> Parts { get; } =
        [.. Enumerable.Range(1, PartCount).Select(index => TestAddress.Wpkh($"del-{index}"))];

    /// <summary>Gets the address a part passes through before the parts meet again.</summary>
    public TestAddress PartHop { get; } = TestAddress.Wpkh("del-2-hopp");

    /// <summary>Gets the address where some of the parts meet again.</summary>
    public TestAddress Joined { get; } = TestAddress.Wpkh("delar-mots");

    /// <summary>Gets the payee of the possible PayJoin: an old-style address, as its own coin and its new output.</summary>
    public TestAddress PayJoinPayee { get; } = TestAddress.Pkh("payjoin-mottagare");

    /// <summary>Gets the PayJoin payee's new address, paid the payment and its own coin back.</summary>
    public TestAddress PayJoinReceived { get; } = TestAddress.Pkh("payjoin-mottagare-ny");

    /// <summary>Gets the payer's change in the possible PayJoin.</summary>
    public TestAddress PayJoinChange { get; } = TestAddress.Wpkh("payjoin-vaxel");

    /// <summary>
    /// Gets the two victims whose money takes the same way through the mixer to Sydbörsen (the pattern
    /// <em>Samma väg</em>).
    /// </summary>
    public IReadOnlyList<TestAddress> PatternVictims { get; } =
        [.. Enumerable.Range(1, PatternWays).Select(index => TestAddress.Wpkh($"monster-offer-{index}"))];

    /// <summary>Gets the scammers' addresses the two victims pay, each depositing into the mixer.</summary>
    public IReadOnlyList<TestAddress> PatternScammers { get; } =
        [.. Enumerable.Range(1, PatternWays).Select(index => TestAddress.Wpkh($"monster-bedragare-{index}"))];

    /// <summary>Gets the mixer's two ways out that the two victims' money takes, one per victim.</summary>
    public IReadOnlyList<TestAddress> PatternOuts { get; } =
        [.. Enumerable.Range(1, PatternWays).Select(index => TestAddress.Wpkh($"monster-blandat-ut-{index}"))];

    /// <summary>
    /// Gets the mules each way passes through after the mixer, two per way: the first way's first, the first way's
    /// second, then the second way's.
    /// </summary>
    public IReadOnlyList<TestAddress> PatternMules { get; } =
    [
        .. Enumerable.Range(1, PatternWays).SelectMany(way => Enumerable.Range(1, PatternMulesPerWay)
            .Select(mule => TestAddress.Wpkh($"monster-mula-{way}-{mule}"))),
    ];

    /// <summary>
    /// Gets Sydbörsen's deposit address that both ways end at, and that the run of alike amounts is paid into.
    /// </summary>
    public TestAddress PatternDeposit { get; } = TestAddress.Wpkh("sydborsen-insattning-monster");

    /// <summary>
    /// Gets the mixer's way out that pays Sydbörsen alike amounts in a row (the pattern <em>Likadana belopp</em>).
    /// </summary>
    public TestAddress Structurer { get; } = TestAddress.Wpkh("uppdelaren");

    /// <summary>Gets the mixer's way out that pays the swap service, whose continuation on Ethereum is suggested.</summary>
    public TestAddress BridgePayer { get; } = TestAddress.Wpkh("till-bryggan");
}
#endif
