using System.Globalization;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Bitcoin Core's own JSON-RPC as a sort of API for Bitcoin: the user's own node, asked straight in the user's own
/// network and never through the exit (only an endpoint marked local may speak it,
/// <see cref="SourceApiInfo.LocalOnly"/>).
/// It answers the lookups whose answer is one of the chain's own values: the height (<c>getblockcount</c>), the newest
/// block's hash (<c>getbestblockhash</c>), the hash of the block at a height (<c>getblockhash</c>), a block's 80-byte
/// header (<c>getblockheader</c> with <c>verbose=false</c>) and a transaction's own bytes (<c>getrawtransaction</c>
/// with <c>verbose=false</c>, which needs <c>txindex=1</c> for a transaction in a block). An address's transactions,
/// a transaction's description and who spends its outputs need an index Bitcoin Core does not keep; they are asked of
/// the chain's Esplora endpoints (an electrs on the own network, or one on the internet through the exit), so that
/// nothing else changes (principle 8).
/// </summary>
/// <remarks>
/// <para>
/// Each call is a POST of a JSON-RPC 1.0 request to the endpoint's address, as Bitcoin Core's own examples send it
/// (<c>{"jsonrpc": "1.0", "id": …, "method": …, "params": […]}</c>). The <c>id</c> is the method and its parameters
/// (<c>getblockhash 840000</c>): Bitcoin Core gives it back in the response, so the sealed raw response itself says
/// what was asked (principle 1), and it holds nothing but what the lookup needs (principle 4).
/// </para>
/// <para>
/// Bitcoin Core answers RPC only over plain http; Chainsaw asks only over https, so the node is reached through a
/// TLS proxy in front of it on the own network (stunnel, nginx or Caddy), whose certificate is pinned (sketch KL3b).
/// The user name and password (<c>rpcauth</c>, or the cookie file's <c>__cookie__:…</c>) are a key in the vault,
/// written <c>användare:lösenord</c>, and go as HTTP Basic; they are never part of a case.
/// </para>
/// </remarks>
internal sealed class BitcoinCoreApi : ISourceApi
{
    /// <summary>The sort's code, as it is saved and sealed.</summary>
    public const string Code = "bitcoin-core";

    /// <summary>The JSON-RPC error for a transaction, block or address the node does not know.</summary>
    private const int NotKnownError = -5;

    /// <summary>The JSON-RPC error for a parameter out of range, as a height above the node's own.</summary>
    private const int InvalidParameterError = -8;

    /// <summary>The JSON-RPC error of a node that is still starting.</summary>
    private const int WarmingUpError = -28;

    /// <summary>The JSON-RPC error for a method the node does not have.</summary>
    private const int MethodNotFoundError = -32601;

    /// <summary>The HTTP status of a node that did not accept the user name and password.</summary>
    private const int UnauthorizedStatus = 401;

    /// <summary>The HTTP status of a node whose <c>rpcwhitelist</c> does not allow the method.</summary>
    private const int ForbiddenStatus = 403;

    /// <summary>
    /// What Bitcoin Core's message says when a transaction in a block cannot be had without <c>-txindex</c>: <em>No
    /// such mempool transaction. Use -txindex or provide a block hash to enable blockchain transaction queries.</em>
    /// </summary>
    private const string NoTxIndexMarker = "-txindex";

    /// <summary>What Bitcoin Core's message says while its transaction index is being built.</summary>
    private const string IndexingMarker = "being indexed";

    /// <summary>What is said when the node has no transaction index.</summary>
    internal const string NoTxIndexText =
        "Den egna noden har inget transaktionsindex (txindex=1 i bitcoin.conf), så den kan bara ge tx:er som väntar "
        + "på ett block. Övriga källor frågas.";

    /// <summary>The call for the chain's height.</summary>
    private static readonly RpcCall HeightCall = new("getblockcount", []);

    /// <summary>How the id of a call for the hash at a height begins; a probe's answer to it must begin so.</summary>
    private const string HashCallPrefix = "getblockhash ";

    /// <summary>The paths of the Esplora form that one method answers, read by <see cref="TryCall"/>.</summary>
    private const string TipHeightPath = "blocks/tip/height";

    /// <summary>The path of the newest block's hash.</summary>
    private const string TipHashPath = "blocks/tip/hash";

    /// <summary>The start of the path of the hash of the block at a height.</summary>
    private const string BlockAtHeightPrefix = "block-height/";

    /// <summary>The start of a path about one block.</summary>
    private const string BlockPrefix = "block/";

    /// <summary>The end of the path of a block's header.</summary>
    private const string HeaderSuffix = "/header";

    /// <summary>The start of a path about one transaction.</summary>
    private const string TransactionPrefix = "tx/";

    /// <summary>The end of the path of a transaction's own bytes.</summary>
    private const string HexSuffix = "/hex";

    /// <summary>Creates the sort for the network of the options.</summary>
    /// <param name="options">The source's settings, Bitcoin's.</param>
    public BitcoinCoreApi(BitcoinSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Info = new SourceApiInfo(Code, "Bitcoin Core (egen nod)", options.Network.ChainOf()) { LocalOnly = true };
        ChainName = RawResponseStore.NameOf(options.Network);
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; }

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults => [];

    /// <inheritdoc/>
    public string ChainName { get; }

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => Call(address, HeightCall);

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body)
    {
        var text = Encoding.UTF8.GetString(Result(body, HeightCall.Id));
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var height) && height > 0
            ? height
            : throw new FormatException("The height is not a whole number above zero.");
    }

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) =>
        Call(address, new RpcCall("getblockhash", [height]));

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) =>
        BlockHash.TryParse(Encoding.UTF8.GetString(Result(body, HashCallPrefix, idIsPrefix: true)), out var hash)
            ? hash.Value
            : throw new FormatException("The hash is not 64 hex characters.");

    /// <inheritdoc/>
    public bool Answers(string path) => TryCall(path, out _);

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path) =>
        TryCall(path, out var call)
            ? Call(address, call!)
            : throw new ArgumentException("Bitcoin Core does not answer that lookup.", nameof(path));

    /// <inheritdoc/>
    public ExitCredential Credential(string secret)
    {
        // A key without a colon is not a user name and password; it is never sent, and the lookup says so.
        if (string.IsNullOrEmpty(secret) || !secret.Contains(':', StringComparison.Ordinal))
        {
            return new ExitCredential("Authorization", string.Empty);
        }

        return new ExitCredential("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)));
    }

    /// <inheritdoc/>
    public SourceVerdict? Judge(ExitResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.StatusCode == UnauthorizedStatus)
        {
            return new SourceVerdict(Outcome.Failed,
                "Den egna noden godtog inte användarnamnet och lösenordet i valvet.");
        }

        if (response.StatusCode == ForbiddenStatus)
        {
            return new SourceVerdict(Outcome.Failed,
                "Den egna noden tillåter inte anropet för användaren (rpcwhitelist).");
        }

        if (Error(response.Body.Span) is { } error)
        {
            return VerdictOf(error);
        }

        // Bitcoin Core says that it does not know something with its own error (-5), never with a bare status: a 404
        // without one is the way in front of it (a TLS proxy's wrong path), not an answer about the chain.
        return response.IsSuccess
            ? null
            : new SourceVerdict(Outcome.Failed, "Den egna noden svarade med status "
                + response.StatusCode.ToString(CultureInfo.InvariantCulture) + ".");
    }

    /// <summary>Gives the call that answers a path of the Esplora form, when Bitcoin Core has one.</summary>
    /// <param name="path">The path under the endpoint's address.</param>
    /// <param name="call">The method and its parameters, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when there is one.</returns>
    internal static bool TryCall(string path, out RpcCall? call)
    {
        ArgumentNullException.ThrowIfNull(path);
        call = path switch
        {
            TipHeightPath => new RpcCall("getblockcount", []),
            TipHashPath => new RpcCall("getbestblockhash", []),
            _ when path.StartsWith(BlockAtHeightPrefix, StringComparison.Ordinal) => AtHeight(path),
            _ when IsAbout(path, BlockPrefix, HeaderSuffix, out var hash) =>
                new RpcCall("getblockheader", [hash, false]),
            _ when IsAbout(path, TransactionPrefix, HexSuffix, out var txid) =>
                new RpcCall("getrawtransaction", [txid, false]),
            _ => null,
        };
        return call is not null;
    }

    /// <summary>Reads the value of a successful JSON-RPC response as the Esplora form gives it: plain text.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expectedId">
    /// The id of the call that was sent (<see cref="RpcCall.Id"/>), which the answer must give back, so that an answer
    /// to another call is never read as this one's; <see langword="null"/> for an answer read back from the case, which
    /// was checked against its call when it was fetched (only then is it indexed as an answer).
    /// </param>
    /// <param name="idIsPrefix">Whether the answer's id need only start with <paramref name="expectedId"/>.</param>
    /// <returns>The <c>result</c>: a text without its quotes, or a number as it is written.</returns>
    /// <exception cref="FormatException">
    /// The body is not a JSON-RPC response with a text or a number, or not the answer to the call.
    /// </exception>
    internal static byte[] Result(ReadOnlySpan<byte> body, string? expectedId, bool idIsPrefix = false)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out var result)
                || (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null))
            {
                throw new FormatException("The response is not a JSON-RPC answer.");
            }

            if (expectedId is not null && !AnswersCall(root, expectedId, idIsPrefix))
            {
                throw new FormatException("The JSON-RPC answer is not the answer to the call that was sent.");
            }

            return result.ValueKind switch
            {
                JsonValueKind.String => Encoding.UTF8.GetBytes(result.GetString()!),
                JsonValueKind.Number => Encoding.UTF8.GetBytes(result.GetRawText()),
                _ => throw new FormatException("The JSON-RPC answer is not a text or a number."),
            };
        }
        catch (JsonException exception)
        {
            throw new FormatException("The response is not JSON.", exception);
        }
    }

    /// <summary>Tells whether a JSON-RPC answer gives back the id of the call that was sent.</summary>
    /// <param name="root">The answer.</param>
    /// <param name="expectedId">The call's id.</param>
    /// <param name="idIsPrefix">Whether the answer's id need only start with it.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool AnswersCall(JsonElement root, string expectedId, bool idIsPrefix)
    {
        if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var given = id.GetString()!;
        return idIsPrefix
            ? given.StartsWith(expectedId, StringComparison.Ordinal)
            : string.Equals(given, expectedId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a fresh answer to the call that answers a path, checked against the call's id; a path no call answers
    /// gives nothing to read.
    /// </summary>
    /// <param name="path">The lookup's path.</param>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The <c>result</c> as plain text.</returns>
    /// <exception cref="FormatException">The body is not the answer to the path's call.</exception>
    internal static byte[] ResultFor(string path, ReadOnlySpan<byte> body) =>
        TryCall(path, out var call)
            ? Result(body, call!.Id)
            : throw new FormatException("Bitcoin Core does not answer that lookup.");

    /// <summary>Builds the POST of one call to the endpoint's address.</summary>
    /// <param name="address">The endpoint's address.</param>
    /// <param name="call">The call.</param>
    /// <returns>The request, without the user name and password.</returns>
    private static ExitRequest Call(Uri address, RpcCall call)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "1.0");
            writer.WriteString("id", call.Id);
            writer.WriteString("method", call.Method);
            writer.WriteStartArray("params");
            foreach (var parameter in call.Parameters)
            {
                switch (parameter)
                {
                    case long number:
                        writer.WriteNumberValue(number);
                        break;
                    case bool flag:
                        writer.WriteBooleanValue(flag);
                        break;
                    default:
                        writer.WriteStringValue((string)parameter);
                        break;
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return new ExitRequest(ExitMethod.Post, address)
        {
            Body = buffer.WrittenMemory.ToArray(),
            ContentType = "text/plain",
            Accept = "application/json",
        };
    }

    /// <summary>Reads the call for the hash at a height from its path.</summary>
    /// <param name="path">The path, starting with <see cref="BlockAtHeightPrefix"/>.</param>
    /// <returns>The call, or <see langword="null"/> when the height is not one.</returns>
    private static RpcCall? AtHeight(string path) =>
        long.TryParse(path.AsSpan(BlockAtHeightPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
            out var height)
            ? new RpcCall("getblockhash", [height])
            : null;

    /// <summary>Reads a path of the form <c>prefix/hash/suffix</c> with 64 hex characters in the middle.</summary>
    /// <param name="path">The path.</param>
    /// <param name="prefix">Its start.</param>
    /// <param name="suffix">Its end.</param>
    /// <param name="hash">The hash in the middle, in lower case.</param>
    /// <returns><see langword="true"/> when the path is of the form.</returns>
    private static bool IsAbout(string path, string prefix, string suffix, out string hash)
    {
        hash = string.Empty;
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        return Hash256Text.TryNormalise(path[prefix.Length..^suffix.Length], out hash);
    }

    /// <summary>Reads the error of a JSON-RPC response, when it has one.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The code and the message, or <see langword="null"/> when there is none or it is not JSON-RPC.</returns>
    private static (int Code, string Message)? Error(ReadOnlySpan<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out var code) || !code.TryGetInt32(out var number))
            {
                return null;
            }

            var message = error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()!
                : string.Empty;
            return (number, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Says what a JSON-RPC error means for the lookup, in Swedish.</summary>
    /// <param name="error">The code and the message.</param>
    /// <returns>The verdict.</returns>
    private static SourceVerdict VerdictOf((int Code, string Message) error) => error.Code switch
    {
        NotKnownError when error.Message.Contains(NoTxIndexMarker, StringComparison.Ordinal) =>
            new SourceVerdict(Outcome.CannotAnswer, NoTxIndexText),
        NotKnownError when error.Message.Contains(IndexingMarker, StringComparison.Ordinal) =>
            new SourceVerdict(Outcome.CannotAnswer,
                "Den egna noden bygger fortfarande sitt transaktionsindex. Övriga källor frågas."),
        NotKnownError => new SourceVerdict(Outcome.NotFound, null),
        InvalidParameterError => new SourceVerdict(Outcome.Failed,
            "Den egna noden har inte kommit så långt i kedjan, eller förstod inte frågan."),
        WarmingUpError => new SourceVerdict(Outcome.Failed, "Den egna noden startar fortfarande."),
        MethodNotFoundError => new SourceVerdict(Outcome.Failed, "Den egna noden saknar anropet."),
        _ => new SourceVerdict(Outcome.Failed,
            "Den egna noden svarade med fel " + error.Code.ToString(CultureInfo.InvariantCulture) + "."),
    };
}

/// <summary>One JSON-RPC call to Bitcoin Core.</summary>
/// <param name="Method">The method, for example <c>getblockhash</c>.</param>
/// <param name="Parameters">Its parameters: whole numbers, flags and texts.</param>
internal sealed record RpcCall(string Method, IReadOnlyList<object> Parameters)
{
    /// <summary>
    /// Gets the call's id: the method and its parameters, which Bitcoin Core gives back in the response, so that the
    /// sealed response says what was asked; for example <c>getblockheader 0000…e6 false</c>.
    /// </summary>
    public string Id => string.Join(' ', [Method, .. Parameters.Select(Text)]);

    /// <summary>Writes a parameter as the id shows it.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>The text.</returns>
    private static string Text(object parameter) => parameter switch
    {
        long number => number.ToString(CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        _ => (string)parameter,
    };
}

/// <summary>What a sort of API reads from a response beyond its HTTP status (<see cref="ISourceApi.Judge"/>).</summary>
/// <param name="Outcome">What the response said.</param>
/// <param name="Problem">
/// Why it cannot be used, in Swedish, or <see langword="null"/> for the lookup's own words.
/// </param>
internal sealed record SourceVerdict(Outcome Outcome, string? Problem);
