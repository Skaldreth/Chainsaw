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

    /// <summary>
    /// Reads the lowest a chain can have been at a time, by the case's own lookups: the highest they had shown by
    /// then, or, when a later lookup shows more, its height less the most blocks the chain can have made between.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="chain">The chain, on the network the sources are set to.</param>
    /// <param name="at">The time.</param>
    /// <returns>The height, or <see langword="null"/> when the case's lookups give no bound.</returns>
    long? AtLeast(ICaseReader reader, Chain chain, DateTimeOffset at);
}
