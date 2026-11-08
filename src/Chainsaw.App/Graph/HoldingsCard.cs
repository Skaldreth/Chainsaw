using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>Where the rate on a card's back comes from, which its foot says (sketch IN4a).</summary>
public enum CardRateKind
{
    /// <summary>Today's rate is being fetched through the exit.</summary>
    Fetching,

    /// <summary>Today's rate, fetched through the exit: a green dot and the time.</summary>
    Today,

    /// <summary>The case's newest rate, because Chainsaw is offline: a clock and the date.</summary>
    FromCase,

    /// <summary>
    /// The case's newest rate, because no new one could be had although the exit lets lookups go: a clock, the date and
    /// <em>Ingen ny kurs</em> rather than <em>Offline</em>.
    /// </summary>
    NoNewRate,

    /// <summary>No rate at all: offline, and the case has none.</summary>
    None,
}

/// <summary>What the card's back is doing (sketch IN4a).</summary>
public enum CardState
{
    /// <summary>The balance is being fetched.</summary>
    Fetching,

    /// <summary>The balance is shown.</summary>
    Shown,

    /// <summary>The balance could not be had; the card says why.</summary>
    Failed,
}

/// <summary>One asset on a card's back: its ring, the amount with its last decimals dimmed, and its value.</summary>
/// <param name="Glyph">The asset's mark, for example <c>₿</c>, or its symbol.</param>
/// <param name="Name">The asset's name, for hovering the ring.</param>
/// <param name="Ink">The ring's brush key.</param>
/// <param name="Head">The amount without its last decimals.</param>
/// <param name="Tail">The last decimals, written dimmer.</param>
/// <param name="Full">The amount with its mark, for hovering.</param>
/// <param name="Value">What it is worth in the currency, or <see langword="null"/> without a rate.</param>
/// <param name="ValueTip">How the value was counted, for hovering, or <see langword="null"/>.</param>
/// <param name="CanBeFrozen">Whether its issuer can freeze it (the snowflake).</param>
public sealed record HoldingsRow(
    string Glyph, string Name, string Ink, string Head, string Tail, string Full, string? Value, string? ValueTip,
    bool CanBeFrozen)
{
    /// <summary>
    /// Gets what waits in the mempool for the asset, as its small clock says on hovering, or <see langword="null"/>.
    /// </summary>
    public string? Pending { get; init; }
}

/// <summary>
/// The back of a node's card (sketch IN4a): a row per asset that is left, with its value at today's rate or the
/// case's newest, the rate's line at the foot and the block the balance was read at. Nothing is made up: a missing
/// rate is a dash, a missing balance says why.
/// </summary>
/// <param name="NodeKey">The node.</param>
/// <param name="State">Whether the balance is fetched, shown or failed.</param>
/// <param name="Rows">The assets that are left; empty when nothing is.</param>
/// <param name="EmptySince">When the money last left, for an empty address, or <see langword="null"/>.</param>
/// <param name="Rate">Where the rate comes from.</param>
/// <param name="RateText">The rate's line at the foot.</param>
/// <param name="RateTip">The rate's line in full, for hovering.</param>
/// <param name="Block">The block the balance was read at, or an empty text.</param>
/// <param name="BlockTip">What the height means, for hovering.</param>
/// <param name="Problem">Why the balance could not be had, or what is missing; otherwise <see langword="null"/>.</param>
public sealed record HoldingsCard(
    string NodeKey,
    CardState State,
    IReadOnlyList<HoldingsRow> Rows,
    string? EmptySince,
    CardRateKind Rate,
    string RateText,
    string RateTip,
    string Block,
    string BlockTip,
    string? Problem)
{
    /// <summary>The card's width on the surface.</summary>
    public const double Width = 260;

    /// <summary>The head's height, with the node's name and the turn back.</summary>
    public const double HeadHeight = 32;

    /// <summary>One asset's row.</summary>
    public const double RowHeight = 50;

    /// <summary>The row of an empty address, or of what could not be had.</summary>
    public const double EmptyHeight = 46;

    /// <summary>The foot with the rate and the height.</summary>
    public const double FootHeight = 30;

    /// <summary>Gets whether the address holds nothing.</summary>
    public bool IsEmpty => State == CardState.Shown && Rows.Count == 0;

    /// <summary>Gets whether the foot shows a rate: not for an empty address, which needs none.</summary>
    public bool ShowsRate => Rows.Count > 0;

    /// <summary>Gets the card's height on the surface, which grows with its rows.</summary>
    public double Height => HeadHeight + (Rows.Count > 0 ? Rows.Count * RowHeight : EmptyHeight) + FootHeight + 2;
}

/// <summary>Builds the card's back (sketch IN4a), without WPF.</summary>
public static class HoldingsCards
{
    /// <summary>What a card says while its balance is fetched.</summary>
    public const string FetchingText = "Hämtar saldot …";

    /// <summary>What the foot says while today's rate is fetched.</summary>
    public const string FetchingRateText = "Hämtar kurs genom utgången";

    /// <summary>What the foot says offline when the case has no rate.</summary>
    public const string NoRateText = "Offline · ingen kurs i ärendet";

    /// <summary>What the foot says online when no rate could be had and the case has none.</summary>
    public const string NoRateOnlineText = "Ingen kurs att hämta";

    /// <summary>What a row's clock says before what waits in the mempool.</summary>
    public const string PendingPrefix = "Väntar i mempool: ";

    /// <summary>What an empty address says.</summary>
    public const string EmptyText = "Tom";

    /// <summary>What a card says when its balance could not be had; why stands under it.</summary>
    public const string FailedTitle = "Saldot kunde inte hämtas";

    /// <summary>Writes since when an address is empty.</summary>
    /// <param name="when">The time, as the card writes it.</param>
    /// <returns>For example <c>sedan 29 sep 10:20</c>.</returns>
    public static string SinceText(string when) => "sedan " + when;

    /// <summary>What a row says in place of a value without a rate.</summary>
    public const string NoValue = "– kr";

    /// <summary>How many of an amount's last decimals are written dimmer, so that the whole coins stand out.</summary>
    public const int TailDigits = 4;

    /// <summary>What the height's tooltip adds when the balance comes from the case.</summary>
    public const string OutdatedText =
        " Läst ur ärendet, eftersom källan inte kunde frågas; det kan ha ändrats sedan.";

    /// <summary>The Swedish culture.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Makes the card while its balance is fetched.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>The card.</returns>
    public static HoldingsCard Fetching(string nodeKey) => new(
        nodeKey, CardState.Fetching, [], null, CardRateKind.Fetching, FetchingRateText, FetchingRateText,
        string.Empty, string.Empty, null);

    /// <summary>Makes the card from a balance and the rates that value it.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <param name="holdings">The balance.</param>
    /// <param name="rates">The rates by asset: today's, or the case's newest.</param>
    /// <param name="rate">Where the rates come from.</param>
    /// <param name="emptySince">When the money last left the node, for an empty address.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone the times are written in.</param>
    /// <returns>The card.</returns>
    public static HoldingsCard Build(
        string nodeKey,
        NodeHoldings holdings,
        IReadOnlyDictionary<Asset, Price> rates,
        CardRateKind rate,
        DateTimeOffset? emptySince,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(rates);
        if (!holdings.HasBalance)
        {
            return new HoldingsCard(
                nodeKey, CardState.Failed, [], null, CardRateKind.None, string.Empty, string.Empty, string.Empty,
                string.Empty, holdings.Problem);
        }

        var rows = holdings.Held.Select(amount => Row(amount, Usable(rates, amount.Asset, rate))).ToList();
        if (holdings.Pending is { } pending)
        {
            rows = WithPending(rows, pending, Usable(rates, pending.Asset, rate));
        }

        var shown = holdings.Held.Select(amount => Usable(rates, amount.Asset, rate)).OfType<Price>().FirstOrDefault();
        var (rateText, rateTip) = RateLine(rate, shown, now, zone);
        var (height, heightTip) = HeightLine(holdings, now, zone);
        var since = emptySince is { } left ? BlockMarks.ShortTime(left, now, zone) : null;
        return new HoldingsCard(
            nodeKey, CardState.Shown, rows, since, RateOf(rate, shown), rateText, rateTip, height, heightTip,
            holdings.Problem);
    }

    /// <summary>
    /// Puts what waits in the mempool on its asset's row, as a small clock (finding M2); when nothing of the asset is
    /// confirmed yet, the row stands with a zero amount, so that the clock has a place.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="pending">What waits, in and out together.</param>
    /// <param name="price">The asset's rate, or <see langword="null"/>.</param>
    /// <returns>The rows.</returns>
    private static List<HoldingsRow> WithPending(List<HoldingsRow> rows, AssetAmount pending, Price? price)
    {
        var sign = pending.Units.Sign > 0 ? "+" : "−";
        var magnitude = new AssetAmount(pending.Asset, System.Numerics.BigInteger.Abs(pending.Units));
        var text = $"{PendingPrefix}{sign}{AmountText.WithUnit(magnitude)}. Räknas in när den är bekräftad.";
        var index = rows.FindIndex(row => row.Name == NameOf(pending.Asset));
        if (index < 0)
        {
            rows.Add(Row(AssetAmount.Zero(pending.Asset), price) with { Pending = text });
            return rows;
        }

        rows[index] = rows[index] with { Pending = text };
        return rows;
    }

    /// <summary>Gives the name a row has for an asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The chain's coin's name, or the token's symbol.</returns>
    private static string NameOf(Asset asset) => asset.Kind == AssetKind.Native ? NativeName(asset) : asset.Symbol;

    /// <summary>Gives the rate of an asset that the card may use, or none.</summary>
    /// <param name="rates">The rates.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="rate">Where they come from; none while fetching or when there are none.</param>
    /// <returns>The price, or <see langword="null"/>.</returns>
    private static Price? Usable(IReadOnlyDictionary<Asset, Price> rates, Asset asset, CardRateKind rate) =>
        rate is CardRateKind.Today or CardRateKind.FromCase or CardRateKind.NoNewRate
            ? rates.GetValueOrDefault(asset)
            : null;

    /// <summary>Gives where the rate comes from: none when no row could be valued.</summary>
    /// <param name="rate">Where the rates come from.</param>
    /// <param name="shown">The rate the foot names.</param>
    /// <returns>The kind.</returns>
    private static CardRateKind RateOf(CardRateKind rate, Price? shown) =>
        rate is CardRateKind.Today or CardRateKind.FromCase or CardRateKind.NoNewRate && shown is null
            ? CardRateKind.None
            : rate;

    /// <summary>Makes one asset's row.</summary>
    /// <param name="amount">What is left.</param>
    /// <param name="price">Its rate, or <see langword="null"/>.</param>
    /// <returns>The row.</returns>
    private static HoldingsRow Row(AssetAmount amount, Price? price)
    {
        var asset = amount.Asset;
        var number = Written(amount);
        var cut = number.Contains(',', StringComparison.Ordinal) ? Math.Min(TailDigits, number.Length) : 0;
        var glyph = Glyph(asset);
        string? value = null;
        string? valueTip = null;
        if (price is not null && price.Asset == asset)
        {
            var valuation = price.ValueOf(amount);
            value = Money(valuation.Value, price.Currency);
            valueTip = string.Join(" ", valuation.Steps);
        }

        return new HoldingsRow(
            glyph, NameOf(asset), Ink(asset),
            number[..^cut], number[^cut..], AmountText.Number(amount) + " " + glyph, value, valueTip,
            KnownTokens.IssuerOf(asset) is not null && !amount.IsZero);
    }

    /// <summary>
    /// Writes what is left with all its decimals up to <see cref="MostDecimals"/> (sketch IN4a: bitcoin's eight), so that
    /// the last ones can be dimmed; ether's eighteen are cut to eight, and the tooltip has the amount in full.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <returns>For example <c>5,85000000</c>.</returns>
    private static string Written(AssetAmount amount)
    {
        var decimals = Math.Min(amount.Asset.Decimals, MostDecimals);
        if (amount.Units.GetBitLength() > MostBits)
        {
            return AmountText.Number(amount);
        }

        var value = (decimal)amount.Units / (decimal)System.Numerics.BigInteger.Pow(10, amount.Asset.Decimals);
        return Math.Round(value, decimals, MidpointRounding.ToZero)
            .ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Swedish).Replace('\u00A0', ' ');
    }

    /// <summary>The most decimals a row writes.</summary>
    private const int MostDecimals = 8;

    /// <summary>The largest amount, in bits of its smallest unit, that a decimal holds exactly.</summary>
    private const long MostBits = 95;

    /// <summary>Writes the rate's line at the foot.</summary>
    /// <param name="rate">Where the rate comes from.</param>
    /// <param name="shown">The rate the line names, or <see langword="null"/>.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The line and its tooltip.</returns>
    private static (string Text, string Tip) RateLine(
        CardRateKind rate, Price? shown, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (rate == CardRateKind.Fetching)
        {
            return (FetchingRateText, "Kursen hämtas från källan genom utgången.");
        }

        if (shown is null)
        {
            return rate == CardRateKind.FromCase
                ? (NoRateText, "Offline: ingen ny kurs kan hämtas, och ärendet har ingen kurs sparad.")
                : (NoRateOnlineText, "Ingen kurs kunde hämtas genom utgången, och ärendet har ingen kurs sparad.");
        }

        var source = shown.CryptoRate.SourceName ?? shown.CryptoRate.Origin.Source;
        var period = shown.CryptoRate.PeriodEnd;
        var full = Full(period, zone);
        if (rate == CardRateKind.Today)
        {
            var today = Today(period, now, zone);
            return ($"{source} · {today}", $"Dagens kurs, hämtad genom utgången: {source}, perioden slutar {full}.");
        }

        var when = BlockMarks.ShortTime(period, now, zone);
        var latest = $"Den senaste kursen i ärendet: {source}, perioden slutar {full}.";
        return rate == CardRateKind.NoNewRate
            ? ($"Ingen ny kurs · kurs {when}", "Ingen ny kurs kunde hämtas genom utgången. " + latest)
            : ($"Offline · kurs {when}", "Offline: ingen ny kurs kan hämtas. " + latest);
    }

    /// <summary>Writes the block the balance was read at, and how old it is.</summary>
    /// <param name="holdings">The balance.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The height and its tooltip.</returns>
    private static (string Height, string Tip) HeightLine(NodeHoldings holdings, DateTimeOffset now, TimeZoneInfo zone)
    {
        var fetched = holdings.FetchedAt is { } at ? BlockMarks.ShortTime(at, now, zone) : string.Empty;
        var outdated = holdings.MayBeOutdated ? OutdatedText : string.Empty;
        if (holdings.Tip is not { } tip)
        {
            return (string.Empty, $"Saldot hämtat {fetched}.{outdated}");
        }

        var height = BlockMarks.Height(tip.Height);
        var status = holdings.MayBeOutdated ? "läst ur ärendet" : "hämtat nu";
        return (height,
            $"Saldot vid höjd {height}, block {BlockMarks.ShortHash(tip.Hash)}, det senaste kända blocket ({status}, "
            + $"saldot hämtat {fetched}).{outdated}");
    }

    /// <summary>Writes an amount of money as the card does, for example <c>5 922 540 kr</c>.</summary>
    /// <param name="value">The exact value, rounded here once to whole units, never first to öre.</param>
    /// <param name="currency">The currency.</param>
    /// <returns>The text.</returns>
    private static string Money(decimal value, FiatCurrency currency)
    {
        var whole = Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("#,##0", Swedish)
            .Replace(' ', ' ');
        var code = currency.Code();
        return code == "SEK" ? whole + " kr" : whole + " " + code;
    }

    /// <summary>Writes a rate's time as today's, for example <c>i dag 14:00</c>.</summary>
    /// <param name="time">The time.</param>
    /// <param name="now">The time now.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The text.</returns>
    private static string Today(DateTimeOffset time, DateTimeOffset now, TimeZoneInfo zone)
    {
        var text = BlockMarks.ShortTime(time, now, zone);
        return text.Contains(' ', StringComparison.Ordinal) ? text : "i dag " + text;
    }

    /// <summary>Writes a time in full, local and UTC.</summary>
    /// <param name="time">The time.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>For example <c>2026-11-08 14:00 (13:00 UTC)</c>.</returns>
    private static string Full(DateTimeOffset time, TimeZoneInfo zone) => string.Create(
        CultureInfo.InvariantCulture,
        $"{TimeZoneInfo.ConvertTime(time, zone):yyyy-MM-dd HH:mm} ({time.UtcDateTime:HH:mm} UTC)");

    /// <summary>Gives an asset's mark: <c>₿</c> for bitcoin, the token's mark, or its symbol.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The mark.</returns>
    public static string Glyph(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset == Asset.Bitcoin)
        {
            return "₿";
        }

        return TokenMarks.Glyph(asset) is { Length: > 0 } mark ? mark : asset.Symbol;
    }

    /// <summary>Gives a chain's own coin's name.</summary>
    /// <param name="asset">The coin.</param>
    /// <returns>For example <c>Bitcoin</c>.</returns>
    private static string NativeName(Asset asset) => asset.Chain switch
    {
        Chain.Bitcoin => "Bitcoin",
        Chain.Ethereum => "Ether",
        _ => asset.Symbol,
    };

    /// <summary>Gives an asset's ring's brush key.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The key.</returns>
    private static string Ink(Asset asset)
    {
        if (asset == Asset.Bitcoin)
        {
            return "Brush.Asset.Bitcoin";
        }

        return asset == Asset.Ether ? "Brush.Asset.Ether" : "Brush.Asset.Token";
    }
}
