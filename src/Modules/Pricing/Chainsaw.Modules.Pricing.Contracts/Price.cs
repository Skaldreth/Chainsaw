using System.Globalization;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Pricing.Contracts;

/// <summary>
/// The price of one unit of an asset (a chain's own coin or a token on it) at a time in a currency: a calculation
/// from one or two rates, each a fact from a named source (principle 2). <see cref="Steps"/> shows how it was worked
/// out, in Swedish, so that a user can ask <i>why?</i> and redo it with any calculator (principle 3).
/// </summary>
public sealed record Price
{
    /// <summary>The Swedish culture the steps are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>A rate as the source gave it, with every decimal and no trailing zeros added.</summary>
    private const string ExactFormat = "#,##0.############################";

    /// <summary>An amount of money for the user, with two decimals.</summary>
    private const string MoneyFormat = "#,##0.00";

    /// <summary>Creates a price from the rates it is worked out from.</summary>
    /// <param name="at">The time asked about.</param>
    /// <param name="currency">The currency counted in.</param>
    /// <param name="cryptoRate">The crypto rate.</param>
    /// <param name="fiatRate">
    /// The rate that converts the crypto rate's currency into <paramref name="currency"/>, or <see langword="null"/>
    /// when the crypto rate is already in it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The rates do not fit together or with the currency, or the crypto rate's asset is not on its chain.
    /// </exception>
    public Price(DateTimeOffset at, FiatCurrency currency, CryptoRate cryptoRate, FiatRate? fiatRate)
    {
        ArgumentNullException.ThrowIfNull(cryptoRate);
        if (cryptoRate.Asset.Chain != cryptoRate.Chain)
        {
            throw new ArgumentException("The rate's asset is not on its chain.", nameof(cryptoRate));
        }

        if (fiatRate is null ? cryptoRate.Quote != currency
            : fiatRate.Currency != cryptoRate.Quote || fiatRate.Base != currency)
        {
            throw new ArgumentException("The rates do not lead to the currency.", nameof(fiatRate));
        }

        At = at;
        Currency = currency;
        CryptoRate = cryptoRate;
        FiatRate = fiatRate;
        UnitPrice = fiatRate is null ? cryptoRate.Close : cryptoRate.Close * fiatRate.PerUnit;
    }

    /// <summary>Gets the chain the priced asset lives on.</summary>
    public Chain Chain => CryptoRate.Chain;

    /// <summary>Gets the asset priced: the chain's own coin, or a token on it such as USDT.</summary>
    public Asset Asset => CryptoRate.Asset;

    /// <summary>Gets the time asked about.</summary>
    public DateTimeOffset At { get; }

    /// <summary>Gets the currency counted in.</summary>
    public FiatCurrency Currency { get; }

    /// <summary>Gets the price of one unit, exact (the rates multiplied, not rounded).</summary>
    public decimal UnitPrice { get; }

    /// <summary>Gets the crypto rate.</summary>
    public CryptoRate CryptoRate { get; }

    /// <summary>Gets the rate that converts into the currency, when one was needed.</summary>
    public FiatRate? FiatRate { get; }

    /// <summary>Gets the sealed responses the price is built on.</summary>
    public IReadOnlyList<RateOrigin> Origins =>
        FiatRate is null ? [CryptoRate.Origin] : [CryptoRate.Origin, FiatRate.Origin];

    /// <summary>Gets how the price was worked out, one line per step, in Swedish.</summary>
    public IReadOnlyList<string> Steps
    {
        get
        {
            var asset = Symbol;
            var crypto = CryptoRate;
            var noTrade = crypto.Volume == 0 ? " Ingen handel under perioden." : string.Empty;
            var cryptoSource = crypto.SourceName ?? crypto.Origin.Source;
            var steps = new List<string>
            {
                $"1 {asset} = {Exact(crypto.Close)} {crypto.Quote.Code()}: {cryptoSource}, " +
                $"{crypto.Market}, sista priset för {Period(crypto)} (UTC).{noTrade}",
            };
            if (FiatRate is { } fiat)
            {
                var moved = fiat.Date == fiat.AskedDate
                    ? string.Empty
                    : $" ({Day(fiat.AskedDate)} saknar kurs, senaste dagen före används)";
                var fiatSource = fiat.SourceName ?? fiat.Origin.Source;
                steps.Add(
                    $"1 {fiat.Currency.Code()} = {Exact(fiat.PerUnit)} {fiat.Base.Code()}: " +
                    $"{fiatSource}, serien {fiat.Series}, kursen för {Day(fiat.Date)}{moved}.");
                steps.Add(
                    $"1 {asset} = {Exact(crypto.Close)} × {Exact(fiat.PerUnit)} = {Exact(UnitPrice)} " +
                    $"{Currency.Code()}.");
            }

            return steps;
        }
    }

    /// <summary>Values an amount of bitcoin at this price.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The value with its steps.</returns>
    /// <exception cref="InvalidOperationException">The price is not for Bitcoin.</exception>
    public Valuation ValueOf(Satoshi amount) => ValueOf(amount.ToAmount());

    /// <summary>
    /// Values an amount of the asset this price is for: the chain's own coin (bitcoin, ether) or a token (USDT, USDC
    /// with 6 decimals). The amount must be in the same asset (the same chain and contract), whatever its symbol says.
    /// </summary>
    /// <param name="amount">The amount, in the priced asset's smallest unit.</param>
    /// <returns>The value with its steps.</returns>
    /// <exception cref="InvalidOperationException">The amount is not in the asset the price is for.</exception>
    /// <exception cref="OverflowException">
    /// The amount is too large to value: more than about 7.9 × 10^28 of its smallest unit (about 79 billion ether).
    /// </exception>
    public Valuation ValueOf(AssetAmount amount)
    {
        if (amount.Asset != Asset)
        {
            throw new InvalidOperationException($"Only an amount of {Asset} is valued at this price.");
        }

        // The priced asset's decimals: for a known token Chainsaw's own, not what a source said.
        var coins = (decimal)amount.Units / Pow10(Asset.Decimals);
        var value = coins * UnitPrice;
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        var line = $"{Exact(coins)} {Symbol} × {Exact(UnitPrice)} = {Exact(value)} {Currency.Code()}, " +
            $"avrundat {rounded.ToString(MoneyFormat, Swedish)} {Currency.Code()}.";
        return new Valuation(this, coins, value, rounded, [.. Steps, line]);
    }

    /// <summary>
    /// Gets how the priced asset is written in the steps: the chain's code for its own coin (<c>BTC</c>), the token's
    /// symbol for a token (<c>USDT</c>).
    /// </summary>
    private string Symbol => Asset.Kind == AssetKind.Native ? Chain.Code() : Asset.Symbol;

    /// <summary>Gives ten to a power exactly, as a decimal.</summary>
    /// <param name="power">The power, at most 28.</param>
    /// <returns>10^power.</returns>
    private static decimal Pow10(int power)
    {
        var result = 1m;
        for (var i = 0; i < power; i++)
        {
            result *= 10m;
        }

        return result;
    }

    /// <summary>Writes a number exactly as it is, in Swedish.</summary>
    /// <param name="value">The number.</param>
    /// <returns>For example <c>35 619</c> or <c>10,4567</c>.</returns>
    private static string Exact(decimal value) => value.ToString(ExactFormat, Swedish);

    /// <summary>Writes a day.</summary>
    /// <param name="day">The day.</param>
    /// <returns>For example <c>2023-11-14</c>.</returns>
    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Writes a crypto rate's period.</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>For example <c>2023-11-14 22:00–23:00</c>.</returns>
    private static string Period(CryptoRate rate)
    {
        var start = rate.PeriodStart.UtcDateTime;
        var end = rate.PeriodEnd.UtcDateTime;
        var startText = start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var endFormat = start.Date == end.Date ? "HH:mm" : "yyyy-MM-dd HH:mm";
        return $"{startText}–{end.ToString(endFormat, CultureInfo.InvariantCulture)}";
    }
}

/// <summary>An amount valued at a price, with the steps that lead there (principle 3).</summary>
/// <param name="Price">The price.</param>
/// <param name="Amount">The amount in the priced asset, for example 0,512 BTC or 1 250,5 USDT.</param>
/// <param name="Value">The value, exact.</param>
/// <param name="Rounded">The value rounded to two decimals (half away from zero), as shown.</param>
/// <param name="Steps">How it was worked out, one line per step, in Swedish.</param>
public sealed record Valuation(
    Price Price, decimal Amount, decimal Value, decimal Rounded, IReadOnlyList<string> Steps);
