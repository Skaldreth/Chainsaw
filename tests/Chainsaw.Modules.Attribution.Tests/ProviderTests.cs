using System.Text;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// The suppliers of attribution, Chainabuse first: what is sent, how every response is sealed and how the reports
/// become labels. Synthetic addresses and reports only; the network is never used.
/// </summary>
public sealed class ProviderTests : IDisposable
{
    private const string Chainabuse = "chainabuse";

    private const string Key = "test-key";

    private static readonly string[] On = [Chainabuse];

    private readonly Harness _harness = new();

    private readonly ChainAddress _address = Synthetic.Wpkh("reported").ToChainAddress();

    public ProviderTests() => _harness.ApiKeys.Save(new ApiKey("Chainabuse", "test", Key));

    public void Dispose() => _harness.Dispose();

    private static string Url(ChainAddress address, string chain) =>
        $"https://api.chainabuse.com/v0/reports?address={address.Value}&chain={chain}&perPage=50";

    private static string Report(
        string id, string? category, string created, string address, bool trusted = false, bool @checked = false)
    {
        var categoryJson = category is null ? "null" : $"\"{category}\"";
        return $$"""
            {"id":"{{id}}","trusted":{{(trusted ? "true" : "false")}},"checked":{{(@checked ? "true" : "false")}},
             "scamCategory":{{categoryJson}},"createdAt":"{{created}}",
             "addresses":[{"address":"{{address}}","chain":"BTC","domain":null},
                          {"address":null,"chain":null,"domain":"x.example"}]}
            """;
    }

    private static string Reports(params string[] reports) =>
        $$"""{"reports":[{{string.Join(",", reports)}}],"count":{{reports.Length}}}""";

    private Task<IReadOnlyList<ProviderLookup>> Lookup(ChainAddress? address = null) =>
        _harness.Providers.LookupAsync(_harness.Case, address ?? _address, On);

    [Fact]
    public void Chainabuse_is_known_for_Bitcoin_and_Ethereum()
    {
        var info = Assert.Single(_harness.Providers.Providers);

        Assert.Equal(Chainabuse, info.Name);
        Assert.Equal([Chain.Bitcoin, Chain.Ethereum], info.Chains);
    }

    [Fact]
    public async Task Only_the_address_is_sent_with_the_key_as_Basic_in_both_fields()
    {
        _harness.Http.Answer(Url(_address, "BTC"), Reports());

        await Lookup();

        var request = Assert.Single(_harness.Http.Sent);
        Assert.Equal(Url(_address, "BTC"), request.Uri.AbsoluteUri);
        Assert.Equal("Authorization", request.Credential!.Header);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Key}:{Key}"));
        Assert.Equal("Basic " + basic, request.Credential.Value);
        Assert.DoesNotContain(Key, request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_supplier_s_report_cannot_be_marked_Sprid_inte()
    {
        _harness.Http.Answer(Url(_address, "BTC"), Reports(
            Report("r1", "PHISHING", "2026-01-02T10:00:00Z", _address.Value, trusted: true)));
        var report = Assert.Single(Assert.Single(await Lookup()).Added);

        Assert.False(_harness.Labels.SetDoNotSpread(_harness.Case, report.Sequence, doNotSpread: true));
    }

    [Fact]
    public async Task Reports_become_one_label_per_category_with_the_supplier_as_origin()
    {
        _harness.Http.Answer(Url(_address, "BTC"), Reports(
            Report("r1", "PHISHING", "2026-01-02T10:00:00Z", _address.Value, trusted: true),
            Report("r2", "PHISHING", "2026-03-04T10:00:00Z", _address.Value, @checked: true),
            Report("r3", "RANSOMWARE", "2026-02-01T10:00:00Z", _address.Value)));

        var result = Assert.Single(await Lookup());

        Assert.Equal(ProviderLookupStatus.Answered, result.Status);
        Assert.Equal(3, result.Reports);
        Assert.Equal(["Nätfiske", "Ransomware"], result.Added.Select(label => label.Name));
        var phishing = result.Added[0];
        Assert.Equal(LabelKind.Reported, phishing.Kind);
        Assert.Equal(LabelGroup.Risk, phishing.Kind.Group());
        Assert.Equal(LabelSource.Provider, phishing.Origin.Source);
        Assert.Equal("Chainabuse", phishing.Origin.SourceName);
        Assert.Contains("2 rapporter", phishing.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains("2026-01-02 – 2026-03-04", phishing.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains("r1, r2", phishing.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains($"svar {result.ResponseSequence}", phishing.Origin.SourceVersion, StringComparison.Ordinal);
        Assert.StartsWith("Underleverantören Chainabuse", phishing.Origin.Describe(), StringComparison.Ordinal);
        Assert.Equal(2, _harness.Labels.GetLabels(_harness.Case, Synthetic.Wpkh("reported")).Count);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_report_that_does_not_name_the_address_is_not_used()
    {
        var other = Synthetic.Wpkh("someone else").ToChainAddress();
        _harness.Http.Answer(
            Url(_address, "BTC"), Reports(Report("r1", "PHISHING", "2026-01-02T10:00:00Z", other.Value)));

        var result = Assert.Single(await Lookup());

        Assert.Equal(0, result.Reports);
        Assert.Empty(result.Added);
    }

    [Fact]
    public async Task Asking_again_labels_nothing_twice_but_seals_the_new_response()
    {
        _harness.Http.Answer(Url(_address, "BTC"), Reports(Report("r1", null, "2026-01-02T10:00:00Z", _address.Value)));

        var first = Assert.Single(await Lookup());
        var second = Assert.Single(await Lookup());

        Assert.Equal("Annat", Assert.Single(first.Added).Name);
        Assert.Empty(second.Added);
        Assert.Equal(1, second.Reports);
        Assert.NotEqual(first.ResponseSequence, second.ResponseSequence);
    }

    [Fact]
    public async Task A_label_taken_back_is_not_set_again()
    {
        _harness.Http.Answer(
            Url(_address, "BTC"), Reports(Report("r1", "PHISHING", "2026-01-02T10:00:00Z", _address.Value)));
        var label = Assert.Single(Assert.Single(await Lookup()).Added);

        Assert.True(_harness.Labels.Retract(_harness.Case, label.Sequence, "Fel adress i rapporten"));
        var again = Assert.Single(await Lookup());

        Assert.Empty(again.Added);
        Assert.True(_harness.Labels.GetHistory(_harness.Case, [_address]).Single().IsRetracted);
    }

    [Fact]
    public async Task Ethereum_accounts_are_asked_as_ETH_and_matched_without_regard_to_case()
    {
        var account = new ChainAddress(Chain.Ethereum, "0x" + new string('a', 40));
        _harness.Http.Answer(Url(account, "ETH"), Reports(
            Report("e1", "RUG_PULL", "2026-01-02T10:00:00Z", "0x" + new string('A', 40))));

        var result = Assert.Single(await Lookup(account));

        Assert.Equal("Rug pull", Assert.Single(result.Added).Name);
    }

    [Fact]
    public async Task Nothing_is_sent_for_a_supplier_that_is_off()
    {
        var results = await _harness.Providers.LookupAsync(_harness.Case, _address, []);

        Assert.Empty(results);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Nothing_is_sent_for_a_chain_it_is_not_asked_about()
    {
        var tron = new ChainAddress(Chain.Tron, "T" + new string('1', 33));

        var result = Assert.Single(await Lookup(tron));

        Assert.Equal(ProviderLookupStatus.NotSupported, result.Status);
        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Nothing_is_sent_without_a_key_or_with_the_vault_locked()
    {
        _harness.ApiKeys.Remove("Chainabuse");
        Assert.Equal(ProviderLookupStatus.NoKey, Assert.Single(await Lookup()).Status);

        _harness.ApiKeys.Save(new ApiKey("Chainabuse", "test", Key));
        _harness.ApiKeys.Locked = true;
        Assert.Equal(ProviderLookupStatus.NoKey, Assert.Single(await Lookup()).Status);

        Assert.Empty(_harness.Http.Sent);
    }

    [Fact]
    public async Task Nothing_is_sent_offline()
    {
        _harness.Http.Connected = false;

        var result = Assert.Single(await Lookup());

        Assert.Equal(ProviderLookupStatus.Offline, result.Status);
        Assert.Null(result.ResponseSequence);
    }

    [Theory]
    [InlineData(401, ProviderLookupStatus.Refused)]
    [InlineData(403, ProviderLookupStatus.Refused)]
    [InlineData(429, ProviderLookupStatus.RateLimited)]
    [InlineData(500, ProviderLookupStatus.Failed)]
    public async Task A_refusal_is_sealed_and_labels_nothing(int status, ProviderLookupStatus expected)
    {
        _harness.Http.Answer(Url(_address, "BTC"), """{"reason":"no"}""", status, TimeSpan.FromMinutes(30));

        var result = Assert.Single(await Lookup());

        Assert.Equal(expected, result.Status);
        Assert.NotNull(result.ResponseSequence);
        Assert.NotNull(result.Problem);
        Assert.Empty(result.Added);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task After_a_429_nothing_is_sent_until_the_wait_is_over()
    {
        _harness.Http.Answer(Url(_address, "BTC"), """{"reason":"slow down"}""", 429, TimeSpan.FromHours(1));

        await Lookup();
        var again = Assert.Single(await Lookup());

        Assert.Equal(ProviderLookupStatus.RateLimited, again.Status);
        Assert.Null(again.ResponseSequence);
        Assert.Single(_harness.Http.Sent);
    }

    [Fact]
    public async Task A_report_that_cannot_be_read_is_left_out_and_the_rest_used()
    {
        _harness.Http.Answer(Url(_address, "BTC"), Reports(
            """{"trusted":true,"addresses":[]}""",
            Report("r2", "PIG_BUTCHERING", "2026-01-02T10:00:00Z", _address.Value)));

        var result = Assert.Single(await Lookup());

        Assert.Equal("Pig butchering", Assert.Single(result.Added).Name);
    }

    [Fact]
    public async Task An_answer_that_cannot_be_read_fails_but_is_sealed()
    {
        _harness.Http.Answer(Url(_address, "BTC"), "not json");

        var result = Assert.Single(await Lookup());

        Assert.Equal(ProviderLookupStatus.Failed, result.Status);
        Assert.NotNull(result.ResponseSequence);
    }
}
