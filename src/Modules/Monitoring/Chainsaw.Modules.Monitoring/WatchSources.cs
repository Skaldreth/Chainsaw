using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// The chains' sources a watch reads through, written once for the check, the feeds and the tips: Bitcoin's and
/// Ethereum's always, and TRON's, Litecoin's and BSC's where they are registered. A chain is watched through the
/// contract of its model: Bitcoin and Litecoin as UTXO chains, Ethereum, TRON and BSC as account chains.
/// </summary>
/// <param name="Bitcoin">Bitcoin's source.</param>
/// <param name="Ethereum">Ethereum's source.</param>
/// <param name="Tron">TRON's source, or <see langword="null"/> where it is not registered.</param>
/// <param name="Litecoin">Litecoin's source, or <see langword="null"/> where it is not registered.</param>
/// <param name="Bsc">BSC's source, or <see langword="null"/> where it is not registered.</param>
internal sealed record WatchSources(
    IBitcoinSource Bitcoin,
    IEthereumSource Ethereum,
    ITronSource? Tron = null,
    ILitecoinSource? Litecoin = null,
    IBscSource? Bsc = null)
{
    /// <summary>The chains that can be watched, whatever is registered.</summary>
    public static readonly IReadOnlySet<Chain> Watchable =
        new HashSet<Chain> { Chain.Bitcoin, Chain.Ethereum, Chain.Tron, Chain.Litecoin, Chain.Bsc };

    /// <summary>Gives a UTXO chain's source.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>
    /// Bitcoin's or Litecoin's source, or <see langword="null"/> for another chain or one not registered.
    /// </returns>
    public IBitcoinSource? Utxo(Chain chain) => chain switch
    {
        Chain.Bitcoin => Bitcoin,
        Chain.Litecoin => Litecoin,
        _ => null,
    };

    /// <summary>Gives an account chain's source.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>Its source, or <see langword="null"/> for another chain or one not registered.</returns>
    public IAccountSource? Account(Chain chain) => chain switch
    {
        Chain.Ethereum => Ethereum,
        Chain.Tron => Tron,
        Chain.Bsc => Bsc,
        _ => null,
    };

    /// <summary>
    /// Asks a chain's height through its source, only of an endpoint in step with the chain
    /// (<see cref="WatchSchedule.HeightAsync"/>).
    /// </summary>
    /// <param name="case">The case the response is sealed in.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The height, or <see langword="null"/> when it could not be had or the chain has no source.</returns>
    public Task<long?> HeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken) =>
        WatchSchedule.HeightAsync(Utxo(chain), Account(chain), @case, cancellationToken);
}
