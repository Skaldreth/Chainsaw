using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>Makes cases in memory with the tables of the modules that seal into one chain (see
/// <see cref="IMemoryCases"/>).</summary>
/// <param name="contributors">Every module's tables.</param>
internal sealed class MemoryCases(IEnumerable<ICaseSchemaContributor> contributors) : IMemoryCases
{
    /// <summary>Every module's tables, read once.</summary>
    private readonly IReadOnlyList<ICaseSchemaContributor> _contributors = [.. contributors];

    /// <inheritdoc/>
    public IMemoryCase Create(string sealedChain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sealedChain);
        return new MemoryCase(
            _contributors.Where(each => each.SealedChains.Any(chain => chain.Name == sealedChain)).ToList());
    }
}
