namespace Chainsaw.Recovery;

/// <summary>The tool's exit codes.</summary>
internal static class ExitCodes
{
    /// <summary>The command did what was asked; for <c>check</c>, the case has an envelope for the key.</summary>
    public const int Success = 0;

    /// <summary><c>check</c> found no envelope for the key in the case.</summary>
    public const int NotCovered = 1;

    /// <summary>The command line was wrong; the usage is shown.</summary>
    public const int Usage = 2;

    /// <summary>The command failed; the reason is written to the standard error.</summary>
    public const int Failed = 3;
}
