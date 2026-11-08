namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// A decision is refused because the suggestion has changed in the case since it was sealed: for a wallet, the
/// clustering no longer proposes the merges that join the addresses as they were shown. Nothing is stored; the checks
/// are run again so that the user decides on the suggestion as it stands now.
/// </summary>
public sealed class SuggestionChangedException : ArgumentException
{
    /// <summary>Creates the exception.</summary>
    public SuggestionChangedException()
        : base("The suggestion has changed since it was sealed; run the checks again.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message, in English, for logs; the shell writes its own Swedish text.</param>
    public SuggestionChangedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public SuggestionChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
