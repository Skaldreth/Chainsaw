using Chainsaw.Modules.Security.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Security.Tests;

public sealed class SecurityModuleTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-vault-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AddSecurityModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddSecurityModule());
    }

    [Fact]
    public void AddSecurityModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => SecurityModule.AddSecurityModule(null!));
    }

    [Fact]
    public void The_default_vault_folder_is_Chainsaw_under_local_application_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw");

        Assert.Equal(expected, new VaultOptions().Directory);
    }

    [Fact]
    public void The_services_share_one_vault_in_the_configured_folder()
    {
        var provider = new ServiceCollection()
            .AddSecurityModule(options =>
            {
                options.Directory = _directory;
                options.Iterations = VaultFixture.TestIterations;
            })
            .BuildServiceProvider();

        var vault = provider.GetRequiredService<IVault>();
        vault.Create(VaultFixture.MasterKey);
        provider.GetRequiredService<IApiKeyStore>().Save(new ApiKey("esplora", "", "secret-value-1"));
        provider.GetRequiredService<IOperatorProfileStore>().Save(new OperatorProfile("Test Operator", "", ""));
        provider.GetRequiredService<IPurposeKeys>().DeriveKey("cases/file-key", new byte[32]);

        Assert.True(File.Exists(Path.Combine(_directory, VaultFile.FileName)));
        Assert.Equal("secret-value-1", provider.GetRequiredService<IApiKeyStore>().Find("esplora")?.Secret);
    }

    [Fact]
    public void Disposing_the_container_locks_the_vault()
    {
        var provider = new ServiceCollection()
            .AddSecurityModule(options =>
            {
                options.Directory = _directory;
                options.Iterations = VaultFixture.TestIterations;
            })
            .BuildServiceProvider();
        var vault = provider.GetRequiredService<IVault>();
        vault.Create(VaultFixture.MasterKey);

        provider.Dispose();

        Assert.Equal(VaultState.Locked, vault.State);
    }
}
