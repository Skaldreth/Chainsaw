using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// What an exposure's amounts were worth at the time (sketches EP1b and KU1a): the kronor small after the amount. The
/// money that reached a service moved on the graph's lines into it, and each line was worth what its transfers were
/// worth at their time (<see cref="EdgeValue"/>); the money's part of a line is valued in proportion to its part of
/// the line's amount. A line without a value gives no value at all, never a part of one (CLAUDE.md, <em>Visa aldrig
/// påhittade data</em>). The value rests on the method's amounts and is a judgement like them (principle 2).
/// </summary>
public static class ExposureValues
{
    /// <summary>The culture whose grouping the kronor are written with: <c>23 400</c>.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>
    /// Gives what several services' money was worth together, for a share's tooltip; empty when one has no value.
    /// </summary>
    /// <param name="snapshot">The graph's lines and their values.</param>
    /// <param name="result">The side.</param>
    /// <param name="services">The services' nodes.</param>
    /// <returns>The value, or an empty text.</returns>
    public static string OfServices(GraphSnapshot snapshot, ExposureResult result, IReadOnlyCollection<string> services)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(services);
        return Write(snapshot, result.Flows.Where(flow => services.Contains(flow.Far)));
    }

    /// <summary>Adds a value after an amount, as the panel writes it: <c>0,2500 BTC · ≈ 23 400 kr</c>.</summary>
    /// <param name="amount">The amount with its unit.</param>
    /// <param name="value">The value, or an empty text.</param>
    /// <returns>The amount, with the value when there is one.</returns>
    public static string After(string amount, string value) => value.Length == 0 ? amount : $"{amount} · {value}";

    /// <summary>Values the money on some lines and writes it; empty when none, or when a line has no value.</summary>
    /// <param name="snapshot">The graph's lines and their values.</param>
    /// <param name="flows">The money per line.</param>
    /// <returns>The value, or an empty text.</returns>
    private static string Write(GraphSnapshot snapshot, IEnumerable<ExposureFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var edges = snapshot.Graph.Edges.ToDictionary(edge => edge.Key, StringComparer.Ordinal);
        var total = 0m;
        string? currency = null;
        var any = false;
        foreach (var flow in flows)
        {
            if (!edges.TryGetValue(flow.EdgeKey, out var edge) || edge.Amount.Units.Sign <= 0
                || snapshot.Values.GetValueOrDefault(flow.EdgeKey) is not { } value
                || (currency is not null && currency != value.Currency))
            {
                return string.Empty;
            }

            currency = value.Currency;
            total += Part(value, flow.Amount.Units, edge.Amount.Units);
            any = true;
        }

        return any
            ? "≈ " + Math.Round(total, 0, MidpointRounding.AwayFromZero).ToString("#,##0", Swedish)
                + MoneyText.Unit(currency!)
            : string.Empty;
    }

    /// <summary>Gives the money's part of a line's value, in proportion to its part of the line's amount.</summary>
    /// <param name="value">The line's value.</param>
    /// <param name="units">The money on the line.</param>
    /// <param name="whole">The line's amount.</param>
    /// <returns>The part.</returns>
    private static decimal Part(EdgeValue value, BigInteger units, BigInteger whole)
    {
        var sum = value.Parts.Count > 0 ? value.Parts.Sum(part => part.Valuation.Value) : value.Rounded;
        return units >= whole ? sum : sum * (decimal)((double)units / (double)whole);
    }
}
