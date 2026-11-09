using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>Etherscan as a sort of API for Ethereum: its queries, its key in the address and its blocks.</summary>
public sealed class EtherscanTests : IDisposable
{
    private const string Address = "https://etherscan.test/v2/api/";

    private const string Hash = "0x8c1e00000000000000000000000000000000000000000000000000000000a04d";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-etherscan-" + Guid.NewGuid().ToString("N"));

    private readonly FakeClock _clock = new();

    private readonly FakeKeys _keys = new();

    private readonly FakeExitHttp _http;

    /// <summary>Creates the test with a fake exit.</summary>
    public EtherscanTests() => _http = new FakeExitHttp(_clock);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("?module=block&action=eth_block_number", "module=proxy&action=eth_blockNumber")]
    [InlineData("v2/blocks/900", "module=proxy&action=eth_getBlockByNumber&tag=0x384&boolean=false")]
    [InlineData("v2/blocks?type=block", "module=proxy&action=eth_getBlockByNumber&tag=latest&boolean=false")]
    [InlineData("?module=account&action=balance&address=0xab", "module=account&action=balance&address=0xab&tag=latest")]
    [InlineData("?module=account&action=txlist&address=0xab&sort=desc&page=1&offset=100",
        "module=account&action=txlist&address=0xab&sort=desc&page=1&offset=100")]
    public void Blockscout_s_paths_become_the_queries_Etherscan_answers_alike(string path, string query) =>
        Assert.Equal(query, EtherscanApi.Translate(path));

    [Fact]
    public void A_request_names_Ethereum_s_chain_id_and_never_carries_the_key()
    {
        var api = new EtherscanApi(new EthereumSourceOptions());

        var request = api.Request(new Uri(Address), "?module=account&action=txlistinternal&txhash=0xab");

        Assert.Equal(
            Address + "?chainid=1&module=account&action=txlistinternal&txhash=0xab", request.Uri.AbsoluteUri);
        Assert.Null(request.Credential);
    }

    [Fact]
    public void The_key_goes_as_the_apikey_parameter()
    {
        var credential = new EtherscanApi(new EthereumSourceOptions()).Credential("S3cretKey");

        Assert.True(credential.InQuery);
        Assert.Equal("apikey", credential.Header);
        Assert.True(credential.IsWellFormed);
        Assert.DoesNotContain("S3cretKey", credential.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("?module=transaction&action=gettxinfo&txhash=0xab", false)]
    [InlineData("?module=account&action=tokenlist&address=0xab", false)]
    [InlineData("?module=token&action=getToken&contractaddress=0xab", false)]
    [InlineData("?module=account&action=tokentx&address=0xab&sort=desc&page=1&offset=100", true)]
    [InlineData("v2/blocks/900", true)]
    public void What_a_free_key_does_not_give_is_left_to_the_other_endpoints(string path, bool answers) =>
        Assert.Equal(answers, new EtherscanApi(new EthereumSourceOptions()).Answers(path));

    [Fact]
    public void The_default_endpoint_is_off_and_paced_for_a_free_key()
    {
        var endpoint = Assert.Single(new EtherscanApi(new EthereumSourceOptions()).Defaults);

        Assert.False(endpoint.Enabled);
        Assert.Equal(Chain.Ethereum, endpoint.Chain);
        Assert.Equal(EtherscanApi.KeyService, endpoint.ApiKey);
        Assert.Equal(EtherscanApi.FreeKeyPace, endpoint.Interval);
    }

    [Fact]
    public void A_block_from_the_JSON_RPC_proxy_is_read_with_its_height_hash_and_time()
    {
        var block = BlockscoutParser.ParseBlock(RpcBlock(900), 900);

        Assert.Equal(900, block.Height);
        Assert.Equal(Hash, block.Hash);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), block.Time);
        Assert.Throws<FormatException>(() => BlockscoutParser.ParseBlock(RpcBlock(901), 900));
        Assert.Equal(900, BlockscoutParser.ParseNewestBlock(RpcBlock(900)).Height);
    }

    [Fact]
    public async Task A_probe_sends_the_key_beside_the_address_and_the_address_stays_without_it()
    {
        _keys.Save(new ApiKey(EtherscanApi.KeyService, string.Empty, "S3cretKey"));
        var sources = new ChainSources(
            [new EtherscanApi(new EthereumSourceOptions { Etherscan = new Uri(Address) })], _http, _keys,
            new FakeVault(), _clock, new ChainSourcesOptions { Directory = _directory }, new FakeLocalHttp(_clock),
            _clock.Delay);
        var endpoint = Assert.Single(sources.Get(Chain.Ethereum).Endpoints).Endpoint;
        await sources.SetEnabledAsync(Chain.Ethereum, endpoint.Id, true);
        _http.AnswerBytes(Address + "?chainid=1&module=proxy&action=eth_blockNumber",
            Encoding.ASCII.GetBytes("""{"jsonrpc":"2.0","id":83,"result":"0x384"}"""), 200);
        _http.AnswerBytes(
            Address + "?chainid=1&module=proxy&action=eth_getBlockByNumber&tag=0x384&boolean=false",
            RpcBlock(900), 200);

        await sources.ProbeAsync(Chain.Ethereum);

        Assert.NotEmpty(_http.Sent);
        Assert.All(_http.Sent, request =>
        {
            Assert.Equal(ExitCredential.Query("apikey", "S3cretKey"), request.Credential);
            Assert.DoesNotContain("S3cretKey", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        });
        Assert.Equal(ProbeOutcome.Answered, sources.Get(Chain.Ethereum).Endpoints[0].Probe!.Outcome);
    }

    [Fact]
    public void A_transaction_s_internal_list_without_the_transaction_named_is_read()
    {
        var hash = EthereumTxHash.Parse("0x" + string.Concat(Enumerable.Repeat("b1", 32)));
        var body = Encoding.UTF8.GetBytes(
            "{\"status\":\"1\",\"message\":\"OK\",\"result\":[{\"blockNumber\":\"900\",\"timeStamp\":\"1700000000\","
            + "\"from\":\"0x" + string.Concat(Enumerable.Repeat("11", 20)) + "\",\"to\":\"0x"
            + string.Concat(Enumerable.Repeat("22", 20)) + "\",\"value\":\"1000\",\"contractAddress\":\"\","
            + "\"input\":\"\",\"type\":\"call\",\"gas\":\"2300\",\"gasUsed\":\"0\",\"isError\":\"0\",\"errCode\":\"\"}]}");

        var calls = BlockscoutParser.ParseInternal(body, hash);

        Assert.Single(calls.Calls);
    }

    [Fact]
    public void A_rate_limit_from_the_proxy_is_a_wait_not_a_failure()
    {
        var body = Encoding.UTF8.GetBytes(
            "{\"status\":\"0\",\"message\":\"NOTOK\",\"result\":\"Max calls per sec rate limit reached (3/sec)\"}");

        Assert.Throws<SourceBusyException>(() => BlockscoutParser.ParseHeight(body));
        Assert.Throws<SourceBusyException>(() => BlockscoutParser.ParseBlock(body, 900));
    }

    [Fact]
    public void A_block_the_proxy_does_not_have_yet_is_not_found()
    {
        var body = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":null}");

        Assert.Throws<SourceNotFoundException>(() => BlockscoutParser.ParseBlock(body, 900));
    }

    [Theory]
    [InlineData("v2/addresses/0xab")]
    [InlineData("v2/blocks/x")]
    public void A_path_Etherscan_has_no_query_for_is_not_claimed(string path) =>
        Assert.False(new EtherscanApi(new EthereumSourceOptions()).Answers(path));

    [Theory]
    [InlineData("etherscan-txlist.json", EthereumHistory.Transactions)]
    [InlineData("etherscan-txlistinternal.json", EthereumHistory.Internal)]
    [InlineData("etherscan-tokentx.json", EthereumHistory.Tokens)]
    public void Etherscan_s_real_history_pages_are_read(string file, EthereumHistory history)
    {
        var page = BlockscoutParser.ParseHistory(
            Real(file), EthereumAddress.Parse("0xde0b295669a9fd93d5f28d9ec85e40f4cb697bae"), history, null, 50);

        Assert.NotEmpty(page.Transfers.Count > 0 ? page.Transfers : page.Fees);
        Assert.False(page.IsIncomplete);
    }

    [Fact]
    public void Etherscan_s_real_height_and_block_are_read()
    {
        Assert.True(BlockscoutParser.ParseHeight(Real("etherscan-height.json")).Height > 26_000_000);
        Assert.StartsWith("0x", BlockscoutParser.ParseBlock(Real("etherscan-block.json"), 0x1927f00).Hash,
            StringComparison.Ordinal);
        Assert.Equal(
            "0xd4e56740f876aef8c010b86a40d5f56745a118d0906a34e69aec8c0db1cb8fa3",
            BlockscoutParser.ParseBlock(Real("etherscan-genesis.json"), 0).Hash);
    }

    [Fact]
    public void A_refused_key_is_said_in_the_source_s_own_words()
    {
        var body = Encoding.ASCII.GetBytes("""{"status":"0","message":"NOTOK","result":"Missing/Invalid API Key"}""");

        var refused = Assert.Throws<SourceKeyRefusedException>(() => BlockscoutParser.ParseHeight(body));

        Assert.Equal("Missing/Invalid API Key", refused.Said);
        Assert.Contains("Missing/Invalid API Key", SourceLookups.KeyRefusedText(refused.Said), StringComparison.Ordinal);
    }

    private static byte[] Real(string file) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ethereum", file));

    [Fact]
    public async Task A_built_in_endpoint_added_later_comes_to_a_saved_chain_once()
    {
        // A file from before Etherscan was built in: Ethereum saved with Blockscout only.
        var options = new EthereumSourceOptions();
        var blockscout = new BlockscoutApi(options);
        var before = Sources([blockscout]);
        await before.SetModeAsync(Chain.Ethereum, SourceMode.Priority);

        var after = Sources([blockscout, new EtherscanApi(options)]);
        var etherscan = Etherscan(after);
        Assert.Equal(SourceMode.Priority, after.Get(Chain.Ethereum).Mode);
        await after.RemoveAsync(Chain.Ethereum, etherscan.Id);

        var again = Sources([blockscout, new EtherscanApi(options)]);
        Assert.DoesNotContain(again.Get(Chain.Ethereum).Endpoints, view => view.Endpoint.Id == etherscan.Id);
    }

    private ChainSources Sources(IReadOnlyList<ISourceApi> apis) => new(
        apis, _http, _keys, new FakeVault(), _clock, new ChainSourcesOptions { Directory = _directory },
        new FakeLocalHttp(_clock), _clock.Delay);

    [Fact]
    public async Task A_saved_key_switches_on_the_endpoint_that_needs_it()
    {
        var sources = new ChainSources(
            [new EtherscanApi(new EthereumSourceOptions())], _http, _keys, new FakeVault(), _clock,
            new ChainSourcesOptions { Directory = _directory }, new FakeLocalHttp(_clock), _clock.Delay);
        Assert.False(Etherscan(sources).Enabled);

        await sources.EnableForKeyAsync("Chainabuse");
        Assert.False(Etherscan(sources).Enabled);
        await sources.EnableForKeyAsync("etherscan");

        Assert.True(Etherscan(sources).Enabled);
    }

    private static SourceEndpoint Etherscan(ChainSources sources) => sources.Get(Chain.Ethereum).Endpoints
        .Select(view => view.Endpoint).Single(endpoint => endpoint.ApiKey == EtherscanApi.KeyService);

    [Fact]
    public void An_Etherscan_endpoint_the_user_adds_keeps_the_free_key_s_pace()
    {
        var sources = new ChainSources(
            [new EtherscanApi(new EthereumSourceOptions())], _http, _keys, new FakeVault(), _clock,
            new ChainSourcesOptions { Directory = _directory }, new FakeLocalHttp(_clock), _clock.Delay);

        var (endpoint, _) = sources.Add(Chain.Ethereum, new Uri("https://other.test/v2/api"), "etherscan", "Etherscan");

        Assert.Equal(EtherscanApi.FreeKeyPace, endpoint.Interval);
    }

    /// <summary>Gives a block as Etherscan's proxy answers <c>eth_getBlockByNumber</c>.</summary>
    /// <param name="height">The block's height.</param>
    /// <returns>The body.</returns>
    private static byte[] RpcBlock(long height) => Encoding.UTF8.GetBytes(
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"number\":\"0x"
        + height.ToString("x", System.Globalization.CultureInfo.InvariantCulture)
        + "\",\"hash\":\"" + Hash + "\",\"timestamp\":\"0x6553f100\"}}");
}
