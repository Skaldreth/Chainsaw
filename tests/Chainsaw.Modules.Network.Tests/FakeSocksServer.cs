using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// A SOCKS5 proxy on the loopback address that answers every CONNECT itself with a canned HTTP response, so that the
/// real pipeline can be tested without any network. It records the target each client asked for and the raw request.
/// </summary>
internal sealed class FakeSocksServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeSocksServer()
    {
        _listener.Start();
        _loop = AcceptLoopAsync();
    }

    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    /// <summary>Gets the user name and password the proxy demands (method 2, RFC 1929).</summary>
    public NetworkCredential Credentials { get; } = new("proxy-user", "proxy-password-1");

    /// <summary>Gets the number of clients refused for wrong or missing credentials.</summary>
    public int Refused { get; private set; }

    /// <summary>Gets the targets asked for: the address type (1 IPv4, 3 name, 4 IPv6), the host and the port.</summary>
    public List<(byte AddressType, string Host, int Port)> Targets { get; } = [];

    /// <summary>Gets the raw HTTP requests, headers and body as text.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Gets or sets the raw HTTP response to send; it should close the connection.</summary>
    public Func<int, string> Response { get; set; } = _ =>
        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException
            or ObjectDisposedException)
        {
            // Stopped.
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var greeting = await ReadExactAsync(stream, 2);
            var methods = await ReadExactAsync(stream, greeting[1]);
            if (!methods.Contains((byte)2))
            {
                Refused++;
                await stream.WriteAsync(new byte[] { 5, 0xFF });
                return;
            }

            await stream.WriteAsync(new byte[] { 5, 2 });
            await ReadExactAsync(stream, 1);
            var user = Encoding.ASCII.GetString(await ReadExactAsync(stream, (await ReadExactAsync(stream, 1))[0]));
            var password = Encoding.ASCII.GetString(
                await ReadExactAsync(stream, (await ReadExactAsync(stream, 1))[0]));
            var accepted = user == Credentials.UserName && password == Credentials.Password;
            await stream.WriteAsync(new byte[] { 1, accepted ? (byte)0 : (byte)1 });
            if (!accepted)
            {
                Refused++;
                return;
            }

            var head = await ReadExactAsync(stream, 4);
            var addressType = head[3];
            var host = addressType switch
            {
                1 => new IPAddress(await ReadExactAsync(stream, 4)).ToString(),
                4 => new IPAddress(await ReadExactAsync(stream, 16)).ToString(),
                _ => Encoding.ASCII.GetString(await ReadExactAsync(stream, (await ReadExactAsync(stream, 1))[0])),
            };
            var portBytes = await ReadExactAsync(stream, 2);
            int index;
            lock (Targets)
            {
                Targets.Add((addressType, host, (portBytes[0] << 8) | portBytes[1]));
                index = Targets.Count - 1;
            }

            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 });

            var request = await ReadHttpRequestAsync(stream);
            lock (Requests)
            {
                Requests.Add(request);
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(Response(index)));
        }
    }

    private static async Task<string> ReadHttpRequestAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        while (!EndsWithBlankLine(buffer))
        {
            buffer.Add((await ReadExactAsync(stream, 1))[0]);
        }

        var headers = Encoding.ASCII.GetString(buffer.ToArray());
        var lengthLine = headers.Split("\r\n")
            .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        var length = lengthLine is null ? 0 : int.Parse(lengthLine["Content-Length:".Length..].Trim());
        return headers + Encoding.ASCII.GetString(await ReadExactAsync(stream, length));
    }

    private static bool EndsWithBlankLine(List<byte> buffer) =>
        buffer.Count >= 4 && buffer[^4] == '\r' && buffer[^3] == '\n' && buffer[^2] == '\r' && buffer[^1] == '\n';

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes);
        return bytes;
    }
}
