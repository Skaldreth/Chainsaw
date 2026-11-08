using System.Globalization;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// How Chainsaw says that nothing new can be fetched now (principle 9), in every form a view needs, so that the title
/// bar, the graph, Nytt ärende and Bevakning say the same thing in the same words. There are three causes: VPN is the
/// way out and no exit is connected, Windows is the way out and the computer does not reach the internet, or a tunnel
/// that fell is being connected again. <see cref="ExitWay.Offline"/> chooses the one that holds now.
/// </summary>
/// <param name="Text">The whole sentence, for a tooltip or a notice.</param>
/// <param name="Reason">Why, in a few words, after <em>Offline:</em> in a short line.</param>
/// <param name="Until">What nothing is fetched until, to end a sentence like <em>inget hämtas förrän …</em>.</param>
/// <param name="Hint">What the user can do about it, on hovering a short line.</param>
/// <param name="Waiting">What a row that waits for a lookup says, such as <em>Väntar på utgången</em>.</param>
public sealed record OfflineWords(string Text, string Reason, string Until, string Hint, string Waiting)
{
    /// <summary>What Chainsaw says while VPN is the way out and no exit is connected.</summary>
    public const string NoExitText =
        "Offline: ingen utgång ansluten. Det ärendet redan har visas, men inget nytt hämtas.";

    /// <summary>What Chainsaw says while Windows is the way out and the computer does not reach the internet.</summary>
    public const string NoNetworkText =
        "Offline: datorn når inte internet. Det ärendet redan har visas, men inget nytt hämtas.";

    /// <summary>What Chainsaw says while a fallen tunnel is connected again; the next attempt may follow.</summary>
    public const string ReconnectingText =
        "Offline: tunneln föll och ansluts igen av sig själv. Det ärendet redan har visas, men inget nytt hämtas.";

    /// <summary>Why nothing is fetched while no exit is connected, in a few words.</summary>
    public const string NoExitReason = "ingen utgång ansluten";

    /// <summary>What nothing is fetched until while no exit is connected.</summary>
    public const string NoExitUntil = "en utgång är ansluten i titelraden";

    /// <summary>What the user can do while no exit is connected.</summary>
    public const string NoExitHint = "Anslut en utgång i titelraden, eller välj Windows i inställningarna";

    /// <summary>What a row says while it waits for an exit.</summary>
    public const string NoExitWaiting = "Väntar på utgången";

    /// <summary>The culture the next attempt's time is written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Gets the words while VPN is the way out and no exit is connected.</summary>
    public static OfflineWords NoExit { get; } = new(NoExitText, NoExitReason, NoExitUntil, NoExitHint, NoExitWaiting);

    /// <summary>Gets the words while Windows is the way out and the computer does not reach the internet.</summary>
    public static OfflineWords NoNetwork { get; } = new(
        NoNetworkText,
        "datorn når inte internet",
        "datorn når internet igen",
        "Datorn når inte internet. Uppslagen går igen av sig själva när nätet kommer tillbaka.",
        "Väntar på nätet");

    /// <summary>Gives the words while a fallen tunnel is connected again.</summary>
    /// <param name="next">When the next attempt is made, or <see langword="null"/> while one runs now.</param>
    /// <returns>The words, with the next attempt's time in the sentence when one waits.</returns>
    public static OfflineWords Reconnecting(DateTimeOffset? next)
    {
        var text = next is { } at
            ? $"{ReconnectingText} Nästa försök kl. {at.ToLocalTime().ToString("HH:mm:ss", Swedish)}."
            : ReconnectingText;
        return new(
            text, "utgången ansluts igen", "utgången är ansluten igen", text, "Väntar på att utgången ansluts igen");
    }
}
