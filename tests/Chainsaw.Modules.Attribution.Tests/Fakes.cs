using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>A vault that is always unlocked.</summary>
internal sealed class FakeVault : IVault
{
    public VaultState State => VaultState.Unlocked;

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

/// <summary>Purpose keys from a secret the test can change, or a locked vault.</summary>
internal sealed class FakePurposeKeys : IPurposeKeys
{
    public string Secret { get; set; } = "attribution-tests";

    public bool Locked { get; set; }

    public void DeriveKey(string purpose, Span<byte> destination)
    {
        if (Locked)
        {
            throw new VaultException(VaultError.Locked);
        }

        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(Secret));
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, destination, [], Encoding.ASCII.GetBytes(purpose));
    }
}

/// <summary>An operator profile in memory that the test changes to play a colleague.</summary>
internal sealed class FakeProfiles : IOperatorProfileStore
{
    public OperatorProfile? Profile { get; set; } = new("Testa Testsson", "Utredare", "Enheten");

    public OperatorProfile? Get() => Profile;

    public void Save(OperatorProfile profile) => Profile = profile;
}

/// <summary>A clock the test moves.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 1, 2, 3, TimeSpan.Zero);
}

/// <summary>The vault's API keys, in memory; can be locked.</summary>
internal sealed class FakeApiKeys : IApiKeyStore
{
    private readonly Dictionary<string, ApiKey> _keys = new(StringComparer.OrdinalIgnoreCase);

    public bool Locked { get; set; }

    public IReadOnlyList<ApiKeyInfo> List() => [.. _keys.Values.Select(key => new ApiKeyInfo(key.Service, key.Identifier))];

    public ApiKey? Find(string service) =>
        Locked ? throw new VaultException(VaultError.Locked) : _keys.GetValueOrDefault(service);

    public void Save(ApiKey key) => _keys[key.Service] = key;

    public bool Remove(string service) => _keys.Remove(service);
}

/// <summary>
/// The exit, without any network: answers come from a table the test fills, every request is recorded, and without
/// a connected exit every request is refused as the real one does.
/// </summary>
internal sealed class FakeExitHttp(FakeClock clock) : IExitHttp
{
    private readonly Dictionary<string, (int Status, byte[] Body, TimeSpan? RetryAfter)> _answers = [];

    public bool Connected { get; set; } = true;

    public List<ExitRequest> Sent { get; } = [];

    public void Answer(string url, string body, int status = 200, TimeSpan? retryAfter = null) =>
        _answers[url] = (status, Encoding.UTF8.GetBytes(body), retryAfter);

    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        if (!Connected)
        {
            throw new ExitNotConnectedException();
        }

        Sent.Add(request);
        var (status, body, retryAfter) = _answers.TryGetValue(request.Uri.AbsoluteUri, out var answer)
            ? answer
            : (404, Encoding.UTF8.GetBytes("{}"), null);
        var sent = clock.UtcNow;
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Empty, status, "application/json", body, sent, sent.AddMilliseconds(80),
            ExitRoute.Windows) { RetryAfter = retryAfter });
    }
}
