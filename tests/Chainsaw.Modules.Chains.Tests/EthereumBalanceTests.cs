using System.Numerics;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Ethereum's balance for an address (ether and ERC-20 tokens) and the newest block with its hash, from made-up
/// Blockscout answers: sealed like every other response, only what the source gave, and never anything made up.
/// </summary>
public sealed class EthereumBalanceTests : IDisposable
{
    private const string NewestHash = "0x8fbd000000000000000000000000000000000000000000000000000000004b8f";

    private readonly EthereumHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string BalanceUrl => EthereumHarness.Url(
        "?module=account&action=balance&address=" + EthereumHarness.A.Value);

    private static string TokensUrl => EthereumHarness.Url(
        "?module=account&action=tokenlist&address=" + EthereumHarness.A.Value);

    private static string NewestUrl => EthereumHarness.Url("v2/blocks?type=block");

    [Fact]
    public async Task The_balance_has_the_ether_and_the_counted_tokens_known_ones_first()
    {
        AnswerBalance();

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        var balance = lookup.Value!;
        Assert.True(balance.IsComplete);
        Assert.Equal(EthereumHarness.A, balance.Address);
        Assert.Equal(new AssetAmount(Asset.Ether, 1_204_000_000_000_000_000), balance.Ether);
        Assert.Collection(
            balance.Tokens,
            usdt =>
            {
                // Known by its contract, with Chainsaw's own symbol and decimals, not the source's spelling.
                Assert.Equal(KnownTokens.EthereumUsdt, usdt.Asset);
                Assert.Equal("USDT", usdt.Asset.Symbol);
                Assert.Equal("12500.000000", usdt.ToDecimalString());
            },
            foo =>
            {
                Assert.Equal("FOO", foo.Asset.Symbol);
                Assert.Equal(EthereumHarness.Foo.Value, foo.Asset.Contract);
                Assert.Equal(18, foo.Asset.Decimals);
            });

        // The NFT and the token without decimals are not counted; the zero balance is left out.
        Assert.Equal(2, balance.Skipped);
        Assert.Equal(2, lookup.Origins.Count);
        Assert.Equal(
            ["ethereum.balance", "ethereum.token-balances"], _harness.Rows().Select(row => row.Kind));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Tokens_the_source_has_not_read_all_of_make_the_balance_incomplete()
    {
        _harness.Http.AnswerBytes(BalanceUrl, Body("""{"status":"1","message":"OK","result":"1"}"""), 200);
        var foo = Token("ERC-20", EthereumHarness.Foo.Value, "FOO", "18", "5");
        _harness.Http.AnswerBytes(TokensUrl, Body(List(foo).Replace("\"status\":\"1\"", "\"status\":\"2\"")), 200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.False(lookup.Value!.IsComplete);
        Assert.Single(lookup.Value.Tokens);
        Assert.Contains(lookup.Notes, note => note.Contains("inte läst alla", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_token_from_a_contract_chainsaw_does_not_know_is_never_taken_for_usdt_by_its_symbol()
    {
        _harness.Http.AnswerBytes(BalanceUrl, Body("""{"status":"1","message":"OK","result":"1"}"""), 200);
        var fake = Token("ERC-20", "0x" + Fixtures.Repeat("99", 20), "USDT", "6", "12500000000");
        _harness.Http.AnswerBytes(TokensUrl, Body(List(fake)), 200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        var token = Assert.Single(lookup.Value!.Tokens);
        Assert.Equal("USDT", token.Asset.Symbol);
        Assert.NotEqual(KnownTokens.EthereumUsdt, token.Asset);
        Assert.Null(KnownTokens.IssuerOf(token.Asset));
    }

    [Fact]
    public async Task Ether_above_what_a_long_holds_is_read_whole()
    {
        _harness.Http.AnswerBytes(
            BalanceUrl, Body("""{"status":"1","message":"OK","result":"123456789012345678901234567"}"""), 200);
        _harness.Http.AnswerBytes(TokensUrl, Body(List()), 200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.Equal(BigInteger.Parse("123456789012345678901234567"), lookup.Value!.Ether.Units);
        Assert.True(lookup.Value.Ether.Units > long.MaxValue);
    }

    [Fact]
    public async Task A_transaction_after_the_newest_block_sealed_again_with_its_index_is_not_fetched_again()
    {
        _harness.Http.AnswerBytes(NewestUrl, Body(Newest(1_000, NewestHash[2..])), 200);
        _harness.AnswerTransaction();
        await _harness.Source.GetTipAsync(_harness.Case);
        await _harness.Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        using var other = _harness.CreateCase("2026-0050");
        var moved = _harness.Case.Read(reader => Enumerable.Range(1, 20)
            .Select(sequence => (Response: _harness.Transfer.Read(reader, sequence),
                Index: _harness.Transfer.ReadIndex(reader, sequence)))
            .Where(each => each.Response is not null && each.Index is not null)
            .ToList());
        other.Write(transaction => moved.Select(each =>
            _harness.Transfer.Seal(transaction, each.Response!, each.Index!)).ToList());
        var sent = _harness.Http.Sent.Count;

        var lookup = await _harness.Source.GetTransactionAsync(other, EthereumHarness.H1);

        Assert.Equal(LookupStatus.FromCase, lookup.Status);
        Assert.True(lookup.Value!.IsComplete);
        Assert.Equal(sent, _harness.Http.Sent.Count);
        Assert.True(_harness.Verify(other).IsIntact);
    }

    [Fact]
    public async Task A_balance_always_changes_so_it_is_fetched_again()
    {
        AnswerBalance();
        await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        var again = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.Equal(LookupStatus.Fetched, again.Status);
        Assert.Equal(4, _harness.Http.Sent.Count);
    }

    [Fact]
    public async Task Offline_the_balance_comes_from_the_case_and_says_when_it_was_fetched()
    {
        AnswerBalance();
        await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);
        _harness.Http.Connected = false;

        var offline = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, offline.Status);
        Assert.Equal(2, offline.Value!.Tokens.Count);
        Assert.Contains("kan ha ändrats", offline.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_tokens_the_ether_is_given_and_the_balance_says_it_is_not_complete()
    {
        _harness.Http.AnswerBytes(BalanceUrl, Body("""{"status":"1","message":"OK","result":"42"}"""), 200);
        _harness.Http.AnswerBytes(TokensUrl, Body("""{"status":"0","message":"NOTOK","result":null}"""), 200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        var balance = lookup.Value!;
        Assert.Equal(42, (long)balance.Ether.Units);
        Assert.False(balance.IsComplete);
        Assert.Empty(balance.Tokens);
        Assert.Contains(
            lookup.Notes, note => note.StartsWith("Adressens tokens kunde inte läsas", StringComparison.Ordinal));
        Assert.Equal(2, lookup.Origins.Count);
    }

    [Fact]
    public async Task No_tokens_is_an_empty_list_not_a_failure()
    {
        _harness.Http.AnswerBytes(BalanceUrl, Body("""{"status":"1","message":"OK","result":"0"}"""), 200);
        _harness.Http.AnswerBytes(
            TokensUrl, Body("""{"status":"0","message":"No tokens found","result":[]}"""), 200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.True(lookup.Value!.IsComplete);
        Assert.Empty(lookup.Value.Tokens);
        Assert.True(lookup.Value.Ether.Units.IsZero);
    }

    [Theory]
    [InlineData("""{"status":"1","message":"OK","result":"-5"}""")]
    [InlineData("""{"status":"1","message":"OK","result":"0x10"}""")]
    [InlineData("""{"status":"0","message":"Invalid address hash","result":null}""")]
    [InlineData("not json")]
    public async Task A_balance_that_does_not_add_up_is_sealed_but_never_used(string body)
    {
        _harness.Http.AnswerBytes(BalanceUrl, Body(body), 200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.Null(lookup.Value);
        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Single(_harness.Rows());
        Assert.Single(_harness.Http.Sent);
    }

    [Fact]
    public async Task A_token_listed_twice_makes_the_list_unusable()
    {
        _harness.Http.AnswerBytes(BalanceUrl, Body("""{"status":"1","message":"OK","result":"1"}"""), 200);
        var foo = Token("ERC-20", EthereumHarness.Foo.Value, "FOO", "18", "5");
        _harness.Http.AnswerBytes(TokensUrl, Body(List(foo, foo)), 200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.False(lookup.Value!.IsComplete);
    }

    [Fact]
    public async Task Too_many_requests_is_waited_for_not_counted_as_a_broken_source()
    {
        _harness.Http.AnswerBytes(
            BalanceUrl,
            Body("""{"message":"Too many requests. Increase limits now","result":null,"status":"0"}"""),
            200);

        var lookup = await _harness.Source.GetBalanceAsync(_harness.Case, EthereumHarness.A);

        Assert.Null(lookup.Value);
        Assert.Equal("Källan bad Chainsaw vänta.", lookup.Problem);
    }

    [Fact]
    public async Task The_newest_block_comes_with_its_height_hash_and_time_in_one_answer()
    {
        _harness.Http.AnswerBytes(NewestUrl, Body(Newest(26_370_583, NewestHash.ToUpperInvariant()[2..])), 200);

        var lookup = await _harness.Source.GetTipAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(
            new ChainTip(26_370_583, NewestHash, new DateTimeOffset(2026, 11, 8, 13, 54, 23, TimeSpan.Zero)),
            lookup.Value);
        Assert.Equal(["ethereum.tip-block"], _harness.Rows().Select(row => row.Kind));
    }

    [Fact]
    public async Task The_newest_block_is_asked_for_every_time()
    {
        _harness.Http.AnswerBytes(NewestUrl, Body(Newest(26_370_583, NewestHash[2..])), 200);

        await _harness.Source.GetTipAsync(_harness.Case);
        await _harness.Source.GetTipAsync(_harness.Case);

        Assert.Equal(2, _harness.Http.Sent.Count);
    }

    [Theory]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"items":[{"hash":"0x12","height":5,"timestamp":"2026-11-08T13:54:23.000000Z"}]}""")]
    [InlineData("""{"next_page_params":null}""")]
    public async Task A_list_without_a_good_newest_block_is_not_used(string body)
    {
        _harness.Http.AnswerBytes(NewestUrl, Body(body), 200);

        var lookup = await _harness.Source.GetTipAsync(_harness.Case);

        Assert.Null(lookup.Value);
        Assert.Equal(LookupStatus.Failed, lookup.Status);
    }

    /// <summary>Answers the address's ether and a list of tokens with one of each kind Chainsaw meets.</summary>
    private void AnswerBalance()
    {
        _harness.Http.AnswerBytes(
            BalanceUrl, Body("""{"status":"1","message":"OK","result":"1204000000000000000"}"""), 200);
        _harness.Http.AnswerBytes(
            TokensUrl,
            Body(List(
                Token("ERC-20", EthereumHarness.Foo.Value, "FOO", "18", "7000000000000000000"),
                Token("ERC-721", "0x" + Fixtures.Repeat("66", 20), "NFT", string.Empty, "1"),
                Token("ERC-20", "0x" + Fixtures.Repeat("77", 20), "NODEC", string.Empty, "9"),
                Token("ERC-20", "0x" + Fixtures.Repeat("88", 20), "ZERO", "18", "0"),
                Token("ERC-20", KnownTokens.EthereumUsdt.Contract!, "USD₮", "6", "12500000000"))),
            200);
    }

    private static byte[] Body(string text) => Encoding.UTF8.GetBytes(text);

    private static string Token(string type, string contract, string symbol, string decimals, string balance) =>
        JsonSerializer.Serialize(new { balance, contractAddress = contract, decimals, name = symbol, symbol, type });

    private static string List(params string[] tokens) =>
        $$"""{"message":"OK","status":"1","result":[{{string.Join(",", tokens)}}]}""";

    private static string Newest(long height, string hexWithoutPrefix) => JsonSerializer.Serialize(new
    {
        items = new[]
        {
            new { hash = "0x" + hexWithoutPrefix, height, timestamp = "2026-11-08T13:54:23.000000Z" },
            new { hash = "0x" + new string('1', 64), height = height - 1, timestamp = "2026-11-08T13:54:11.000000Z" },
        },
        next_page_params = new { block_number = 1 },
    });
}
