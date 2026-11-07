namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// What a label says an address or a cluster is, sorted by what happens to the money there (CLAUDE.md,
/// <em>Sorterna efter vad som händer med pengarna</em>). The names are stored in the case, so they never change.
/// </summary>
public enum LabelKind
{
    /// <summary>An exchange with customer checks (KYC): where the money is usually taken out.</summary>
    Exchange,

    /// <summary>An exchange without customer checks (drawn with a dashed frame).</summary>
    NoKycExchange,

    /// <summary>A nested service: a service that runs on an account at an exchange.</summary>
    NestedService,

    /// <summary>OTC desks and peer-to-peer trading.</summary>
    OtcP2P,

    /// <summary>A crypto ATM.</summary>
    CryptoAtm,

    /// <summary>A crypto card: a payment card loaded with crypto.</summary>
    CryptoCard,

    /// <summary>Gambling and casinos (two playing cards).</summary>
    Gambling,

    /// <summary>A merchant or a payment service (a shop).</summary>
    Merchant,

    /// <summary>Gift cards bought with crypto.</summary>
    GiftCard,

    /// <summary>A darknet market.</summary>
    Darknet,

    /// <summary>An escrow (guarantee) market.</summary>
    EscrowMarket,

    /// <summary>A mixer: mixes customers' money and pays out from other coins.</summary>
    Mixer,

    /// <summary>A CoinJoin coordinator or its participants' transactions.</summary>
    CoinJoin,

    /// <summary>On a sanction list.</summary>
    Sanctioned,

    /// <summary>A swap service or a bridge to another chain.</summary>
    SwapBridge,

    /// <summary>The victim, whose money is traced.</summary>
    Victim,

    /// <summary>Something the user has looked at but does not know what it is.</summary>
    Unknown,
}

/// <summary>The colour group of a <see cref="LabelKind"/> in the graph, after what happens to the money.</summary>
public enum LabelGroup
{
    /// <summary>Blue: the money is taken out (exchanges and the like).</summary>
    Withdrawal,

    /// <summary>Yellow: the money is spent.</summary>
    Spending,

    /// <summary>Red: risk and the end of the trail.</summary>
    Risk,

    /// <summary>Purple: the money continues on another chain.</summary>
    OtherChain,

    /// <summary>The accent colour: the victim's money.</summary>
    Victim,

    /// <summary>No colour of its own.</summary>
    Neutral,
}

/// <summary>Reads what a <see cref="LabelKind"/> means.</summary>
public static class LabelKinds
{
    /// <summary>Gives the kind's colour group.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The group.</returns>
    public static LabelGroup Group(this LabelKind kind) => kind switch
    {
        LabelKind.Exchange or LabelKind.NoKycExchange or LabelKind.NestedService or LabelKind.OtcP2P
            or LabelKind.CryptoAtm or LabelKind.CryptoCard => LabelGroup.Withdrawal,
        LabelKind.Gambling or LabelKind.Merchant or LabelKind.GiftCard => LabelGroup.Spending,
        LabelKind.Darknet or LabelKind.EscrowMarket or LabelKind.Mixer or LabelKind.CoinJoin
            or LabelKind.Sanctioned => LabelGroup.Risk,
        LabelKind.SwapBridge => LabelGroup.OtherChain,
        LabelKind.Victim => LabelGroup.Victim,
        _ => LabelGroup.Neutral,
    };

    /// <summary>Gives the kind as the user reads it, in Swedish.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A short text.</returns>
    public static string Describe(this LabelKind kind) => kind switch
    {
        LabelKind.Exchange => "Börs",
        LabelKind.NoKycExchange => "Börs utan kundkontroll",
        LabelKind.NestedService => "Nästlad tjänst",
        LabelKind.OtcP2P => "OTC eller P2P",
        LabelKind.CryptoAtm => "Kryptoautomat",
        LabelKind.CryptoCard => "Kryptokort",
        LabelKind.Gambling => "Spel",
        LabelKind.Merchant => "Handel",
        LabelKind.GiftCard => "Presentkort",
        LabelKind.Darknet => "Darknet",
        LabelKind.EscrowMarket => "Garantimarknad",
        LabelKind.Mixer => "Mixer",
        LabelKind.CoinJoin => "CoinJoin",
        LabelKind.Sanctioned => "Sanktionerad",
        LabelKind.SwapBridge => "Swap eller brygga",
        LabelKind.Victim => "Offer",
        LabelKind.Unknown => "Okänd",
        _ => kind.ToString(),
    };
}
