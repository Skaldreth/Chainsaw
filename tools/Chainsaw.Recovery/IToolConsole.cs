namespace Chainsaw.Recovery;

/// <summary>
/// What the tool says and asks. Secrets are read without echo into a buffer the caller clears, and are never
/// written anywhere; the tests give their own console.
/// </summary>
internal interface IToolConsole
{
    /// <summary>Writes a line to the standard output.</summary>
    /// <param name="line">The line.</param>
    void Out(string line);

    /// <summary>Writes a line to the standard error.</summary>
    /// <param name="line">The line.</param>
    void Error(string line);

    /// <summary>Asks for a secret (a password or a master key) without showing it.</summary>
    /// <param name="prompt">What is asked for.</param>
    /// <returns>The secret in a buffer that the caller clears.</returns>
    char[] ReadSecret(string prompt);
}
