using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Chainsaw.App.Links;

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
/// <para>
/// The pipe listens from the moment this Chainsaw becomes the one, before the host is built, and what arrives until
/// the receiver is there is held (<see cref="MessageHold"/>), so that a link from a second start during a slow start
/// is not lost. The next instance of the pipe is made before a connected sender is read, so that there is always an
/// instance of this user's, and no other user can make the pipe of that name between two senders.
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

    /// <summary>The most messages held until the receiver is there.</summary>
    private const int HeldMessages = 16;

    /// <summary>The instances of the pipe that may exist at once: the one being read and the next.</summary>
    private const int PipeInstances = 2;

    /// <summary>The messages that wait for the receiver.</summary>
    private readonly MessageHold _hold = new(HeldMessages);

    /// <summary>The mutex that this Chainsaw holds while it runs.</summary>
    private readonly Mutex _mutex;

    /// <summary>Stops the listening loop.</summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Takes the held mutex and starts listening, holding what arrives.</summary>
    /// <param name="mutex">The mutex, owned.</param>
    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        var stop = _stop.Token;
        _ = Task.Run(() => ListenAsync(_hold.Arrive, stop));
    }

    /// <summary>
    /// Becomes the one Chainsaw of this user and session, unless another already runs, and starts listening for
    /// links at once.
    /// </summary>
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

    /// <summary>Says whether a Chainsaw runs for this user and session, without becoming it.</summary>
    /// <returns><see langword="true"/> when one does.</returns>
    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting($@"Local\Chainsaw.Instance.{Identity()}", out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
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
    /// Hands every message that has arrived, and each one that arrives until disposed, to
    /// <paramref name="received"/> on the pipe's thread; the receiver posts it to the UI thread.
    /// </summary>
    /// <param name="received">Called with each message's text.</param>
    public void Listen(Action<string> received) => _hold.Attach(received);

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

    /// <summary>
    /// Accepts one sender at a time and reads its message, until stopped. The next instance of the pipe is made as
    /// soon as a sender has connected, before its message is read, so that the name is never without an instance of
    /// this user's.
    /// </summary>
    /// <param name="received">Called with each message's text.</param>
    /// <param name="stop">Stops the loop.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    private static async Task ListenAsync(Action<string> received, CancellationToken stop)
    {
        NamedPipeServerStream? waiting = null;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    waiting ??= NewInstance();
                    await waiting.WaitForConnectionAsync(stop).ConfigureAwait(false);
                    await using var connected = waiting;
                    waiting = null;
                    waiting = NewInstance();
                    if (await ReadAsync(connected, stop).ConfigureAwait(false) is { } text)
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
                    // A sender that broke off, or a pipe of that name that is not ours: wait a moment and listen
                    // again with a new instance. A stop during the wait ends the wait quietly, and the loop then ends.
                    if (waiting is not null)
                    {
                        await waiting.DisposeAsync().ConfigureAwait(false);
                        waiting = null;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1), stop)
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopped while waiting to listen again.
        }
        finally
        {
            if (waiting is not null)
            {
                await waiting.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Makes an instance of the pipe that only this Windows user can connect to.</summary>
    /// <returns>The instance, not yet connected.</returns>
    private static NamedPipeServerStream NewInstance() =>
        new(
            PipeName(),
            PipeDirection.In,
            PipeInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

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
