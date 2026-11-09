using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>Etherscan as a sort of API for Ethereum: its queries, its key in the address and its blocks.</summary>
public sealed class EtherscanTests : IDisposable
{
    private const string Address = "https://etherscan.test/v2/api";

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

    /// <summary>Gives a block as Etherscan's proxy answers <c>eth_getBlockByNumber</c>.</summary>
    /// <param name="height">The block's height.</param>
    /// <returns>The body.</returns>
    private static byte[] RpcBlock(long height) => Encoding.UTF8.GetBytes(
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"number\":\"0x"
        + height.ToString("x", System.Globalization.CultureInfo.InvariantCulture)
        + "\",\"hash\":\"" + Hash + "\",\"timestamp\":\"0x6553f100\"}}");
}
