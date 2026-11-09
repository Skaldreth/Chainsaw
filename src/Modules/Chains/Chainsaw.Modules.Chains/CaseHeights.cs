using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>Reads the highest block a case's own lookups have shown (see <see cref="ICaseHeights"/>).</summary>
/// <param name="bitcoin">The Bitcoin source's settings, which name the network its answers are of.</param>
internal sealed class CaseHeights(BitcoinSourceOptions bitcoin) : ICaseHeights
{
    /// <inheritdoc/>
    public long? Known(ICaseReader reader, Chain chain)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return StoreOf(chain)?.KnownHeight(reader);
    }

    /// <inheritdoc/>
    public long? AtLeast(ICaseReader reader, Chain chain, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (StoreOf(chain) is not { } store)
        {
            return null;
        }

        var known = store.KnownHeight(reader, null, at);
        var later = store.LowestAt(reader, at);
        return known is null || later is null ? known ?? later : Math.Max(known.Value, later.Value);
    }

    /// <summary>Gives the store of a chain's answers, on the network the sources are set to.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The store, or <see langword="null"/> for a chain without one.</returns>
    private RawResponseStore? StoreOf(Chain chain)
    {
        var name = chain switch
        {
            Chain.Bitcoin => RawResponseStore.NameOf(bitcoin.Network),
            Chain.Ethereum => BlockscoutEthereumSource.ChainName,
            _ => null,
        };
        return name is null ? null : new RawResponseStore(TimeZoneInfo.Utc, name);
    }
}
