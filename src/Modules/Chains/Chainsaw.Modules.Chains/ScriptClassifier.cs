using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Reads the kind of a locking script (scriptPubKey) from its bytes and writes its address, so that Chainsaw does not
/// take the source's label or address on trust: what the source says is checked against what the script says.
/// </summary>
internal static class ScriptClassifier
{
    /// <summary>OP_0, which starts a SegWit version 0 script.</summary>
    private const byte Op0 = 0x00;

    /// <summary>OP_1, which starts a SegWit version 1 script (and OP_1–OP_16 are versions 1–16).</summary>
    private const byte Op1 = 0x51;

    /// <summary>OP_16.</summary>
    private const byte Op16 = 0x60;

    /// <summary>OP_RETURN, which marks an output that can never be spent.</summary>
    private const byte OpReturn = 0x6a;

    /// <summary>OP_DUP.</summary>
    private const byte OpDup = 0x76;

    /// <summary>OP_HASH160.</summary>
    private const byte OpHash160 = 0xa9;

    /// <summary>OP_EQUAL.</summary>
    private const byte OpEqual = 0x87;

    /// <summary>OP_EQUALVERIFY.</summary>
    private const byte OpEqualVerify = 0x88;

    /// <summary>OP_CHECKSIG.</summary>
    private const byte OpCheckSig = 0xac;

    /// <summary>OP_CHECKMULTISIG.</summary>
    private const byte OpCheckMultisig = 0xae;

    /// <summary>The length of a 20-byte hash and its push.</summary>
    private const int Hash160Length = 20;

    /// <summary>The length of a compressed and an uncompressed public key.</summary>
    private const int CompressedKeyLength = 33, UncompressedKeyLength = 65;

    /// <summary>The shortest and longest witness program.</summary>
    private const int MinimumProgramLength = 2, MaximumProgramLength = 40;

    /// <summary>Classifies a script.</summary>
    /// <param name="script">The script's bytes.</param>
    /// <param name="network">The network its address belongs to.</param>
    /// <returns>The kind and the address, which is <see langword="null"/> for kinds without one.</returns>
    public static (BitcoinScriptType Type, BitcoinAddress? Address) Classify(
        ReadOnlySpan<byte> script, BitcoinNetwork network)
    {
        if (IsPayToPublicKeyHash(script))
        {
            var hash = script.Slice(3, Hash160Length);
            return (BitcoinScriptType.PayToPublicKeyHash,
                BitcoinAddress.FromHash(BitcoinScriptType.PayToPublicKeyHash, hash, network));
        }

        if (IsPayToScriptHash(script))
        {
            var hash = script.Slice(2, Hash160Length);
            return (BitcoinScriptType.PayToScriptHash,
                BitcoinAddress.FromHash(BitcoinScriptType.PayToScriptHash, hash, network));
        }

        // Zcash has no witness programs; there such a script is only non-standard.
        if (network != BitcoinNetwork.Zcash && TryReadWitness(script, out var version, out var program))
        {
            var address = BitcoinAddress.FromWitness(version, program, network);
            return (address.Type, address);
        }

        return (ClassifyWithoutAddress(script), null);
    }

    /// <summary>Classifies the scripts that have no address.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The kind.</returns>
    private static BitcoinScriptType ClassifyWithoutAddress(ReadOnlySpan<byte> script)
    {
        if (!script.IsEmpty && script[0] == OpReturn)
        {
            return BitcoinScriptType.OpReturn;
        }

        if (IsPayToPublicKey(script))
        {
            return BitcoinScriptType.PayToPublicKey;
        }

        return IsBareMultisig(script) ? BitcoinScriptType.Multisig : BitcoinScriptType.NonStandard;
    }

    /// <summary>Checks for <c>OP_DUP OP_HASH160 &lt;20&gt; OP_EQUALVERIFY OP_CHECKSIG</c>.</summary>
    /// <param name="script">The script.</param>
    /// <returns><see langword="true"/> for P2PKH.</returns>
    private static bool IsPayToPublicKeyHash(ReadOnlySpan<byte> script) =>
        script.Length == 25 && script[0] == OpDup && script[1] == OpHash160 && script[2] == Hash160Length
        && script[23] == OpEqualVerify && script[24] == OpCheckSig;

    /// <summary>Checks for <c>OP_HASH160 &lt;20&gt; OP_EQUAL</c>.</summary>
    /// <param name="script">The script.</param>
    /// <returns><see langword="true"/> for P2SH.</returns>
    private static bool IsPayToScriptHash(ReadOnlySpan<byte> script) =>
        script.Length == 23 && script[0] == OpHash160 && script[1] == Hash160Length && script[22] == OpEqual;

    /// <summary>Checks for <c>&lt;33 or 65&gt; OP_CHECKSIG</c>.</summary>
    /// <param name="script">The script.</param>
    /// <returns><see langword="true"/> for P2PK.</returns>
    private static bool IsPayToPublicKey(ReadOnlySpan<byte> script) =>
        script.Length >= 2 && script[^1] == OpCheckSig && script[0] is CompressedKeyLength or UncompressedKeyLength
        && script.Length == script[0] + 2;

    /// <summary>
    /// Checks for <c>OP_m &lt;keys&gt; OP_n OP_CHECKMULTISIG</c>, roughly: a small number first and last.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <returns><see langword="true"/> for bare multisig.</returns>
    private static bool IsBareMultisig(ReadOnlySpan<byte> script) =>
        script.Length >= 3 && script[^1] == OpCheckMultisig
        && script[0] is >= Op1 and <= Op16 && script[^2] is >= Op1 and <= Op16;

    /// <summary>Reads a witness program: a version (OP_0 or OP_1–OP_16) and one push of 2–40 bytes (BIP 141).</summary>
    /// <param name="script">The script.</param>
    /// <param name="version">The witness version.</param>
    /// <param name="program">The program.</param>
    /// <returns><see langword="true"/> when the script is a witness program with a valid address.</returns>
    private static bool TryReadWitness(ReadOnlySpan<byte> script, out int version, out byte[] program)
    {
        version = -1;
        program = [];
        if (script.Length < 2 + MinimumProgramLength || script.Length > 2 + MaximumProgramLength
            || script[1] != script.Length - 2)
        {
            return false;
        }

        version = script[0] switch
        {
            Op0 => 0,
            >= Op1 and <= Op16 => script[0] - Op1 + 1,
            _ => -1,
        };
        var length = script.Length - 2;
        if (version < 0 || (version == 0 && length is not (20 or 32)))
        {
            return false;
        }

        program = script[2..].ToArray();
        return true;
    }
}
