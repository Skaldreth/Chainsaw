using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Starts an exit's tunnel with Chainsaw's own WireGuard helper (<c>tools/Chainsaw.Tunnel</c>): a new process per
/// tunnel, which runs WireGuard in user mode and offers it as a SOCKS5 proxy on 127.0.0.1 only. The configuration
/// and a new random user name and password are handed over on the helper's stdin, so the configuration is never
/// written to disk, a command line or a log. The helper answers with the proxy's port once the exit's server has
/// answered a handshake.
/// </summary>
/// <param name="starter">Starts the helper.</param>
/// <param name="options">How long the helper may take to start.</param>
internal sealed class WireGuardConnector(ITunnelProcessStarter starter, NetworkOptions options) : IExitConnector
{
    /// <summary>The bytes of randomness in each of the user name and the password (128 bits).</summary>
    private const int CredentialBytes = 16;

    /// <inheritdoc />
    public async Task<IExitTunnel> ConnectAsync(
        ExitInfo exit, string configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        ITunnelProcess process;
        try
        {
            process = starter.Start();
        }
        catch (TunnelHelperUnavailableException exception)
        {
            throw new ExitConnectException(ExitFailure.TunnelUnavailable, exception.Message, exception);
        }

        var credentials = new NetworkCredential(NewSecret(), NewSecret());
        var output = new TunnelOutput(process.Output);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(options.TunnelStartTimeout);
        try
        {
            // The pipes cannot always be cancelled while a read waits; ending the helper ends the read.
            using (limit.Token.UnsafeRegister(static state => ((ITunnelProcess)state!).Kill(), process))
            {
                await WriteStartAsync(process.Input, configuration, credentials, limit.Token).ConfigureAwait(false);
                var line = await output.ReadLineAsync(limit.Token).ConfigureAwait(false);
                limit.Token.ThrowIfCancellationRequested();
                var port = ReadPort(line);
                return new WireGuardTunnel(process, output, port, credentials);
            }
        }
        catch (Exception exception)
        {
            Abandon(process);
            throw Translate(exception, cancellationToken);
        }
    }

    /// <summary>Makes a new random secret for the proxy, in hex.</summary>
    /// <returns>32 hex characters.</returns>
    private static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(CredentialBytes));

    /// <summary>
    /// Writes the first line on the helper's stdin: the configuration and the credentials as one JSON object. The
    /// byte buffer is wiped afterwards; that is tidiness, not protection, since the configuration also lives in
    /// .NET strings that cannot be wiped.
    /// </summary>
    /// <param name="input">The helper's stdin.</param>
    /// <param name="configuration">The WireGuard file from the vault.</param>
    /// <param name="credentials">The proxy's user name and password.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>A task that completes when the line has been sent.</returns>
    private static async Task WriteStartAsync(
        Stream input, string configuration, NetworkCredential credentials, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        try
        {
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("configuration", configuration);
                writer.WriteString("username", credentials.UserName);
                writer.WriteString("password", credentials.Password);
                writer.WriteEndObject();
            }

            buffer.Write("\n"u8);
            await input.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsMemory(
                buffer.WrittenMemory).Span);
        }
    }

    /// <summary>Reads the helper's first line: the proxy's port, or why the tunnel could not be started.</summary>
    /// <param name="line">The line, or <see langword="null"/> when the helper ended without one.</param>
    /// <returns>The port on 127.0.0.1.</returns>
    /// <exception cref="ExitConnectException">The helper reported a failure or answered wrongly.</exception>
    internal static int ReadPort(string? line)
    {
        if (line is null)
        {
            throw new ExitConnectException(ExitFailure.ConnectFailed, "The helper ended without an answer.");
        }

        using var document = ParseLine(line);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
        {
            var code = error.GetString() ?? string.Empty;
            throw new ExitConnectException(FailureFor(code), DescribeFailure(code, root));
        }

        if (root.TryGetProperty("port", out var port) && port.TryGetInt32(out var number)
            && number is > 0 and <= IPEndPoint.MaxPort)
        {
            return number;
        }

        throw new ExitConnectException(ExitFailure.ConnectFailed, "The helper's answer has no valid port.");
    }

    /// <summary>Gives the reason for a code the helper reported (the codes are in <c>tools/Chainsaw.Tunnel</c>).
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The reason.</returns>
    internal static ExitFailure FailureFor(string code) => code switch
    {
        "configuration" => ExitFailure.ConfigurationRefused,
        "handshake" or "endpoint" => ExitFailure.NoAnswer,
        _ => ExitFailure.ConnectFailed,
    };

    /// <summary>
    /// Describes a failure the helper reported. For a refused configuration the helper's own reason is kept (the line
    /// and the key, never a value: the helper builds it only from fixed texts), so that the user can be told which
    /// line of the file is wrong.
    /// </summary>
    /// <param name="code">The helper's code.</param>
    /// <param name="root">The helper's answer.</param>
    /// <returns>The message.</returns>
    private static string DescribeFailure(string code, JsonElement root)
    {
        var message = $"The helper could not start the tunnel ({code}).";
        if (code == "configuration" && root.TryGetProperty("message", out var reason)
            && reason.ValueKind == JsonValueKind.String)
        {
            return $"{message} {reason.GetString()}";
        }

        return message;
    }

    /// <summary>Parses one of the helper's lines as a JSON object.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ExitConnectException">The line is not a JSON object.</exception>
    private static JsonDocument ParseLine(string line)
    {
        try
        {
            var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
        }
        catch (JsonException)
        {
            // Reported below.
        }

        throw new ExitConnectException(ExitFailure.ConnectFailed, "The helper's answer is not understood.");
    }

    /// <summary>Gives the exception to throw for a failed start.</summary>
    /// <param name="exception">What went wrong.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>A cancellation when the caller cancelled, otherwise an <see cref="ExitConnectException"/>.</returns>
    private static Exception Translate(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException(cancellationToken);
        }

        return exception switch
        {
            ExitConnectException connect => connect,
            OperationCanceledException => new ExitConnectException(
                ExitFailure.NoAnswer, "The helper did not answer in time.", exception),
            _ => new ExitConnectException(ExitFailure.ConnectFailed, "The helper failed to start.", exception),
        };
    }

    /// <summary>Ends a helper whose start failed and lets go of it.</summary>
    /// <param name="process">The helper.</param>
    private static void Abandon(ITunnelProcess process)
    {
        process.Kill();
        process.Dispose();
    }
}
