using System.Diagnostics.CodeAnalysis;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A Bitcoin address that has been checked: its checksum holds and its kind and network are known. An instance can
/// only be made by <see cref="TryParse"/> or <see cref="Parse"/>, so an address in the model is always valid.
/// </summary>
/// <remarks>
/// SegWit addresses are kept in lower case, the form the chain's tools show; Base58 addresses are case-sensitive and
/// kept as written.
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

    /// <summary>Gets the chain, which is always Bitcoin.</summary>
    public Chain Chain => Chain.Bitcoin;

    /// <summary>Gets the address in its canonical form.</summary>
    public string Value { get; }

    /// <summary>Gets the kind of script the address pays to.</summary>
    public BitcoinScriptType Type { get; }

    /// <summary>Gets the network the address belongs to.</summary>
    public BitcoinNetwork Network { get; }

    /// <summary>Checks a text and makes an address of it.</summary>
    /// <param name="text">The text; surrounding white space is not allowed.</param>
    /// <param name="address">The address, when the text is valid.</param>
    /// <returns>
    /// <see langword="true"/> when the text is a Base58Check address with a known version byte or a SegWit address
    /// with the right checksum (Bech32 for version 0, Bech32m from version 1).
    /// </returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BitcoinAddress? address)
    {
        address = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        address = TryParseSegwit(text) ?? TryParseBase58(text);
        return address is not null;
    }

    /// <summary>Makes an address of a text that must be valid.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not a valid Bitcoin address.</exception>
    public static BitcoinAddress Parse(string text) =>
        TryParse(text, out var address) ? address : throw new FormatException("Not a valid Bitcoin address.");

    /// <summary>Writes the address of a key hash or a script hash in Base58Check.</summary>
    /// <param name="type"><see cref="BitcoinScriptType.PayToPublicKeyHash"/> or <see cref="BitcoinScriptType.PayToScriptHash"/>.</param>
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

        var version = (type, network) switch
        {
            (BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Mainnet) => MainnetKeyHash,
            (BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Mainnet) => MainnetScriptHash,
            (BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Testnet) => TestnetKeyHash,
            (BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Testnet) => TestnetScriptHash,
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
        var text = Bech32.EncodeSegwit(PrefixOf(network), witnessVersion, program);
        return new BitcoinAddress(text, WitnessType(witnessVersion, program.Length), network);
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
            _ => null,
        };
        return network is { } known
            ? new BitcoinAddress(text.ToLowerInvariant(), WitnessType(version, program.Length), known)
            : null;
    }

    /// <summary>Reads a Base58Check address.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address, or <see langword="null"/> when the text is not a valid Base58 address.</returns>
    private static BitcoinAddress? TryParseBase58(string text)
    {
        if (!Base58Check.TryDecode(text, out var data) || data.Length != Base58PayloadLength)
        {
            return null;
        }

        return data[0] switch
        {
            MainnetKeyHash => new BitcoinAddress(text, BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Mainnet),
            MainnetScriptHash => new BitcoinAddress(text, BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Mainnet),
            TestnetKeyHash => new BitcoinAddress(text, BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Testnet),
            TestnetScriptHash => new BitcoinAddress(text, BitcoinScriptType.PayToScriptHash, BitcoinNetwork.Testnet),
            _ => null,
        };
    }

    /// <summary>Gives the script type of a witness program.</summary>
    /// <param name="version">The witness version.</param>
    /// <param name="length">The program's length in bytes.</param>
    /// <returns>The script type.</returns>
    private static BitcoinScriptType WitnessType(int version, int length) => (version, length) switch
    {
        (0, 20) => BitcoinScriptType.PayToWitnessPublicKeyHash,
        (0, 32) => BitcoinScriptType.PayToWitnessScriptHash,
        (1, 32) => BitcoinScriptType.PayToTaproot,
        _ => BitcoinScriptType.WitnessUnknown,
    };

    /// <summary>Gives the SegWit prefix of a network.</summary>
    /// <param name="network">The network.</param>
    /// <returns><c>bc</c> or <c>tb</c>.</returns>
    private static string PrefixOf(BitcoinNetwork network) =>
        network == BitcoinNetwork.Mainnet ? MainnetPrefix : TestnetPrefix;
}
