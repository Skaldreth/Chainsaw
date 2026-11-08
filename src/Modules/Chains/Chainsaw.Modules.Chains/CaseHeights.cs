using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>Reads the highest block a case's own lookups have shown (see <see cref="ICaseHeights"/>).</summary>
/// <param name="bitcoin">The Bitcoin source's settings, which name the network its answers are of.</param>
internal sealed class CaseHeights(BitcoinSourceOptions bitcoin) : ICaseHeights
{
    /// <inheritdoc/>
    public long? Known(ICaseReader reader, Chain chain, DateTimeOffset? by = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var name = chain switch
        {
            Chain.Bitcoin => RawResponseStore.NameOf(bitcoin.Network),
            Chain.Ethereum => BlockscoutEthereumSource.ChainName,
            _ => null,
        };
        return name is null ? null : new RawResponseStore(TimeZoneInfo.Utc, name).KnownHeight(reader, null, by);
    }
}
