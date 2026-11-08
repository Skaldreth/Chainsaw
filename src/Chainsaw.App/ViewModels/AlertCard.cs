using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>Where the message in the middle of the screen is shown from (sketch BV4a).</summary>
public enum AlertCardSource
{
    /// <summary><em>Prova</em> in the settings: how it looks.</summary>
    Preview,

    /// <summary>The background process, while Chainsaw is closed.</summary>
    Closed,

    /// <summary>The background process, while Chainsaw runs (open or locked, which the card cannot know).</summary>
    Running,
}

/// <summary>
/// The message in the middle of the screen (sketch BV4a): a small card with the eye and its rings, on top of every
/// window until it is closed, without darkening anything. Like every way of saying it, it says only that a watch has
/// hit, never the address, the amount or the case; the details stand in Bevakning after the unlock.
/// </summary>
/// <param name="Source">Where it is shown from.</param>
/// <param name="At">When the hit was said, in local time.</param>
public sealed record AlertCard(AlertCardSource Source, DateTimeOffset At)
{
    /// <summary>The argument that starts Chainsaw as only the card, as the background process does.</summary>
    public const string Argument = BackgroundNames.ScreenMessageArgument;

    /// <summary>The card's title.</summary>
    public const string Title = "En bevakning har slagit till";

    /// <summary>The tip on the pin in the corner.</summary>
    public const string StaysText = "Ligger överst tills det stängs";

    /// <summary>The line at the top when the card comes from the background: who says it.</summary>
    public const string FromText = "Chainsaw · Bevakning";

    /// <summary>Gets whether the line at the top says who it comes from: only when Chainsaw is not in front.</summary>
    public bool ShowsFrom => Source != AlertCardSource.Preview;

    /// <summary>Gets the line under the title.</summary>
    public string Text => Source switch
    {
        AlertCardSource.Preview => "Så här ser det ut. Detaljerna står i Bevakning.",
        AlertCardSource.Closed => "Öppna Chainsaw och lås upp för att se vilken.",
        _ => "Detaljerna står i Bevakning, efter upplåsning.",
    };

    /// <summary>Gets the main button's words.</summary>
    public string ActionText => Source switch
    {
        AlertCardSource.Preview => "Visa",
        AlertCardSource.Closed => "Öppna Chainsaw",
        _ => "Visa Chainsaw",
    };

    /// <summary>Gets whether the main button starts or brings forward Chainsaw, rather than only closing.</summary>
    public bool OpensChainsaw => Source != AlertCardSource.Preview;

    /// <summary>Gets when, as the card shows it: <c>14:32</c>.</summary>
    public string TimeText => At.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gives the card for the arguments Chainsaw was started with, or none. Only the argument alone counts, as the
    /// background process gives it: a <c>chainsaw:</c> link with a quotation mark in it can split into more arguments
    /// (<c>"%1"</c>), and a link must never show the card.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="isRunning">Whether a Chainsaw already runs for the user, open or locked.</param>
    /// <param name="now">Now, in local time.</param>
    /// <returns>The card, or <see langword="null"/> when Chainsaw was not started as the card.</returns>
    public static AlertCard? FromArguments(IReadOnlyList<string> arguments, bool isRunning, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 1 || !string.Equals(arguments[0], Argument, StringComparison.Ordinal))
        {
            return null;
        }

        return new AlertCard(isRunning ? AlertCardSource.Running : AlertCardSource.Closed, now);
    }
}
