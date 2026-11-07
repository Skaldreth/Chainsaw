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
}
