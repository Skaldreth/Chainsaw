#if DEBUG
using System.Collections.Specialized;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// Answers Blockscout's requests from a made-up part on a chain that Blockscout serves (Ethereum's
/// <see cref="TestEthereumChain"/> or BSC's <see cref="TestBscChain"/>), in this process: the test case's source is
/// asked through this instead of through the exit, so nothing about the test case ever leaves the computer. A request
/// for anything else than the made-up source's address is refused, never sent.
/// </summary>
/// <param name="chain">The made-up part.</param>
/// <param name="clock">The clock, for the response's times.</param>
public sealed class TestEthereumHttp(ITestBlockscoutChain chain, IClock clock) : IExitHttp
{
    /// <summary>
    /// The first topic of an ERC-20 <c>Transfer</c> event: the Keccak-256 of <c>Transfer(address,address,uint256)</c>.
    /// </summary>
    private const string TransferTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

    /// <summary>The path of the newest blocks.</summary>
    private const string NewestBlocks = "v2/blocks?type=block";

    /// <summary>The part of a path that asks for a block at a height.</summary>
    private const string BlockPart = "v2/blocks/";

    /// <summary>The media type of the answers.</summary>
    private const string Json = "application/json";

    /// <summary>The answer's status when it has something.</summary>
    private const string Found = "1";

    /// <inheritdoc/>
    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var root = chain.Root.AbsoluteUri;
        if (!request.Uri.AbsoluteUri.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ExitException(ExitError.RequestFailed);
        }

        var now = clock.UtcNow;
        var answer = Answer(request.Uri.AbsoluteUri[root.Length..]);
        var (status, body) = answer is null
            ? (404, new JsonObject { ["message"] = "Not Found" }.ToJsonString())
            : (200, answer.ToJsonString());
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, status, Json, Encoding.UTF8.GetBytes(body), now, now, ExitRoute.Windows)
        {
            IsLocal = true,
        });
    }

    /// <summary>Answers a path under the source's address.</summary>
    /// <param name="path">The path and query.</param>
    /// <returns>The answer, or <see langword="null"/> for a path Blockscout does not have.</returns>
    private JsonNode? Answer(string path)
    {
        if (path == NewestBlocks)
        {
            return new JsonObject { ["items"] = new JsonArray(Block(chain.Newest)) };
        }

        if (path.StartsWith(BlockPart, StringComparison.Ordinal))
        {
            return long.TryParse(path[BlockPart.Length..], NumberStyles.None, CultureInfo.InvariantCulture,
                out var height) && height >= chain.First && height <= chain.Newest
                ? Block(height)
                : null;
        }

        if (!path.StartsWith('?'))
        {
            return null;
        }

        var query = HttpUtility.ParseQueryString(path[1..]);
        return (query["module"], query["action"]) switch
        {
            ("block", "eth_block_number") => new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["result"] = "0x" + chain.Newest.ToString("x", CultureInfo.InvariantCulture),
                ["id"] = 1,
            },
            ("transaction", "gettxinfo") => Find(query["txhash"] ?? string.Empty) is { } transaction
                ? Ok(Info(transaction))
                : NotFound("Transaction not found"),
            ("token", "getToken") => IsToken(query["contractaddress"])
                ? Ok(new JsonObject
                {
                    ["contractAddress"] = chain.Token.Contract.Value,
                    ["decimals"] = Decimals,
                    ["symbol"] = chain.Token.Symbol,
                    ["name"] = chain.Token.Name,
                    ["type"] = "ERC-20",
                })
                : NotFound("Token not found"),
            ("account", _) => Account(query),
            _ => null,
        };
    }

    /// <summary>Answers the account module: an address's histories, ether and tokens, and a transaction's calls.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The answer, or <see langword="null"/> for an action Blockscout does not have.</returns>
    private JsonNode? Account(NameValueCollection query)
    {
        if (query["action"] == "txlistinternal" && query["txhash"] is { } hash)
        {
            var calls = Find(hash) is { } transaction ? CallEntries(transaction) : [];
            return List(calls);
        }

        if (!EthereumAddress.TryParse(chain.Chain, query["address"], out var address))
        {
            return null;
        }

        var limit = int.TryParse(query["offset"], NumberStyles.None, CultureInfo.InvariantCulture, out var asked)
            ? asked
            : 100;
        long? below = long.TryParse(query["endblock"], NumberStyles.None, CultureInfo.InvariantCulture, out var end)
            ? end
            : null;
        var transactions = chain.Transactions
            .Where(transaction => below is null || transaction.Height <= below)
            .OrderByDescending(transaction => transaction.Height)
            .ToList();
        return query["action"] switch
        {
            "txlist" => List(transactions
                .Where(transaction => transaction.From == address || transaction.To == address)
                .Select(Entry).Take(limit)),
            "txlistinternal" => List(transactions.SelectMany(CallEntries)
                .Where(entry => Touches(entry, address)).Take(limit)),
            "tokentx" => List(transactions.SelectMany(TokenEntries)
                .Where(entry => Touches(entry, address)).Take(limit)),
            "balance" => new JsonObject
            {
                ["status"] = Found,
                ["message"] = "OK",
                ["result"] = BigInteger.Max(EtherOf(address), 0).ToString(CultureInfo.InvariantCulture),
            },
            "tokenlist" => List(TokensOf(address)),
            _ => null,
        };
    }

    /// <summary>Gets the token's decimals, as text as the Etherscan-style API writes them.</summary>
    private string Decimals => chain.Token.Decimals.ToString(CultureInfo.InvariantCulture);

    /// <summary>Finds a transaction by its hash.</summary>
    /// <param name="hash">The hash, in any case.</param>
    /// <returns>The transaction, or <see langword="null"/>.</returns>
    private TestEthereumTransaction? Find(string hash) => chain.Transactions.FirstOrDefault(
        transaction => string.Equals(transaction.Hash, hash, StringComparison.OrdinalIgnoreCase));

    /// <summary>Says whether a contract is the made-up part's token's.</summary>
    /// <param name="contract">The contract as asked.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool IsToken(string? contract) =>
        EthereumAddress.TryParse(chain.Chain, contract, out var address) && address == chain.Token.Contract;

    /// <summary>Says whether a history's entry touches an address.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it was sent from or to it.</returns>
    private static bool Touches(JsonObject entry, EthereumAddress address) =>
        (string?)entry["from"] == address.Value || (string?)entry["to"] == address.Value;

    /// <summary>Gives the ether an address holds after the made-up transactions, its fees paid.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The wei; below zero for a made-up payer whose income is not in the story.</returns>
    private BigInteger EtherOf(EthereumAddress address) => chain.Transactions.Aggregate(BigInteger.Zero, (sum, each) =>
        sum
        + (each.To == address ? each.Value : 0) - (each.From == address ? each.Value + each.Fee : 0)
        + each.Calls.Aggregate(BigInteger.Zero, (calls, call) =>
            calls + (call.To == address ? call.Wei : 0) - (call.From == address ? call.Wei : 0)));

    /// <summary>Writes the tokens an address holds after the made-up transactions: its token, when it has any.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The entries.</returns>
    private IEnumerable<JsonObject> TokensOf(EthereumAddress address)
    {
        var usdt = chain.Transactions.SelectMany(transaction => transaction.Tokens).Aggregate(
            BigInteger.Zero,
            (sum, token) => sum + (token.To == address ? token.Units : 0) - (token.From == address ? token.Units : 0));
        if (usdt.Sign <= 0)
        {
            yield break;
        }

        yield return new JsonObject
        {
            ["balance"] = usdt.ToString(CultureInfo.InvariantCulture),
            ["contractAddress"] = chain.Token.Contract.Value,
            ["decimals"] = Decimals,
            ["name"] = chain.Token.Name,
            ["symbol"] = chain.Token.Symbol,
            ["type"] = "ERC-20",
        };
    }

    /// <summary>Writes a block, as <c>v2/blocks/:height</c> gives it.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The answer.</returns>
    private JsonObject Block(long height) => new()
    {
        ["hash"] = chain.HashAt(height),
        ["height"] = height,
        ["timestamp"] = chain.TimeAt(height).UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
    };

    /// <summary>Writes a transaction as <c>gettxinfo</c> gives it, with its log.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The result.</returns>
    private JsonObject Info(TestEthereumTransaction transaction)
    {
        var logs = new JsonArray();
        for (var index = 0; index < transaction.Tokens.Count; index++)
        {
            var token = transaction.Tokens[index];
            logs.Add(new JsonObject
            {
                ["address"] = chain.Token.Contract.Value,
                ["topics"] = new JsonArray(TransferTopic, Word(token.From), Word(token.To), null),
                ["data"] = "0x" + Hex(token.Units),
                ["index"] = Text(LogIndex(transaction, index)),
            });
        }

        return new JsonObject
        {
            ["hash"] = transaction.Hash,
            ["blockNumber"] = Text(transaction.Height),
            ["timeStamp"] = Seconds(transaction.Height),
            ["from"] = transaction.From.Value,
            ["to"] = transaction.To.Value,
            ["value"] = transaction.Value.ToString(CultureInfo.InvariantCulture),
            ["gasUsed"] = Text(transaction.GasUsed),
            ["gasPrice"] = Text(TestEthereumChain.GasPrice),
            ["success"] = true,
            ["logs"] = logs,
            ["transactionIndex"] = Text(transaction.Index),
        };
    }

    /// <summary>Writes an entry of an address's list of transactions.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The entry.</returns>
    private JsonObject Entry(TestEthereumTransaction transaction) => new()
    {
        ["hash"] = transaction.Hash,
        ["blockNumber"] = Text(transaction.Height),
        ["blockHash"] = chain.HashAt(transaction.Height),
        ["timeStamp"] = Seconds(transaction.Height),
        ["from"] = transaction.From.Value,
        ["to"] = transaction.To.Value,
        ["value"] = transaction.Value.ToString(CultureInfo.InvariantCulture),
        ["gasUsed"] = Text(transaction.GasUsed),
        ["gasPrice"] = Text(TestEthereumChain.GasPrice),
        ["isError"] = "0",
        ["txreceipt_status"] = Found,
        ["contractAddress"] = string.Empty,
        ["transactionIndex"] = Text(transaction.Index),
    };

    /// <summary>Writes a transaction's internal transactions, as the internal lists give them.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The entries.</returns>
    private IEnumerable<JsonObject> CallEntries(TestEthereumTransaction transaction) =>
        transaction.Calls.Select((call, index) => new JsonObject
        {
            ["transactionHash"] = transaction.Hash,
            ["blockNumber"] = Text(transaction.Height),
            ["timeStamp"] = Seconds(transaction.Height),
            ["from"] = call.From.Value,
            ["to"] = call.To.Value,
            ["value"] = call.Wei.ToString(CultureInfo.InvariantCulture),
            ["isError"] = "0",
            ["index"] = Text(index + 1),
            ["contractAddress"] = string.Empty,
            ["type"] = "call",
        });

    /// <summary>Writes a transaction's USDT movements, as an address's token list gives them.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The entries.</returns>
    private IEnumerable<JsonObject> TokenEntries(TestEthereumTransaction transaction) =>
        transaction.Tokens.Select((token, index) => new JsonObject
        {
            ["hash"] = transaction.Hash,
            ["blockNumber"] = Text(transaction.Height),
            ["blockHash"] = chain.HashAt(transaction.Height),
            ["timeStamp"] = Seconds(transaction.Height),
            ["from"] = token.From.Value,
            ["to"] = token.To.Value,
            ["value"] = token.Units.ToString(CultureInfo.InvariantCulture),
            ["contractAddress"] = chain.Token.Contract.Value,
            ["tokenSymbol"] = chain.Token.Symbol,
            ["tokenDecimal"] = Decimals,
            ["tokenName"] = chain.Token.Name,
            ["logIndex"] = Text(LogIndex(transaction, index)),
            ["transactionIndex"] = Text(transaction.Index),
        });

    /// <summary>Writes a list answer: status 1 with the entries, or status 0 when there are none.</summary>
    /// <param name="items">The entries.</param>
    /// <returns>The answer.</returns>
    private static JsonObject List(IEnumerable<JsonObject> items)
    {
        var result = new JsonArray([.. items]);
        return new JsonObject
        {
            ["status"] = result.Count > 0 ? Found : "0",
            ["message"] = result.Count > 0 ? "OK" : "No transactions found",
            ["result"] = result,
        };
    }

    /// <summary>Writes an answer with a result.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The answer.</returns>
    private static JsonObject Ok(JsonObject result) => new()
    {
        ["status"] = Found,
        ["message"] = "OK",
        ["result"] = result,
    };

    /// <summary>Writes the answer Blockscout gives for something it does not know.</summary>
    /// <param name="message">What it says.</param>
    /// <returns>The answer.</returns>
    private static JsonObject NotFound(string message) => new()
    {
        ["status"] = "0",
        ["message"] = message,
        ["result"] = null,
    };

    /// <summary>Gives a log's index in its block: after the transaction's place, so that two never meet.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="index">The log's place in the transaction.</param>
    /// <returns>The index.</returns>
    private static long LogIndex(TestEthereumTransaction transaction, int index) => (transaction.Index * 10L) + index;

    /// <summary>Writes an address as a 32-byte word (an event's topic).</summary>
    /// <param name="address">The address.</param>
    /// <returns>The word, <c>0x</c> and 64 hex digits.</returns>
    private static string Word(EthereumAddress address) => "0x" + new string('0', 24) + address.Value[2..];

    /// <summary>Writes an amount as 64 hex digits.</summary>
    /// <param name="units">The amount.</param>
    /// <returns>The digits.</returns>
    private static string Hex(BigInteger units) =>
        units.ToString("x", CultureInfo.InvariantCulture).TrimStart('0').PadLeft(64, '0');

    /// <summary>Writes a whole number as text, as the Etherscan-style API does.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Writes a block's time in seconds since 1970, as text.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The text.</returns>
    private string Seconds(long height) => Text(chain.TimeAt(height).ToUnixTimeSeconds());
}
#endif
