using System.Diagnostics.CodeAnalysis;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// An address in the UTXO model that has been checked: its checksum holds and its kind and network are known. On
/// Bitcoin, or on Litecoin, which writes Bitcoin's scripts with its own version bytes (<c>L</c>, <c>M</c>) and prefix
/// (<c>ltc1</c>); <see cref="Chain"/> says which. An instance can only be made by parsing a text or writing a hash
/// or a witness program, so an address in the model is always valid. The type keeps Bitcoin's name from when that
/// was the only UTXO chain.
/// </summary>
/// <remarks>
/// SegWit addresses are kept in lower case, the form the chain's tools show; Base58 addresses are case-sensitive and
/// kept as written. Litecoin's older P2SH form (<c>3…</c>, Bitcoin's version byte) names the same script as its
/// <c>M…</c> form; read as a Litecoin address it is kept as <c>M…</c>, so that one address is never two. Zcash's
/// transparent addresses (<c>t1…</c>, <c>t3…</c>) are Bitcoin's P2PKH and P2SH with two version bytes; Zcash has
/// no SegWit, and its shielded addresses are not addresses in this model.
/// </remarks>
public sealed record BitcoinAddress
{
    /// <summary>The version byte of a P2PKH address on the main network.</summary>
    private const byte MainnetKeyHash = 0x00;

    /// <summary>The version byte of a P2SH address on the main network.</summary>
    private const byte MainnetScriptHash = 0x05;

    /// <summary>The version byte of a P2PKH address on the test networks.</summary>
    private const byte TestnetKeyHash = 0x6f;

    /// <summary>The version byte of a P2SH address on the test networks.</summary>
    private const byte TestnetScriptHash = 0xc4;

    /// <summary>The version byte of a P2PKH address on Litecoin (<c>L…</c>).</summary>
    private const byte LitecoinKeyHash = 0x30;

    /// <summary>The version byte of a P2SH address on Litecoin (<c>M…</c>).</summary>
    private const byte LitecoinScriptHash = 0x32;

    /// <summary>The two version bytes of a transparent P2PKH address on Zcash (<c>t1…</c>).</summary>
    private static readonly byte[] ZcashKeyHash = [0x1c, 0xb8];

    /// <summary>The two version bytes of a transparent P2SH address on Zcash (<c>t3…</c>).</summary>
    private static readonly byte[] ZcashScriptHash = [0x1c, 0xbd];

    /// <summary>The length of a hash in a Zcash transparent address: two version bytes and 20 bytes.</summary>
    private const int ZcashPayloadLength = 22;

    /// <summary>The SegWit prefix on Litecoin.</summary>
    private const string LitecoinPrefix = "ltc";

    /// <summary>The witness version of Litecoin's extension block's own address (the HogAddr, MWEB).</summary>
    private const int HogAddressVersion = 8;

    /// <summary>The witness version of a peg-in to Litecoin's extension block (MWEB).</summary>
    private const int PegInVersion = 9;

    /// <summary>The length of a hash in a Base58 address: version byte and 20 bytes.</summary>
    private const int Base58PayloadLength = 21;

    /// <summary>The SegWit prefix on the main network.</summary>
    private const string MainnetPrefix = "bc";

    /// <summary>The SegWit prefix on the test networks.</summary>
    private const string TestnetPrefix = "tb";

    /// <summary>Creates a checked address.</summary>
    /// <param name="value">The address in its canonical form.</param>
    /// <param name="type">The kind of script it pays to.</param>
    /// <param name="network">The network.</param>
    private BitcoinAddress(string value, BitcoinScriptType type, BitcoinNetwork network)
    {
        Value = value;
        Type = type;
        Network = network;
    }

    /// <summary>Gets the chain: Bitcoin, or Litecoin or Zcash for an address on their networks.</summary>
    public Chain Chain => Network.ChainOf();

    /// <summary>Gets the address in its canonical form.</summary>
    public string Value { get; }

    /// <summary>Gives the address with its chain, as the graph and the labels name an actor on any chain.</summary>
    /// <returns>The address on its chain.</returns>
    public ChainAddress ToChainAddress() => new(Chain, Value);

    /// <summary>Gets the kind of script the address pays to.</summary>
    public BitcoinScriptType Type { get; }

    /// <summary>Gets the network the address belongs to.</summary>
    public BitcoinNetwork Network { get; }

    /// <summary>Checks a text and makes an address of it.</summary>
    /// <param name="text">The text; surrounding white space is not allowed.</param>
    /// <param name="address">The address, when the text is valid.</param>
    /// <returns>
    /// <see langword="true"/> when the text is a Bitcoin address (main or test network): Base58Check with a known
    /// version byte, or SegWit with the right checksum (Bech32 for version 0, Bech32m from version 1). A Litecoin
    /// address is not one; <see cref="TryParse(Chain, string?, out BitcoinAddress?)"/> reads it.
    /// </returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BitcoinAddress? address)
    {
        address = TryParseAny(text) is { Chain: Chain.Bitcoin } bitcoin ? bitcoin : null;
        return address is not null;
    }

    /// <summary>
    /// Checks a text as an address on whichever UTXO chain writes it so: Bitcoin's (main and test networks),
    /// Litecoin's own forms (<c>L…</c>, <c>M…</c>, <c>ltc1…</c>; the older <c>3…</c> is read as Bitcoin's) or Zcash's
    /// transparent ones (<c>t1…</c>, <c>t3…</c>). No two chains write an address alike, so a stored address that
    /// names no chain is read on its own; the address carries the chain.
    /// </summary>
    /// <param name="text">The text; surrounding white space is not allowed.</param>
    /// <param name="address">The address, when the text is one.</param>
    /// <returns><see langword="true"/> when the text is an address on a UTXO chain Chainsaw knows.</returns>
    public static bool TryParseOnAnyChain(string? text, [NotNullWhen(true)] out BitcoinAddress? address)
    {
        address = TryParseAny(text);
        return address is not null;
    }

    /// <summary>Reads an address on any UTXO network Chainsaw knows, Bitcoin's, Litecoin's or Zcash's.</summary>
    /// <param name="text">The text; surrounding white space is not allowed.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    private static BitcoinAddress? TryParseAny(string? text) =>
        string.IsNullOrEmpty(text) ? null : TryParseSegwit(text) ?? TryParseBase58(text);

    /// <summary>
    /// Checks a text as an address on one UTXO chain: on Litecoin, Litecoin's addresses and the older <c>3…</c> P2SH
    /// form (kept as its <c>M…</c> form); on Zcash, its transparent addresses; on Bitcoin, Bitcoin's (main and test
    /// networks).
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text; surrounding white space is not allowed.</param>
    /// <param name="address">The address, when the text is one on the chain.</param>
    /// <returns><see langword="true"/> when the text is an address on the chain.</returns>
    public static bool TryParse(Chain chain, string? text, [NotNullWhen(true)] out BitcoinAddress? address)
    {
        address = null;
        if (TryParseAny(text) is not { } read)
        {
            return false;
        }

        if (chain == Chain.Litecoin
            && read is { Network: BitcoinNetwork.Mainnet, Type: BitcoinScriptType.PayToScriptHash }
            && Base58Check.TryDecode(read.Value, out var data))
        {
            read = FromHash(BitcoinScriptType.PayToScriptHash, data.AsSpan(1), BitcoinNetwork.Litecoin);
        }

        address = read.Chain == chain ? read : null;
        return address is not null;
    }

    /// <summary>Makes an address of a text that must be valid.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not a valid Bitcoin address.</exception>
    public static BitcoinAddress Parse(string text) =>
        TryParse(text, out var address) ? address : throw new FormatException("Not a valid Bitcoin address.");

    /// <summary>Makes an address on a UTXO chain of a text that must be one.</summary>
    /// <param name="chain">The chain: Bitcoin, Litecoin or Zcash.</param>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not a valid address on the chain.</exception>
    public static BitcoinAddress Parse(Chain chain, string text) =>
        TryParse(chain, text, out var address)
            ? address
            : throw new FormatException($"Not a valid address on {chain}.");

    /// <summary>Writes the address of a key hash or a script hash in Base58Check.</summary>
    /// <param name="type"><see cref="BitcoinScriptType.PayToPublicKeyHash"/> or <see
    /// cref="BitcoinScriptType.PayToScriptHash"/>.</param>
    /// <param name="hash">The 20-byte hash.</param>
    /// <param name="network">The network.</param>
    /// <returns>The address.</returns>
    /// <exception cref="ArgumentException">The type is not a Base58 type or the hash is not 20 bytes.</exception>
    public static BitcoinAddress FromHash(BitcoinScriptType type, ReadOnlySpan<byte> hash, BitcoinNetwork network)
    {
        if (hash.Length != Base58PayloadLength - 1)
        {
            throw new ArgumentException("The hash must be 20 bytes.", nameof(hash));
        }

        if (network == BitcoinNetwork.Zcash)
        {
            var versions = type switch
            {
                BitcoinScriptType.PayToPublicKeyHash => ZcashKeyHash,
                BitcoinScriptType.PayToScriptHash => ZcashScriptHash,
                _ => throw new ArgumentException("Only P2PKH and P2SH are written in Base58.", nameof(type)),
            };
            return new BitcoinAddress(Base58Check.Encode([.. versions, .. hash]), type, network);
        }

        var version = (type, network) switch
        {
            (BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Mainnet) => MainnetKeyHash,
            (BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Mainnet) => MainnetScriptHash,
            (BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Testnet) => TestnetKeyHash,
            (BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Testnet) => TestnetScriptHash,
            (BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Litecoin) => LitecoinKeyHash,
            (BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Litecoin) => LitecoinScriptHash,
            _ => throw new ArgumentException("Only P2PKH and P2SH are written in Base58.", nameof(type)),
        };
        return new BitcoinAddress(Base58Check.Encode([version, .. hash]), type, network);
    }

    /// <summary>Writes the address of a witness program in Bech32 or Bech32m.</summary>
    /// <param name="witnessVersion">The witness version, 0–16.</param>
    /// <param name="program">The witness program.</param>
    /// <param name="network">The network.</param>
    /// <returns>The address.</returns>
    /// <exception cref="ArgumentException">The version and program do not make a valid SegWit address.</exception>
    public static BitcoinAddress FromWitness(int witnessVersion, ReadOnlySpan<byte> program, BitcoinNetwork network)
    {
        if (network == BitcoinNetwork.Zcash)
        {
            throw new ArgumentException("Zcash has no witness programs.", nameof(network));
        }

        var text = Bech32.EncodeSegwit(PrefixOf(network), witnessVersion, program);
        return new BitcoinAddress(text, WitnessType(witnessVersion, program.Length, network), network);
    }

    /// <summary>Gives the address, as written.</summary>
    /// <returns><see cref="Value"/>.</returns>
    public override string ToString() => Value;

    /// <summary>Reads a SegWit address.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address, or <see langword="null"/> when the text is not a valid SegWit address.</returns>
    private static BitcoinAddress? TryParseSegwit(string text)
    {
        if (!Bech32.TryDecodeSegwit(text, out var prefix, out var version, out var program))
        {
            return null;
        }

        BitcoinNetwork? network = prefix switch
        {
            MainnetPrefix => BitcoinNetwork.Mainnet,
            TestnetPrefix => BitcoinNetwork.Testnet,
            LitecoinPrefix => BitcoinNetwork.Litecoin,
            _ => null,
        };
        return network is { } known
            ? new BitcoinAddress(text.ToLowerInvariant(), WitnessType(version, program.Length, known), known)
            : null;
    }

    /// <summary>Reads a Base58Check address.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address, or <see langword="null"/> when the text is not a valid Base58 address.</returns>
    private static BitcoinAddress? TryParseBase58(string text)
    {
        if (!Base58Check.TryDecode(text, out var data))
        {
            return null;
        }

        if (data.Length == ZcashPayloadLength)
        {
            return ZcashType(data) is { } zcash ? new BitcoinAddress(text, zcash, BitcoinNetwork.Zcash) : null;
        }

        if (data.Length != Base58PayloadLength)
        {
            return null;
        }

        return data[0] switch
        {
            MainnetKeyHash => new BitcoinAddress(text, BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Mainnet),
            MainnetScriptHash => new BitcoinAddress(text, BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Mainnet),
            TestnetKeyHash => new BitcoinAddress(text, BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Testnet),
            TestnetScriptHash => new BitcoinAddress(text, BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Testnet),
            LitecoinKeyHash => new BitcoinAddress(
                text, BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Litecoin),
            LitecoinScriptHash => new BitcoinAddress(
                text, BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Litecoin),
            _ => null,
        };
    }

    /// <summary>Gives the script type a Zcash transparent address's two version bytes name.</summary>
    /// <param name="data">The decoded address: two version bytes and the hash.</param>
    /// <returns>P2PKH or P2SH, or <see langword="null"/> for other version bytes.</returns>
    private static BitcoinScriptType? ZcashType(ReadOnlySpan<byte> data)
    {
        if (data[..2].SequenceEqual(ZcashKeyHash))
        {
            return BitcoinScriptType.PayToPublicKeyHash;
        }

        return data[..2].SequenceEqual(ZcashScriptHash) ? BitcoinScriptType.PayToScriptHash : null;
    }

    /// <summary>
    /// Gives the script type of a witness program. On Litecoin, version 8 with 32 bytes is the extension block's own
    /// address (the HogAddr) and version 9 with 32 bytes a peg-in to MWEB, the shielded part.
    /// </summary>
    /// <param name="version">The witness version.</param>
    /// <param name="length">The program's length in bytes.</param>
    /// <param name="network">The network.</param>
    /// <returns>The script type.</returns>
    private static BitcoinScriptType WitnessType(int version, int length, BitcoinNetwork network) =>
        (version, length, network == BitcoinNetwork.Litecoin) switch
        {
            (0, 20, _) => BitcoinScriptType.PayToWitnessPublicKeyHash,
            (0, 32, _) => BitcoinScriptType.PayToWitnessScriptHash,
            (1, 32, _) => BitcoinScriptType.PayToTaproot,
            (HogAddressVersion, 32, true) => BitcoinScriptType.MwebHogAddress,
            (PegInVersion, 32, true) => BitcoinScriptType.MwebPegIn,
            _ => BitcoinScriptType.WitnessUnknown,
        };

    /// <summary>Gives the SegWit prefix of a network.</summary>
    /// <param name="network">The network.</param>
    /// <returns><c>bc</c>, <c>tb</c> or <c>ltc</c>.</returns>
    private static string PrefixOf(BitcoinNetwork network) => network switch
    {
        BitcoinNetwork.Mainnet => MainnetPrefix,
        BitcoinNetwork.Litecoin => LitecoinPrefix,
        _ => TestnetPrefix,
    };
}
