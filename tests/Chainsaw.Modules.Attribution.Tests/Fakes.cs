using System.Security.Cryptography;
using System.Text;
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
