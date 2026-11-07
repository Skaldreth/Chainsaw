#if DEBUG
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>
/// A made-up place money can be paid to in the test case: an address, or a script without one (a public key, an
/// OP_RETURN). Every key and hash is SHA-256 of the label with a prefix of its own, so none is one that anyone has
/// used, and the same label gives the same address in every run.
/// </summary>
/// <param name="Label">The label it is made from, as the scenario names it.</param>
/// <param name="Address">The address, or <see langword="null"/> for a script without one.</param>
/// <param name="Script">The output script, in hex as the chain writes it.</param>
public sealed record TestAddress(string Label, BitcoinAddress? Address, string Script)
{
    /// <summary>What every made-up hash starts from, so that a label never gives a hash outside the test data.</summary>
    private const string Prefix = "chainsaw-testdata:";

    /// <summary>The 20-byte hash's length.</summary>
    private const int ShortHash = 20;

    /// <summary>Makes a P2WPKH address (<c>bc1q…</c>, the usual wallet today).</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    public static TestAddress Wpkh(string label)
    {
        var hash = Hash(label).AsSpan(0, ShortHash);
        return new(label, BitcoinAddress.FromWitness(0, hash, BitcoinNetwork.Mainnet), "0014" + Hex(hash));
    }

    /// <summary>Makes a P2PKH address (<c>1…</c>, the oldest kind with an address).</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    public static TestAddress Pkh(string label)
    {
        var hash = Hash(label).AsSpan(0, ShortHash);
        var address = BitcoinAddress.FromHash(BitcoinScriptType.PayToPublicKeyHash, hash, BitcoinNetwork.Mainnet);
        return new(label, address, "76a914" + Hex(hash) + "88ac");
    }

    /// <summary>Makes a P2WSH address (<c>bc1q…</c> with a 32-byte program, as services often use).</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    public static TestAddress Wsh(string label)
    {
        var hash = Hash(label);
        return new(label, BitcoinAddress.FromWitness(0, hash, BitcoinNetwork.Mainnet), "0020" + Hex(hash));
    }

    /// <summary>
    /// Makes a pay-to-public-key script (P2PK, as the first miners were paid): a coin without an address, drawn as a
    /// coin of its own in the graph.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns>The script.</returns>
    public static TestAddress PublicKey(string label) =>
        new(label, null, "21" + "02" + Hex(Hash(label)) + "ac");

    /// <summary>Makes an OP_RETURN script with a 32-byte commitment, as every coinbase carries.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The script, which nothing can ever spend.</returns>
    public static TestAddress OpReturn(string label) =>
        new(label, null, "6a24aa21a9ed" + Hex(Hash(label)));

    /// <summary>Gives SHA-256 of the label with the test data's prefix.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The 32 bytes.</returns>
    public static byte[] Hash(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(Prefix + label));

    /// <summary>Writes bytes as lower-case hex.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The hex.</returns>
    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);
}
#endif
