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
/// and <c>chainsaw:</c> links. Only Bitcoin can be traced yet; an Ethereum or TRON value is recognised so that the
/// user is told honestly that its chain is not built, rather than that the text is wrong.
/// </summary>
/// <param name="Chain">The chain, or <see langword="null"/> when the text is not recognised.</param>
/// <param name="Kind">Whether it is an address or a transaction.</param>
/// <param name="Value">
/// The value in its canonical form (a txid in lower case), or the trimmed text when unknown.
/// </param>
/// <param name="Bitcoin">The Bitcoin start point, when the chain is Bitcoin.</param>
public sealed record ChainStartPoint(Chain? Chain, StartPointKind Kind, string Value, BitcoinStartPoint? Bitcoin)
{
    /// <summary>The longest text that is looked at; no address or txid on any known chain is longer.</summary>
    public const int MaximumLength = 128;

    /// <summary>The version byte of a TRON address in Base58Check.</summary>
    private const byte TronVersion = 0x41;

    /// <summary>The length of a TRON address's version byte and payload.</summary>
    private const int TronAddressBytes = 21;

    /// <summary>The number of hex digits in an Ethereum address, after <c>0x</c>.</summary>
    private const int EthereumAddressDigits = 40;

    /// <summary>The number of hex digits in a transaction hash on every known chain.</summary>
    private const int TransactionDigits = 64;

    /// <summary>The hex digits in both cases.</summary>
    private static readonly System.Buffers.SearchValues<char> HexDigits =
        System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>Gets whether Chainsaw can trace from this start point now: only Bitcoin is built.</summary>
    public bool IsSupported => Chain == SharedKernel.Chain.Bitcoin;

    /// <summary>Gets whether the text was recognised on some chain.</summary>
    public bool IsRecognised => Kind != StartPointKind.Unknown;

    /// <summary>
    /// Recognises a text without being told its chain. A Bitcoin address or a bare 64-digit hash (a Bitcoin txid) is
    /// Bitcoin; <c>0x</c> and 40 or 64 hex digits is Ethereum; a Base58Check address with TRON's version byte is
    /// TRON. A TRON txid looks like a Bitcoin txid, so it is only recognised with its chain (see the other overload).
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

        return RecogniseBitcoin(trimmed) ?? RecogniseEthereum(trimmed) ?? RecogniseTronAddress(trimmed)
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

    /// <summary>Recognises an Ethereum address or transaction hash: <c>0x</c> and 40 or 64 hex digits.</summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    /// <remarks>The EIP-55 checksum in an address's letter case is not checked; Ethereum is not built yet.</remarks>
    private static ChainStartPoint? RecogniseEthereum(string text)
    {
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !IsHex(text.AsSpan(2)))
        {
            return null;
        }

        var lower = "0x" + text[2..].ToLowerInvariant();
        return (text.Length - 2) switch
        {
            EthereumAddressDigits => new(SharedKernel.Chain.Ethereum, StartPointKind.Address, lower, null),
            TransactionDigits => new(SharedKernel.Chain.Ethereum, StartPointKind.Transaction, lower, null),
            _ => null,
        };
    }

    /// <summary>Recognises a TRON address: Base58Check with the version byte 0x41 and 20 bytes after it.</summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseTronAddress(string text) =>
        text.StartsWith('T') && Base58Check.TryDecode(text, out var data)
            && data.Length == TronAddressBytes && data[0] == TronVersion
            ? new(SharedKernel.Chain.Tron, StartPointKind.Address, text, null)
            : null;

    /// <summary>Recognises a TRON transaction id: 64 hex digits, as on Bitcoin.</summary>
    /// <param name="text">The trimmed text.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static ChainStartPoint? RecogniseTronTransaction(string text) =>
        text.Length == TransactionDigits && IsHex(text)
            ? new(SharedKernel.Chain.Tron, StartPointKind.Transaction, text.ToLowerInvariant(), null)
            : null;

    /// <summary>Tells whether every character is a hex digit.</summary>
    /// <param name="text">The characters.</param>
    /// <returns><see langword="true"/> when they all are and there is at least one.</returns>
    private static bool IsHex(ReadOnlySpan<char> text) => !text.IsEmpty && !text.ContainsAnyExcept(HexDigits);
}
