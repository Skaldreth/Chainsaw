using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Gives the feeds that read a chain through a case's sources with <see cref="LookupMode.InStep"/>: the same for the
/// open Chainsaw and for the background process, whose case is one in memory that only holds the responses.
/// </summary>
/// <param name="bitcoin">Bitcoin's source.</param>
/// <param name="ethereum">Ethereum's source.</param>
internal sealed class WatchFeeds(IBitcoinSource bitcoin, IEthereumSource ethereum) : IWatchFeeds
{
    /// <inheritdoc/>
    public IWatchFeed? For(ICase @case, Chain chain)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return chain switch
        {
            Chain.Bitcoin => new BitcoinCaseFeed(bitcoin, @case),
            Chain.Ethereum => new EthereumCaseFeed(ethereum, @case),
            _ => null,
        };
    }
}
