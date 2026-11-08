namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What a known contract on Ethereum is, as Chainsaw reads what happens to money sent to it.</summary>
public enum KnownContractCategory
{
    /// <summary>
    /// A mixer's contract (Tornado Cash): money paid in is paid out to another address that the chain does not link
    /// to the one that paid in, so the trail ends here.
    /// </summary>
    Mixer,

    /// <summary>
    /// A decentralised exchange's router: it swaps one token for another for the account that calls it. The money
    /// changes token but not owner, so a swap through it is not an end of the trail.
    /// </summary>
    Dex,

    /// <summary>
    /// An aggregator: a router that splits a swap over several exchanges for the account that calls it; like
    /// <see cref="Dex"/>, the money changes token but not owner.
    /// </summary>
    Aggregator,

    /// <summary>
    /// A bridge's contract on Ethereum: money paid in continues on another chain, where the trail goes on when the
    /// user links the other chain's transaction.
    /// </summary>
    Bridge,
}

/// <summary>
/// A contract on Ethereum that Chainsaw knows from a public source: the source and the day it was read go with it,
/// so that every conclusion drawn from it can say what it rests on (principles 2 and 3). That an address is one of
/// these is the source's statement, never a fact Chainsaw read from the chain.
/// </summary>
/// <param name="Address">The contract's address.</param>
/// <param name="Name">The contract as the user reads it, for example <c>Tornado Cash 1 ETH</c>.</param>
/// <param name="Operator">The service the contract belongs to, for example <c>Tornado Cash</c>.</param>
/// <param name="Category">What it is.</param>
/// <param name="Source">The public page the address was read from.</param>
/// <param name="Checked">The day the address was read from the source.</param>
public sealed record KnownContract(
    EthereumAddress Address,
    string Name,
    string Operator,
    KnownContractCategory Category,
    Uri Source,
    DateOnly Checked)
{
    /// <summary>
    /// Gets the sanction the contract was under and has been taken off, or <see langword="null"/> when it never was
    /// one Chainsaw knows of.
    /// </summary>
    public FormerSanction? FormerSanction { get; init; }

    /// <summary>Says where Chainsaw has the contract from, in Swedish, for <em>varför?</em>.</summary>
    /// <returns>For example <c>enligt https://… (läst 2026-11-08)</c>.</returns>
    public string Provenance() => $"enligt {Source} (läst {Checked:yyyy-MM-dd})";
}

/// <summary>
/// A sanction a contract was under and has been taken off, with the list's own public notices of both (principle 2: a
/// judgement with its source; a sanction in force comes only from the list the user imported).
/// </summary>
/// <param name="List">The list, for example <c>OFAC SDN</c>.</param>
/// <param name="Designated">The day it was put on the list.</param>
/// <param name="DesignationSource">The list's notice of the designation.</param>
/// <param name="Removed">The day it was taken off the list.</param>
/// <param name="RemovalSource">The list's notice of the removal.</param>
/// <param name="Checked">The day the notices were read.</param>
public sealed record FormerSanction(
    string List, DateOnly Designated, Uri DesignationSource, DateOnly Removed, Uri RemovalSource, DateOnly Checked)
{
    /// <summary>Says what the sanction was, in Swedish, for <em>varför?</em>.</summary>
    /// <returns>The dates and both notices.</returns>
    public string Describe() =>
        $"Stod på {List} från {Designated:yyyy-MM-dd} ({DesignationSource}) till {Removed:yyyy-MM-dd} "
        + $"({RemovalSource}), läst {Checked:yyyy-MM-dd}. Det är inte en sanktion som gäller nu.";
}

/// <summary>
/// The contracts on Ethereum that Chainsaw recognises by their address, never by a name a source or a contract gives:
/// Tornado Cash's pools (a mixer), the routers of the largest decentralised exchanges and aggregators, and the
/// bridges of the largest layer-2 chains. Every address is copied from the operator's own public page, named with the
/// day it was read; none is made up. The list is versioned (<see cref="Version"/>), so that a label set from it says
/// which list it came from.
/// </summary>
/// <remarks>
/// Tornado Cash's addresses were put on OFAC's SDN list on 2022-08-08 and taken off it on 2025-03-21 (both read from
/// OFAC's own notices on 2026-11-08), so the list here says mixer and formerly sanctioned (the user's decision
/// 2026-11-08), never sanctioned. A sanction in force comes only from the list the user imported. Every Tornado entry
/// here, the 19 pools, the router and the proxy, is among the addresses of both notices.
/// </remarks>
public static class KnownContracts
{
    /// <summary>The list's name, as a label's origin states it.</summary>
    public const string Name = "Chainsaws kända kontrakt";

    /// <summary>The list's version: raise it whenever an entry is added, changed or removed.</summary>
    public const string Version = "v1 2026-11-08";

    /// <summary>The day the addresses below were read from their sources.</summary>
    private static readonly DateOnly Read = new(2026, 11, 8);

    /// <summary>Tornado Cash's own list of its contracts.</summary>
    private static readonly Uri TornadoDocs =
        new("https://github.com/tornadocash/docs/blob/en/general/tornado-cash-smart-contracts.md");

    /// <summary>
    /// Tornado Cash's time on OFAC's SDN list: the designation of 2022-08-08 and the delisting of 2025-03-21, each
    /// read from OFAC's own notice of recent actions, which lists every Tornado address here.
    /// </summary>
    private static readonly FormerSanction TornadoSanction = new(
        "OFAC SDN",
        new DateOnly(2022, 8, 8),
        new Uri("https://ofac.treasury.gov/recent-actions/20220808"),
        new DateOnly(2025, 3, 21),
        new Uri("https://ofac.treasury.gov/recent-actions/20250321"),
        new DateOnly(2026, 11, 8));

    /// <summary>Uniswap's list of its v2 contracts on Ethereum.</summary>
    private static readonly Uri UniswapV2Docs = new("https://developers.uniswap.org/docs/protocols/v2/deployments");

    /// <summary>Uniswap's list of its v3 contracts on Ethereum.</summary>
    private static readonly Uri UniswapV3Docs =
        new("https://developers.uniswap.org/docs/protocols/v3/deployments/v3-ethereum-deployments");

    /// <summary>Uniswap's list of the Universal Router's addresses on Ethereum.</summary>
    private static readonly Uri UniversalRouterDocs =
        new("https://github.com/Uniswap/universal-router/blob/main/deploy-addresses/mainnet.json");

    /// <summary>1inch's list of its Router v6 (with the Limit Order Protocol v4).</summary>
    private static readonly Uri OneInchDocs =
        new("https://github.com/1inch/limit-order-protocol/blob/master/README.md");

    /// <summary>0x's list of its contract addresses.</summary>
    private static readonly Uri ZeroExDocs = new(
        "https://github.com/0xProject/protocol/blob/development/packages/contract-addresses/addresses.json");

    /// <summary>Arbitrum's list of its contracts on Ethereum.</summary>
    private static readonly Uri ArbitrumDocs = new("https://docs.arbitrum.io/for-devs/useful-addresses");

    /// <summary>The Superchain registry's entry for OP Mainnet.</summary>
    private static readonly Uri OptimismDocs = new(
        "https://github.com/ethereum-optimism/superchain-registry/blob/main/superchain/configs/mainnet/op.toml");

    /// <summary>Base's list of its contracts on Ethereum.</summary>
    private static readonly Uri BaseDocs =
        new("https://docs.base.org/base-chain/network-information/base-contracts");

    /// <summary>Polygon's list of its contracts on Ethereum.</summary>
    private static readonly Uri PolygonDocs = new("https://static.polygon.technology/network/mainnet/v1/index.json");

    /// <summary>The contracts, by address.</summary>
    private static readonly IReadOnlyDictionary<EthereumAddress, KnownContract> ByAddress = Build();

    /// <summary>Gets every known contract, in the list's order.</summary>
    public static IReadOnlyList<KnownContract> All { get; } = [.. ByAddress.Values];

    /// <summary>Gives the known contract at an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The contract, or <see langword="null"/> when Chainsaw does not know the address.</returns>
    public static KnownContract? Find(EthereumAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return ByAddress.GetValueOrDefault(address);
    }

    /// <summary>Builds the list from its entries.</summary>
    /// <returns>The contracts by address, in the entries' order.</returns>
    private static Dictionary<EthereumAddress, KnownContract> Build()
    {
        var list = new Dictionary<EthereumAddress, KnownContract>();
        foreach (var entry in Tornado().Concat(Exchanges()).Concat(Bridges()))
        {
            list.Add(entry.Address, entry);
        }

        return list;
    }

    /// <summary>Tornado Cash Classic's pools on Ethereum, its router and its old proxy.</summary>
    /// <returns>The contracts.</returns>
    private static IEnumerable<KnownContract> Tornado()
    {
        const string tornado = "Tornado Cash";
        KnownContract Pool(string address, string pool) =>
            Entry(address, $"{tornado} {pool}", tornado, KnownContractCategory.Mixer, TornadoDocs) with
            {
                FormerSanction = TornadoSanction,
            };

        yield return Pool("0x12D66f87A04A9E220743712cE6d9bB1B5616B8Fc", "0,1 ETH");
        yield return Pool("0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936", "1 ETH");
        yield return Pool("0x910Cbd523D972eb0a6f4cAe4618aD62622b39DbF", "10 ETH");
        yield return Pool("0xA160cdAB225685dA1d56aa342Ad8841c3b53f291", "100 ETH");
        yield return Pool("0xD4B88Df4D29F5CedD6857912842cff3b20C8Cfa3", "100 DAI");
        yield return Pool("0xFD8610d20aA15b7B2E3Be39B396a1bC3516c7144", "1 000 DAI");
        yield return Pool("0x07687e702b410Fa43f4cB4Af7FA097918ffD2730", "10 000 DAI");
        yield return Pool("0x23773E65ed146A459791799d01336DB287f25334", "100 000 DAI");
        yield return Pool("0x22aaA7720ddd5388A3c0A3333430953C68f1849b", "5 000 cDAI");
        yield return Pool("0x03893a7c7463AE47D46bc7f091665f1893656003", "50 000 cDAI");
        yield return Pool("0x2717c5e28cf931547B621a5dddb772Ab6A35B701", "500 000 cDAI");
        yield return Pool("0xD21be7248e0197Ee08E0c20D4a96DEBdaC3D20Af", "5 000 000 cDAI");
        yield return Pool("0x4736dCf1b7A3d580672CcE6E7c65cd5cc9cFBa9D", "100 USDC");
        yield return Pool("0xd96f2B1c14Db8458374d9Aca76E26c3D18364307", "1 000 USDC");
        yield return Pool("0x169AD27A470D064DEDE56a2D3ff727986b15D52B", "100 USDT");
        yield return Pool("0x0836222F2B2B24A3F36f98668Ed8F0B38D1a872f", "1 000 USDT");
        yield return Pool("0x178169B423a011fff22B9e3F3abeA13414dDD0F1", "0,1 WBTC");
        yield return Pool("0x610B717796ad172B316836AC95a2ffad065CeaB4", "1 WBTC");
        yield return Pool("0xbB93e510BbCD0B7beb5A853875f9eC60275CF498", "10 WBTC");
        yield return Pool("0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b", "router");
        yield return Pool("0x722122dF12D4e14e13Ac3b6895a86e84145b6967", "proxy");
    }

    /// <summary>The routers of the largest decentralised exchanges and aggregators on Ethereum.</summary>
    /// <returns>The contracts.</returns>
    private static IEnumerable<KnownContract> Exchanges()
    {
        const string uniswap = "Uniswap";
        KnownContract Uniswap(string address, string router, Uri source) =>
            Entry(address, $"{uniswap} {router}", uniswap, KnownContractCategory.Dex, source);

        yield return Uniswap("0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D", "V2 Router02", UniswapV2Docs);
        yield return Uniswap("0xE592427A0AEce92De3Edee1F18E0157C05861564", "V3 SwapRouter", UniswapV3Docs);
        yield return Uniswap("0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45", "SwapRouter02", UniswapV3Docs);
        yield return Uniswap("0xEf1c6E67703c7BD7107eed8303Fbe6EC2554BF6B", "Universal Router v1", UniversalRouterDocs);
        yield return Uniswap(
            "0x3fC91A3afd70395Cd496C647d5a6CC9D4B2b7FAD", "Universal Router v1.2", UniversalRouterDocs);
        yield return Uniswap("0x66a9893cc07d91d95644aedd05d03f95e1dba8af", "Universal Router v2", UniversalRouterDocs);
        yield return Uniswap(
            "0x4C82D1fBFe28C977cBB58D8C7FF8FCF9F70a2cCA", "Universal Router v2.1.1", UniversalRouterDocs);
        yield return Uniswap(
            "0x23617e59A5925b2A4Bf75d73ff6711cD0b29De85", "Universal Router v2.1.2", UniversalRouterDocs);
        yield return Entry(
            "0x111111125421cA6dc452d289314280a0f8842A65", "1inch Router v6", "1inch",
            KnownContractCategory.Aggregator, OneInchDocs);
        yield return Entry(
            "0xdef1c0ded9bec7f1a1670819833240f027b25eff", "0x Exchange Proxy", "0x",
            KnownContractCategory.Aggregator, ZeroExDocs);
    }

    /// <summary>The bridges of the largest layer-2 chains, where money paid in continues on the other chain.</summary>
    /// <returns>The contracts.</returns>
    private static IEnumerable<KnownContract> Bridges()
    {
        const string arbitrum = "Arbitrum One";
        const string optimism = "OP Mainnet";
        const string polygon = "Polygon PoS";
        const string baseChain = "Base";
        KnownContract Bridge(string address, string name, string chain, Uri source) =>
            Entry(address, $"{chain} {name}", chain, KnownContractCategory.Bridge, source);

        yield return Bridge("0x4Dbd4fc535Ac27206064B68FfCf827b0A60BAB3f", "Delayed Inbox", arbitrum, ArbitrumDocs);
        yield return Bridge("0x8315177aB297bA92A06054cE80a67Ed4DBd7ed3a", "Bridge", arbitrum, ArbitrumDocs);
        yield return Bridge("0x72Ce9c846789fdB6fC1f34aC4AD25Dd9ef7031ef", "L1 Gateway Router", arbitrum, ArbitrumDocs);
        yield return Bridge("0xa3A7B6F88361F48403514059F1F16C8E78d60EeC", "L1 ERC20 Gateway", arbitrum, ArbitrumDocs);
        yield return Bridge("0xcEe284F754E854890e311e3280b767F80797180d", "L1 Custom Gateway", arbitrum, ArbitrumDocs);
        yield return Bridge("0xd92023E9d9911199a6711321D1277285e6d4e2db", "L1 WETH Gateway", arbitrum, ArbitrumDocs);
        yield return Bridge("0x99C9fc46f92E8a1c0deC1b1747d010903E884bE1", "L1 Standard Bridge", optimism, OptimismDocs);
        yield return Bridge("0xbEb5Fc579115071764c7423A4f12eDde41f106Ed", "Optimism Portal", optimism, OptimismDocs);
        yield return Bridge("0x3154Cf16ccdb4C6d922629664174b904d80F2C35", "L1 Standard Bridge", baseChain, BaseDocs);
        yield return Bridge("0x49048044D57e1C92A77f79988d21Fa8fAF74E97e", "Optimism Portal", baseChain, BaseDocs);
        yield return Bridge("0xA0c68C638235ee32657e8f720a23ceC1bFc77C77", "RootChainManager", polygon, PolygonDocs);
        yield return Bridge("0x40ec5B33f54e0E8A33A975908C5BA1c14e5BbbDf", "ERC20 Predicate", polygon, PolygonDocs);
        yield return Bridge("0x8484Ef722627bf18ca5Ae6BcF031c23E6e922B30", "Ether Predicate", polygon, PolygonDocs);
        yield return Bridge(
            "0x401F6c983eA34274ec46f84D70b31C151321188b", "Deposit Manager (Plasma)", polygon, PolygonDocs);
    }

    /// <summary>Gives one entry, reading its address as the source wrote it (checking the checksum in mixed case).
    /// </summary>
    /// <param name="address">The address as the source wrote it.</param>
    /// <param name="name">The contract's name.</param>
    /// <param name="operator">The service it belongs to.</param>
    /// <param name="category">What it is.</param>
    /// <param name="source">The page it was read from.</param>
    /// <returns>The entry.</returns>
    private static KnownContract Entry(
        string address, string name, string @operator, KnownContractCategory category, Uri source) =>
        new(EthereumAddress.Parse(address), name, @operator, category, source, Read);
}
