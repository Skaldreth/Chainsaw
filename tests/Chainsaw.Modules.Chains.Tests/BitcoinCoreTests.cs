using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The user's own Bitcoin Core as a source (<see cref="BitcoinCoreApi"/>): JSON-RPC 1.0 over https in the own network,
/// only for an endpoint marked local, answering the lookups whose answer is one of the chain's own values, while an
/// address's history is asked of the Esplora endpoints. The answers are synthetic, in the form of Bitcoin Core's RPC
/// documentation (<c>{"result": …, "error": null, "id": …}</c>, and an error object with <c>code</c> and
/// <c>message</c>); never the network.
/// </summary>
public sealed class BitcoinCoreTests : IDisposable
{
    private const string Node = "https://bitcoind.lan/";

    private const string Genesis = "000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f";

    private readonly SourceHarness _harness = new();

    private readonly BitcoinCoreApi _api = new(new BitcoinSourceOptions());

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("blocks/tip/height", "getblockcount")]
    [InlineData("blocks/tip/hash", "getbestblockhash")]
    [InlineData("block-height/840000", "getblockhash 840000")]
    [InlineData("block/" + Genesis + "/header", "getblockheader " + Genesis + " false")]
    [InlineData("tx/4A5E1E4BAAB89F3A32518A88C31BC87F618F76673E2CC77AB2127B7AFDEDA33B/hex",
        "getrawtransaction 4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b false")]
    public void The_lookups_whose_answer_is_the_chains_own_value_are_one_call_each(string path, string id)
    {
        Assert.True(BitcoinCoreApi.TryCall(path, out var call));
        Assert.Equal(id, call!.Id);
        Assert.True(_api.Answers(path));
    }

    [Theory]
    [InlineData("address/bc1qexample/txs")]
    [InlineData("tx/4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b")]
    [InlineData("tx/4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b/outspends")]
    [InlineData("tx/4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b/merkle-proof")]
    [InlineData("block/" + Genesis)]
    [InlineData("block-height/-1")]
    [InlineData("block/zz/header")]
    public void What_needs_an_index_or_another_form_is_not_answered(string path)
    {
        Assert.False(_api.Answers(path));
        Assert.Throws<ArgumentException>(() => _api.Request(new Uri(Node), path));
    }

    [Fact]
    public void A_call_is_a_json_rpc_post_whose_id_says_what_was_asked()
    {
        var request = _api.Request(new Uri(Node), "block-height/840000");

        Assert.Equal(ExitMethod.Post, request.Method);
        Assert.Equal(new Uri(Node), request.Uri);
        Assert.Equal("text/plain", request.ContentType);
        var root = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("1.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("getblockhash", root.GetProperty("method").GetString());
        Assert.Equal("getblockhash 840000", root.GetProperty("id").GetString());
        Assert.Equal(840000, root.GetProperty("params")[0].GetInt64());
    }

    [Fact]
    public void The_user_name_and_password_go_as_basic_and_nothing_else_is_sent()
    {
        var credential = _api.Credential("__cookie__:abc123");

        Assert.Equal("Authorization", credential.Header);
        Assert.Equal("Basic " + Convert.ToBase64String("__cookie__:abc123"u8.ToArray()), credential.Value);
        Assert.True(credential.IsWellFormed);
        Assert.False(_api.Credential("no-colon").IsWellFormed);
    }

    [Theory]
    [InlineData("""{"result":800010,"error":null,"id":"getblockcount"}""", "800010")]
    [InlineData("""{"result":"00000000000000000001","error":null,"id":"x"}""", "00000000000000000001")]
    public void An_answer_is_read_as_the_plain_text_esplora_gives(string body, string text)
    {
        Assert.Equal(text, Encoding.UTF8.GetString(BitcoinCoreApi.Result(Encoding.UTF8.GetBytes(body))));
    }

    [Theory]
    [InlineData("""{"result":null,"error":{"code":-5,"message":"Block not found"},"id":"x"}""")]
    [InlineData("""{"result":{"a":1},"error":null,"id":"x"}""")]
    [InlineData("800010")]
    public void An_error_or_another_form_is_not_an_answer(string body)
    {
        Assert.ThrowsAny<FormatException>(() => BitcoinCoreApi.Result(Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData(-5, "No such mempool transaction. Use -txindex or provide a block hash to enable blockchain transaction "
        + "queries. Use gettransaction for wallet transactions.", "CannotAnswer", "txindex")]
    [InlineData(-5, "No such mempool or blockchain transaction. Use gettransaction for wallet transactions.",
        "NotFound", null)]
    [InlineData(-5, "Block not found", "NotFound", null)]
    [InlineData(-8, "Block height out of range", "Failed", "inte kommit så långt")]
    [InlineData(-28, "Loading block index…", "Failed", "startar")]
    [InlineData(-32601, "Method not found", "Failed", "saknar anropet")]
    public void A_json_rpc_error_says_honestly_what_it_means(int code, string message, string outcome, string? words)
    {
        var body = JsonSerializer.Serialize(new { result = (string?)null, error = new { code, message }, id = "x" });

        var verdict = _api.Judge(Response(500, body));

        Assert.Equal(outcome, verdict!.Outcome.ToString());
        if (words is not null)
        {
            Assert.Contains(words, verdict.Problem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_refused_password_and_a_whitelist_are_said_and_an_answer_is_read_by_its_status()
    {
        Assert.Contains("lösenordet", _api.Judge(Response(401, string.Empty))!.Problem, StringComparison.Ordinal);
        Assert.Contains("rpcwhitelist", _api.Judge(Response(403, string.Empty))!.Problem, StringComparison.Ordinal);
        Assert.Null(_api.Judge(Response(200, """{"result":1,"error":null,"id":"getblockcount"}""")));
    }

    [Fact]
    public async Task Bitcoin_core_is_spoken_only_by_an_endpoint_marked_local_and_never_through_the_exit()
    {
        Assert.Contains(_harness.Sources.Apis(Chain.Bitcoin), api => api is { Code: BitcoinCoreApi.Code, LocalOnly: true });
        Assert.Throws<ArgumentException>(
            () => _harness.Sources.Add(Chain.Bitcoin, new Uri(Node), BitcoinCoreApi.Code, null, local: false));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Sources.TryAsync(Chain.Bitcoin, new Uri(Node), BitcoinCoreApi.Code, null, local: false));

        // An endpoint that somehow is not marked local is refused before anything goes through the exit.
        var stray = new SourceEndpoint(Guid.NewGuid(), Chain.Bitcoin, "x", new Uri("https://node.example/"),
            BitcoinCoreApi.Code, null, true);
        await Assert.ThrowsAsync<ExitException>(() => _harness.Sources.SendAsync(
            stray, _api.HeightRequest(stray.Address), default));
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public void A_file_cannot_make_bitcoin_core_an_endpoint_asked_through_the_exit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chainsaw-core-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ChainSourcesOptions.FileName), $$"""
                {"Chains": [{"Chain": "Bitcoin", "Mode": "Fastest", "Endpoints": [
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://node.example/", "Api": "bitcoin-core",
                   "Enabled": true, "Local": false},
                  {"Id": "{{Guid.NewGuid()}}", "Address": "https://bitcoind.lan/", "Api": "bitcoin-core",
                   "Enabled": true, "Local": true}]}]}
                """);
            var sources = new ChainSources(
                [new EsploraApi(new BitcoinSourceOptions()), _api], _harness.Http, null, null, _harness.Clock,
                new ChainSourcesOptions { Directory = directory });

            var kept = Assert.Single(sources.Get(Chain.Bitcoin).Endpoints).Endpoint;
            Assert.True(kept.Local);
            Assert.Equal("bitcoind.lan", kept.Address.Host);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_own_node_answers_the_height_sealed_raw_with_its_endpoint_and_marked_local()
    {
        await OwnNodeFirstAsync();
        _harness.Local.AnswerRpc(Node, "getblockcount", """{"result":800010,"error":null,"id":"getblockcount"}""");

        var lookup = await _harness.Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(800_010, lookup.Value!.Height);
        Assert.Empty(_harness.Http.Sent);
        var origin = Assert.Single(lookup.Origins);
        Assert.Equal(BitcoinCoreApi.Code, origin.Source);
        Assert.Equal("bitcoind.lan", origin.Endpoint);
        Assert.Equal(ExitResponse.LocalRouteCode, _harness.ExitColumns()[^1].Route);

        // The raw JSON-RPC answer is what is sealed, with the call's id.
        var row = _harness.RawRows()[^1];
        Assert.Contains("\"id\":\"getblockcount\"", Encoding.UTF8.GetString(row.Body), StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task An_address_is_asked_of_the_esplora_endpoints_never_of_bitcoin_core()
    {
        _harness.AnswerAll();
        await OwnNodeFirstAsync();

        var lookup = await _harness.Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.Address("wpkh"));

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Empty(_harness.Local.Sent);
        Assert.All(lookup.Origins, origin => Assert.Equal(EsploraApi.Code, origin.Source));
    }

    [Fact]
    public async Task A_transactions_bytes_come_from_the_own_node_and_check_its_description()
    {
        _harness.AnswerReal();
        await OwnNodeFirstAsync();
        var hex = Fixtures.Text("tx-real-hex.txt").Trim();
        var id = $"getrawtransaction {Fixtures.Real} false";
        _harness.Local.AnswerRpc(Node, id, $$"""{"result":"{{hex}}","error":null,"id":"{{id}}"}""");

        var lookup = await _harness.Source.VerifyTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.True(lookup.Value!.BytesVerified);
        Assert.Contains(id, _harness.Local.RpcIds);
        Assert.DoesNotContain(_harness.Http.Sent, r => r.Uri.AbsolutePath.EndsWith("/hex", StringComparison.Ordinal));
        Assert.Contains(lookup.Origins, origin => origin.Source == BitcoinCoreApi.Code);

        // Read again from the case, the sealed JSON-RPC answer still checks the description, without asking.
        var sent = _harness.Local.Sent.Count;
        var again = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Real, LookupMode.CaseOnly);
        Assert.True(again.Value!.BytesVerified);
        Assert.Equal(sent, _harness.Local.Sent.Count);
    }

    [Fact]
    public async Task Without_txindex_the_own_node_says_so_and_the_other_endpoints_give_the_bytes()
    {
        _harness.AnswerReal();
        await OwnNodeFirstAsync();
        var id = $"getrawtransaction {Fixtures.Real} false";
        _harness.Local.AnswerRpc(Node, id, $$"""
            {"result":null,"error":{"code":-5,"message":"No such mempool transaction. Use -txindex or provide a block hash to enable blockchain transaction queries. Use gettransaction for wallet transactions."},"id":"{{id}}"}
            """, status: 500);

        var lookup = await _harness.Source.VerifyTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.True(lookup.Value!.BytesVerified);
        Assert.Contains(id, _harness.Local.RpcIds);
        Assert.Contains(_harness.Http.Sent, r => r.Uri.AbsolutePath.EndsWith("/hex", StringComparison.Ordinal));

        // The refusal is sealed too (principle 1), and the own node is not taken as failing for the next lookup.
        Assert.Contains(_harness.RawRows(), row => row.Source == BitcoinCoreApi.Code && row.Status == 500);
        Assert.DoesNotContain(_harness.Sources.Get(Chain.Bitcoin).Endpoints, e => e.FailedSince);
    }

    [Fact]
    public async Task A_probe_of_bitcoin_core_asks_its_height_and_the_first_block()
    {
        var own = await AddNodeAsync();
        _harness.Local.AnswerRpc(Node, "getblockcount", """{"result":800010,"error":null,"id":"getblockcount"}""");
        _harness.Local.AnswerRpc(Node, "getblockhash 0", $$"""{"result":"{{Genesis}}","error":null,"id":"x"}""");
        _harness.Local.AnswerRpc(Node, "getblockhash 800010", $$"""{"result":"{{Genesis}}","error":null,"id":"y"}""");

        await _harness.Sources.ProbeAsync(Chain.Bitcoin);

        var probe = _harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == own.Id).Probe!;
        Assert.Equal(800_010, probe.Height);
        Assert.Equal(Genesis, probe.BlockHash);
        Assert.Contains("getblockhash 0", _harness.Local.RpcIds);
    }

    /// <summary>Adds the own Bitcoin Core, marked local, and puts it first in the user's order.</summary>
    private async Task OwnNodeFirstAsync()
    {
        var own = await AddNodeAsync();
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, own.Id, -10);
    }

    /// <summary>Adds the own Bitcoin Core, marked local.</summary>
    private async Task<SourceEndpoint> AddNodeAsync()
    {
        var (endpoint, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Node), BitcoinCoreApi.Code, null, true);
        await saved;
        return endpoint;
    }

    /// <summary>Makes a response from the own node.</summary>
    private ExitResponse Response(int status, string body)
    {
        var now = _harness.Clock.UtcNow;
        return new ExitResponse(new Uri(Node), Guid.Empty, status, "application/json", Encoding.UTF8.GetBytes(body),
            now, now, ExitRoute.Vpn) { IsLocal = true };
    }
}
