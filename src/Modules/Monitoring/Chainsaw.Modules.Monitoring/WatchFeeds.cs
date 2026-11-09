using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Gives the feeds that read a chain through a case's sources with <see cref="LookupMode.InStep"/>: the same for the
/// open Chainsaw and for the background process, whose case is one in memory that only holds the responses.
/// </summary>
/// <param name="sources">The chains' sources.</param>
internal sealed class WatchFeeds(WatchSources sources) : IWatchFeeds
{
    /// <inheritdoc/>
    public IWatchFeed? For(ICase @case, Chain chain)
    {
        ArgumentNullException.ThrowIfNull(@case);
        if (sources.Utxo(chain) is { } utxo)
        {
            return new BitcoinCaseFeed(utxo, @case);
        }

        return sources.Account(chain) is { } account ? new EthereumCaseFeed(account, @case) : null;
    }

    /// <inheritdoc/>
    public Task<long?> GetHeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return sources.HeightAsync(@case, chain, cancellationToken);
    }
}
