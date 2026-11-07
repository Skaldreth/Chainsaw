using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Chainsaw.App.Interop;

/// <summary>
/// Keeps one Chainsaw per Windows user and session, and carries a <c>chainsaw:</c> link from a second start to the
/// running one over a named pipe (CLAUDE.md, *Öppna med en adress*).
/// </summary>
/// <remarks>
/// <para>
/// The pipe is created with <see cref="PipeOptions.CurrentUserOnly"/>: only the same Windows user can connect, and
/// the sender checks that the pipe belongs to that user, so that another user's program can neither send links nor
/// pose as Chainsaw to read them. The names carry the user's SID and the session, since pipes are seen across sessions.
/// </para>
/// <para>
/// A message is at most <see cref="MaximumMessageBytes"/> bytes of UTF-8 text, the link itself; anything longer is
/// dropped. What arrives is only text: the receiver reads it with <c>ChainsawLink</c>, which refuses everything that
/// is not exactly a link to a start point, and a link never does anything by itself.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The longest message that is read; a link is far shorter.</summary>
    public const int MaximumMessageBytes = 512;

    /// <summary>How long a second start tries to reach the running Chainsaw.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long the running Chainsaw waits for a connected sender to finish its message.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The mutex that this Chainsaw holds while it runs.</summary>
    private readonly Mutex _mutex;

    /// <summary>Stops the listening loop.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Takes the held mutex.</summary>
    /// <param name="mutex">The mutex, owned.</param>
    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Becomes the one Chainsaw of this user and session, unless another already runs.</summary>
    /// <returns>The instance, or <see langword="null"/> when another Chainsaw runs.</returns>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\Chainsaw.Instance.{Identity()}", out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Sends a text (a link, or empty to only bring the window forward) to the running Chainsaw.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it was delivered.</returns>
    public static bool Send(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaximumMessageBytes)
        {
            return false;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", PipeName(), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(ConnectTimeout);
            pipe.Write(bytes);
            pipe.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Listens for messages on a background thread until disposed, and hands each to <paramref name="received"/> on
    /// that thread; the receiver posts it to the UI thread.
    /// </summary>
    /// <param name="received">Called with each message's text.</param>
    public void Listen(Action<string> received)
    {
        ArgumentNullException.ThrowIfNull(received);
        _ = Task.Run(() => ListenAsync(received, _stop.Token));
    }

    /// <summary>Stops listening and lets go of the mutex; on the thread that acquired it.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not held by this thread; the mutex is let go when the process ends.
        }

        _mutex.Dispose();
    }

    /// <summary>Accepts one sender at a time and reads its message, until stopped.</summary>
    /// <param name="received">Called with each message's text.</param>
    /// <param name="stop">Stops the loop.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    private static async Task ListenAsync(Action<string> received, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName(), PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop);
                if (await ReadAsync(pipe, stop) is { } text)
                {
                    received(text);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A sender that broke off, or a pipe of that name that is not ours: wait a moment and listen again.
                await Task.Delay(TimeSpan.FromSeconds(1), stop).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads one message, at most <see cref="MaximumMessageBytes"/> bytes, within the read timeout.</summary>
    /// <param name="pipe">The connected pipe.</param>
    /// <param name="stop">Stops the read.</param>
    /// <returns>The text, or <see langword="null"/> when it was too long, not UTF-8 or too slow.</returns>
    private static async Task<string?> ReadAsync(Stream pipe, CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(ReadTimeout);
        var buffer = new byte[MaximumMessageBytes + 1];
        var length = 0;
        try
        {
            int read;
            while (length < buffer.Length
                && (read = await pipe.ReadAsync(buffer.AsMemory(length), timeout.Token)) > 0)
            {
                length += read;
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return null;
        }

        if (length > MaximumMessageBytes)
        {
            return null;
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(buffer, 0, length);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>Gives the pipe's name for this user and session.</summary>
    /// <returns>The name.</returns>
    private static string PipeName() => $"Chainsaw.Link.{Identity()}";

    /// <summary>Gives this user's SID and the Windows session, so that names never meet another user's.</summary>
    /// <returns>The identity.</returns>
    private static string Identity()
    {
        using var user = WindowsIdentity.GetCurrent();
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var sid = user.User?.Value ?? Environment.UserName;
        return $"{sid}.{process.SessionId}";
    }
}
