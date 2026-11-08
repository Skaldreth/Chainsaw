namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A request through the exit failed. The message is Swedish and meant for the user; it never contains the address
/// asked, the configuration or anything about the case.
/// </summary>
/// <remarks>
/// The inner exception is for diagnostics on this computer only. It comes from .NET and can name the host and port
/// asked, so it must never be shown to the user, written to a shared log or put in a case.
/// </remarks>
public class ExitException : Exception
{
    /// <summary>Creates the exception for a reason, with the reason's standard message.</summary>
    /// <param name="error">Why the request failed.</param>
    public ExitException(ExitError error)
        : base(MessageFor(error))
    {
        Error = error;
    }

    /// <summary>Creates the exception for a reason with the underlying failure as the inner exception.</summary>
    /// <param name="error">Why the request failed.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public ExitException(ExitError error, Exception innerException)
        : base(MessageFor(error), innerException)
    {
        Error = error;
    }

    /// <summary>
    /// Creates the exception for a reason on a known way out, so that the message names the way the request took: a
    /// request through Windows never says that it went through an exit.
    /// </summary>
    /// <param name="error">Why the request failed.</param>
    /// <param name="route">The way out the request took.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public ExitException(ExitError error, ExitRoute route, Exception innerException)
        : base(MessageFor(error, route), innerException)
    {
        Error = error;
        Route = route;
    }

    /// <summary>Gets why the request failed.</summary>
    public ExitError Error { get; }

    /// <summary>Gets the way out the request took, or <see langword="null"/> when it is not known.</summary>
    public ExitRoute? Route { get; }

    /// <summary>Gives the message for a reason on a way out.</summary>
    /// <param name="error">The reason.</param>
    /// <param name="route">The way out.</param>
    /// <returns>The message for the user.</returns>
    private static string MessageFor(ExitError error, ExitRoute route) => (error, route) switch
    {
        (ExitError.Timeout, ExitRoute.Windows) => "Svaret kom inte i tid genom datorns nät.",
        (ExitError.RequestFailed, ExitRoute.Windows) => "Uppslaget misslyckades på vägen genom datorns nät.",
        (ExitError.ConnectionLost, ExitRoute.Windows) =>
            "Vägen ut byttes medan uppslaget pågick, så det avbröts. Försök igen.",
        _ => MessageFor(error),
    };

    /// <summary>Gives the fixed Swedish message for a reason.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>The message for the user.</returns>
    private static string MessageFor(ExitError error) => error switch
    {
        ExitError.NotConnected =>
            "Ingen utgång är ansluten. Chainsaw gör inga uppslag direkt från datorns nät; anslut en utgång först.",
        ExitError.Timeout => "Svaret kom inte i tid genom utgången.",
        ExitError.ResponseTooLarge => "Svaret var större än vad Chainsaw tar emot.",
        ExitError.RequestFailed => "Uppslaget misslyckades på vägen genom utgången.",
        ExitError.ConnectionLost => "Utgången kopplades ner medan uppslaget pågick.",
        ExitError.Offline => "Datorn når inte internet. Chainsaw gör inga uppslag förrän nätet är tillbaka.",
        ExitError.ProxyLoginRequired =>
            "Proxyn kräver inloggning. Inga uppslag görs förrän den släpper igenom; se Inställningar › Nätverk.",
        ExitError.CertificateChanged =>
            "Den egna noden visar ett annat certifikat än det som är fäst, så inget skickades. "
            + "Se Inställningar › Källor.",
        ExitError.NotLocal =>
            "Adressen pekar inte på datorn eller ett privat nät. En lokal endpoint nås bara där, och inget skickades.",
        _ => "Uppslaget genom utgången misslyckades.",
    };
}
