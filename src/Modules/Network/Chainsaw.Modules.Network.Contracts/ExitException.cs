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

    /// <summary>Gets why the request failed.</summary>
    public ExitError Error { get; }

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
        ExitError.Offline => "Datorn har inget nät. Chainsaw gör inga uppslag förrän nätet är tillbaka.",
        ExitError.NotLocal =>
            "Adressen pekar inte på datorn eller ett privat nät. En lokal endpoint nås bara där, och inget skickades.",
        _ => "Uppslaget genom utgången misslyckades.",
    };
}
