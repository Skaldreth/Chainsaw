namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What a typed text is as a starting point on the Bitcoin chain.</summary>
public enum BitcoinStartPointKind
{
    /// <summary>Neither an address nor a txid.</summary>
    Unknown,

    /// <summary>A valid address (<see cref="BitcoinStartPoint.Address"/>).</summary>
    Address,

    /// <summary>64 hex characters, which is a txid (or a block hash; the source tells them apart).</summary>
    Transaction,
}

/// <summary>
/// Recognises a starting point in what the user types or another program sends, for example in the command line or
/// a <c>chainsaw:bitcoin/…</c> link.
/// </summary>
/// <param name="Kind">What the text is.</param>
/// <param name="Address">The address, when it is one.</param>
/// <param name="Txid">The txid, when it is one.</param>
public sealed record BitcoinStartPoint(BitcoinStartPointKind Kind, BitcoinAddress? Address, Txid? Txid)
{
    /// <summary>Recognises a text, ignoring white space around it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>What it is.</returns>
    public static BitcoinStartPoint Recognise(string? text)
    {
        var trimmed = text?.Trim();
        if (BitcoinAddress.TryParse(trimmed, out var address))
        {
            return new BitcoinStartPoint(BitcoinStartPointKind.Address, address, null);
        }

        return Txid.TryParse(trimmed, out var txid)
            ? new BitcoinStartPoint(BitcoinStartPointKind.Transaction, null, txid)
            : new BitcoinStartPoint(BitcoinStartPointKind.Unknown, null, null);
    }
}
