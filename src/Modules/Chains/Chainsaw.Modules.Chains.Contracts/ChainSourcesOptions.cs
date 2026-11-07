namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>Where the chains' endpoints are kept.</summary>
public sealed class ChainSourcesOptions
{
    /// <summary>The file's name in <see cref="Directory"/>.</summary>
    public const string FileName = "sources.json";

    /// <summary>
    /// Gets or sets the folder of <see cref="FileName"/>, in the program's data (<c>%LOCALAPPDATA%\Chainsaw</c> in the
    /// application), never next to a case. <see langword="null"/> keeps the endpoints in memory only.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Gets or sets how an endpoint's turn is waited for; <see langword="null"/> waits for real. Only the tests set
    /// it, to move their own clock instead of waiting.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }
}
