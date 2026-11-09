using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What a start point is, whatever its chain.</summary>
public enum StartPointKind
{
    /// <summary>Not recognised on any chain.</summary>
    Unknown,

    /// <summary>An address.</summary>
    Address,

    /// <summary>A transaction.</summary>
    Transaction,
}

/// <summary>
/// Recognises a start point on every chain Chainsaw knows of, for the start points of a new case, the command line
/// and <c>chainsaw:</c> links. Bitcoin, Ethereum, TRON, BSC and Solana can be traced in the graph; a Litecoin value is
/// recognised and fetched in advance, so that the user is told honestly that it cannot be traced in the graph yet,
/// rather than that the text is wrong.
/// </summary>
/// <param name="Chain">The chain, or <see langword="null"/> when the text is not recognised.</param>
/// <param name="Kind">Whether it is an address or a transaction.</param>
/// <param name="Value">
/// The value in its canonical form (a txid in lower case), or the trimmed text when unknown.
/// </param>
/// <param name="Bitcoin">The Bitcoin start point, when the chain is Bitcoin.</param>
public sealed record ChainStartPoint(Chain? Chain, StartPointKind Kind, string Value, BitcoinStartPoint? Bitcoin)
{
    /// <summary>
    /// The longest text that is looked at; no address or txid on any known chain is longer (Zcash's unified addresses
    /// with three receivers are the longest, a little over 210 characters).
    /// </summary>
    public const int MaximumLength = 256;

    /// <summary>What Zcash's Sapling addresses begin with (Bech32).</summary>
    private const string SaplingPrefix = "zs1";

    /// <summary>A Sapling address's length: the prefix, 43 bytes in Bech32 and the checksum.</summary>
    private const int SaplingLength = 78;

    /// <summary>What Zcash's unified addresses begin with (Bech32m, ZIP 316).</summary>
    private const string UnifiedPrefix = "u1";

    /// <summary>The shortest unified address: one Orchard receiver, the padding and the checksum.</summary>
    private const int UnifiedShortest = 100;

    /// <summary>What Zcash's first shielded addresses (Sprout) begin with (Base58).</summary>
    private const string SproutPrefix = "zc";

    /// <summary>A Sprout address's length.</summary>
    private const int SproutLength = 95;

    /// <summary>Base58's characters, which a Sprout address is written in.</summary>
    private static readonly System.Buffers.SearchValues<char> Base58Characters =
        System.Buffers.SearchValues.Create("123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz");

    /// <summary>What an address in Litecoin's MWEB begins with.</summary>
    private const string MwebPrefix = "ltcmweb1";

    /// <summary>The shortest MWEB address: the prefix, two 33-byte keys in Bech32 and the checksum.</summary>
    private const int MwebShortest = 100;

    /// <summary>The characters of Bech32's data part.</summary>
    private static readonly System.Buffers.SearchValues<char> Bech32Characters =
        System.Buffers.SearchValues.Create("qpzry9x8gf2tvdw0s3jn54khce6mua7l");

    /// <summary>The number of hex digits in a transaction hash on every known chain.</summary>
    private const int TransactionDigits = 64;

    /// <summary>The hex digits in both cases.</summary>
    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>
    /// Gets whether Chainsaw can trace from this start point in the graph now: Bitcoin, Litecoin, Zcash's transparent
    /// part, Ethereum, TRON, BSC and Solana are built. An address in a shielded part (MWEB, Zcash's pools) is not: the
    /// trail ends there.
    /// </summary>
    public bool IsSupported =>
        !IsShielded && Chain is SharedKernel.Chain.Bitcoin or SharedKernel.Chain.Ethereum or SharedKernel.Chain.Tron
            or SharedKernel.Chain.Bsc or SharedKernel.Chain.Litecoin or SharedKernel.Chain.Zcash
            or SharedKernel.Chain.Solana;

    /// <summary>
    /// Gets whether Chainsaw has a source that can answer for this start point: every chain it can trace (not an
    /// address in a shielded part, which no source can).
    /// </summary>
    public bool CanFetch => IsSupported;

    /// <summary>
    /// Gets whether the value is an address in a shielded part of its chain (Litecoin's MWEB, <c>ltcmweb1…</c>, or
    /// Zcash's pools, <c>zs1…</c>, <c>u1…</c>, <c>zc…</c>), where amounts and owners do not show: the trail ends there,
    /// and nothing can be fetched.
    /// </summary>
    public bool IsShielded { get; init; }

    /// <summary>
    /// Gets the address on a UTXO chain (Bitcoin, Litecoin or Zcash), when the start point is one; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public BitcoinAddress? UtxoAddress =>
        Chain is { } chain && Kind == StartPointKind.Address && BitcoinAddress.TryParse(chain, Value, out var address)
            ? address
            : null;

    /// <summary>
    /// Gets the transaction on a UTXO chain (Bitcoin, Litecoin or Zcash), when the start point is one; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public Txid? UtxoTransaction =>
        Chain is { } chain && chain.Model() == LedgerModel.Utxo && Kind == StartPointKind.Transaction
            && Txid.TryParse(chain, Value, out var txid)
            ? txid
            : null;

    /// <summary>Gets whether the text was recognised on some chain.</summary>
    public bool IsRecognised => Kind != StartPointKind.Unknown;

    /// <summary>
    /// Gets the address on an account chain (Ethereum, BSC, TRON or Solana), when the start point is one; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public EthereumAddress? EthereumAccount =>
        Chain is { } chain && Kind == StartPointKind.Address && EthereumAddress.TryParse(chain, Value, out var address)
            ? address
            : null;

    /// <summary>
    /// Gets the transaction on an account chain (Ethereum, BSC, TRON or Solana), when the start point is one; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public EthereumTxHash? AccountTransaction =>
        Chain is { } chain && Kind == StartPointKind.Transaction && EthereumTxHash.TryParse(chain, Value, out var hash)
            ? hash
            : null;


    /// <summary>
    /// Recognises a text without being told its chain. A Bitcoin address or a bare 64-digit hash (a Bitcoin txid) is
    /// Bitcoin; a Litecoin address (<c>L…</c>, <c>M…</c>, <c>ltc1…</c>) is Litecoin; <c>0x</c> and 40 or 64 hex
    /// digits is Ethereum; a Base58Check address with TRON's version byte is TRON; plain Base58 of 32 bytes is a Solana
    /// address and of 64 bytes a Solana signature (no other chain's value decodes to those lengths). A TRON or
    /// Litecoin txid looks like a Bitcoin txid, and a BSC address or hash like Ethereum's, so they are only recognised
    /// with their chain (see the other overload, as in a <c>chainsaw:bsc/…</c> link).
    /// </summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <returns>What it is.</returns>
    public static ChainStartPoint Recognise(string? text)
    {
        var trimmed = Trim(text);
        if (trimmed.Length == 0 || trimmed.Length > MaximumLength)
        {
            return Unknown(trimmed);
        }

        return RecogniseBitcoin(trimmed) ?? RecogniseLitecoinAddress(trimmed) ?? RecogniseMweb(trimmed)
            ?? RecogniseZcashAddress(trimmed) ?? RecogniseZcashShielded(trimmed)
            ?? RecogniseEthereum(trimmed) ?? RecogniseTronAddress(trimmed) ?? RecogniseSolana(trimmed)
            ?? Unknown(trimmed);
    }

    /// <summary>Recognises a text on a chain that was named, as in a <c>chainsaw:tron/…</c> link.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <returns>What it is; unknown when it is not an address or a transaction on that chain.</returns>
    public static ChainStartPoint Recognise(Chain chain, string? text)
    {
        var trimmed = Trim(text);
        if (trimmed.Length == 0 || trimmed.Length > MaximumLength)
        {
            return Unknown(trimmed);
        }

        return chain switch
        {
            SharedKernel.Chain.Bitcoin => RecogniseBitcoin(trimmed) ?? Unknown(trimmed),
            SharedKernel.Chain.Ethereum => RecogniseEthereum(trimmed) ?? Unknown(trimmed),
            SharedKernel.Chain.Tron => RecogniseTronAddress(trimmed) ?? RecogniseTronTransaction(trimmed)
                ?? Unknown(trimmed),
            SharedKernel.Chain.Litecoin => RecogniseLitecoinAddress(trimmed) ?? RecogniseMweb(trimmed)
                ?? RecogniseBareTransaction(SharedKernel.Chain.Litecoin, trimmed) ?? Unknown(trimmed),
            SharedKernel.Chain.Bsc => RecogniseBsc(trimmed) ?? Unknown(trimmed),
            SharedKernel.Chain.Zcash => RecogniseZcashAddress(trimmed) ?? RecogniseZcashShielded(trimmed)
                ?? RecogniseBareTransaction(SharedKernel.Chain.Zcash, trimmed) ?? Unknown(trimmed),
            SharedKernel.Chain.Solana => RecogniseSolana(trimmed) ?? Unknown(trimmed),
            _ => Unknown(trimmed),
        };
    }

    /// <summary>Trims a text, treating a missing one as empty.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The trimmed text.</returns>
    private static string Trim(string? text) => text?.Trim() ?? string.Empty;

    /// <summary>Gives the result for a text that was not recognised.</summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result.</returns>
    private static ChainStartPoint Unknown(string text) => new(null, StartPointKind.Unknown, text, null);

    /// <summary>Recognises a Bitcoin address or txid with <see cref="BitcoinStartPoint.Recognise"/>.</summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseBitcoin(string text)
    {
        var bitcoin = BitcoinStartPoint.Recognise(text);
        return bitcoin.Kind switch
        {
            BitcoinStartPointKind.Address =>
                new(SharedKernel.Chain.Bitcoin, StartPointKind.Address, bitcoin.Address!.Value, bitcoin),
            BitcoinStartPointKind.Transaction =>
                new(SharedKernel.Chain.Bitcoin, StartPointKind.Transaction, bitcoin.Txid!.Value, bitcoin),
            _ => null,
        };
    }

    /// <summary>
    /// Recognises an Ethereum address or transaction hash: <c>0x</c> and 40 or 64 hex digits. An address in mixed case
    /// must carry the right EIP-55 checksum (<see cref="EthereumAddress"/>), since a wrong letter makes it another
    /// address; then the text is not recognised.
    /// </summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseEthereum(string text)
    {
        if (EthereumAddress.TryParse(text, out var address))
        {
            return new(SharedKernel.Chain.Ethereum, StartPointKind.Address, address.Value, null);
        }

        return EthereumTxHash.TryParse(text, out var hash)
            ? new(SharedKernel.Chain.Ethereum, StartPointKind.Transaction, hash.Value, null)
            : null;
    }

    /// <summary>
    /// Recognises a TRON address: Base58Check with its checksum, the version byte 0x41 and 20 bytes after it, or the same
    /// in hex (<c>41</c> and 40 hex digits); either is kept in Base58Check (<see cref="EthereumAddress.TryParseTron"/>).
    /// </summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseTronAddress(string text) =>
        EthereumAddress.TryParseTron(text, out var address, out _)
            ? new(SharedKernel.Chain.Tron, StartPointKind.Address, address.Value, null)
            : null;

    /// <summary>Recognises a TRON transaction id: 64 hex digits, as on Bitcoin.</summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseTronTransaction(string text) =>
        RecogniseBareTransaction(SharedKernel.Chain.Tron, text);

    /// <summary>Recognises a transaction id written as 64 hex digits on a chain that was named.</summary>
    /// <param name="chain">The chain: TRON or Litecoin.</param>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseBareTransaction(Chain chain, string text) =>
        text.Length == TransactionDigits && IsHex(text)
            ? new(chain, StartPointKind.Transaction, text.ToLowerInvariant(), null)
            : null;

    /// <summary>
    /// Recognises a Litecoin address: <c>L…</c>, <c>M…</c> or <c>ltc1…</c>, and with its chain named also the older
    /// <c>3…</c> P2SH form, kept as its <c>M…</c> form.
    /// </summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseLitecoinAddress(string text) =>
        BitcoinAddress.TryParse(SharedKernel.Chain.Litecoin, text, out var address)
            ? new(SharedKernel.Chain.Litecoin, StartPointKind.Address, address.Value, null)
            : null;

    /// <summary>
    /// Recognises an address in Zcash's shielded pools: Sapling's (<c>zs1…</c>), a unified address (<c>u1…</c>) or
    /// Sprout's (<c>zc…</c>). As for MWEB, only its form is checked (prefix, characters, one letter case, length), not
    /// its checksum, since nothing can be looked up there; it is a start point on Zcash where the trail ends.
    /// </summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseZcashShielded(string text)
    {
        var lower = text.ToLowerInvariant();
        var isBech32 = (lower == text || text.ToUpperInvariant() == text)
            && ((lower.Length == SaplingLength && lower.StartsWith(SaplingPrefix, StringComparison.Ordinal)
                    && !lower.AsSpan(SaplingPrefix.Length).ContainsAnyExcept(Bech32Characters))
                || (lower.Length is >= UnifiedShortest and <= MaximumLength
                    && lower.StartsWith(UnifiedPrefix, StringComparison.Ordinal)
                    && !lower.AsSpan(UnifiedPrefix.Length).ContainsAnyExcept(Bech32Characters)));
        var isSprout = text.Length == SproutLength && text.StartsWith(SproutPrefix, StringComparison.Ordinal)
            && !text.AsSpan().ContainsAnyExcept(Base58Characters);
        if (!isBech32 && !isSprout)
        {
            return null;
        }

        return new(SharedKernel.Chain.Zcash, StartPointKind.Address, isSprout ? text : lower, null) { IsShielded = true };
    }

    /// <summary>Recognises a transparent Zcash address (<c>t1…</c>, <c>t3…</c>), whose checksum must hold.</summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseZcashAddress(string text) =>
        BitcoinAddress.TryParse(SharedKernel.Chain.Zcash, text, out var address)
            ? new(SharedKernel.Chain.Zcash, StartPointKind.Address, address.Value, null)
            : null;

    /// <summary>
    /// Recognises an address in Litecoin's MWEB (<c>ltcmweb1…</c>, Bech32 with two 33-byte keys). Only its form is
    /// checked (the prefix, the Bech32 characters, one letter case and the length), not its checksum or keys, since
    /// nothing can be looked up there anyway; a text of that form with a wrong character is still taken as one. It is
    /// a start point on Litecoin where the trail ends.
    /// </summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseMweb(string text)
    {
        var lower = text.ToLowerInvariant();
        var isForm = lower.Length is >= MwebShortest and <= MaximumLength
            && lower.StartsWith(MwebPrefix, StringComparison.Ordinal)
            && (lower == text || text.ToUpperInvariant() == text)
            && !lower.AsSpan(MwebPrefix.Length).ContainsAnyExcept(Bech32Characters);
        return isForm
            ? new(SharedKernel.Chain.Litecoin, StartPointKind.Address, lower, null) { IsShielded = true }
            : null;
    }

    /// <summary>
    /// Recognises a BSC address or transaction hash: written as Ethereum's, <c>0x</c> and 40 or 64 hex digits, with
    /// the EIP-55 checksum checked in mixed case.
    /// </summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseBsc(string text)
    {
        if (EthereumAddress.TryParse(SharedKernel.Chain.Bsc, text, out var address))
        {
            return new(SharedKernel.Chain.Bsc, StartPointKind.Address, address.Value, null);
        }

        return EthereumTxHash.TryParse(SharedKernel.Chain.Bsc, text, out var hash)
            ? new(SharedKernel.Chain.Bsc, StartPointKind.Transaction, hash.Value, null)
            : null;
    }


    /// <summary>
    /// Recognises a Solana address (a public key of 32 bytes in Base58) or transaction (its first signature, 64 bytes
    /// in Base58). Neither has a checksum, so a mistyped value of the right length is taken as another one.
    /// </summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseSolana(string text)
    {
        if (EthereumAddress.TryParseSolana(text, out var address, out _))
        {
            return new(SharedKernel.Chain.Solana, StartPointKind.Address, address.Value, null);
        }

        return EthereumTxHash.TryParse(SharedKernel.Chain.Solana, text, out var hash)
            ? new(SharedKernel.Chain.Solana, StartPointKind.Transaction, hash.Value, null)
            : null;
    }

    /// <summary>Tells whether every character is a hex digit.</summary>
    /// <param name="text">The characters.</param>
    /// <returns><see langword="true"/> when they all are and there is at least one.</returns>
    private static bool IsHex(ReadOnlySpan<char> text) => !text.IsEmpty && !text.ContainsAnyExcept(HexDigits);
}
