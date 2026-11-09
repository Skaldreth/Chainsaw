#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Chains.TestData;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// Answers TronGrid's requests from the made-up TRON part (<see cref="TestTronChain"/>), in this process: the test
/// case's TRON source is asked through this instead of through the exit, so nothing about the test case ever leaves
/// the computer. A request for anything else than the made-up source's address is refused, never sent.
/// </summary>
/// <param name="chain">The made-up part.</param>
/// <param name="clock">The clock, for the response's times and the source's own clock in a list.</param>
public sealed class TestTronHttp(TestTronChain chain, IClock clock) : IExitHttp
{
    /// <summary>
    /// The first topic of a TRC-20 <c>Transfer</c> event: the Keccak-256 of <c>Transfer(address,address,uint256)</c>,
    /// the same on every chain with Ethereum's virtual machine.
    /// </summary>
    private const string TransferTopic = "ddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

    /// <summary>The path of the newest block.</summary>
    private const string NewestBlock = "wallet/getblock?detail=false";

    /// <summary>The part of a path that asks for a block at a height.</summary>
    private const string BlockPart = "wallet/getblock";

    /// <summary>The part of a path that asks for a transaction as it was signed.</summary>
    private const string TransactionPart = "wallet/gettransactionbyid";

    /// <summary>The part of a path that asks for what a transaction did in its block.</summary>
    private const string InfoPart = "wallet/gettransactioninfobyid";

    /// <summary>The part of a path that asks for an address.</summary>
    private const string AccountPart = "v1/accounts/";

    /// <summary>The media type of the answers.</summary>
    private const string Json = "application/json";

    /// <inheritdoc/>
    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var root = TestDataTron.Address.AbsoluteUri;
        if (!request.Uri.AbsoluteUri.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ExitException(ExitError.RequestFailed);
        }

        var now = clock.UtcNow;
        var answer = Answer(request.Uri.AbsoluteUri[root.Length..]);
        var (status, body) = answer is null ? (404, "Not found") : (200, answer.ToJsonString());
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, status, Json, Encoding.UTF8.GetBytes(body), now, now, ExitRoute.Windows)
        {
            IsLocal = true,
        });
    }

    /// <summary>Answers a path under the source's address.</summary>
    /// <param name="path">The path and query.</param>
    /// <returns>The answer, or <see langword="null"/> for a path TronGrid does not have.</returns>
    private JsonNode? Answer(string path)
    {
        var split = path.Split('?', 2);
        var query = HttpUtility.ParseQueryString(split.Length > 1 ? split[1] : string.Empty);
        return split[0] switch
        {
            BlockPart when path == NewestBlock => Block(TestTronChain.Tip),
            BlockPart => long.TryParse(query["id_or_num"], NumberStyles.None, CultureInfo.InvariantCulture,
                out var height) && height <= TestTronChain.Tip ? Block(height) : new JsonObject(),
            TransactionPart => chain.Find(query["value"] ?? string.Empty) is { } signed
                ? Signed(signed)
                : new JsonObject(),
            InfoPart => chain.Find(query["value"] ?? string.Empty) is { } ran ? Info(ran) : new JsonObject(),
            _ when split[0].StartsWith(AccountPart, StringComparison.Ordinal) =>
                Account(split[0][AccountPart.Length..], query),
            _ => null,
        };
    }

    /// <summary>Answers <c>v1/accounts/:address</c> and its two lists.</summary>
    /// <param name="rest">The path after <c>v1/accounts/</c>.</param>
    /// <param name="query">The query.</param>
    /// <returns>The answer, or <see langword="null"/>.</returns>
    private JsonNode? Account(string rest, System.Collections.Specialized.NameValueCollection query)
    {
        var parts = rest.Split('/', 2);
        if (!EthereumAddress.TryParseTron(parts[0], out var address, out _))
        {
            return null;
        }

        var limit = int.TryParse(query["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out var asked)
            ? asked
            : 20;
        long? atOrBelow = long.TryParse(query["max_timestamp"], NumberStyles.None, CultureInfo.InvariantCulture,
            out var start) ? start : null;
        return parts.Length == 1
            ? Holdings(address)
            : parts[1] switch
            {
                "transactions" => List(Page(address, atOrBelow, limit, OwnList).Select(Entry)),
                "transactions/trc20" => List(Page(address, atOrBelow, limit, TokenList).Select(TokenEntry)),
                _ => null,
            };
    }

    /// <summary>
    /// Gives the newest transactions of a list that touch an address, as TronGrid pages them: newest first, at or
    /// below a block time, at most a number.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">The block time in milliseconds, or <see langword="null"/> for the newest.</param>
    /// <param name="limit">The most.</param>
    /// <param name="onList">Says whether a transaction is on the list for the address.</param>
    /// <returns>The transactions.</returns>
    private IEnumerable<TestTronTransaction> Page(
        EthereumAddress address, long? atOrBelow, int limit, Func<TestTronTransaction, EthereumAddress, bool> onList) =>
        chain.Transactions
            .Where(transaction => onList(transaction, address))
            .Where(transaction => atOrBelow is null || Milliseconds(transaction.Height) <= atOrBelow)
            .OrderByDescending(transaction => transaction.Height)
            .Take(limit);

    /// <summary>
    /// Says whether a transaction is in an address's own list: the address signed it, or it sent the address TRX.
    /// A token sent to the address by someone else's call is only in the token list, as on TronGrid.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool OwnList(TestTronTransaction transaction, EthereumAddress address) =>
        transaction.From == address || (transaction.Move == TestTronMove.Trx && transaction.To == address);

    /// <summary>Says whether a transaction is in an address's token list: it moved USDT to or from the address.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool TokenList(TestTronTransaction transaction, EthereumAddress address) =>
        transaction.Move == TestTronMove.Usdt && (transaction.From == address || transaction.To == address);

    /// <summary>Writes what an address holds now: its TRX and USDT left after the made-up transactions.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The answer; an address never used is not in it.</returns>
    private JsonNode Holdings(EthereumAddress address)
    {
        var touching = chain.Transactions
            .Where(transaction => transaction.From == address || transaction.To == address)
            .ToList();
        if (touching.Count == 0)
        {
            return List([]);
        }

        var trx = Sum(touching, address, TestTronMove.Trx)
            - touching.Where(each => each.From == address).Sum(each => each.Fee);
        var usdt = System.Numerics.BigInteger.Max(Sum(touching, address, TestTronMove.Usdt), 0);
        var account = new JsonObject
        {
            ["address"] = address.TronHex,
            ["balance"] = (long)System.Numerics.BigInteger.Max(trx, 0),
            ["trc20"] = new JsonArray(new JsonObject { [KnownTokens.TronUsdt.Contract!] = usdt.ToString() }),
        };
        return List([account]);
    }

    /// <summary>Adds what an address received less what it sent of one kind.</summary>
    /// <param name="transactions">The transactions that touch it.</param>
    /// <param name="address">The address.</param>
    /// <param name="move">The kind.</param>
    /// <returns>The sum; it may be below zero for the made-up payer, whose income is not in the story.</returns>
    private static System.Numerics.BigInteger Sum(
        IEnumerable<TestTronTransaction> transactions, EthereumAddress address, TestTronMove move) =>
        transactions.Where(each => each.Move == move).Aggregate(
            System.Numerics.BigInteger.Zero,
            (sum, each) => sum + (each.To == address ? each.Units : 0) - (each.From == address ? each.Units : 0));

    /// <summary>Writes a block's header, as <c>wallet/getblock?detail=false</c> gives it.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The answer.</returns>
    private static JsonObject Block(long height) => new()
    {
        ["blockID"] = TestTronChain.BlockId(height),
        ["block_header"] = new JsonObject
        {
            ["raw_data"] = new JsonObject
            {
                ["number"] = height,
                ["timestamp"] = Milliseconds(height),
                ["parentHash"] = TestTronChain.BlockId(height - 1),
            },
        },
    };

    /// <summary>Writes a transaction as it was signed, as <c>wallet/gettransactionbyid</c> gives it.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The answer.</returns>
    private static JsonObject Signed(TestTronTransaction transaction)
    {
        var signed = Raw(transaction);
        signed["ret"] = new JsonArray(new JsonObject { ["contractRet"] = "SUCCESS" });
        return signed;
    }

    /// <summary>Writes an entry of an address's list of transactions, with its block and the fee it paid.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The entry.</returns>
    private static JsonObject Entry(TestTronTransaction transaction)
    {
        var entry = Raw(transaction);
        entry["ret"] = new JsonArray(new JsonObject { ["contractRet"] = "SUCCESS", ["fee"] = transaction.Fee });
        entry["blockNumber"] = transaction.Height;
        entry["block_timestamp"] = Milliseconds(transaction.Height);
        entry["internal_transactions"] = new JsonArray();
        return entry;
    }

    /// <summary>Writes an entry of an address's TRC-20 list.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The entry.</returns>
    private static JsonObject TokenEntry(TestTronTransaction transaction) => new()
    {
        ["transaction_id"] = transaction.Id,
        ["token_info"] = new JsonObject
        {
            ["symbol"] = "USDT",
            ["address"] = KnownTokens.TronUsdt.Contract,
            ["decimals"] = KnownTokens.TronUsdt.Decimals,
            ["name"] = "Tether USD",
        },
        ["block_timestamp"] = Milliseconds(transaction.Height),
        ["from"] = transaction.From.Value,
        ["to"] = transaction.To.Value,
        ["type"] = "Transfer",
        ["value"] = transaction.Units.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Writes what a transaction did in its block, as <c>wallet/gettransactioninfobyid</c> gives it.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The answer.</returns>
    private static JsonObject Info(TestTronTransaction transaction)
    {
        var info = new JsonObject
        {
            ["id"] = transaction.Id,
            ["fee"] = transaction.Fee,
            ["blockNumber"] = transaction.Height,
            ["blockTimeStamp"] = Milliseconds(transaction.Height),
        };
        if (transaction.Move == TestTronMove.Usdt)
        {
            info["receipt"] = new JsonObject { ["energy_fee"] = transaction.Fee, ["result"] = "SUCCESS" };
            info["log"] = new JsonArray(new JsonObject
            {
                ["address"] = TestTronChain.UsdtContractHex[2..],
                ["topics"] = new JsonArray(TransferTopic, Word(transaction.From), Word(transaction.To)),
                ["data"] = Amount(transaction.Units),
            });
        }

        return info;
    }

    /// <summary>Writes the part of a transaction that was signed: its one contract and its parameters.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The transaction without its result.</returns>
    private static JsonObject Raw(TestTronTransaction transaction)
    {
        var (type, value) = transaction.Move == TestTronMove.Trx
            ? ("TransferContract", new JsonObject
            {
                ["amount"] = (long)transaction.Units,
                ["owner_address"] = transaction.From.TronHex,
                ["to_address"] = transaction.To.TronHex,
            })
            : ("TriggerSmartContract", new JsonObject
            {
                ["data"] = "a9059cbb" + Word(transaction.To) + Amount(transaction.Units),
                ["owner_address"] = transaction.From.TronHex,
                ["contract_address"] = TestTronChain.UsdtContractHex,
            });
        return new JsonObject
        {
            ["txID"] = transaction.Id,
            ["raw_data"] = new JsonObject
            {
                ["contract"] = new JsonArray(new JsonObject
                {
                    ["parameter"] = new JsonObject
                    {
                        ["value"] = value,
                        ["type_url"] = "type.googleapis.com/protocol." + type,
                    },
                    ["type"] = type,
                }),
                ["timestamp"] = Milliseconds(transaction.Height),
            },
        };
    }

    /// <summary>Writes a TronGrid list with the source's own clock.</summary>
    /// <param name="items">The entries.</param>
    /// <returns>The answer.</returns>
    private JsonObject List(IEnumerable<JsonNode> items)
    {
        var data = new JsonArray([.. items]);
        return new JsonObject
        {
            ["data"] = data,
            ["success"] = true,
            ["meta"] = new JsonObject
            {
                ["at"] = clock.UtcNow.ToUnixTimeMilliseconds(),
                ["page_size"] = data.Count,
            },
        };
    }

    /// <summary>Writes an address as a 32-byte word of the virtual machine (an event's topic).</summary>
    /// <param name="address">The address.</param>
    /// <returns>The word, 64 hex digits.</returns>
    private static string Word(EthereumAddress address) => new string('0', 24) + address.Hex[2..];

    /// <summary>Writes an amount as a 32-byte word of the virtual machine.</summary>
    /// <param name="units">The amount.</param>
    /// <returns>The word, 64 hex digits.</returns>
    private static string Amount(System.Numerics.BigInteger units) =>
        units.ToString("x", CultureInfo.InvariantCulture).TrimStart('0').PadLeft(64, '0');

    /// <summary>Gives a block's time in milliseconds since 1970.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The time.</returns>
    private static long Milliseconds(long height) => TestTronChain.TimeOf(height).ToUnixTimeMilliseconds();
}
#endif
