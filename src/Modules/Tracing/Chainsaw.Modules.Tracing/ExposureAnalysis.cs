using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The analysis <em>exposure v5</em> (sketch EP1b). It picks the tracing method registered for the asset's chain, by
/// its scope, and reads the money in that chain's model: coin by coin on a UTXO chain (<see cref="UtxoExposureRun"/>),
/// so that it never says otherwise than the graph about where a coin went, and wallet by wallet on an account chain
/// (<see cref="AccountExposureRun"/>). What both find is gathered by <see cref="ExposureTally"/>.
/// </summary>
internal sealed class ExposureAnalysis : IExposureAnalysis
{
    /// <summary>Every registered tracing method; the ones for the asset's chain are picked per request.</summary>
    private readonly IReadOnlyList<ITracingMethod> _methods;

    /// <summary>Creates the analysis with the registered methods.</summary>
    /// <param name="methods">The tracing methods.</param>
    public ExposureAnalysis(IEnumerable<ITracingMethod> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        _methods = [.. methods];
    }

    /// <inheritdoc/>
    public ExposureResult Analyse(ExposureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var chain = request.Asset.Chain;
        if (chain.Model() == LedgerModel.Utxo)
        {
            var utxo = TracingMethods.For<IUtxoTracingMethod>(_methods, chain);
            return new UtxoExposureRun(request, Pick(utxo, request.Method, chain)).Run();
        }

        var account = TracingMethods.For<IAccountTracingMethod>(_methods, chain);
        return new AccountExposureRun(request, Pick(account, request.Method, chain)).Run();
    }

    /// <summary>Picks the method of a kind among those registered for the chain.</summary>
    /// <typeparam name="T">The model's contract.</typeparam>
    /// <param name="methods">The chain's methods by kind.</param>
    /// <param name="kind">The kind asked for.</param>
    /// <param name="chain">The chain, for the error.</param>
    /// <returns>The method.</returns>
    /// <exception cref="InvalidOperationException">No method of the kind covers the chain.</exception>
    private static T Pick<T>(IReadOnlyDictionary<TracingMethodKind, T> methods, TracingMethodKind kind, Chain chain) =>
        methods.TryGetValue(kind, out var method)
            ? method
            : throw new InvalidOperationException($"No {kind} method is registered for {chain}.");
}
