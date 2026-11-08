using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The row <em>Sparas i ärendet</em> on the settings' page Kurser (sketch KU1a): how many rates the open case keeps,
/// each sealed with its source, time and SHA-256, counted off the UI thread when the page is shown. Nothing is sent.
/// </summary>
public sealed partial class RatesSummaryViewModel : ObservableObject
{
    /// <summary>What the row says without an open case.</summary>
    public const string NoCaseText = "Inget ärende öppet";

    /// <summary>What the row says while it counts.</summary>
    public const string CountingText = "Räknar …";

    /// <summary>What the row says when the case could not be read.</summary>
    public const string UnreadText = "Kunde inte läsas";

    /// <summary>The open case, if any.</summary>
    private readonly CaseSession _session;

    /// <summary>The rates in the case.</summary>
    private readonly IPriceService _prices;

    /// <summary>Counts the refreshes, so that only the latest one's answer is shown.</summary>
    private int _refreshes;

    /// <summary>Creates the row.</summary>
    /// <param name="session">The open case, if any.</param>
    /// <param name="prices">The rates in the case.</param>
    public RatesSummaryViewModel(CaseSession session, IPriceService prices)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(prices);
        _session = session;
        _prices = prices;
    }

    /// <summary>Gets what the row says: for example <c>23 kurser</c>, or that no case is open.</summary>
    [ObservableProperty]
    public partial string StoredText { get; private set; } = NoCaseText;

    /// <summary>Gets what hovering the count says: how the rates divide between the sources.</summary>
    [ObservableProperty]
    public partial string? StoredTip { get; private set; }

    /// <summary>Counts the rates in the open case again, off the UI thread.</summary>
    /// <returns>A task that completes when the row says the count.</returns>
    public async Task RefreshAsync()
    {
        var refresh = ++_refreshes;
        if (_session.Current is not { } open)
        {
            Show(refresh, NoCaseText, null);
            return;
        }

        StoredText = CountingText;
        StoredTip = null;
        try
        {
            var summary = await Task.Run(() => _prices.Summarize(open)).ConfigureAwait(true);
            Show(refresh, Describe(summary), Tip(summary));
        }
        catch (CaseException)
        {
            // The case was closed or locked while counting: say so honestly rather than a number.
            Show(refresh, _session.Current is null ? NoCaseText : UnreadText, null);
        }
    }

    /// <summary>Writes the count: the crypto rates, each one used with a day's exchange rate.</summary>
    /// <param name="summary">The counts.</param>
    /// <returns>For example <c>23 kurser</c> or <c>1 kurs</c>.</returns>
    public static string Describe(RateSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var count = summary.CryptoRates;
        return count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " kurs" : " kurser");
    }

    /// <summary>Writes how the sealed answers divide between the sources.</summary>
    /// <param name="summary">The counts.</param>
    /// <returns>For example <c>23 svar från Bitstamp och 2 från Riksbanken, förseglade.</c></returns>
    public static string Tip(RateSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{summary.CryptoRates} svar från Bitstamp och {summary.FiatResponses} från Riksbanken, förseglade.");
    }

    /// <summary>Shows a refresh's answer, unless a newer refresh has started.</summary>
    /// <param name="refresh">The refresh.</param>
    /// <param name="text">What the row says.</param>
    /// <param name="tip">What hovering it says.</param>
    private void Show(int refresh, string text, string? tip)
    {
        if (refresh != _refreshes)
        {
            return;
        }

        StoredText = text;
        StoredTip = tip;
    }
}
