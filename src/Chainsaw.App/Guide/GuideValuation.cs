using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Guide;

/// <summary>
/// <em>Varför?</em> for a line's value in the Guide's panel (sketch KU1a): for each transfer the amount on the chain
/// (a fact, with its tx, block and block time), the crypto rate and the exchange rate (each with its source and the
/// SHA-256 of the sealed answer) and the calculation, so that a user can redo it with any calculator (principle 3).
/// </summary>
/// <param name="Total">The line's value as the line writes it, for example <c>412 650 kr</c>.</param>
/// <param name="Exact">The exact value with two decimals, for example <c>412 653,21 kr</c>.</param>
/// <param name="Parts">Each transfer valued at its own time, in time order.</param>
public sealed record GuideValuation(string Total, string Exact, IReadOnlyList<GuideValuationPart> Parts)
{
    /// <summary>The Swedish culture the numbers are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Gets whether the line sums more than one transfer, so that the total stands apart at the end.</summary>
    public bool HasSeveralParts => Parts.Count > 1;

    /// <summary>Gives the steps behind a line's value.</summary>
    /// <param name="value">The line's value, with its parts.</param>
    /// <param name="local">The time zone local times are written in.</param>
    /// <returns>The steps, or <see langword="null"/> when the value has no parts to show.</returns>
    public static GuideValuation? Of(EdgeValue value, TimeZoneInfo local)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(local);
        if (value.Parts.Count == 0)
        {
            return null;
        }

        var unit = MoneyText.Unit(value.Currency);
        var exact = value.Parts.Sum(part => part.Valuation.Value);
        return new GuideValuation(
            Money(value.Rounded, "#,##0") + unit,
            Money(exact, "#,##0.00") + unit,
            [.. value.Parts.Select(part => GuideValuationPart.Of(part, local))]);
    }

    /// <summary>Writes a number in Swedish with a format.</summary>
    /// <param name="number">The number.</param>
    /// <param name="format">The format.</param>
    /// <returns>For example <c>412 650</c>.</returns>
    internal static string Money(decimal number, string format) => number.ToString(format, Swedish);
}

/// <summary>One transfer on a line, valued: the amount, the rates and the calculation.</summary>
/// <param name="Amount">The amount on the chain: a fact.</param>
/// <param name="CryptoRate">The crypto rate from the exchange.</param>
/// <param name="FiatRate">The central bank's exchange rate, or <see langword="null"/> when none was needed.</param>
/// <param name="Formula">The calculation, for example <c>0,412 × 89 412,3 × 11,2041</c>.</param>
/// <param name="Value">The transfer's value, rounded to whole units, for example <c>= 412 650 kr</c>.</param>
/// <param name="Exact">The exact value, for example <c>412 653,21 kr, avrundat</c>.</param>
public sealed record GuideValuationPart(
    GuideValuationStep Amount,
    GuideValuationStep CryptoRate,
    GuideValuationStep? FiatRate,
    string Formula,
    string Value,
    string Exact)
{
    /// <summary>A rate as the source gave it, with every decimal.</summary>
    private const string ExactFormat = "#,##0.############################";

    /// <summary>Gets whether an exchange rate was needed.</summary>
    public bool HasFiatRate => FiatRate is not null;

    /// <summary>Gives a transfer's steps.</summary>
    /// <param name="part">The transfer and its valuation.</param>
    /// <param name="local">The time zone local times are written in.</param>
    /// <returns>The steps.</returns>
    public static GuideValuationPart Of(EdgeValuePart part, TimeZoneInfo local)
    {
        ArgumentNullException.ThrowIfNull(part);
        var valuation = part.Valuation;
        var price = valuation.Price;
        var unit = MoneyText.Unit(price.Currency.Code());
        var factors = new List<string>
        {
            GuideValuation.Money(valuation.Amount, ExactFormat),
            GuideValuation.Money(price.CryptoRate.Close, ExactFormat),
        };
        if (price.FiatRate is { } fiat)
        {
            factors.Add(GuideValuation.Money(fiat.PerUnit, ExactFormat));
        }

        var rounded = Math.Round(valuation.Value, 0, MidpointRounding.AwayFromZero);
        return new GuideValuationPart(
            GuideValuationStep.OfAmount(part, local),
            GuideValuationStep.OfCryptoRate(price.CryptoRate, local),
            price.FiatRate is { } rate ? GuideValuationStep.OfFiatRate(rate, local) : null,
            string.Join(" × ", factors),
            "= " + GuideValuation.Money(rounded, "#,##0") + unit,
            GuideValuation.Money(valuation.Value, "#,##0.00") + unit + ", avrundat");
    }
}

/// <summary>One step behind a value: what it is, where it comes from, its value and the details it rests on.</summary>
/// <param name="Title">What it is, for example <c>Kursen BTC → EUR</c>.</param>
/// <param name="Source">Where it comes from, for example <c>Bitstamp</c>, or <c>fakta</c> for the chain.</param>
/// <param name="Value">Its value, for example <c>89 412,3 EUR</c>.</param>
/// <param name="IsFact">Whether it is a fact from the chain (principle 2), drawn with the check.</param>
/// <param name="Rows">The details, each a short name and a value.</param>
public sealed record GuideValuationStep(
    string Title, string Source, string Value, bool IsFact, IReadOnlyList<GuideValuationRow> Rows)
{
    /// <summary>
    /// Gets the mark's icon's resource key: the check for the fact from the chain, the price curve for an exchange's
    /// rate and the bank for a central bank's.
    /// </summary>
    public string Icon { get; init; } = "Icon.Check";

    /// <summary>A rate as the source gave it, with every decimal.</summary>
    private const string ExactFormat = "#,##0.############################";

    /// <summary>How a time is written.</summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>How a day is written.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>What each way out is called after <em>genom</em>.</summary>
    private static readonly Dictionary<string, string> Routes = new(StringComparer.Ordinal)
    {
        ["vpn"] = "VPN",
        ["windows"] = "Windows",
        ["local"] = "det lokala nätet",
    };

    /// <summary>Gives the amount on the chain: the fact the value rests on.</summary>
    /// <param name="part">The transfer and its valuation.</param>
    /// <param name="local">The time zone local times are written in.</param>
    /// <returns>The step.</returns>
    public static GuideValuationStep OfAmount(EdgeValuePart part, TimeZoneInfo local)
    {
        ArgumentNullException.ThrowIfNull(part);
        var transfer = part.Transfer;
        var where = transfer.Coin is { } coin
            ? " · utgång " + coin.Index.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        var rows = new List<GuideValuationRow>
        {
            new("Tx", GraphScene.Shorten(transfer.Transaction.Id) + where, transfer.Transaction.Id),
        };
        if (transfer.Height is { } height)
        {
            var hash = transfer.Block?.Hash.Value ?? transfer.AccountBlock?.Hash;
            var hashText = hash is null ? string.Empty : " · " + GraphScene.Shorten(hash);
            rows.Add(new("Block", height.ToString("#,##0", CultureInfo.GetCultureInfo("sv-SE")) + hashText, hash));
        }

        if (transfer.Time is { } time)
        {
            rows.Add(new("Blocktid", Utc(time), null));
            rows.Add(new(string.Empty, Local(time, local), null));
        }

        return new GuideValuationStep(
            "Beloppet", "fakta", AmountText.WithUnit(transfer.Amount), true, rows);
    }

    /// <summary>Gives the crypto rate: the closing price for the period that holds the block's time.</summary>
    /// <param name="rate">The rate.</param>
    /// <param name="local">The time zone local times are written in.</param>
    /// <returns>The step.</returns>
    public static GuideValuationStep OfCryptoRate(CryptoRate rate, TimeZoneInfo local)
    {
        ArgumentNullException.ThrowIfNull(rate);
        var symbol = rate.Asset.Kind == AssetKind.Native ? rate.Chain.Code() : rate.Asset.Symbol;
        var rows = new List<GuideValuationRow>
        {
            new(PeriodName(rate.Resolution), Period(rate), null),
            new("Värde", ClosingName(rate.Resolution) + (rate.Volume == 0 ? ", ingen handel" : string.Empty), null),
        };
        rows.AddRange(OriginRows(rate.Origin, local));
        return new GuideValuationStep(
            $"Kursen {symbol} → {rate.Quote.Code()}",
            rate.SourceName ?? rate.Origin.Source,
            GuideValuation.Money(rate.Close, ExactFormat) + " " + rate.Quote.Code(),
            false,
            rows)
        {
            Icon = "Icon.Pulse",
        };
    }

    /// <summary>Gives the exchange rate: the central bank's rate for the day.</summary>
    /// <param name="rate">The rate.</param>
    /// <param name="local">The time zone local times are written in.</param>
    /// <returns>The step.</returns>
    public static GuideValuationStep OfFiatRate(FiatRate rate, TimeZoneInfo local)
    {
        ArgumentNullException.ThrowIfNull(rate);
        var day = rate.Date.ToString(DayFormat, CultureInfo.InvariantCulture);
        if (rate.Date != rate.AskedDate)
        {
            day += $", {rate.AskedDate.ToString(DayFormat, CultureInfo.InvariantCulture)} saknar kurs";
        }

        var rows = new List<GuideValuationRow> { new("Dag", day, null), new("Serie", rate.Series, null) };
        rows.AddRange(OriginRows(rate.Origin, local));
        return new GuideValuationStep(
            $"Växelkursen {rate.Currency.Code()} → {rate.Base.Code()}",
            rate.SourceName ?? rate.Origin.Source,
            GuideValuation.Money(rate.PerUnit, ExactFormat) + " " + rate.Base.Code(),
            false,
            rows)
        {
            Icon = "Icon.Kind.Bank",
        };
    }

    /// <summary>Gives when and how a sealed answer was fetched, and its SHA-256.</summary>
    /// <param name="origin">The sealed answer.</param>
    /// <param name="local">The time zone local times are written in.</param>
    /// <returns>The rows.</returns>
    private static IEnumerable<GuideValuationRow> OriginRows(RateOrigin origin, TimeZoneInfo local)
    {
        var way = origin.Route is { } route && Routes.TryGetValue(route, out var name)
            ? " · genom " + name
            : string.Empty;
        yield return new GuideValuationRow("Hämtad", Local(origin.ReceivedUtc, local) + way, null);
        yield return new GuideValuationRow("Svaret", ShortHash(origin.Sha256), origin.Sha256);
    }

    /// <summary>Writes a SHA-256 short, in groups as the sketch shows it: <c>9C1E 07B2 … 44B0</c>.</summary>
    /// <param name="sha256">The hash, as hex.</param>
    /// <returns>The short form; the full hash is copied.</returns>
    public static string ShortHash(string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        var upper = sha256.ToUpperInvariant();
        return upper.Length < 12 ? upper : $"{upper[..4]} {upper[4..8]} … {upper[^4..]}";
    }

    /// <summary>Gives what a period of a resolution is called.</summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>For example <c>Timme</c>.</returns>
    private static string PeriodName(PriceResolution resolution) => resolution switch
    {
        PriceResolution.Minute => "Minut",
        PriceResolution.Day => "Dygn",
        _ => "Timme",
    };

    /// <summary>Gives what the closing price of a period of a resolution is called.</summary>
    /// <param name="resolution">The resolution.</param>
    /// <returns>For example <c>Timmens stängning</c>.</returns>
    private static string ClosingName(PriceResolution resolution) => resolution switch
    {
        PriceResolution.Minute => "Minutens stängning",
        PriceResolution.Day => "Dygnets stängning",
        _ => "Timmens stängning",
    };

    /// <summary>Writes a crypto rate's period in UTC.</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>For example <c>2026-10-14 · 13:00–14:00 UTC</c>.</returns>
    private static string Period(CryptoRate rate)
    {
        var start = rate.PeriodStart.UtcDateTime;
        var end = rate.PeriodEnd.UtcDateTime;
        var day = start.ToString(DayFormat, CultureInfo.InvariantCulture);
        var endFormat = start.Date == end.Date || end == start.Date.AddDays(1) ? "HH:mm" : TimeFormat;
        var endText = end == start.Date.AddDays(1) ? "24:00" : end.ToString(endFormat, CultureInfo.InvariantCulture);
        return $"{day} · {start.ToString("HH:mm", CultureInfo.InvariantCulture)}–{endText} UTC";
    }

    /// <summary>Writes a time in UTC.</summary>
    /// <param name="time">The time.</param>
    /// <returns>For example <c>2026-10-14 13:42:07 UTC</c>.</returns>
    private static string Utc(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>Writes a time in local time.</summary>
    /// <param name="time">The time.</param>
    /// <param name="local">The time zone.</param>
    /// <returns>For example <c>2026-10-14 15:42 lokal tid</c>.</returns>
    private static string Local(DateTimeOffset time, TimeZoneInfo local) =>
        TimeZoneInfo.ConvertTime(time, local).ToString(TimeFormat, CultureInfo.InvariantCulture) + " lokal tid";
}

/// <summary>A detail of a step: a short name and a value, with the full id to copy when it is shortened.</summary>
/// <param name="Name">The name, for example <c>Svaret</c>; empty for a second line under the one before.</param>
/// <param name="Value">The value as shown.</param>
/// <param name="Copy">The full id to copy (a txid, a block hash, a SHA-256), or <see langword="null"/>.</param>
public sealed record GuideValuationRow(string Name, string Value, string? Copy)
{
    /// <summary>Gets whether the row can be copied in full.</summary>
    public bool CanCopy => Copy is not null;
}
