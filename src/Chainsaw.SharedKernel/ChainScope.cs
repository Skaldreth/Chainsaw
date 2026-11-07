namespace Chainsaw.SharedKernel;

/// <summary>
/// Which chains a tracing method, a heuristic or another analysis is registered for: one model, and either every
/// chain of that model or some of them by name. A step registered for one model never runs on the other, so a
/// method or heuristic for accounts can be added without anything for Bitcoin changing (principle 8).
/// </summary>
public sealed record ChainScope
{
    /// <summary>The chains named, or null for every chain of the model.</summary>
    private readonly Chain[]? _chains;

    /// <summary>Creates a scope.</summary>
    /// <param name="model">The model.</param>
    /// <param name="chains">The chains, or null for every chain of the model.</param>
    /// <exception cref="ArgumentException">No chain is named, or a chain keeps its money in another model.</exception>
    private ChainScope(LedgerModel model, Chain[]? chains)
    {
        if (!Enum.IsDefined(model))
        {
            throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown model.");
        }

        if (chains is { Length: 0 } || chains?.Any(chain => chain.Model() != model) == true)
        {
            throw new ArgumentException("A scope names at least one chain, all of its model.", nameof(chains));
        }

        (Model, _chains) = (model, chains?.Distinct().Order().ToArray());
    }

    /// <summary>Gets the model.</summary>
    public LedgerModel Model { get; }

    /// <summary>Gets the chains named, or null for every chain of the model.</summary>
    public IReadOnlyList<Chain>? Chains => _chains;

    /// <summary>Gives a scope of every chain of a model, as for a tracing method that only counts amounts.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The scope.</returns>
    public static ChainScope AllOf(LedgerModel model) => new(model, null);

    /// <summary>
    /// Gives a scope of some chains, all of the same model, as for a heuristic that knows their habits.
    /// </summary>
    /// <param name="chain">The first chain.</param>
    /// <param name="more">Further chains.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentException">The chains are of different models.</exception>
    public static ChainScope Only(Chain chain, params Chain[] more) => new(chain.Model(), [chain, .. more]);

    /// <summary>Says whether the scope covers a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>Whether a step in this scope runs on the chain.</returns>
    public bool Covers(Chain chain) => chain.Model() == Model && (_chains is null || _chains.Contains(chain));

    /// <inheritdoc/>
    public bool Equals(ChainScope? other) =>
        other is not null && Model == other.Model
        && (_chains ?? []).SequenceEqual(other._chains ?? []) && (_chains is null) == (other._chains is null);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        (_chains ?? []).Aggregate(HashCode.Combine(Model, _chains is null), HashCode.Combine);

    /// <summary>Writes the model and the chains, for example <c>Utxo</c> or <c>Account: Ethereum, Tron</c>.</summary>
    /// <returns>The scope.</returns>
    public override string ToString() => _chains is null ? Model.ToString() : Model + ": " + string.Join(", ", _chains);
}
