using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security.Tests;

public sealed class ApiKeyStoreTests : IDisposable
{
    private readonly VaultFixture _fixture = new VaultFixture().Created();

    private ApiKeyStore Store => new(_fixture.Vault);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_new_vault_has_no_keys()
    {
        Assert.Empty(Store.List());
        Assert.Null(Store.Find("esplora"));
    }

    [Fact]
    public void Find_gives_the_saved_key_without_regard_to_case()
    {
        Store.Save(new ApiKey("Esplora", "account-1", "secret-value-1"));

        Assert.Equal(new ApiKey("Esplora", "account-1", "secret-value-1"), Store.Find("esplora"));
    }

    [Fact]
    public void Save_replaces_the_key_for_the_same_service()
    {
        Store.Save(new ApiKey("esplora", "account-1", "secret-value-1"));
        Store.Save(new ApiKey("ESPLORA", "account-2", "secret-value-2"));

        Assert.Equal([new ApiKeyInfo("ESPLORA", "account-2")], Store.List());
        Assert.Equal("secret-value-2", Store.Find("esplora")?.Secret);
    }

    [Fact]
    public void List_is_ordered_by_service_and_has_no_secrets()
    {
        Store.Save(new ApiKey("zeta", "z", "secret-z"));
        Store.Save(new ApiKey("alpha", "a", "secret-a"));

        Assert.Equal([new ApiKeyInfo("alpha", "a"), new ApiKeyInfo("zeta", "z")], Store.List());
    }

    [Fact]
    public void Remove_removes_only_the_named_service()
    {
        Store.Save(new ApiKey("alpha", "", "secret-a"));
        Store.Save(new ApiKey("zeta", "", "secret-z"));

        Assert.True(Store.Remove("ALPHA"));
        Assert.False(Store.Remove("alpha"));

        Assert.Equal([new ApiKeyInfo("zeta", "")], Store.List());
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData(" ", "secret")]
    [InlineData("esplora", "")]
    public void Save_refuses_an_empty_service_or_secret(string service, string secret)
    {
        Assert.ThrowsAny<ArgumentException>(() => Store.Save(new ApiKey(service, "", secret)));
    }

    [Fact]
    public void ToString_masks_the_secret()
    {
        var text = new ApiKey("esplora", "account-1", "secret-value-1").ToString();

        Assert.DoesNotContain("secret-value-1", text, StringComparison.Ordinal);
        Assert.Contains("esplora", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_locked_vault_refuses_every_operation()
    {
        _fixture.Vault.Lock();

        VaultFixture.AssertFails(VaultError.Locked, () => Store.List());
        VaultFixture.AssertFails(VaultError.Locked, () => Store.Save(new ApiKey("esplora", "", "secret")));
        VaultFixture.AssertFails(VaultError.Locked, () => Store.Remove("esplora"));
    }
}
