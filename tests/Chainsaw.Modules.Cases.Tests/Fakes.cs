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

    /// <summary>The master key the vault opens with.</summary>
    public const string MasterKey = "en lång och riktig huvudnyckel";

    public void VerifyMasterKey(ReadOnlySpan<char> masterKey)
    {
        if (State != VaultState.Unlocked)
        {
            throw new VaultException(VaultError.Locked);
        }

        if (!masterKey.SequenceEqual(MasterKey))
        {
            throw new VaultException(VaultError.WrongMasterKey);
        }
    }

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

/// <summary>A vault's named secrets in memory, like the real ones in the vault file.</summary>
internal sealed class FakeSecrets(FakeVault vault) : IVaultSecrets
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> List(string prefix)
    {
        RequireUnlocked();
        return [.. Values.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order()];
    }

    public string? Find(string name)
    {
        RequireUnlocked();
        return Values.GetValueOrDefault(name);
    }

    public void Save(string name, string value)
    {
        RequireUnlocked();
        Values[name] = value;
    }

    public bool Remove(string name)
    {
        RequireUnlocked();
        return Values.Remove(name);
    }

    private void RequireUnlocked()
    {
        if (vault.State != VaultState.Unlocked)
        {
            throw new VaultException(VaultError.Locked);
        }
    }
}
