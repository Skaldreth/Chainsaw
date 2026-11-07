using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security.Tests;

public sealed class OperatorProfileStoreTests : IDisposable
{
    private readonly VaultFixture _fixture = new VaultFixture().Created();

    private OperatorProfileStore Store => new(_fixture.Vault);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_new_vault_has_no_profile()
    {
        Assert.Null(Store.Get());
    }

    [Fact]
    public void Save_replaces_the_profile()
    {
        Store.Save(new OperatorProfile("First Operator", "Analyst", "Unit A"));
        Store.Save(new OperatorProfile("Second Operator", "Investigator", "Unit B"));

        Assert.Equal(new OperatorProfile("Second Operator", "Investigator", "Unit B"), Store.Get());
    }

    [Fact]
    public void Save_refuses_an_empty_name()
    {
        Assert.ThrowsAny<ArgumentException>(() => Store.Save(new OperatorProfile(" ", "Analyst", "Unit")));
    }

    [Fact]
    public void A_locked_vault_refuses_the_profile()
    {
        _fixture.Vault.Lock();

        VaultFixture.AssertFails(VaultError.Locked, () => Store.Save(new OperatorProfile("Name", "", "")));
    }
}
