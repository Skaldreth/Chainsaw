namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Bech32 (BIP 173) and Bech32m (BIP 350), the encodings of SegWit addresses: a human-readable part such as
/// <c>bc</c>, the separator <c>1</c>, the witness version and program in groups of five bits, and a six-character
/// checksum. Version 0 uses Bech32; version 1 (Taproot) and later use Bech32m, which fixes a weakness in Bech32's
/// checksum.
/// </summary>
public static class Bech32
{
    /// <summary>The 32 characters, in the order of their values.</summary>
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    /// <summary>The longest address, in characters (BIP 173).</summary>
    private const int MaximumLength = 90;

    /// <summary>The length of the checksum in characters.</summary>
    private const int ChecksumLength = 6;

    /// <summary>What the checksum's polynomial gives for a valid Bech32 text.</summary>
    private const uint Bech32Constant = 1;

    /// <summary>Litecoin's SegWit prefix.</summary>
    private const string LitecoinPrefix = "ltc";

    /// <summary>The witness version of Litecoin's extension block's own address (MWEB's HogAddr).</summary>
    private const int MwebHogVersion = 8;

    /// <summary>The witness version of a peg-in to Litecoin's MWEB.</summary>
    private const int MwebPegInVersion = 9;

    /// <summary>What the checksum's polynomial gives for a valid Bech32m text.</summary>
    private const uint Bech32mConstant = 0x2bc830a3;

    /// <summary>The highest witness version.</summary>
    private const int MaximumWitnessVersion = 16;

    /// <summary>The shortest and longest witness program, in bytes (BIP 141).</summary>
    private const int MinimumProgramLength = 2, MaximumProgramLength = 40;

    /// <summary>The program lengths of version 0: a key hash (P2WPKH) or a script hash (P2WSH).</summary>
    private const int KeyHashLength = 20, ScriptHashLength = 32;

    /// <summary>The generator of the checksum's polynomial.</summary>
    private static readonly uint[] Generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];

    /// <summary>Encodes a SegWit address, choosing Bech32 or Bech32m by the version.</summary>
    /// <param name="humanReadablePart">The network's prefix in lower case, for example <c>bc</c>.</param>
    /// <param name="witnessVersion">The witness version, 0–16.</param>
    /// <param name="program">The witness program, 2–40 bytes.</param>
    /// <returns>The address in lower case.</returns>
    /// <exception cref="ArgumentException">The version or the program is out of range.</exception>
    public static string EncodeSegwit(string humanReadablePart, int witnessVersion, ReadOnlySpan<byte> program)
    {
        ArgumentException.ThrowIfNullOrEmpty(humanReadablePart);
        if (!IsValidProgram(witnessVersion, program.Length))
        {
            throw new ArgumentException("The witness version or program is out of range.", nameof(program));
        }

        var data = new List<byte> { (byte)witnessVersion };
        data.AddRange(ConvertBits(program, 8, 5, pad: true)!);
        var constant = ChecksumFor(humanReadablePart, witnessVersion);
        var values = ExpandPart(humanReadablePart).Concat(data).Concat(new byte[ChecksumLength]).ToArray();
        var polymod = Polymod(values) ^ constant;
        for (var i = 0; i < ChecksumLength; i++)
        {
            data.Add((byte)((polymod >> (5 * (5 - i))) & 31));
        }

        return humanReadablePart + "1" + new string([.. data.Select(value => Charset[value])]);
    }

    /// <summary>Decodes a SegWit address and checks its checksum and program.</summary>
    /// <param name="text">The address, all lower case or all upper case.</param>
    /// <param name="humanReadablePart">The prefix in lower case, when the address is valid.</param>
    /// <param name="witnessVersion">The witness version, when the address is valid.</param>
    /// <param name="program">The witness program, when the address is valid.</param>
    /// <returns>
    /// <see langword="true"/> when the text is a valid SegWit address: the right checksum for its version, a program
    /// of an allowed length, and for version 0 a key hash or a script hash.
    /// </returns>
    public static bool TryDecodeSegwit(
        string text, out string humanReadablePart, out int witnessVersion, out byte[] program)
    {
        humanReadablePart = string.Empty;
        witnessVersion = -1;
        program = [];
        if (!TryDecode(text, out var part, out var data, out var constant) || data.Length == 0)
        {
            return false;
        }

        var version = data[0];
        var converted = ConvertBits(data.AsSpan(1), 5, 8, pad: false);
        var expected = ChecksumFor(part, version);
        if (converted is null || constant != expected || !IsValidProgram(version, converted.Length))
        {
            return false;
        }

        humanReadablePart = part;
        witnessVersion = version;
        program = converted;
        return true;
    }

    /// <summary>
    /// Gives the checksum a SegWit address of a version is written with: Bech32 for version 0 and Bech32m from version
    /// 1 (BIP 350), except Litecoin's MWEB addresses (prefix <c>ltc</c>, versions 8 and 9: the extension block's own
    /// address and a peg-in), which Litecoin and litecoinspace.org write with Bech32 (read from litecoinspace.org's
    /// answers, 2026-11-09).
    /// </summary>
    /// <param name="humanReadablePart">The prefix in lower case.</param>
    /// <param name="version">The witness version.</param>
    /// <returns>The constant the checksum ends in.</returns>
    private static uint ChecksumFor(string humanReadablePart, int version) =>
        version == 0 || (humanReadablePart == LitecoinPrefix && version is MwebHogVersion or MwebPegInVersion)
            ? Bech32Constant
            : Bech32mConstant;

    /// <summary>Checks a witness version and program length (BIP 141 and BIP 173).</summary>
    /// <param name="version">The version.</param>
    /// <param name="length">The program's length in bytes.</param>
    /// <returns><see langword="true"/> when they go together.</returns>
    private static bool IsValidProgram(int version, int length) =>
        version is >= 0 and <= MaximumWitnessVersion
        && length is >= MinimumProgramLength and <= MaximumProgramLength
        && (version != 0 || length is KeyHashLength or ScriptHashLength);

    /// <summary>Decodes Bech32 or Bech32m text into its prefix and data, without the checksum.</summary>
    /// <param name="text">The text.</param>
    /// <param name="humanReadablePart">The prefix in lower case.</param>
    /// <param name="data">The five-bit values between the separator and the checksum.</param>
    /// <param name="constant">Which checksum held: <see cref="Bech32Constant"/> or <see
    /// cref="Bech32mConstant"/>.</param>
    /// <returns><see langword="true"/> when the text is well formed and one of the checksums holds.</returns>
    private static bool TryDecode(string text, out string humanReadablePart, out byte[] data, out uint constant)
    {
        humanReadablePart = string.Empty;
        data = [];
        constant = 0;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumLength || HasMixedCaseOrBadCharacters(text))
        {
            return false;
        }

        var lower = text.ToLowerInvariant();
        var separator = lower.LastIndexOf('1');
        if (separator < 1 || separator + ChecksumLength + 1 > lower.Length)
        {
            return false;
        }

        var values = new byte[lower.Length - separator - 1];
        for (var i = 0; i < values.Length; i++)
        {
            var value = Charset.IndexOf(lower[separator + 1 + i], StringComparison.Ordinal);
            if (value < 0)
            {
                return false;
            }

            values[i] = (byte)value;
        }

        humanReadablePart = lower[..separator];
        constant = Polymod([.. ExpandPart(humanReadablePart), .. values]);
        if (constant is not (Bech32Constant or Bech32mConstant))
        {
            return false;
        }

        data = values[..^ChecksumLength];
        return true;
    }

    /// <summary>Checks that a text uses one case and only printable ASCII (BIP 173).</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text mixes cases or has other characters.</returns>
    private static bool HasMixedCaseOrBadCharacters(string text)
    {
        var lower = false;
        var upper = false;
        foreach (var character in text)
        {
            if (character is < (char)33 or > (char)126)
            {
                return true;
            }

            lower |= char.IsAsciiLetterLower(character);
            upper |= char.IsAsciiLetterUpper(character);
        }

        return lower && upper;
    }

    /// <summary>
    /// Expands the prefix for the checksum: the high bits of each character, a zero, then the low bits.
    /// </summary>
    /// <param name="part">The prefix.</param>
    /// <returns>The values that go into the checksum.</returns>
    private static byte[] ExpandPart(string part) =>
        [.. part.Select(character => (byte)(character >> 5)), 0, .. part.Select(character => (byte)(character & 31))];

    /// <summary>Computes the checksum's polynomial over five-bit values.</summary>
    /// <param name="values">The values.</param>
    /// <returns>The remainder.</returns>
    private static uint Polymod(IEnumerable<byte> values)
    {
        uint checksum = 1;
        foreach (var value in values)
        {
            var top = checksum >> 25;
            checksum = ((checksum & 0x1ffffff) << 5) ^ value;
            for (var i = 0; i < Generator.Length; i++)
            {
                if (((top >> i) & 1) != 0)
                {
                    checksum ^= Generator[i];
                }
            }
        }

        return checksum;
    }

    /// <summary>Regroups bits, for example bytes into five-bit values and back.</summary>
    /// <param name="data">The values to regroup.</param>
    /// <param name="fromBits">The bits per value in.</param>
    /// <param name="toBits">The bits per value out.</param>
    /// <param name="pad">Whether to pad the last value with zeros; when not, leftover bits must be zero and
    /// few.</param>
    /// <returns>The regrouped values, or <see langword="null"/> when the input does not regroup cleanly.</returns>
    private static byte[]? ConvertBits(ReadOnlySpan<byte> data, int fromBits, int toBits, bool pad)
    {
        var accumulator = 0;
        var bits = 0;
        var result = new List<byte>();
        var maximum = (1 << toBits) - 1;
        foreach (var value in data)
        {
            if (value >> fromBits != 0)
            {
                return null;
            }

            accumulator = (accumulator << fromBits) | value;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                result.Add((byte)((accumulator >> bits) & maximum));
            }
        }

        if (pad)
        {
            if (bits > 0)
            {
                result.Add((byte)((accumulator << (toBits - bits)) & maximum));
            }
        }
        else if (bits >= fromBits || ((accumulator << (toBits - bits)) & maximum) != 0)
        {
            return null;
        }

        return [.. result];
    }
}
