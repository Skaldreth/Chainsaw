using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>A vault that is unlocked unless the test locks it.</summary>
internal sealed class FakeVault : IVault
{
    public VaultState State { get; set; } = VaultState.Unlocked;

    public event EventHandler<VaultState>? StateChanged
    {
        add { }
        remove { }
    }

    public void Create(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Unlock(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Lock() => throw new NotSupportedException();

    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey) =>
        throw new NotSupportedException();
}

/// <summary>Purpose keys from a fixed secret.</summary>
internal sealed class FakePurposeKeys : IPurposeKeys
{
    public void DeriveKey(string purpose, Span<byte> destination)
    {
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes("chains-tests"));
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, destination, [], Encoding.ASCII.GetBytes(purpose));
    }
}

/// <summary>An operator profile in memory, or none, or a locked vault.</summary>
internal sealed class FakeProfiles : IOperatorProfileStore
{
    public OperatorProfile? Profile { get; set; } = new("Testa Testsson", "Utredare", "Enheten");

    public bool Locked { get; set; }

    public OperatorProfile? Get() => Locked ? throw new VaultException(VaultError.Locked) : Profile;

    public void Save(OperatorProfile profile) => Profile = profile;
}

/// <summary>A clock the test moves.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 1, 2, 3, TimeSpan.Zero);

    /// <summary>Every wait for an endpoint's turn, in order.</summary>
    public List<TimeSpan> Waits { get; } = [];

    /// <summary>Waits by moving the clock, at once, and records the wait.</summary>
    /// <param name="wait">How long.</param>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>A completed task.</returns>
    public Task Delay(TimeSpan wait, CancellationToken cancellationToken)
    {
        lock (Waits)
        {
            Waits.Add(wait);
            UtcNow += wait;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The exit, without any network: answers come from a table the test fills, every request that would have been sent
/// is recorded, and without a connected exit every request is refused as the real one does.
/// </summary>
internal sealed class FakeExitHttp(FakeClock clock) : IExitHttp
{
    public static readonly Guid ExitId = Guid.Parse("0b9e1e4a-5c39-4a8e-9a55-3f1f6a1c0001");

    private readonly Dictionary<string, (int Status, byte[] Body, string? ContentType)> _answers = [];

    /// <summary>The <c>Retry-After</c> each address answers with, when the test gives one.</summary>
    private readonly Dictionary<string, TimeSpan> _retryAfter = [];

    public bool Connected { get; set; } = true;

    public ExitRoute Route { get; set; } = ExitRoute.Vpn;

    public ExitError? Failure { get; set; }

    /// <summary>The time each host takes to answer, in milliseconds; 120 when not given.</summary>
    public Dictionary<string, int> Milliseconds { get; } = [];

    /// <summary>Hosts whose requests fail on the way, as a dead endpoint's do.</summary>
    public HashSet<string> FailingHosts { get; } = [];

    /// <summary>A task every request waits for before it answers, or <see langword="null"/>.</summary>
    public Task? Gate { get; set; }

    public List<ExitRequest> Sent { get; } = [];

    public int Refused { get; private set; }

    public void Answer(string url, string fixture, int status = 200) =>
        _answers[url] = (status, Fixtures.Bytes(fixture), fixture.EndsWith(".txt", StringComparison.Ordinal)
            ? "text/plain" : "application/json");

    public void AnswerBytes(string url, byte[] body, int status) => _answers[url] = (status, body, "text/plain");

    /// <summary>Answers an address with a status that asks Chainsaw to wait, with or without <c>Retry-After</c>.</summary>
    public void AnswerBusy(string url, TimeSpan? retryAfter, int status = 429)
    {
        _answers[url] = (status, "slow down"u8.ToArray(), "text/plain");
        if (retryAfter is { } wait)
        {
            _retryAfter[url] = wait;
        }
        else
        {
            _retryAfter.Remove(url);
        }
    }

    public async Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        if (Gate is { } gate)
        {
            await gate;
        }

        if (!Connected)
        {
            Refused++;
            throw new ExitNotConnectedException();
        }

        Sent.Add(request);
        if (Failure is { } failure)
        {
            throw new ExitException(failure);
        }

        if (FailingHosts.Contains(request.Uri.Host))
        {
            throw new ExitException(ExitError.RequestFailed);
        }

        var url = request.Uri.AbsoluteUri;
        var (status, body, contentType) = _answers.TryGetValue(url, out var answer)
            ? answer
            : (404, Encoding.UTF8.GetBytes("Not found"), "text/plain");
        var sent = clock.UtcNow;
        return new ExitResponse(
            request.Uri, Route == ExitRoute.Vpn ? ExitId : Guid.Empty, status, contentType, body, sent,
            sent.AddMilliseconds(Milliseconds.GetValueOrDefault(request.Uri.Host, 120)), Route)
        {
            RetryAfter = _retryAfter.TryGetValue(url, out var wait) ? wait : null,
        };
    }
}

/// <summary>
/// The way to the user's own node, without any network: answers come from the same kind of table, every request is
/// recorded, and a request whose host is not on the computer or a private network (an IP address, <c>localhost</c> or
/// a name ending in <c>.lan</c>) is refused as the real one refuses it.
/// </summary>
internal sealed class FakeLocalHttp(FakeClock clock) : ILocalHttp
{
    private readonly Dictionary<string, (int Status, byte[] Body)> _answers = [];

    public List<ExitRequest> Sent { get; } = [];

    public void Answer(string url, string fixture, int status = 200) => _answers[url] = (status, Fixtures.Bytes(fixture));

    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        var host = request.Uri.IdnHost;
        var local = LocalAddresses.IsLocal(request.Uri) || host.EndsWith(".lan", StringComparison.Ordinal);
        if (!local)
        {
            throw new ExitException(ExitError.NotLocal);
        }

        Sent.Add(request);
        var (status, body) = _answers.TryGetValue(request.Uri.AbsoluteUri, out var answer)
            ? answer
            : (404, Encoding.UTF8.GetBytes("Not found"));
        var sent = clock.UtcNow;
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, status, "application/json", body, sent, sent.AddMilliseconds(5), ExitRoute.Vpn)
        {
            IsLocal = true,
        });
    }
}

/// <summary>API keys in memory, or a locked vault.</summary>
internal sealed class FakeKeys : IApiKeyStore
{
    public Dictionary<string, ApiKey> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Locked { get; set; }

    public IReadOnlyList<ApiKeyInfo> List() => throw new NotSupportedException();

    public ApiKey? Find(string service) =>
        Locked ? throw new VaultException(VaultError.Locked) : Keys.GetValueOrDefault(service);

    public void Save(ApiKey key) => Keys[key.Service] = key;

    public bool Remove(string service) => Keys.Remove(service);
}
