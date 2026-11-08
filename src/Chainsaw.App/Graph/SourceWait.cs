using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>One endpoint in the box under <em>Väntar på källan</em>: its host, why it waits and for how long.</summary>
/// <param name="Host">The endpoint's host, for example <c>mempool.space</c>.</param>
/// <param name="Why">Why it is not asked now.</param>
/// <param name="Until">Until when it asked Chainsaw to wait, or <see langword="null"/> when it does not answer.</param>
public sealed record SourceWaitRow(string Host, string Why, DateTimeOffset? Until)
{
    /// <summary>Gets whether it asked Chainsaw to wait, rather than not answering.</summary>
    public bool IsWaiting => Until is not null;

    /// <summary>Writes how long is left of its wait.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>For example <c>12 s</c>, or a dash when it does not answer.</returns>
    public string LeftText(DateTimeOffset now) =>
        Until is { } until ? SourceWait.Seconds(until - now) : "–";
}

/// <summary>
/// <em>Väntar på källan</em> (sketch GR9a): every endpoint of a chain asks Chainsaw to wait, or does not answer, so no
/// lookup on the chain can be made until the first wait is over. While one endpoint still answers the others take over
/// in silence, and nothing is shown.
/// </summary>
/// <param name="Chain">The chain.</param>
/// <param name="Rows">The endpoints that are on, those that wait first.</param>
/// <param name="Since">When the wait began, for the ring that drains.</param>
/// <param name="Until">When Chainsaw may ask again: the first endpoint's wait is over.</param>
public sealed record SourceWait(Chain Chain, IReadOnlyList<SourceWaitRow> Rows, DateTimeOffset Since, DateTimeOffset Until)
{
    /// <summary>The box's title.</summary>
    public const string Title = "Källorna ber Chainsaw vänta";

    /// <summary>What the box says at its foot.</summary>
    public const string Foot =
        "Chainsaw frågar inte fortare än källorna tillåter, och tar den första som svarar igen. Arbetet går vidare.";

    /// <summary>Why an endpoint that asked Chainsaw to wait is not asked.</summary>
    public const string WaitsText = "ber om väntan";

    /// <summary>Why an endpoint that failed is not asked.</summary>
    public const string SilentText = "svarar inte";

    /// <summary>What a node waiting for its source says on hovering its hourglass.</summary>
    public const string NodeTip = "Uppslaget väntar på källan";

    /// <summary>Gets the chain's mark in the ring, for example <c>₿</c>.</summary>
    public string Glyph => Chain switch
    {
        Chain.Bitcoin => "₿",
        Chain.Ethereum => "Ξ",
        _ => "₮",
    };

    /// <summary>
    /// Reads whether every endpoint of a chain waits now. An endpoint that failed since it was tried counts as
    /// waiting only when another asked Chainsaw to wait, so that a chain whose endpoints all fail is not told as a
    /// wait.
    /// </summary>
    /// <param name="view">The chain's sources.</param>
    /// <param name="now">The time now.</param>
    /// <param name="since">When the wait was first seen, or <see langword="null"/> to start it now.</param>
    /// <returns>The wait, or <see langword="null"/> when a lookup can be made now.</returns>
    public static SourceWait? Read(ChainSourcesView view, DateTimeOffset now, DateTimeOffset? since = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Mode == SourceMode.Off)
        {
            return null;
        }

        var on = view.Endpoints.Where(endpoint => endpoint.Endpoint.Enabled).ToList();
        var waiting = on.Where(endpoint => endpoint.WaitUntil > now).ToList();
        if (waiting.Count == 0 || on.Any(endpoint => !(endpoint.WaitUntil > now) && !endpoint.FailedSince))
        {
            return null;
        }

        var rows = waiting.OrderBy(endpoint => endpoint.WaitUntil)
            .Select(endpoint => new SourceWaitRow(Host(endpoint.Endpoint), WaitsText, endpoint.WaitUntil))
            .Concat(on.Except(waiting).Select(endpoint => new SourceWaitRow(Host(endpoint.Endpoint), SilentText, null)))
            .ToList();
        return new SourceWait(view.Chain, rows, since ?? now, waiting.Min(endpoint => endpoint.WaitUntil!.Value));
    }

    /// <summary>Writes how long is left until Chainsaw may ask again.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>For example <c>34 s</c>.</returns>
    public string LeftText(DateTimeOffset now) => Seconds(Until - now);

    /// <summary>Gives how much of the wait is left, for the ring that drains: 1 at its start, 0 when it is over.</summary>
    /// <param name="now">The time now.</param>
    /// <returns>The share, between 0 and 1.</returns>
    public double LeftShare(DateTimeOffset now)
    {
        var whole = (Until - Since).TotalSeconds;
        return whole <= 0 ? 0 : Math.Clamp((Until - now).TotalSeconds / whole, 0, 1);
    }

    /// <summary>Writes a span as whole seconds, rounded up, never below zero.</summary>
    /// <param name="left">The span.</param>
    /// <returns>For example <c>34 s</c>.</returns>
    public static string Seconds(TimeSpan left) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)Math.Ceiling(left.TotalSeconds))} s");

    /// <summary>Gives an endpoint's host, or its name when it has none.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The host.</returns>
    private static string Host(SourceEndpoint endpoint) =>
        endpoint.Address.Host.Length > 0 ? endpoint.Address.Host : endpoint.Name;
}
