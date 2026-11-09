#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Chainsaw.Modules.Chains.TestData;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// Answers Blockbook's requests from the made-up Zcash part (<see cref="TestZcashChain"/>), in this process: the test
/// case's Zcash source is asked through this instead of through the exit, so nothing about the test case ever leaves
/// the computer. A request for anything else than the made-up source's address is refused, never sent.
/// </summary>
/// <param name="clock">The clock, for the response's times.</param>
public sealed class TestZcashHttp(IClock clock) : IExitHttp
{
    /// <summary>The path of the chain's height.</summary>
    private const string Status = "api/";

    /// <summary>The part of a path that asks for the hash at a height.</summary>
    private const string IndexPart = "api/v2/block-index/";

    /// <summary>The part of a path that asks for a block.</summary>
    private const string BlockPart = "api/v2/block/";

    /// <summary>The part of a path that asks for a transaction's transparent part.</summary>
    private const string TransactionPart = "api/v2/tx/";

    /// <summary>The part of a path that asks for zcashd's own answer about a transaction.</summary>
    private const string SpecificPart = "api/v2/tx-specific/";

    /// <summary>The part of a path that asks for an address.</summary>
    private const string AddressPart = "api/v2/address/";

    /// <summary>The media type of the answers.</summary>
    private const string Json = "application/json";

    /// <summary>A transparent output's script and address, as Blockbook says it is one.</summary>
    private const string IsAddress = "isAddress";

    /// <inheritdoc/>
    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var root = TestDataChains.ZcashAddress.AbsoluteUri;
        if (!request.Uri.AbsoluteUri.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ExitException(ExitError.RequestFailed);
        }

        var now = clock.UtcNow;
        var answer = Answer(request.Uri.AbsoluteUri[root.Length..]);
        var (status, body) = answer is null
            ? (404, new JsonObject { ["error"] = "Not found" }.ToJsonString())
            : (200, answer.ToJsonString());
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, status, Json, Encoding.UTF8.GetBytes(body), now, now, ExitRoute.Windows)
        {
            IsLocal = true,
        });
    }

    /// <summary>Answers a path under the source's address.</summary>
    /// <param name="path">The path and query.</param>
    /// <returns>The answer, or <see langword="null"/> for something Blockbook does not have.</returns>
    private static JsonNode? Answer(string path)
    {
        var split = path.Split('?', 2);
        var query = HttpUtility.ParseQueryString(split.Length > 1 ? split[1] : string.Empty);
        var route = split[0];
        return route switch
        {
            Status => new JsonObject
            {
                ["blockbook"] = new JsonObject { ["coin"] = "Zcash", ["bestHeight"] = TestZcashChain.Tip },
                ["backend"] = new JsonObject(),
            },
            _ when route.StartsWith(IndexPart, StringComparison.Ordinal) =>
                HeightOf(route[IndexPart.Length..]) is { } height
                    ? new JsonObject { ["blockHash"] = TestZcashChain.BlockHash(height) }
                    : null,
            _ when route.StartsWith(BlockPart, StringComparison.Ordinal) => Block(route[BlockPart.Length..]),
            _ when route.StartsWith(SpecificPart, StringComparison.Ordinal) =>
                TestZcashChain.Find(route[SpecificPart.Length..]) is { } specific ? Specific(specific) : null,
            _ when route.StartsWith(TransactionPart, StringComparison.Ordinal) =>
                TestZcashChain.Find(route[TransactionPart.Length..]) is { } transaction ? Transaction(transaction) : null,
            _ when route.StartsWith(AddressPart, StringComparison.Ordinal) =>
                Address(Uri.UnescapeDataString(route[AddressPart.Length..]), query),
            _ => null,
        };
    }

    /// <summary>Reads a height the made-up part has.</summary>
    /// <param name="text">The height as written.</param>
    /// <returns>The height, or <see langword="null"/>.</returns>
    private static long? HeightOf(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var height)
        && height is >= TestZcashChain.FirstHeight and <= TestZcashChain.Tip
            ? height
            : null;

    /// <summary>Answers <c>api/v2/block/:hash</c>.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns>The block, or <see langword="null"/> when the part has none with that hash.</returns>
    private static JsonObject? Block(string hash)
    {
        for (var height = TestZcashChain.FirstHeight; height <= TestZcashChain.Tip; height++)
        {
            if (TestZcashChain.BlockHash(height) != hash)
            {
                continue;
            }

            var block = new JsonObject
            {
                ["hash"] = hash,
                ["height"] = height,
                ["time"] = Seconds(height),
                ["merkleRoot"] = Convert.ToHexStringLower(TestAddress.Hash("zec-merkle:" + hash)),
                ["txCount"] = 1 + TestZcashChain.Transactions.Count(transaction => transaction.Height == height),
                ["size"] = 2_000,
                ["version"] = 4,
            };
            if (height > TestZcashChain.FirstHeight)
            {
                block["previousBlockHash"] = TestZcashChain.BlockHash(height - 1);
            }

            return block;
        }

        return null;
    }

    /// <summary>
    /// Answers <c>api/v2/address/:address</c>: the summary (<c>details=basic</c>), or the transactions, newest first,
    /// within the heights <c>from</c> and <c>to</c> when asked, all on one page.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="query">The query.</param>
    /// <returns>The answer.</returns>
    private static JsonObject Address(string address, System.Collections.Specialized.NameValueCollection query)
    {
        var from = long.TryParse(query["from"], NumberStyles.None, CultureInfo.InvariantCulture, out var low)
            ? low
            : long.MinValue;
        var to = long.TryParse(query["to"], NumberStyles.None, CultureInfo.InvariantCulture, out var high)
            ? high
            : long.MaxValue;
        var history = TestZcashChain.History(address).ToList();
        var received = history.SelectMany(transaction => transaction.Outputs)
            .Where(output => output.To.Address?.Value == address).Sum(output => output.Value);
        var sent = history.SelectMany(transaction => transaction.Inputs)
            .Where(input => input.From.Address?.Value == address).Sum(input => input.Zatoshi);
        var answer = new JsonObject
        {
            ["page"] = 1,
            ["totalPages"] = 1,
            ["itemsOnPage"] = 1_000,
            ["address"] = address,
            ["balance"] = Text(received - sent),
            ["totalReceived"] = Text(received),
            ["totalSent"] = Text(sent),
            ["unconfirmedBalance"] = "0",
            ["unconfirmedTxs"] = 0,
            ["txs"] = history.Count,
        };
        if (query["details"] == "txs")
        {
            answer["transactions"] = new JsonArray(
            [
                .. history.Where(transaction => transaction.Height >= from && transaction.Height <= to)
                    .Select(Transaction),
            ]);
        }

        return answer;
    }

    /// <summary>Writes a transaction's transparent part as Blockbook's <c>api/v2/tx</c> does.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The answer.</returns>
    private static JsonObject Transaction(TestZcashTransaction transaction)
    {
        var inputs = new JsonArray();
        if (transaction.IsCoinbase)
        {
            inputs.Add(new JsonObject { ["n"] = 0, [IsAddress] = false, ["value"] = "0" });
        }

        for (var index = 0; index < transaction.Inputs.Count; index++)
        {
            var spent = transaction.Inputs[index];
            inputs.Add(new JsonObject
            {
                ["txid"] = TestZcashChain.Id(spent.Previous),
                ["vout"] = spent.Index,
                ["sequence"] = uint.MaxValue,
                ["n"] = index,
                ["addresses"] = new JsonArray(spent.From.Address!.Value),
                [IsAddress] = true,
                ["value"] = Text(spent.Zatoshi),
            });
        }

        var outputs = new JsonArray(
        [
            .. transaction.Outputs.Select((output, index) => new JsonObject
            {
                ["value"] = Text(output.Value),
                ["n"] = index,
                ["hex"] = output.To.Script,
                ["addresses"] = new JsonArray(output.To.Address!.Value),
                [IsAddress] = true,
            }),
        ]);
        return new JsonObject
        {
            ["txid"] = transaction.Txid,
            ["version"] = 5,
            ["vin"] = inputs,
            ["vout"] = outputs,
            ["blockHash"] = TestZcashChain.BlockHash(transaction.Height),
            ["blockHeight"] = transaction.Height,
            ["confirmations"] = TestZcashChain.Tip - transaction.Height + 1,
            ["blockTime"] = Seconds(transaction.Height),
            ["size"] = 400,
            ["value"] = "0",
            ["valueIn"] = "0",
            ["fees"] = "0",
        };
    }

    /// <summary>
    /// Writes zcashd's own answer about a transaction, as Blockbook's <c>api/v2/tx-specific</c> passes it on: the
    /// inputs, the outputs' amounts, the block and the Sapling pool's value balance with a spend or an output in it.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The answer.</returns>
    private static JsonObject Specific(TestZcashTransaction transaction)
    {
        var inputs = transaction.IsCoinbase
            ? new JsonArray(new JsonObject { ["coinbase"] = "03" })
            : new JsonArray(
            [
                .. transaction.Inputs.Select(spent => new JsonObject
                {
                    ["txid"] = TestZcashChain.Id(spent.Previous),
                    ["vout"] = spent.Index,
                }),
            ]);
        var outputs = new JsonArray(
        [
            .. transaction.Outputs.Select((output, index) => new JsonObject
            {
                ["valueZat"] = output.Value,
                ["n"] = index,
            }),
        ]);
        return new JsonObject
        {
            ["txid"] = transaction.Txid,
            ["locktime"] = 0,
            ["vin"] = inputs,
            ["vout"] = outputs,
            ["vjoinsplit"] = new JsonArray(),
            ["vShieldedSpend"] = Shielded(transaction.SaplingBalance > 0),
            ["vShieldedOutput"] = Shielded(transaction.SaplingBalance < 0),
            ["valueBalanceZat"] = transaction.SaplingBalance,
            ["orchard"] = new JsonObject { ["actions"] = new JsonArray(), ["valueBalanceZat"] = 0 },
            ["blockhash"] = TestZcashChain.BlockHash(transaction.Height),
            ["height"] = transaction.Height,
        };
    }

    /// <summary>Writes a list of Sapling spends or outputs: one when the money goes that way, none otherwise.</summary>
    /// <param name="any">Whether there is one.</param>
    /// <returns>The list.</returns>
    private static JsonArray Shielded(bool any) => any ? new JsonArray(new JsonObject()) : [];

    /// <summary>Writes an amount as text, as Blockbook does.</summary>
    /// <param name="value">The amount.</param>
    /// <returns>The text.</returns>
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gives a block's time in seconds since 1970.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The seconds.</returns>
    private static long Seconds(long height) => TestZcashChain.TimeOf(height).ToUnixTimeSeconds();
}
#endif
