namespace Chainsaw.Modules.Chains;

/// <summary>
/// A response from the source that Chainsaw cannot read or that does not add up (for example a fee that is not the
/// inputs minus the outputs, or an address that does not match its script). Such a response is still sealed in the
/// case, but nothing is built on it.
/// </summary>
internal sealed class EsploraFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong, in English, for diagnostics.</param>
    public EsploraFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the failure that caused it.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EsploraFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
