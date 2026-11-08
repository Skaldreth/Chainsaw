namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// Checks the way out (sketch UT3b): asks <see cref="TraceUri"/> through <see cref="IExitHttp"/>, the way chosen now,
/// which address lookups come from and where it is. The question carries nothing about a case. The latest answer for
/// each exit (VPN) is kept encrypted in the vault with the exit, never in a case, and goes when the exit goes.
/// </summary>
public interface IExitCheck
{
    /// <summary>The address asked: Cloudflare's trace, which answers with the asker's address and country.</summary>
    static readonly Uri TraceUri = new("https://1.1.1.1/cdn-cgi/trace");

    /// <summary>Asks the way chosen now, and keeps the answer with the exit when it left through one.</summary>
    /// <param name="cancellationToken">Stops the question.</param>
    /// <returns>What the answer saw.</returns>
    /// <exception cref="ExitNotConnectedException">No way out is open now; nothing was sent.</exception>
    /// <exception cref="ExitException">
    /// The question failed on the way, or the answer did not say an address (<see cref="ExitError.RequestFailed"/>).
    /// </exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    Task<ExitSighting> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the latest kept answer for an exit.</summary>
    /// <param name="exitId">The exit.</param>
    /// <returns>The answer, or <see langword="null"/> when the exit has not been checked or the vault is locked.</returns>
    ExitSighting? FindLast(Guid exitId);
}
