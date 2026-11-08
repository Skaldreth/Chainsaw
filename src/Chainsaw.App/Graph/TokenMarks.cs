using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// An amount as a line carries it after TK1a: the token's mark first (<c>₮</c>, <c>Ξ</c>), then the number. Bitcoin
/// has no mark, so its lines read as before.
/// </summary>
/// <param name="Glyph">The mark, or an empty text for an asset without one.</param>
/// <param name="Number">The amount, for example <c>52 000,0000</c>.</param>
/// <param name="Tip">The amount with its symbol, for hovering.</param>
public sealed record TokenLabel(string Glyph, string Number, string Tip)
{
    /// <summary>Gets whether a mark stands before the number.</summary>
    public bool HasGlyph => Glyph.Length > 0;
}

/// <summary>
/// One row of the inspector's balance per token (TK1a): what came in, went out and is left of one asset at an address,
/// with who issued it, and the snowflake <em>Kan frysas</em> when its issuer can freeze what is left.
/// </summary>
/// <param name="Glyph">The token's mark, or its symbol when it has none.</param>
/// <param name="Name">The symbol, for example <c>USDT</c>.</param>
/// <param name="In">What came in.</param>
/// <param name="Out">What went out.</param>
/// <param name="Left">
/// What is left: in less out, as the chain's transfers in the case give it; a dash when more went out than the case
/// shows came in (the history read is incomplete).
/// </param>
/// <param name="Note">Who issued it, or that it is the chain's own coin.</param>
/// <param name="CanBeFrozen">Whether the issuer can freeze what is left (something is left and the issuer can).</param>
/// <param name="FreezeText">What the snowflake's box says, or <see langword="null"/>.</param>
public sealed record TokenBalanceRow(
    string Glyph, string Name, string In, string Out, string Left, string Note, bool CanBeFrozen, string? FreezeText)
{
    /// <summary>
    /// Gets what is left worth in the currency Pricing is set to, small under the amount (sketch TK1a), for example
    /// <c>≈ 210 kr</c>; <see langword="null"/> when the case has no rate for the asset or nothing left is known.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Gets how <see cref="Value"/> was counted, rate by rate, for hovering; <see langword="null"/> without.
    /// </summary>
    public string? ValueTip { get; init; }

    /// <summary>Gets whether a value stands under the amount.</summary>
    public bool HasValue => Value is not null;

    /// <summary>Gets whether nothing is left, which the row writes dimmed.</summary>
    public bool IsEmpty { get; init; }

    /// <summary>Gets what is left with the symbol, as the row writes it to the right, e.g. <c>20 USDT</c>.</summary>
    public string LeftWithUnit => Left == TokenMarks.UnknownLeft ? Left : Left + " " + Name;

    /// <summary>Gets the row's tooltip: what came in, went out and is left.</summary>
    public string ToolTip => $"In {In} · Ut {Out} · Kvar {LeftWithUnit}";
}

/// <summary>Writes the tokens' marks and the inspector's rows after TK1a, without WPF.</summary>
public static class TokenMarks
{
    /// <summary>What a coin's row says instead of an issuer.</summary>
    public const string NativeNote = "Kedjans eget mynt";

    /// <summary>What a row says when more went out than the case shows came in.</summary>
    public const string IncompleteNote = "Historiken i ärendet är ofullständig";

    /// <summary>Gives an asset's mark: <c>Ξ</c>, <c>₮</c> or <c>$</c>, and none for bitcoin or an unknown
    /// token.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The mark, or an empty text.</returns>
    public static string Glyph(Asset asset) => KnownTokens.GlyphOf(asset) ?? string.Empty;

    /// <summary>Writes what a line carries, with the token's mark first.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The label.</returns>
    public static TokenLabel Label(AssetAmount amount) =>
        new(Glyph(amount.Asset), AmountText.Number(amount), AmountText.WithUnit(amount));

    /// <summary>What a row writes for what is left when it is not known.</summary>
    public const string UnknownLeft = "–";

    /// <summary>Makes the inspector's row for one asset at an address.</summary>
    /// <param name="incoming">What came in.</param>
    /// <param name="outgoing">What went out, in the same asset.</param>
    /// <param name="rate">
    /// The latest rate the case has for the asset, which values what is left; <see langword="null"/> for none, and
    /// then no value is shown rather than a guess.
    /// </param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentException">The amounts are of different assets.</exception>
    public static TokenBalanceRow Balance(AssetAmount incoming, AssetAmount outgoing, Price? rate = null)
    {
        var asset = incoming.Asset;
        if (outgoing.Asset != asset)
        {
            throw new ArgumentException("A row is one asset.", nameof(outgoing));
        }

        var left = incoming - outgoing;
        if (left.Units.Sign < 0)
        {
            // More went out than the case shows came in: the history read is incomplete, and no balance is shown.
            return new TokenBalanceRow(
                Glyph(asset) is { Length: > 0 } mark ? mark : asset.Symbol, asset.Symbol, AmountText.Number(incoming),
                AmountText.Number(outgoing), UnknownLeft, IncompleteNote, false, null);
        }

        var row = Known(incoming, outgoing, left) with { IsEmpty = left.IsZero };
        return rate is null || rate.Asset != asset ? row : Valued(row, rate, left);
    }

    /// <summary>
    /// Adds to a row what is left worth at a rate, written as a line's value is, and how it was counted (principle 3).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="rate">The rate, of the row's asset.</param>
    /// <param name="left">What is left.</param>
    /// <returns>The row with its value.</returns>
    private static TokenBalanceRow Valued(TokenBalanceRow row, Price rate, AssetAmount left)
    {
        var valuation = rate.ValueOf(left);
        var value = new EdgeValue(
            Math.Round(valuation.Value, 0, MidpointRounding.AwayFromZero), rate.Currency.Code(), valuation.Steps);
        var tip = string.Create(
            CultureInfo.InvariantCulture,
            $"Det som ligger kvar, till den senaste kursen i ärendet ({rate.At.UtcDateTime:yyyy-MM-dd HH:mm} UTC). ");
        return row with { Value = value.Text, ValueTip = tip + string.Join(" ", value.Steps) };
    }

    /// <summary>Makes the row for an asset whose balance is known from the case's history.</summary>
    /// <param name="incoming">What came in.</param>
    /// <param name="outgoing">What went out.</param>
    /// <param name="left">What is left, not below zero.</param>
    /// <returns>The row.</returns>
    private static TokenBalanceRow Known(AssetAmount incoming, AssetAmount outgoing, AssetAmount left)
    {
        var asset = incoming.Asset;
        var issuer = KnownTokens.IssuerOf(asset);
        var canBeFrozen = issuer is not null && left.Units.Sign > 0;
        var note = issuer is null
            ? (asset.Kind == AssetKind.Native ? NativeNote : "Kontrakt " + Short(asset.Contract!))
            : "Utgivare: " + issuer.Name;
        var freeze = canBeFrozen
            ? string.Create(CultureInfo.InvariantCulture,
                $"{issuer!.Name} kan frysa {AmountText.WithUnit(left)} på adressen, enligt historiken i ärendet. ")
                + "Frysning begärs hos utgivaren, inte hos börsen."
            : null;
        var glyph = Glyph(asset);
        return new TokenBalanceRow(
            glyph.Length > 0 ? glyph : asset.Symbol, asset.Symbol, AmountText.Number(incoming),
            AmountText.Number(outgoing), AmountText.Number(left), note, canBeFrozen, freeze);
    }

    /// <summary>Shortens a contract for a row, for example <c>0x4444…4444</c>.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns>The short form.</returns>
    private static string Short(string contract) =>
        contract.Length <= 13 ? contract : $"{contract[..6]}…{contract[^4..]}";
}
