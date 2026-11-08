using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The highest block of a chain that a case's own lookups have shown: what Chainsaw itself knows the chain has come
/// to, from the sealed answers its lookups took (never from responses sealed only as evidence, such as the background
/// watching's), and never above the highest the chain can have come by when each answer came.
/// </summary>
public interface ICaseHeights
{
    /// <summary>Reads the highest block a case's own lookups have shown of a chain.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="chain">The chain, on the network the sources are set to.</param>
    /// <returns>The height, or <see langword="null"/> when the case's lookups show none.</returns>
    long? Known(ICaseReader reader, Chain chain);
}
