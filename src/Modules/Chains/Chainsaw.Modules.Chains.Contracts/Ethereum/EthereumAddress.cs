using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// An address in the account model: 20 bytes, on Ethereum, on BSC (BNB Smart Chain, which runs Ethereum's virtual
/// machine and writes its addresses as Ethereum does) or on TRON, whose virtual machine is Ethereum's and whose
/// addresses are the same 20 bytes behind the version byte <c>0x41</c>; or a public key of 32 bytes on Solana. The
/// type keeps Ethereum's name from when that was the only account chain (BACKLOG: renamed when no other stage touches
/// Chains); <see cref="Chain"/> says which chain it is on, so that the same bytes on two chains are never taken for one
/// actor.
/// </summary>
/// <remarks>
/// <para>
/// On Ethereum it is <c>0x</c> and 20 bytes in hex, read by <c>TryParse</c>, which checks the EIP-55 checksum when the
/// text has one: an address written in mixed case carries its checksum in the letters' case, and a single wrong letter
/// makes it another address that belongs to nobody, so a mixed-case address whose case does not match is refused. All
/// lower or all upper case has no checksum and is taken as written.
/// </para>
/// <para>
/// On TRON it is written in Base58Check (<c>T…</c>, 34 characters), whose last four bytes are a checksum (a double
/// SHA-256), so that a wrong character is refused; or in hex as <c>41</c> and the 20 bytes, as TRON's own API writes it.
/// <c>TryParseTron</c> reads both and keeps the Base58Check form, so that one address is never two.
/// </para>
/// <para>
/// On Solana it is a public key of 32 bytes in plain Base58 (32 to 44 characters), read by <c>TryParseSolana</c>.
/// It carries no checksum: a wrong character gives another valid address that belongs to nobody, and the user is told.
/// </para>
/// </remarks>
public sealed record EthereumAddress
{
    /// <summary>The number of hex digits after <c>0x</c>.</summary>
    public const int Digits = 40;

    /// <summary>The version byte before a TRON address's 20 bytes (the letter <c>T</c> in Base58Check).</summary>
    public const byte TronVersion = 0x41;

    /// <summary>The number of hex digits in a 32-byte word of the virtual machine (an event's topic).</summary>
    private const int WordDigits = 64;

    /// <summary>The hex digits in both cases.</summary>
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>Creates an address on Ethereum or BSC from its checked lower-case form.</summary>
    /// <param name="chain">The chain: Ethereum or BSC, which write their addresses alike.</param>
    /// <param name="lower">The address in lower case with <c>0x</c>.</param>
    private EthereumAddress(Chain chain, string lower)
    {
        (Chain, Value, Hex) = (chain, lower, lower);
        Checksummed = Checksum(lower);
    }

    /// <summary>Creates a TRON or Solana address from its checked forms.</summary>
    /// <param name="chain">The chain: TRON or Solana, which write their addresses in Base58.</param>
    /// <param name="base58">The Base58 form: <c>T…</c> with its checksum on TRON, the 32 bytes on Solana.</param>
    /// <param name="hex">The bytes as <c>0x</c> and hex digits in lower case: 20 on TRON, 32 on Solana.</param>
    private EthereumAddress(Chain chain, string base58, string hex) =>
        (Chain, Value, Checksummed, Hex) = (chain, base58, base58, hex);

    /// <summary>The number of bytes in a Solana address (a public key).</summary>
    public const int SolanaBytes = 32;

    /// <summary>
    /// Gets the chain the address is on: <see cref="Chain.Ethereum"/>, <see cref="Chain.Bsc"/>,
    /// <see cref="Chain.Tron"/> or <see cref="Chain.Solana"/>.
    /// </summary>
    public Chain Chain { get; }

    /// <summary>
    /// Gets the address as the chain compares it: on Ethereum and BSC in lower case (<c>0x5aae…</c>), on TRON in
    /// Base58Check (<c>T…</c>), on Solana in Base58 as written (case matters).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the address as it is shown: on Ethereum and BSC with its EIP-55 checksum in the letters' case, on TRON in
    /// Base58Check.
    /// </summary>
    public string Checksummed { get; }

    /// <summary>
    /// Gets the 20 bytes as <c>0x</c> and 40 hex digits in lower case: Ethereum's address, and on TRON the address its
    /// virtual machine and event logs use (without the version byte). On Solana the public key's 32 bytes as <c>0x</c>
    /// and 64 hex digits.
    /// </summary>
    public string Hex { get; }

    /// <summary>
    /// Gets the address in TRON's own hex form, <c>41</c> and 40 hex digits in lower case, as TRON's API writes it;
    /// <see langword="null"/> on Ethereum.
    /// </summary>
    public string? TronHex => Chain == Chain.Tron ? "41" + Hex[2..] : null;

    /// <summary>Gets the address as an address on any chain.</summary>
    public ChainAddress ToChainAddress() => new(Chain, Value);

    /// <summary>Reads an Ethereum address, checking the EIP-55 checksum when the letters are in mixed case.</summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is an address.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address) =>
        TryParse(text, out address, out _);

    /// <summary>Reads an Ethereum address, and says why it is not one.</summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <param name="problem">Why it is not an address, in Swedish, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is an address.</returns>
    public static bool TryParse(
        [NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address, out string? problem)
    {
        address = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length != Digits + 2 || !trimmed.StartsWith("0x", StringComparison.Ordinal)
            || trimmed.AsSpan(2).ContainsAnyExcept(HexDigits))
        {
            problem = "En Ethereum-adress är 0x och 40 hexadecimala tecken.";
            return false;
        }

        var digits = trimmed[2..];
        var lower = "0x" + digits.ToLowerInvariant();
        var mixed = digits.Any(char.IsLower) && digits.Any(char.IsUpper);
        if (mixed && !string.Equals(Checksum(lower), trimmed, StringComparison.Ordinal))
        {
            problem = "Adressens kontrollsumma (EIP-55, stora och små bokstäver) stämmer inte. "
                + "Ett tecken kan vara fel.";
            return false;
        }

        problem = null;
        address = new EthereumAddress(Chain.Ethereum, lower);
        return true;
    }

    /// <summary>
    /// Gives the same 20 bytes on another chain that writes its addresses as Ethereum does (BSC), as a source for that
    /// chain reads them with Ethereum's form; the chain makes it another actor than the same bytes on Ethereum.
    /// </summary>
    /// <param name="chain">The chain: Ethereum or BSC.</param>
    /// <returns>The address on that chain.</returns>
    /// <exception cref="ArgumentException">The address is on TRON, or the chain does not write addresses as Ethereum
    /// does.</exception>
    public EthereumAddress OnChain(Chain chain)
    {
        if (!chain.IsEvm() || !Chain.IsEvm())
        {
            throw new ArgumentException("Only an address on Ethereum or BSC moves between them.", nameof(chain));
        }

        return chain == Chain ? this : new EthereumAddress(chain, Value);
    }

    /// <summary>Reads an Ethereum address that must be one, as a source's answer gives it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not an address.</exception>
    public static EthereumAddress Parse(string text) =>
        TryParse(text, out var address, out var problem) ? address : throw new FormatException(problem);

    /// <summary>Reads an address on an account chain.</summary>
    /// <param name="chain">The chain: Ethereum, BSC, TRON or Solana.</param>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is an address on the chain.</returns>
    public static bool TryParse(
        Chain chain, [NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address)
    {
        address = null;
        return chain switch
        {
            Chain.Ethereum => TryParse(text, out address),
            Chain.Bsc => TryParseOn(Chain.Bsc, text, out address),
            Chain.Tron => TryParseTron(text, out address, out _),
            Chain.Solana => TryParseSolana(text, out address, out _),
            _ => false,
        };
    }

    /// <summary>Reads an address in Ethereum's form on a chain that writes its addresses so (BSC).</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is an address in Ethereum's form.</returns>
    private static bool TryParseOn(
        Chain chain, [NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address)
    {
        address = TryParse(text, out var parsed) ? parsed.OnChain(chain) : null;
        return address is not null;
    }

    /// <summary>Reads an address on an account chain that must be one, as the case or a source's answer gives it.</summary>
    /// <param name="chain">The chain: Ethereum, BSC, TRON or Solana.</param>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not an address on the chain.</exception>
    public static EthereumAddress Parse(Chain chain, string text) =>
        TryParse(chain, text, out var address) ? address : throw new FormatException($"Not an address on {chain}.");

    /// <summary>
    /// Reads a TRON address: Base58Check (<c>T…</c>) with its checksum and the version byte <c>0x41</c>, or hex as
    /// <c>41</c> (or <c>0x41</c>) and 40 hex digits, as TRON's API writes it. Both give the same address, kept in
    /// Base58Check.
    /// </summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <param name="problem">Why it is not a TRON address, in Swedish, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is a TRON address.</returns>
    public static bool TryParseTron(
        [NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address, out string? problem)
    {
        address = null;
        var trimmed = text?.Trim() ?? string.Empty;
        var hex = trimmed.StartsWith("0x41", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed;
        if (hex.Length == Digits + 2 && hex.StartsWith("41", StringComparison.Ordinal)
            && !hex.AsSpan().ContainsAnyExcept(HexDigits))
        {
            problem = null;
            address = FromTronBytes(Convert.FromHexString(hex));
            return true;
        }

        if (!trimmed.StartsWith('T') || !Base58Check.TryDecode(trimmed, out var data))
        {
            problem = "En TRON-adress börjar med T och har en kontrollsumma (Base58Check), eller är 41 och 40 "
                + "hexadecimala tecken. Ett tecken kan vara fel.";
            return false;
        }

        if (data.Length != Digits / 2 + 1 || data[0] != TronVersion)
        {
            problem = "Texten har rätt kontrollsumma men är ingen TRON-adress (versionen 0x41 och 20 byte).";
            return false;
        }

        problem = null;
        address = FromTronBytes(data);
        return true;
    }

    /// <summary>
    /// Reads a Solana address: a public key of 32 bytes in plain Base58, in its canonical form. There is no checksum,
    /// so a mistyped address of the right length is read as another address; the problem says so when the length or
    /// a character is wrong.
    /// </summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <param name="problem">Why it is not a Solana address, in Swedish, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is a Solana address.</returns>
    public static bool TryParseSolana(
        [NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address, out string? problem)
    {
        address = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (!Base58.TryDecode(trimmed, SolanaBytes, out var key))
        {
            problem = "En Solana-adress är 32 byte i Base58 (32 till 44 tecken, utan 0, O, I och l). Den har ingen "
                + "kontrollsumma, så ett fel tecken kan ge en annan giltig adress.";
            return false;
        }

        problem = null;
        address = new EthereumAddress(Chain.Solana, trimmed, "0x" + Convert.ToHexStringLower(key));
        return true;
    }

    /// <summary>
    /// Reads the TRON address in 20 bytes as TRON's virtual machine and event logs write them: 40 hex digits with or
    /// without <c>0x</c>, or a 32-byte word (an event's topic) whose first 12 bytes are zero.
    /// </summary>
    /// <param name="hex">The hex.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the hex holds an address.</returns>
    public static bool TryFromTronWord(
        [NotNullWhen(true)] string? hex, [NotNullWhen(true)] out EthereumAddress? address)
    {
        address = null;
        var digits = hex?.Trim() ?? string.Empty;
        digits = digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? digits[2..] : digits;
        if (digits.Length == WordDigits)
        {
            if (digits.AsSpan(0, WordDigits - Digits).ContainsAnyExcept('0'))
            {
                return false;
            }

            digits = digits[(WordDigits - Digits)..];
        }

        if (digits.Length != Digits || digits.AsSpan().ContainsAnyExcept(HexDigits))
        {
            return false;
        }

        address = FromTronBytes([TronVersion, .. Convert.FromHexString(digits)]);
        return true;
    }

    /// <summary>
    /// Writes the address as it is shown: EIP-55 on Ethereum and BSC, Base58Check on TRON, Base58 on Solana.
    /// </summary>
    /// <returns>The address.</returns>
    public override string ToString() => Checksummed;

    /// <summary>Makes a TRON address of its version byte and 20 bytes.</summary>
    /// <param name="data">The 21 bytes.</param>
    /// <returns>The address.</returns>
    private static EthereumAddress FromTronBytes(byte[] data) =>
        new(Chain.Tron, Base58Check.Encode(data), "0x" + Convert.ToHexStringLower(data.AsSpan(1)));

    /// <summary>
    /// Gives the EIP-55 form: the Keccak-256 of the lower-case hex (without <c>0x</c>) decides each letter's case:
    /// upper where the hash's matching nibble is 8 or more.
    /// </summary>
    /// <param name="lower">The address in lower case with <c>0x</c>.</param>
    /// <returns>The checksummed address.</returns>
    private static string Checksum(string lower)
    {
        var hash = Keccak256.Hash(lower[2..]);
        return string.Create(lower.Length, (lower, hash), static (span, state) =>
        {
            var (text, digest) = state;
            span[0] = '0';
            span[1] = 'x';
            for (var i = 0; i < Digits; i++)
            {
                var nibble = (i % 2 == 0 ? digest[i / 2] >> 4 : digest[i / 2]) & 0x0F;
                var c = text[i + 2];
                span[i + 2] = nibble >= 8 ? char.ToUpperInvariant(c) : c;
            }
        });
    }
}
