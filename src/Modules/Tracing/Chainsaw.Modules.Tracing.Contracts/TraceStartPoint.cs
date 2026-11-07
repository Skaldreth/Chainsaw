using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// Where the trace starts: an address (for example the one the victim paid to) or a transaction (for example the
/// victim's payment). Exactly one of <see cref="Address"/> and <see cref="Txid"/> is set.
/// </summary>
public sealed record TraceStartPoint
{
    /// <summary>Creates a start point; use <see cref="ForAddress"/> or <see cref="ForTransaction"/>.</summary>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <param name="txid">The transaction, or <see langword="null"/>.</param>
    private TraceStartPoint(BitcoinAddress? address, Txid? txid) => (Address, Txid) = (address, txid);

    /// <summary>Gets the address, when the start point is one.</summary>
    public BitcoinAddress? Address { get; }

    /// <summary>Gets the transaction, when the start point is one.</summary>
    public Txid? Txid { get; }

    /// <summary>Gives a start point at an address; the actor it belongs to is the trace's first column.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The start point.</returns>
    public static TraceStartPoint ForAddress(BitcoinAddress address) =>
        new(address ?? throw new ArgumentNullException(nameof(address)), null);

    /// <summary>
    /// Gives a start point at a transaction; the actors that pay into it are the trace's first column and those it
    /// pays to the next.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The start point.</returns>
    public static TraceStartPoint ForTransaction(Txid txid) =>
        new(null, txid ?? throw new ArgumentNullException(nameof(txid)));

    /// <summary>Writes the address or the txid.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => Address?.Value ?? Txid!.Value;
}
