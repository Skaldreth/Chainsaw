using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The probe asks Cloudflare's trace and then Windows' connect test through the way out through Windows, and counts
/// only a 2xx with the expected content: a login page's redirect, own page or refused certificate is not the internet,
/// and neither is a target that does not answer in time. Nothing but the fixed request is sent.
/// </summary>
public sealed class InternetProbeTests
{
    private const string Trace = "fl=123f45\nh=1.1.1.1\nip=203.0.113.7\nts=1762550400.1\n";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly RecordingWindowsHandlerFactory _handlers = new();
    private readonly FakeTimeProvider _time = new();

    private InternetProbe Probe => new(_handlers, _time);

    [Fact]
    public async Task Cloudflare_s_trace_reaches_the_internet_with_a_bare_request()
    {
        _handlers.Answer = (_, _) => Answer(HttpStatusCode.OK, Trace);

        Assert.True(await Probe.ReachesInternetAsync(CancellationToken.None));

        var request = Assert.Single(Assert.Single(_handlers.Handlers).Requests);
        Assert.Equal(InternetProbe.CloudflareTrace, request.RequestUri);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(NetworkOptions.UserAgent, request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Referrer);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task Windows_connect_test_is_asked_when_cloudflare_does_not_answer()
    {
        _handlers.Answer = (request, _) => request.RequestUri == InternetProbe.CloudflareTrace
            ? throw new HttpRequestException("no route")
            : Answer(HttpStatusCode.OK, InternetProbe.ConnectTestContent);

        Assert.True(await Probe.ReachesInternetAsync(CancellationToken.None));

        Assert.Equal(
            [InternetProbe.CloudflareTrace, InternetProbe.WindowsConnectTest],
            _handlers.Handlers.SelectMany(handler => handler.Requests).Select(request => request.RequestUri));
    }

    [Theory]
    [InlineData("<html><body><form>Logga in på gäst-Wi-Fi</form></body></html>")]
    [InlineData("Microsoft Connect Test, please log in")]
    [InlineData("")]
    public async Task A_login_page_s_own_content_is_not_the_internet(string page)
    {
        _handlers.Answer = (_, _) => Answer(HttpStatusCode.OK, page);

        Assert.False(await Probe.ReachesInternetAsync(CancellationToken.None));
        Assert.Equal(2, _handlers.Handlers.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    [InlineData(HttpStatusCode.NetworkAuthenticationRequired)]
    public async Task A_redirect_or_refusal_is_not_followed_and_not_the_internet(HttpStatusCode status)
    {
        _handlers.Answer = (_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(Trace) };
            response.Headers.Location = new Uri("https://login.example/portal");
            return Task.FromResult(response);
        };

        Assert.False(await Probe.ReachesInternetAsync(CancellationToken.None));
        Assert.Equal(2, _handlers.Handlers.SelectMany(handler => handler.Requests).Count());
    }

    [Fact]
    public async Task A_refused_certificate_is_not_the_internet()
    {
        _handlers.Answer = (_, _) => throw new HttpRequestException(
            "The SSL connection could not be established.",
            new System.Security.Authentication.AuthenticationException("certificate"));

        Assert.False(await Probe.ReachesInternetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_body_far_larger_than_the_answer_is_not_the_internet()
    {
        _handlers.Answer = (_, _) => Answer(HttpStatusCode.OK, Trace + new string('x', 64 * 1024));

        Assert.False(await Probe.ReachesInternetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_target_that_does_not_answer_times_out_after_three_seconds()
    {
        using var entered = new SemaphoreSlim(0);
        _handlers.Answer = async (_, token) =>
        {
            entered.Release();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Never reached.");
        };

        var probing = Probe.ReachesInternetAsync(CancellationToken.None);
        Assert.True(await entered.WaitAsync(Patience));
        _time.Advance(InternetProbe.Timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(probing.IsCompleted);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(await entered.WaitAsync(Patience));
        _time.Advance(InternetProbe.Timeout);

        Assert.False(await probing.WaitAsync(Patience));
    }

    [Fact]
    public async Task Closing_chainsaw_stops_the_probe()
    {
        using var stop = new CancellationTokenSource();
        _handlers.Answer = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Never reached.");
        };

        var probing = Probe.ReachesInternetAsync(stop.Token);
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing.WaitAsync(Patience));
    }

    [Fact]
    public async Task A_pipeline_that_cannot_be_built_is_not_the_internet()
    {
        _handlers.Fails = true;

        Assert.False(await Probe.ReachesInternetAsync(CancellationToken.None));
    }

    /// <summary>Answers with a status and a body.</summary>
    private static Task<HttpResponseMessage> Answer(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
}
