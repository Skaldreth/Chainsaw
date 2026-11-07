using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>A vault whose state the test sets.</summary>
internal sealed class FakeVault : IVault
{
    public VaultState State { get; private set; } = VaultState.Unlocked;

    public event EventHandler<VaultState>? StateChanged;

    public void Create(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Unlock(ReadOnlySpan<char> masterKey) => SetState(VaultState.Unlocked);

    public void Lock() => SetState(VaultState.Locked);

    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey) =>
        throw new NotSupportedException();

    private void SetState(VaultState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}

/// <summary>Purpose keys from a fixed secret per vault, like the real ones from the data key.</summary>
internal sealed class FakePurposeKeys(FakeVault vault, string vaultName) : IPurposeKeys
{
    public void DeriveKey(string purpose, Span<byte> destination)
    {
        if (vault.State != VaultState.Unlocked)
        {
            throw new VaultException(VaultError.Locked);
        }

        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(vaultName));
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, destination, [], Encoding.ASCII.GetBytes(purpose));
    }
}

/// <summary>An operator profile in memory.</summary>
internal sealed class FakeProfiles(FakeVault vault) : IOperatorProfileStore
{
    public OperatorProfile? Profile { get; set; } = new("Testa Testsson", "Utredare", "Enheten");

    public OperatorProfile? Get() =>
        vault.State == VaultState.Unlocked ? Profile : throw new VaultException(VaultError.Locked);

    public void Save(OperatorProfile profile) => Profile = profile;
}

/// <summary>A clock the test moves.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 1, 2, 3, TimeSpan.Zero);
}
