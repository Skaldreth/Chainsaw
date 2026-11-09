using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Solana's JSON-RPC as a sort of API for Solana: every call is a POST of <c>{"jsonrpc":"2.0","id":1,"method":…,
/// "params":[…]}</c> to the endpoint's address, always with the commitment <c>finalized</c>, which every Solana node
/// answers. The height is the newest finalized slot (<c>getSlot</c>); the hash at height 0 is the cluster's genesis
/// hash (<c>getGenesisHash</c>), since the first slot's block is often pruned from a node that is not archival. A key
/// comes from the vault and goes in <c>Authorization: Bearer</c>, never in the address. A response is sealed under the
/// endpoint's host alone, with the call as the fragment (<see cref="SourceLookups.SealedPostUri"/>), so that nothing
/// in an endpoint's path ever reaches a case.
/// </summary>
internal sealed class SolanaRpcApi : ISourceApi
{
    /// <summary>The sort's code, as it is saved and sealed.</summary>
    public const string Code = "solana-rpc";

    /// <summary>
    /// The newest transaction version Chainsaw reads (<c>maxSupportedTransactionVersion</c>). The main network had
    /// transactions of version 1 when the public endpoint was tried (2026-11-09: asked with 0, it refused one of
    /// version 1); a newer version is refused by the node, and the lookup fails and says so rather than read it wrong.
    /// </summary>
    public const int SupportedTransactionVersion = 1;

    /// <summary>The JSON-RPC version every call names.</summary>
    private const string JsonRpcVersion = "2.0";

    /// <summary>The media type of a call and its answer.</summary>
    private const string JsonMediaType = "application/json";

    /// <summary>The default endpoint's fixed id, so that the same default is the same endpoint in every file.</summary>
    private static readonly Guid DefaultId = Guid.Parse("3c5b9a1e-2f4d-4c6b-8e0a-5d7f1b2c9a01");

    /// <summary>Creates the sort with Solana's default endpoints from the options.</summary>
    /// <param name="options">The Solana source's settings.</param>
    /// <exception cref="ArgumentException">A default endpoint is not plain https.</exception>
    public SolanaRpcApi(SolanaSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Defaults = [.. options.DefaultEndpoints.Select(address => Default(address, options.DefaultPace))];
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; } = new(Code, "Solana JSON-RPC", Chain.Solana);

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <inheritdoc/>
    public string ChainName => SolanaRpcSource.ChainName;

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => Post(address, string.Empty, SlotCall());

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body) => SolanaRpcParser.ParseSlot(body);

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) =>
        Post(address, string.Empty, height == 0 ? Call("getGenesisHash") : BlockCall(height));

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) => SolanaRpcParser.ParseHash(body);

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Always: every Solana call is a POST.</exception>
    public ExitRequest Request(Uri address, string path) =>
        throw new NotSupportedException("Solana's JSON-RPC is only asked with POST.");

    /// <inheritdoc/>
    public ExitRequest Post(Uri address, string path, string body)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new ExitRequest(ExitMethod.Post, new Uri(address, path))
        {
            Body = Encoding.UTF8.GetBytes(body),
            ContentType = JsonMediaType,
            Accept = JsonMediaType,
        };
    }

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => new("Authorization", "Bearer " + secret);

    /// <summary>The call for the newest finalized slot.</summary>
    /// <returns>The body.</returns>
    public static string SlotCall() => Call("getSlot", Commitment());

    /// <summary>The call for a slot's block, without its transactions or rewards.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The body.</returns>
    public static string BlockCall(long slot) => Call("getBlock", slot, new JsonObject
    {
        ["encoding"] = "json",
        ["transactionDetails"] = "none",
        ["rewards"] = false,
        ["commitment"] = SolanaRpcParser.Finalized,
        ["maxSupportedTransactionVersion"] = SupportedTransactionVersion,
    });

    /// <summary>
    /// The call for a transaction with its balances before and after and its instructions parsed by the node
    /// (<c>jsonParsed</c>), so that the System and Token programs' transfers can be read as they ran.
    /// </summary>
    /// <param name="hash">The transaction's first signature.</param>
    /// <returns>The body.</returns>
    public static string TransactionCall(EthereumTxHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return Call("getTransaction", hash.Value, new JsonObject
        {
            ["encoding"] = "jsonParsed",
            ["commitment"] = SolanaRpcParser.Finalized,
            ["maxSupportedTransactionVersion"] = SupportedTransactionVersion,
        });
    }

    /// <summary>The call for a page of an address's signatures, newest first.</summary>
    /// <param name="address">The address.</param>
    /// <param name="size">How many entries.</param>
    /// <param name="before">The signature the page starts after, or <see langword="null"/> for the newest.</param>
    /// <returns>The body.</returns>
    public static string SignaturesCall(EthereumAddress address, int size, EthereumTxHash? before)
    {
        ArgumentNullException.ThrowIfNull(address);
        var options = new JsonObject { ["limit"] = size, ["commitment"] = SolanaRpcParser.Finalized };
        if (before is not null)
        {
            options["before"] = before.Value;
        }

        return Call("getSignaturesForAddress", address.Value, options);
    }

    /// <summary>The call for an address's SOL now.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The body.</returns>
    public static string BalanceCall(EthereumAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Call("getBalance", address.Value, Commitment());
    }

    /// <summary>
    /// The call for the token accounts an owner holds now of one mint, or of every token by its program.
    /// </summary>
    /// <param name="owner">The owner.</param>
    /// <param name="filter">The filter: <c>mint</c> or <c>programId</c>.</param>
    /// <param name="value">The mint or the program.</param>
    /// <returns>The body.</returns>
    public static string TokenAccountsCall(EthereumAddress owner, string filter, string value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return Call("getTokenAccountsByOwner", owner.Value, new JsonObject { [filter] = value }, new JsonObject
        {
            ["encoding"] = "jsonParsed",
            ["commitment"] = SolanaRpcParser.Finalized,
        });
    }

    /// <summary>The options that ask for the finalized state.</summary>
    /// <returns>The options.</returns>
    private static JsonObject Commitment() => new() { ["commitment"] = SolanaRpcParser.Finalized };

    /// <summary>Writes a JSON-RPC call.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">Its parameters, in order.</param>
    /// <returns>The body.</returns>
    private static string Call(string method, params JsonNode?[] parameters) =>
        new JsonObject
        {
            ["jsonrpc"] = JsonRpcVersion,
            ["id"] = 1,
            ["method"] = method,
            ["params"] = new JsonArray(parameters),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    /// <summary>Makes a default endpoint of an address.</summary>
    /// <param name="address">The address.</param>
    /// <param name="pace">The shortest time between two requests to it.</param>
    /// <returns>The endpoint, switched on.</returns>
    /// <exception cref="ArgumentException">The address is not plain https.</exception>
    private static SourceEndpoint Default(Uri address, TimeSpan pace)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!SourceAddress.TryRead(address.OriginalString, out var checkedAddress, out _))
        {
            throw new ArgumentException(
                "Solana's base address must be https without user, query or fragment.", nameof(address));
        }

        var known = checkedAddress!;
        var id = known == SolanaSourceOptions.PublicRpc
            ? DefaultId
            : new Guid(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(known.AbsoluteUri)).AsSpan(0, 16));
        var name = known == SolanaSourceOptions.PublicRpc ? "Solana (publik)" : SourceAddress.NameOf(known);
        return new SourceEndpoint(id, Chain.Solana, name, known, Code, null, Enabled: true) { Interval = pace };
    }
}
