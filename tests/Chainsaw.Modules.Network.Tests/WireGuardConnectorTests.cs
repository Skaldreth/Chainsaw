using System.Net;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network.Tests;

public sealed class WireGuardConnectorTests
{
    private static readonly ExitInfo Exit = new(Guid.NewGuid(), "Exit A", "Stockholm", ExitKind.WireGuard);

    private readonly FakeStarter _starter = new();

    private WireGuardConnector Connector(TimeSpan? startTimeout = null) =>
        new(_starter, new NetworkOptions { TunnelStartTimeout = startTimeout ?? TimeSpan.FromSeconds(10) });

    /// <summary>Starts a connection and plays a helper that answers with a port.</summary>
    private async Task<(IExitTunnel Tunnel, FakeTunnelProcess Process)> ConnectAsync(bool stopsOnRequest = true)
    {
        var connecting = Connector().ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await _starter.First;
        if (stopsOnRequest)
        {
            process.ExitWhenStdinCloses();
        }

        await process.ReadStartAsync();
        await process.AnswerAsync(new { port = 40_123 });
        return (await connecting, process);
    }

    [Fact]
    public async Task The_configuration_and_new_credentials_go_over_stdin_and_the_proxy_is_on_loopback()
    {
        var connecting = Connector().ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await _starter.First;
        process.ExitWhenStdinCloses();

        var start = await process.ReadStartAsync();
        await process.AnswerAsync(new { port = 40_123 });
        await using var tunnel = await connecting;

        Assert.Equal(NetworkHarness.Configuration, start.GetProperty("configuration").GetString());
        var user = start.GetProperty("username").GetString();
        var password = start.GetProperty("password").GetString();
        Assert.Matches("^[0-9a-f]{32}$", user);
        Assert.Matches("^[0-9a-f]{32}$", password);
        Assert.NotEqual(user, password);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 40_123), tunnel.Socks5Proxy);
        Assert.Equal(user, tunnel.Socks5Credentials.UserName);
        Assert.Equal(password, tunnel.Socks5Credentials.Password);
        Assert.False(tunnel.Stopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Each_tunnel_has_its_own_credentials()
    {
        var (first, _) = await ConnectAsync();
        var second = new FakeStarter();
        var connecting = new WireGuardConnector(second, new NetworkOptions())
            .ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await second.First;
        process.ExitWhenStdinCloses();
        await process.ReadStartAsync();
        await process.AnswerAsync(new { port = 40_124 });
        var other = await connecting;

        Assert.NotEqual(first.Socks5Credentials.UserName, other.Socks5Credentials.UserName);
        Assert.NotEqual(first.Socks5Credentials.Password, other.Socks5Credentials.Password);
        await first.DisposeAsync();
        await other.DisposeAsync();
    }

    [Theory]
    [InlineData("configuration", ExitFailure.ConfigurationRefused)]
    [InlineData("handshake", ExitFailure.NoAnswer)]
    [InlineData("endpoint", ExitFailure.NoAnswer)]
    [InlineData("device", ExitFailure.ConnectFailed)]
    [InlineData("listen", ExitFailure.ConnectFailed)]
    [InlineData("start", ExitFailure.ConnectFailed)]
    public async Task A_failure_the_helper_reports_ends_the_attempt_with_its_reason(string code, ExitFailure failure)
    {
        var connecting = Connector().ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await _starter.First;
        await process.ReadStartAsync();

        await process.AnswerAsync(new { error = code, message = "line 3: PrivateKey is not a key" });

        var exception = await Assert.ThrowsAsync<ExitConnectException>(() => connecting);
        Assert.Equal(failure, exception.Failure);
        Assert.True(process.Killed);
        Assert.True(process.Disposed);
        Assert.DoesNotContain("PrivateKey = ", exception.Message, StringComparison.Ordinal);
        Assert.Equal(code == "configuration", exception.Message.Contains("line 3", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"port\":0}")]
    [InlineData("{\"port\":70000}")]
    [InlineData("{\"port\":\"80\"}")]
    [InlineData("[1080]")]
    [InlineData("not json")]
    public async Task An_answer_without_a_valid_port_fails(string answer)
    {
        var connecting = Connector().ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await _starter.First;
        await process.ReadStartAsync();

        await process.AnswerAsync(answer);

        var exception = await Assert.ThrowsAsync<ExitConnectException>(() => connecting);
        Assert.Equal(ExitFailure.ConnectFailed, exception.Failure);
        Assert.True(process.Killed);
    }

    [Fact]
    public async Task A_helper_that_ends_without_answering_fails()
    {
        var connecting = Connector().ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await _starter.First;
        await process.ReadStartAsync();

        process.Exit();

        var exception = await Assert.ThrowsAsync<ExitConnectException>(() => connecting);
        Assert.Equal(ExitFailure.ConnectFailed, exception.Failure);
    }

    [Fact]
    public async Task A_helper_that_writes_an_endless_line_fails()
    {
        var connecting = Connector().ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await _starter.First;
        await process.ReadStartAsync();

        await process.AnswerAsync(new string('x', TunnelOutput.MaximumLineBytes * 2));

        var exception = await Assert.ThrowsAsync<ExitConnectException>(() => connecting);
        Assert.Equal(ExitFailure.ConnectFailed, exception.Failure);
        Assert.True(process.Killed);
    }

    [Fact]
    public async Task A_helper_that_does_not_answer_in_time_is_ended()
    {
        var connecting = Connector(TimeSpan.FromMilliseconds(200))
            .ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None);
        var process = await _starter.First;
        await process.ReadStartAsync();

        var exception = await Assert.ThrowsAsync<ExitConnectException>(() => connecting);

        Assert.Equal(ExitFailure.NoAnswer, exception.Failure);
        Assert.True(process.Killed);
    }

    [Fact]
    public async Task A_cancelled_attempt_ends_the_helper()
    {
        using var cancel = new CancellationTokenSource();
        var connecting = Connector().ConnectAsync(Exit, NetworkHarness.Configuration, cancel.Token);
        var process = await _starter.First;
        await process.ReadStartAsync();

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.True(process.Killed);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task Without_the_helper_the_tunnel_is_unavailable()
    {
        var connector = new WireGuardConnector(new MissingHelper(), new NetworkOptions());

        var exception = await Assert.ThrowsAsync<ExitConnectException>(
            () => connector.ConnectAsync(Exit, NetworkHarness.Configuration, CancellationToken.None));

        Assert.Equal(ExitFailure.TunnelUnavailable, exception.Failure);
    }

    [Fact]
    public async Task The_tunnel_stops_when_the_helper_reports_it_fell()
    {
        var (tunnel, process) = await ConnectAsync();

        await process.AnswerAsync(new { stopped = "lost" });

        await NetworkHarness.WaitUntilAsync(() => tunnel.Stopped.IsCancellationRequested);
        await tunnel.DisposeAsync();
    }

    [Fact]
    public async Task The_tunnel_stops_when_the_helper_exits()
    {
        var (tunnel, process) = await ConnectAsync();

        process.Exit();

        await NetworkHarness.WaitUntilAsync(() => tunnel.Stopped.IsCancellationRequested);
        await tunnel.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_closes_stdin_so_the_helper_stops_and_is_not_reported_as_fallen()
    {
        var (tunnel, process) = await ConnectAsync();

        await tunnel.DisposeAsync();

        Assert.True(process.Exited.IsCompleted);
        Assert.False(process.Killed);
        Assert.True(process.Disposed);
        Assert.False(tunnel.Stopped.IsCancellationRequested);
        Assert.False(tunnel.IsRunning);
    }

    [Fact]
    public async Task The_tunnel_runs_until_the_helper_exits()
    {
        var (tunnel, process) = await ConnectAsync();
        Assert.True(tunnel.IsRunning);

        process.Exit();

        Assert.False(tunnel.IsRunning);
        await tunnel.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_ends_a_helper_that_does_not_stop()
    {
        var (tunnel, process) = await ConnectAsync(stopsOnRequest: false);

        await tunnel.DisposeAsync();

        Assert.True(process.Killed);
        Assert.False(tunnel.Stopped.IsCancellationRequested);
    }

    [Fact]
    public async Task The_exit_connects_through_the_helper_and_fails_when_the_tunnel_falls()
    {
        var harness = new NetworkHarness();
        using var connection = new ExitConnection(
            harness.Records, new WireGuardConnector(_starter, new NetworkOptions()), harness.Handlers);
        var exit = await harness.SelectedExitAsync();

        var connecting = connection.ConnectAsync();
        var process = await _starter.First;
        var start = await process.ReadStartAsync();
        await process.AnswerAsync(new { port = 40_125 });
        var status = await connecting;

        Assert.Equal(ExitState.Connected, status.State);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, 40_125), Assert.Single(harness.Handlers.Proxies));
        Assert.Equal(start.GetProperty("password").GetString(), harness.Handlers.Credentials[0].Password);
        var proxyIsTunnel = Assert.Single(harness.Handlers.ProxyChecks);
        Assert.True(proxyIsTunnel());

        process.ExitWhenStdinCloses();
        await process.AnswerAsync(new { stopped = "lost" });
        await NetworkHarness.WaitUntilAsync(() => connection.Status.State == ExitState.Failed);

        Assert.Equal(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectionLost), connection.Status);
        Assert.False(connection.TryLease(out _));
        Assert.False(proxyIsTunnel());
    }

    [Fact]
    public async Task Disconnecting_stops_the_helper()
    {
        var harness = new NetworkHarness();
        using var connection = new ExitConnection(
            harness.Records, new WireGuardConnector(_starter, new NetworkOptions()), harness.Handlers);
        await harness.SelectedExitAsync();
        var connecting = connection.ConnectAsync();
        var process = await _starter.First;
        await process.ReadStartAsync();
        await process.AnswerAsync(new { port = 40_126 });
        await connecting;
        process.ExitWhenStdinCloses();

        await connection.DisconnectAsync();

        Assert.True(process.Exited.IsCompleted);
        Assert.False(process.Killed);
        Assert.Equal(ExitState.NotConnected, connection.Status.State);
    }

    [Fact]
    public async Task The_output_reader_splits_lines_across_reads_and_drops_carriage_returns()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"port\":1}\r\n{\"stopped\":\"lost\"}\n"));
        var output = new TunnelOutput(new TrickleStream(stream));

        Assert.Equal("{\"port\":1}", await output.ReadLineAsync(CancellationToken.None));
        Assert.Equal("{\"stopped\":\"lost\"}", await output.ReadLineAsync(CancellationToken.None));
        Assert.Null(await output.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public void The_shipped_helper_matches_the_hash_chainsaw_is_built_with()
    {
        var expected = HelperProcessStarter.ShippedHash();
        var helper = Path.Combine(RepositoryRoot(), "tools", "Chainsaw.Tunnel", "dist", "chainsaw-tunnel.exe");

        using var file = File.OpenRead(helper);

        Assert.NotNull(expected);
        Assert.True(HelperProcessStarter.MatchesHash(file, expected));
    }

    [Fact]
    public void A_missing_helper_is_unavailable()
    {
        var starter = new HelperProcessStarter(new NetworkOptions
        {
            TunnelHelperPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"),
            TunnelHelperSha256 = new byte[32],
        });

        Assert.Throws<TunnelHelperUnavailableException>(() => starter.Start());
    }

    [Fact]
    public void A_replaced_helper_is_never_started()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        File.WriteAllText(path, "not the helper");
        try
        {
            var starter = new HelperProcessStarter(new NetworkOptions
            {
                TunnelHelperPath = path,
                TunnelHelperSha256 = SHA256.HashData("the helper"u8),
            });

            var exception = Assert.Throws<TunnelHelperUnavailableException>(() => starter.Start());

            Assert.Contains("not the copy", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Finds the repository's root from the test's output directory.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Chainsaw.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>A stream that gives at most three bytes per read, as a pipe may.</summary>
    private sealed class TrickleStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, 3));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
