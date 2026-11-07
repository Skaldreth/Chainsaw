namespace Chainsaw.Modules.Network;

/// <summary>Chainsaw's WireGuard helper is missing, is not the expected copy, or cannot be started.</summary>
internal sealed class TunnelHelperUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the helper is unavailable.</param>
    public TunnelHelperUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the underlying failure.</summary>
    /// <param name="message">Why the helper is unavailable.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public TunnelHelperUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
