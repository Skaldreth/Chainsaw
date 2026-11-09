namespace Chainsaw.SharedKernel;

/// <summary>
/// An address on any chain, as the chain writes it: what the account model's contract names an account by. Each
/// chain's own address type (for Bitcoin <c>BitcoinAddress</c>) checks the format; this only carries it with its
/// chain, so that the same text on two chains is never taken for one actor.
/// </summary>
public sealed record ChainAddress
{
    /// <summary>Creates an address.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="value">
    /// The address; hex on Ethereum's virtual machine (Ethereum and BSC) is kept in lower case (the mixed case is only a
    /// checksum), other chains' as given.
    /// </param>
    /// <exception cref="ArgumentException">The address is empty or holds white space.</exception>
    public ChainAddress(Chain chain, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.IsDefined(chain))
        {
            throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain.");
        }

        var trimmed = value.Trim();
        if (trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("An address holds no white space.", nameof(value));
        }

        (Chain, Value) = (chain, chain.IsEvm() ? trimmed.ToLowerInvariant() : trimmed);
    }

    /// <summary>Gets the chain.</summary>
    public Chain Chain { get; }

    /// <summary>Gets the address as the chain compares it.</summary>
    public string Value { get; }

    /// <summary>Writes the chain and the address, for example <c>Ethereum:0x…</c>.</summary>
    /// <returns>The chain, a colon and the address.</returns>
    public override string ToString() => Chain + ":" + Value;
}
