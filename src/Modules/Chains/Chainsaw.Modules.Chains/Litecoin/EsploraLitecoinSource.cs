using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Litecoin from an Esplora API (litecoinspace.org by default), asked only through <see cref="IExitHttp"/>: the same
/// lookups as Bitcoin's, on Litecoin's network, sealed under <see cref="RawResponseStore.LitecoinChainName"/>.
/// </summary>
internal sealed class EsploraLitecoinSource : EsploraBitcoinSource, ILitecoinSource
{
    /// <summary>Creates the source.</summary>
    /// <param name="sources">The chains' endpoints, of which Litecoin's are asked.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings, on Litecoin's network.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <exception cref="ArgumentException">The settings are not on Litecoin's network.</exception>
    public EsploraLitecoinSource(
        ChainSources sources, IOperatorProfileStore operators, IClock clock, BitcoinSourceOptions options,
        TimeZoneInfo timeZone)
        : base(sources, operators, clock, OnLitecoin(options), timeZone)
    {
    }

    /// <summary>Checks that the settings are on Litecoin's network.</summary>
    /// <param name="options">The settings.</param>
    /// <returns>The same settings.</returns>
    /// <exception cref="ArgumentException">They are on another network.</exception>
    private static BitcoinSourceOptions OnLitecoin(BitcoinSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Network == BitcoinNetwork.Litecoin
            ? options
            : throw new ArgumentException("Litecoin's source answers only on Litecoin's network.", nameof(options));
    }
}
