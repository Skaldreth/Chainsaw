using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The registered tracing methods, sorted by the chains they cover: each chain gets the newest version of each method
/// registered for its model and itself, so that a method for another model or chain never changes what it gives.
/// </summary>
internal static class TracingMethods
{
    /// <summary>
    /// Checks the methods that cover a chain and gives the newest version of each. Methods for other chains are not
    /// looked at, so one registered wrongly for another chain never stops this one (as for the heuristics).
    /// </summary>
    /// <typeparam name="T">The model's contract, for example <see cref="IUtxoTracingMethod"/>.</typeparam>
    /// <param name="methods">Every registered method.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The newest version of each method for the chain, by kind.</returns>
    /// <exception cref="ArgumentException">
    /// A method for the chain does not implement its model's contract, or two have the same kind and version.
    /// </exception>
    public static IReadOnlyDictionary<TracingMethodKind, T> For<T>(IEnumerable<ITracingMethod> methods, Chain chain)
        where T : class, ITracingMethod
    {
        ArgumentNullException.ThrowIfNull(methods);
        var covering = methods.Where(method => method.Scope?.Covers(chain) ?? true).ToList();
        if (covering.FirstOrDefault(method => !Fits(method)) is { } misfit)
        {
            throw new ArgumentException(
                $"{misfit.Id} is registered for {misfit.Scope} but does not implement that model's contract.",
                nameof(methods));
        }

        if (covering.GroupBy(method => method.Id).FirstOrDefault(group => group.Count() > 1) is { } twice)
        {
            throw new ArgumentException($"{twice.Key} is registered twice for {chain}.", nameof(methods));
        }

        return covering.OfType<T>()
            .GroupBy(method => method.Id.Kind)
            .ToDictionary(group => group.Key, group => group.MaxBy(method => method.Id.Version)!);
    }

    /// <summary>Says whether a method implements the contract of the model it is registered for.</summary>
    /// <param name="method">The method.</param>
    /// <returns>Whether it does.</returns>
    private static bool Fits(ITracingMethod method) => method.Scope?.Model switch
    {
        LedgerModel.Utxo => method is IUtxoTracingMethod,
        LedgerModel.Account => method is IAccountTracingMethod,
        _ => false,
    };
}
