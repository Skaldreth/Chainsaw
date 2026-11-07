using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security.Tests;

/// <summary>A vault in its own temporary folder, with few iterations so that the tests stay fast.</summary>
internal sealed class VaultFixture : IDisposable
{
    /// <summary>A master key that is long enough.</summary>
    public const string MasterKey = "correct horse battery";

    /// <summary>Another master key that is long enough.</summary>
    public const string OtherMasterKey = "staple wrong pony door";

    /// <summary>The iteration count the tests use.</summary>
    public const int TestIterations = 1_000;

    /// <summary>Creates a fixture with a fresh temporary folder and a vault that has not been created.</summary>
    public VaultFixture()
    {
        Directory = Path.Combine(Path.GetTempPath(), "chainsaw-vault-tests", Guid.NewGuid().ToString("N"));
        Vault = NewVault();
    }

    /// <summary>Gets the temporary folder.</summary>
    public string Directory { get; }

    /// <summary>Gets the vault under test.</summary>
    public Vault Vault { get; }

    /// <summary>Gets the path of the vault file.</summary>
    public string FilePath => Path.Combine(Directory, VaultFile.FileName);

    /// <summary>Creates another vault on the same folder, as a restarted application would.</summary>
    /// <returns>The new vault, locked or not created.</returns>
    public Vault NewVault() => new(new VaultOptions { Directory = Directory, Iterations = TestIterations });

    /// <summary>Creates the vault with <see cref="MasterKey"/>.</summary>
    /// <returns>The fixture, for chaining.</returns>
    public VaultFixture Created()
    {
        Vault.Create(MasterKey);
        return this;
    }

    /// <summary>Reads the vault file's bytes.</summary>
    /// <returns>The bytes.</returns>
    public byte[] FileBytes() => File.ReadAllBytes(FilePath);

    /// <summary>Locks the vault and removes the folder.</summary>
    public void Dispose()
    {
        Vault.Dispose();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    /// <summary>Asserts that an action fails with a vault error.</summary>
    /// <param name="error">The expected error.</param>
    /// <param name="action">The action.</param>
    /// <returns>The exception.</returns>
    public static VaultException AssertFails(VaultError error, Action action)
    {
        var exception = Assert.Throws<VaultException>(action);
        Assert.Equal(error, exception.Error);
        return exception;
    }
}
