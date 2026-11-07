using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Starts the helper that ships with Chainsaw, next to the program. Before it is started, its SHA-256 is compared with
/// the hash Chainsaw was built with (<c>tools/Chainsaw.Tunnel/dist/chainsaw-tunnel.exe.sha256</c>, built in as a
/// resource), so that a replaced helper is never given an exit's configuration. The file stays open for reading only,
/// and closed to writers, from the check until the process has started.
/// </summary>
/// <param name="options">Where the helper is and which hash it must have; the defaults are the shipped helper.</param>
internal sealed class HelperProcessStarter(NetworkOptions options) : ITunnelProcessStarter
{
    /// <summary>The helper's file name next to the program.</summary>
    internal const string HelperFileName = "chainsaw-tunnel.exe";

    /// <summary>The name of the built-in resource with the shipped helper's SHA-256 in hex.</summary>
    internal const string HashResourceName = "Chainsaw.Modules.Network.chainsaw-tunnel.exe.sha256";

    /// <summary>The shipped helper's hash, read once; <see langword="null"/> when the resource is missing.</summary>
    private static readonly Lazy<byte[]?> ShippedHashValue = new(ReadShippedHash);

    /// <inheritdoc />
    public ITunnelProcess Start()
    {
        var path = options.TunnelHelperPath ?? Path.Combine(AppContext.BaseDirectory, HelperFileName);
        var expected = options.TunnelHelperSha256 ?? ShippedHash()
            ?? throw new TunnelHelperUnavailableException("Chainsaw was built without the helper's hash.");

        FileStream file;
        try
        {
            // Writers and deleters are shut out while the handle is open, so the file cannot change after the check.
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new TunnelHelperUnavailableException("The helper is missing or cannot be read.", exception);
        }

        using (file)
        {
            if (!MatchesHash(file, expected))
            {
                throw new TunnelHelperUnavailableException("The helper is not the copy Chainsaw was built with.");
            }

            return HelperProcess.Start(path);
        }
    }

    /// <summary>Gives the shipped helper's hash, built into Chainsaw.</summary>
    /// <returns>The hash, or <see langword="null"/> when Chainsaw was built without it.</returns>
    internal static byte[]? ShippedHash() => ShippedHashValue.Value;

    /// <summary>Tells whether a file has the expected SHA-256.</summary>
    /// <param name="file">The file, read from the start.</param>
    /// <param name="expected">The expected hash.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    internal static bool MatchesHash(Stream file, byte[] expected)
    {
        var actual = SHA256.HashData(file);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Reads the shipped helper's hash from the built-in resource.</summary>
    /// <returns>The hash, or <see langword="null"/> when the resource is missing or not a SHA-256 in hex.</returns>
    private static byte[]? ReadShippedHash()
    {
        using var stream = typeof(HelperProcessStarter).Assembly.GetManifestResourceStream(HashResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Trim();
        var hex = text.Split(' ', 2)[0];
        try
        {
            var hash = Convert.FromHexString(hex);
            return hash.Length == SHA256.HashSizeInBytes ? hash : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The helper as an operating system process.</summary>
    private sealed class HelperProcess : ITunnelProcess
    {
        /// <summary>The process.</summary>
        private readonly Process _process;

        /// <summary>Wraps a started process.</summary>
        /// <param name="process">The process.</param>
        private HelperProcess(Process process)
        {
            _process = process;
            Exited = process.WaitForExitAsync();
        }

        /// <inheritdoc />
        public Stream Input => _process.StandardInput.BaseStream;

        /// <inheritdoc />
        public Stream Output => _process.StandardOutput.BaseStream;

        /// <inheritdoc />
        public Task Exited { get; }

        /// <summary>
        /// Starts the helper without a window, a shell or arguments. Its stderr is read and dropped (it writes
        /// nothing there), so that it can never block on a full pipe.
        /// </summary>
        /// <param name="path">The helper's file.</param>
        /// <returns>The running helper.</returns>
        /// <exception cref="TunnelHelperUnavailableException">The process cannot be started.</exception>
        public static HelperProcess Start(string path)
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
            };

            var process = new Process { StartInfo = info };
            try
            {
                process.Start();
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                process.Dispose();
                throw new TunnelHelperUnavailableException("The helper cannot be started.", exception);
            }

            process.ErrorDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();
            return new HelperProcess(process);
        }

        /// <inheritdoc />
        public void Kill()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                // It has already exited, or is exiting.
            }
        }

        /// <inheritdoc />
        public void Dispose() => _process.Dispose();
    }
}
