using System.Globalization;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>One step of the arithmetic behind a swap's choice, as the pill's box writes it (sketch SW1a).</summary>
/// <param name="Key">The step's name, for example <c>Andel</c>.</param>
/// <param name="Value">Its value, for example <c>62,2 %</c>.</param>
/// <param name="Sub">The quiet line under, for example <c>FIFO v1</c>.</param>
public sealed record SwapStep(string Key, string Value, string Sub);

/// <summary>
/// What a swap's pill says on hovering (sketch SW1a): whether the traced value follows and whose choice it is, the
/// steps (in, share, out, what follows) and the transaction with the analysis; the same arithmetic everywhere it is
/// explained, from <see cref="SwapValue"/>.
/// </summary>
/// <param name="Title">The box's title.</param>
/// <param name="Sub">Who chose it: the default, a judgement, or the user's choice with when.</param>
/// <param name="Steps">The steps, in order.</param>
/// <param name="Foot">The transaction and the analysis.</param>
public sealed record SwapPillBox(string Title, string Sub, IReadOnlyList<SwapStep> Steps, string Foot);

/// <summary>Writes a swap's pill and its box (sketch SW1a). Knows nothing of WPF.</summary>
public static class SwapPill
{
    /// <summary>The choice that the value follows, in the pill.</summary>
    public const string FollowsText = "Följer";

    /// <summary>The choice that the value does not follow, in the pill.</summary>
    public const string StopsText = "Följer inte";

    /// <summary>The tip on <see cref="FollowsText"/>.</summary>
    public const string FollowsTip = "Värdet följer med: samma andel av det som kom ut";

    /// <summary>The tip on <see cref="StopsText"/>.</summary>
    public const string StopsTip = "Spåret slutar i swappen";

    /// <summary>The Guide's heading that explains the choice.</summary>
    public const string GuideHeading = "Följer eller följer inte";

    /// <summary>Writes the box for a line's swap; the first, when the line carries several.</summary>
    /// <param name="edge">The line, which carries at least one swap.</param>
    /// <returns>The box, or <see langword="null"/> when the line carries no swap.</returns>
    public static SwapPillBox? Box(EdgeView edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (edge.Swaps.Count == 0)
        {
            return null;
        }

        var swap = edge.Swaps[0];
        var stop = edge.SwapStops.FirstOrDefault(choice => choice.Is(swap));
        var conversion = edge.SwapConversions.FirstOrDefault(found => found.Swap == swap);
        var marks = SwapMarks.Of(swap);
        var (title, sub) = Heading(edge, stop);
        var steps = new List<SwapStep>
        {
            new("In", $"{marks.Sold} {AmountText.Number(swap.Sold)}", conversion is null
                ? "inget spårat nådde swappen"
                : $"varav spårat {marks.Sold} {AmountText.Number(conversion.TracedSold)}"),
            new("Andel", conversion?.ShareText ?? "–", conversion?.Method.ToString() ?? string.Empty),
            new("Ut", $"{marks.Bought} {AmountText.Number(swap.Bought)}", swap.Venue?.Name ?? "okänd DEX"),
            Outcome(stop is null && !edge.IsSwapPriceAbsurd, conversion, marks),
        };
        var foot = $"tx {GraphScene.Shorten(swap.Transaction.Value)} · {SwapValue.Analysis}";
        return new SwapPillBox(title, sub, steps, foot);
    }

    /// <summary>
    /// Writes the box's title and the quiet line under it: an absurd price first (the value never follows), then the
    /// user's choice, then the default, which says when the price could not be tested.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <param name="stop">The user's choice on the swap, or <see langword="null"/>.</param>
    /// <returns>The title and the line under.</returns>
    private static (string Title, string Sub) Heading(EdgeView edge, SwapStop? stop)
    {
        if (edge.SwapPrice is { IsUnreasonable: true } price)
        {
            var swedish = CultureInfo.GetCultureInfo("sv-SE");
            return ("Orimligt pris: värdet följer inte med", string.Create(
                swedish, $"{SwapPriceCheck.Check} · in {price.SoldValue:N0}, ut {price.BoughtValue:N0} {price.Currency}"));
        }

        if (stop is not null)
        {
            return ("Värdet följer inte med", "Ditt val · " + NodeHistory.Short(stop.RecordedUtc));
        }

        return edge.IsSwapPriceUntested
            ? ("Värdet följer med genom swappen", "Förval · en bedömning · priset inte prövat")
            : ("Värdet följer med genom swappen", "Förval · en bedömning · priset prövat");
    }

    /// <summary>Writes the last step: what follows the traced value, or that nothing does.</summary>
    /// <param name="follows">Whether the value follows.</param>
    /// <param name="conversion">What the rule did, or <see langword="null"/> when no traced money reached the swap.</param>
    /// <param name="marks">The pill's marks.</param>
    /// <returns>The step.</returns>
    private static SwapStep Outcome(bool follows, SwapConversion? conversion, SwapMarks marks)
    {
        if (!follows)
        {
            return new SwapStep("Spårat", "inget", "spåret slutar i swappen");
        }

        return conversion is null
            ? new SwapStep(FollowsText, "inget", "inget spårat att följa")
            : new SwapStep(
                FollowsText, $"{marks.Bought} {AmountText.Number(conversion.TracedBought)}", "samma andel i ny token");
    }
}
