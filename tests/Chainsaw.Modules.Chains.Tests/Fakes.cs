using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

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
}

/// <summary>
/// The exit, without any network: answers come from a table the test fills, every request that would have been sent
/// is recorded, and without a connected exit every request is refused as the real one does.
/// </summary>
internal sealed class FakeExitHttp(FakeClock clock) : IExitHttp
{
    public static readonly Guid ExitId = Guid.Parse("0b9e1e4a-5c39-4a8e-9a55-3f1f6a1c0001");

    private readonly Dictionary<string, (int Status, byte[] Body, string? ContentType)> _answers = [];

    public bool Connected { get; set; } = true;

    public ExitError? Failure { get; set; }

    public List<ExitRequest> Sent { get; } = [];

    public int Refused { get; private set; }

    public void Answer(string url, string fixture, int status = 200) =>
        _answers[url] = (status, Fixtures.Bytes(fixture), fixture.EndsWith(".txt", StringComparison.Ordinal)
            ? "text/plain" : "application/json");

    public void AnswerBytes(string url, byte[] body, int status) => _answers[url] = (status, body, "text/plain");

    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
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

        var url = request.Uri.AbsoluteUri;
        var (status, body, contentType) = _answers.TryGetValue(url, out var answer)
            ? answer
            : (404, Encoding.UTF8.GetBytes("Not found"), "text/plain");
        var sent = clock.UtcNow;
        return Task.FromResult(new ExitResponse(
            request.Uri, ExitId, status, contentType, body, sent, sent.AddMilliseconds(120)));
    }
}
