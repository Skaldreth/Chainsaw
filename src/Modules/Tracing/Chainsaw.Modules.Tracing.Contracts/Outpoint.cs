using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// One output of a transaction on a UTXO chain (Bitcoin, Litecoin, Zcash's transparent part), named by the transaction
/// (which carries its chain) and the output's index (its <c>vout</c>): a coin that can be spent once. Tracing follows coins, because an input always spends a whole earlier output.
/// </summary>
/// <param name="Txid">The transaction that created the output.</param>
/// <param name="Index">The output's index, from 0.</param>
public sealed record Outpoint(Txid Txid, int Index)
{
    /// <summary>Gets the transaction that created the output.</summary>
    public Txid Txid { get; } = Txid ?? throw new ArgumentNullException(nameof(Txid));

    /// <summary>Gets the output's index, from 0.</summary>
    public int Index { get; } = Index >= 0
        ? Index
        : throw new ArgumentOutOfRangeException(nameof(Index), "An output's index starts at 0.");

    /// <summary>Writes the outpoint as it is usually written, <c>txid:index</c>.</summary>
    /// <returns>The txid, a colon and the index.</returns>
    public override string ToString() => Txid.Value + ":" + Index.ToString(CultureInfo.InvariantCulture);
}
