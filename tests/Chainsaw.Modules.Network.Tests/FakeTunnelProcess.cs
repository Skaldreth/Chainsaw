using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Chainsaw.Modules.Network.Tests;

/// <summary>
/// The WireGuard helper as a process in memory: real pipes for stdin and stdout, so the connector reads and writes
/// as it would with the helper, and the test plays the helper's part on the other ends. Nothing is started and
/// nothing goes on the network.
/// </summary>
internal sealed class FakeTunnelProcess : ITunnelProcess
{
    private readonly AnonymousPipeServerStream _stdinWriter = new(PipeDirection.Out);
    private readonly AnonymousPipeClientStream _stdinReader;
    private readonly AnonymousPipeServerStream _stdoutWriter = new(PipeDirection.Out);
    private readonly AnonymousPipeClientStream _stdoutReader;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();

    /// <summary>The first line Chainsaw writes, read as soon as the helper "starts".</summary>
    private readonly Task<string?> _startLine;

    public FakeTunnelProcess()
    {
        _stdinReader = new AnonymousPipeClientStream(PipeDirection.In, _stdinWriter.ClientSafePipeHandle);
        _stdoutReader = new AnonymousPipeClientStream(PipeDirection.In, _stdoutWriter.ClientSafePipeHandle);
        StdinLines = new StreamReader(_stdinReader, Encoding.UTF8);
        _startLine = StdinLines.ReadLineAsync();
    }

    public Stream Input => _stdinWriter;

    public Stream Output => _stdoutReader;

    public Task Exited => _exited.Task;

    /// <summary>Gets the helper's side of stdin: what Chainsaw wrote.</summary>
    public StreamReader StdinLines { get; }

    public bool Killed { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>
    /// Writes a line on the helper's stdout. A helper that Chainsaw has already ended writes nothing, as a real one
    /// could not.
    /// </summary>
    public Task AnswerAsync(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_gate)
        {
            if (!_exited.Task.IsCompleted)
            {
                _stdoutWriter.Write(bytes);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Writes a JSON object on the helper's stdout.</summary>
    public Task AnswerAsync(object message) => AnswerAsync(JsonSerializer.Serialize(message));

    /// <summary>Reads Chainsaw's start line as the helper would.</summary>
    public async Task<JsonElement> ReadStartAsync()
    {
        var line = await _startLine.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(line);
        return JsonDocument.Parse(line).RootElement.Clone();
    }

    /// <summary>Makes the helper exit when its stdin closes, as the real one does.</summary>
    public void ExitWhenStdinCloses() => _ = Task.Run(async () =>
    {
        await _startLine;
        await StdinLines.ReadToEndAsync();
        Exit();
    });

    /// <summary>Ends the helper on its own: stdout closes and the process exits.</summary>
    public void Exit()
    {
        lock (_gate)
        {
            _stdoutWriter.Dispose();
        }

        _exited.TrySetResult();
    }

    public void Kill()
    {
        Killed = true;
        Exit();
    }

    public void Dispose()
    {
        Disposed = true;
        _stdinReader.Dispose();
        _stdoutReader.Dispose();
    }
}

/// <summary>A starter that hands out fake helpers, one per start.</summary>
internal sealed class FakeStarter : ITunnelProcessStarter
{
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<FakeTunnelProcess> _first =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<FakeTunnelProcess> Started { get; } = [];

    /// <summary>Gets the first helper once it is started.</summary>
    public Task<FakeTunnelProcess> First => _first.Task;

    public ITunnelProcess Start()
    {
        var process = new FakeTunnelProcess();
        lock (_gate)
        {
            Started.Add(process);
        }

        _first.TrySetResult(process);
        return process;
    }
}

/// <summary>A starter for a program without its helper.</summary>
internal sealed class MissingHelper : ITunnelProcessStarter
{
    public ITunnelProcess Start() => throw new TunnelHelperUnavailableException("The helper is missing.");
}
