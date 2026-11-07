using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The synthetic Esplora responses in <c>Esplora/</c>, made by <c>Esplora/generate.py</c> with its own Bech32 and
/// Base58 code: made-up hashes, so the addresses are valid but belong to nobody.
/// </summary>
internal static class Fixtures
{
    public const string Base = "https://esplora.test/api/";

    public static readonly Txid Older = Txid.Parse(Repeat("a4", 32));

    public static readonly Txid Confirmed = Txid.Parse(Repeat("a1", 32));

    public static readonly Txid Unconfirmed = Txid.Parse(Repeat("a2", 32));

    public static readonly Txid Coinbase = Txid.Parse(Repeat("a3", 32));

    public static readonly BlockHash Block = BlockHash.Parse("00000000000000000001" + Repeat("a0", 22));

    private static readonly Dictionary<string, string> AddressTable =
        JsonSerializer.Deserialize<Dictionary<string, string>>(Text("addresses.json"))!;

    public static BitcoinAddress KeyHash => Address("pkh");

    public static BitcoinAddress ScriptHash => Address("sh");

    public static BitcoinAddress WitnessKeyHash => Address("wpkh");

    public static BitcoinAddress WitnessScriptHash => Address("wsh");

    public static BitcoinAddress Taproot => Address("tr");

    public static string AddressText(string key) => AddressTable[key];

    public static BitcoinAddress Address(string key) => BitcoinAddress.Parse(AddressTable[key]);

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    public static string Url(string path) => Base + path;

    public static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    private static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Esplora", name);
}
