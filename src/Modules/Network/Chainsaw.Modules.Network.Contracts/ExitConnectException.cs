namespace Chainsaw.Modules.Network.Contracts;

/// <summary>A tunnel could not be started. Thrown by an <see cref="IExitConnector"/>.</summary>
public sealed class ExitConnectException : Exception
{
    /// <summary>Creates the exception for a reason.</summary>
    /// <param name="failure">Why the tunnel could not be started.</param>
    /// <param name="message">A message for logs; it must never contain the configuration.</param>
    public ExitConnectException(ExitFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>Creates the exception for a reason with the underlying failure as the inner exception.</summary>
    /// <param name="failure">Why the tunnel could not be started.</param>
    /// <param name="message">A message for logs; it must never contain the configuration.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public ExitConnectException(ExitFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>Gets why the tunnel could not be started.</summary>
    public ExitFailure Failure { get; }
}
