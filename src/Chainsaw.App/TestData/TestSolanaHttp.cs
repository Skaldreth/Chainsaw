#if DEBUG
using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Chains.TestData;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// Answers Solana's JSON-RPC calls from the made-up Solana part (<see cref="TestSolanaChain"/>), in this process: the
/// test case's Solana source is asked through this instead of through the exit, so nothing about the test case ever
/// leaves the computer. A request for anything else than the made-up source's address is refused, never sent.
/// </summary>
/// <param name="clock">The clock, for the response's times.</param>
public sealed class TestSolanaHttp(IClock clock) : IExitHttp
{
    /// <summary>The media type of the answers.</summary>
    private const string Json = "application/json";

    /// <summary>What every answer's status says: the slot is final.</summary>
    private const string Finalized = "finalized";

    /// <summary>The JSON-RPC error code a node gives for a method it does not have.</summary>
    private const int MethodNotFound = -32601;

    /// <inheritdoc/>
    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.Uri.AbsoluteUri.StartsWith(TestDataChains.SolanaAddress.AbsoluteUri, StringComparison.Ordinal)
            || request.Body.IsEmpty)
        {
            throw new ExitException(ExitError.RequestFailed);
        }

        var call = JsonNode.Parse(request.Body.Span)!.AsObject();
        var parameters = call["params"]?.AsArray() ?? [];
        var answer = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1 };
        if (ResultOf((string?)call["method"], parameters) is { } result)
        {
            answer["result"] = result.Value;
        }
        else
        {
            answer["error"] = new JsonObject { ["code"] = MethodNotFound, ["message"] = "Method not found" };
        }

        var now = clock.UtcNow;
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, 200, Json, Encoding.UTF8.GetBytes(answer.ToJsonString()), now, now,
            ExitRoute.Windows)
        {
            IsLocal = true,
        });
    }

    /// <summary>Answers one call.</summary>
    /// <param name="method">The method.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <returns>
    /// The result (which may be JSON's null), or <see langword="null"/> for a method the part does not have.
    /// </returns>
    private static Result? ResultOf(string? method, JsonArray parameters) => method switch
    {
        "getSlot" => new(TestSolanaChain.Tip),
        "getGenesisHash" => new(TestSolanaChain.BlockHash(0)),
        "getBlock" => new(Block((long)parameters[0]!)),
        "getTransaction" => new(TestSolanaChain.Find((string)parameters[0]!) is { } found ? Transaction(found) : null),
        "getSignaturesForAddress" => new(Signatures((string)parameters[0]!, parameters[1]?.AsObject())),
        "getBalance" => new(Context(Held((string)parameters[0]!, TestSolanaChain.Tip + 1))),
        "getTokenAccountsByOwner" => new(Context(new JsonArray())),
        _ => null,
    };

    /// <summary>Writes a slot's block as <c>getBlock</c> gives it without its transactions.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The block, or JSON's null for a slot the part does not have.</returns>
    private static JsonObject? Block(long slot) => slot is < TestSolanaChain.FirstSlot or > TestSolanaChain.Tip
        ? null
        : new JsonObject
        {
            ["blockHeight"] = slot - 20_000_000,
            ["blockTime"] = TestSolanaChain.TimeOf(slot).ToUnixTimeSeconds(),
            ["blockhash"] = TestSolanaChain.BlockHash(slot),
            ["parentSlot"] = slot - 1,
            ["previousBlockhash"] = TestSolanaChain.BlockHash(slot - 1),
        };

    /// <summary>
    /// Writes an address's signatures as <c>getSignaturesForAddress</c> gives them: newest first, older than the
    /// signature named <c>before</c>, at most <c>limit</c>.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="options">The options.</param>
    /// <returns>The entries.</returns>
    private static JsonArray Signatures(string address, JsonObject? options)
    {
        var limit = (int?)options?["limit"] ?? 1_000;
        var before = (string?)options?["before"];
        var touching = TestSolanaChain.Transactions
            .Where(transaction => transaction.From.Value == address || transaction.To.Value == address)
            .OrderByDescending(transaction => transaction.Slot)
            .ToList();
        var start = before is null ? 0 : touching.FindIndex(transaction => transaction.Signature == before) + 1;
        return new JsonArray(
        [
            .. touching.Skip(start).Take(limit).Select(transaction => new JsonObject
            {
                ["blockTime"] = TestSolanaChain.TimeOf(transaction.Slot).ToUnixTimeSeconds(),
                ["confirmationStatus"] = Finalized,
                ["err"] = null,
                ["memo"] = null,
                ["signature"] = transaction.Signature,
                ["slot"] = transaction.Slot,
                ["transactionIndex"] = 3,
            }),
        ]);
    }

    /// <summary>Writes a transaction as <c>getTransaction</c> gives it with <c>jsonParsed</c>.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The transaction.</returns>
    private static JsonObject Transaction(TestSolanaTransaction transaction)
    {
        var fromBefore = TestSolanaChain.HeldBefore(transaction.From, transaction.Slot);
        var toBefore = TestSolanaChain.HeldBefore(transaction.To, transaction.Slot);
        return new JsonObject
        {
            ["blockTime"] = TestSolanaChain.TimeOf(transaction.Slot).ToUnixTimeSeconds(),
            ["meta"] = new JsonObject
            {
                ["computeUnitsConsumed"] = 450,
                ["err"] = null,
                ["fee"] = TestSolanaChain.Fee,
                ["innerInstructions"] = new JsonArray(),
                ["logMessages"] = new JsonArray(),
                ["preBalances"] = new JsonArray(fromBefore, toBefore, 1),
                ["postBalances"] = new JsonArray(
                    fromBefore - transaction.Lamports - TestSolanaChain.Fee, toBefore + transaction.Lamports, 1),
                ["preTokenBalances"] = new JsonArray(),
                ["postTokenBalances"] = new JsonArray(),
                ["rewards"] = new JsonArray(),
                ["status"] = new JsonObject { ["Ok"] = null },
            },
            ["slot"] = transaction.Slot,
            ["transaction"] = new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["accountKeys"] = new JsonArray(
                        Key(transaction.From.Value, signer: true),
                        Key(transaction.To.Value, signer: false),
                        Key(TestSolanaChain.SystemProgram, signer: false)),
                    ["instructions"] = new JsonArray(new JsonObject
                    {
                        ["parsed"] = new JsonObject
                        {
                            ["info"] = new JsonObject
                            {
                                ["destination"] = transaction.To.Value,
                                ["lamports"] = transaction.Lamports,
                                ["source"] = transaction.From.Value,
                            },
                            ["type"] = "transfer",
                        },
                        ["program"] = "system",
                        ["programId"] = TestSolanaChain.SystemProgram,
                        ["stackHeight"] = 1,
                    }),
                    ["recentBlockhash"] = TestSolanaChain.BlockHash(transaction.Slot - 1),
                },
                ["signatures"] = new JsonArray(transaction.Signature),
            },
            ["transactionIndex"] = 3,
            ["version"] = "legacy",
        };
    }

    /// <summary>Writes an account key as <c>jsonParsed</c> lists it.</summary>
    /// <param name="pubkey">The account.</param>
    /// <param name="signer">Whether it signed.</param>
    /// <returns>The key.</returns>
    private static JsonObject Key(string pubkey, bool signer) => new()
    {
        ["pubkey"] = pubkey,
        ["signer"] = signer,
        ["source"] = "transaction",
        ["writable"] = true,
    };

    /// <summary>Gives what an address holds before a slot, in lamports.</summary>
    /// <param name="address">The address, as written.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The lamports; 0 for an address that is not Solana's.</returns>
    private static long Held(string address, long slot) =>
        EthereumAddress.TryParse(Chain.Solana, address, out var parsed) ? TestSolanaChain.HeldBefore(parsed, slot) : 0;

    /// <summary>Writes a value with the context of the slot it was read at, as the node does.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The result.</returns>
    private static JsonObject Context(JsonNode value) => new()
    {
        ["context"] = new JsonObject { ["apiVersion"] = "4.4.0", ["slot"] = TestSolanaChain.Tip },
        ["value"] = value,
    };

    /// <summary>A call's result, which may be JSON's null (a transaction or a block the part does not have).</summary>
    /// <param name="Value">The result.</param>
    private sealed record Result(JsonNode? Value);
}
#endif
