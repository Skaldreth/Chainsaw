using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Recovery;

/// <summary>
/// The Security and Cases modules as the tool uses them: either the operator's own vault (unlocked with the master
/// key the operator types), or a temporary vault that exists only for one run and is removed with everything in it.
/// </summary>
internal sealed class CaseSession : IDisposable
{
    /// <summary>The length of the temporary vault's random master key.</summary>
    private const int TemporaryMasterKeyLength = 32;

    /// <summary>The container that owns the modules.</summary>
    private readonly ServiceProvider _provider;

    /// <summary>The temporary folder to remove, or <see langword="null"/> for the operator's own vault.</summary>
    private readonly string? _temporaryDirectory;

    /// <summary>Builds the modules.</summary>
    /// <param name="vaultDirectory">The vault's folder.</param>
    /// <param name="temporaryDirectory">The temporary folder to remove afterwards, if any.</param>
    private CaseSession(string vaultDirectory, string? temporaryDirectory)
    {
        _temporaryDirectory = temporaryDirectory;
        var services = new ServiceCollection();
        services.AddSecurityModule(options => options.Directory = vaultDirectory);
        services.AddCasesModule(options =>
        {
            options.Directory = vaultDirectory;
            options.AnchorDirectory = Path.Combine(vaultDirectory, CasesOptions.AnchorsFolder);
        });
        _provider = services.BuildServiceProvider();
    }

    /// <summary>Gets the vault.</summary>
    public IVault Vault => _provider.GetRequiredService<IVault>();

    /// <summary>Gets the case store.</summary>
    public ICaseStore Store => _provider.GetRequiredService<ICaseStore>();

    /// <summary>Gets the operator profile store.</summary>
    public IOperatorProfileStore Profiles => _provider.GetRequiredService<IOperatorProfileStore>();

    /// <summary>
    /// Makes a session without a vault, for what only reads a case file's header; its temporary folder is never
    /// written to.
    /// </summary>
    /// <returns>The session.</returns>
    public static CaseSession WithoutVault()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chainsaw-recovery-" + Guid.NewGuid().ToString("N"));
        return new CaseSession(directory, directory);
    }

    /// <summary>
    /// Makes a session with a temporary vault whose operator is the administrator, so that the journal records who
    /// opened the case. The vault's master key is random and never shown.
    /// </summary>
    /// <param name="administrator">The administrator, as the journal shows them.</param>
    /// <returns>The session; disposing it removes the vault.</returns>
    public static CaseSession ForAdministrator(OperatorProfile administrator)
    {
        var directory = Path.Combine(Path.GetTempPath(), "chainsaw-recovery-" + Guid.NewGuid().ToString("N"));
        var session = new CaseSession(directory, directory);
        var masterKey = new char[TemporaryMasterKeyLength];
        try
        {
            const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            System.Security.Cryptography.RandomNumberGenerator.GetItems(Alphabet, masterKey);
            session.Vault.Create(masterKey);
            session.Profiles.Save(administrator);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
        finally
        {
            Array.Clear(masterKey);
        }
    }

    /// <summary>Makes a session with the operator's own vault, unlocked with the operator's master key.</summary>
    /// <param name="vaultDirectory">The vault's folder.</param>
    /// <param name="masterKey">The master key.</param>
    /// <returns>The session; disposing it locks the vault.</returns>
    /// <exception cref="VaultException">The vault is missing, damaged, or the master key is wrong.</exception>
    public static CaseSession ForOperator(string vaultDirectory, ReadOnlySpan<char> masterKey)
    {
        var session = new CaseSession(Path.GetFullPath(vaultDirectory), temporaryDirectory: null);
        try
        {
            session.Vault.Unlock(masterKey);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>Closes the modules (locking the vault) and removes a temporary vault.</summary>
    public void Dispose()
    {
        try
        {
            Vault.Lock();
            _provider.Dispose();
        }
        finally
        {
            if (_temporaryDirectory is not null && Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
    }
}
