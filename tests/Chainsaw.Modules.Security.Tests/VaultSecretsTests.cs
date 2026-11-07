using System.Text;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security.Tests;

public sealed class VaultSecretsTests : IDisposable
{
    private readonly VaultFixture _fixture = new VaultFixture().Created();

    private VaultSecrets Secrets => new(_fixture.Vault);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_new_vault_has_no_secrets()
    {
        Assert.Empty(Secrets.List(""));
        Assert.Null(Secrets.Find("network/exits/a"));
    }

    [Fact]
    public void A_saved_secret_is_found_and_replaced_by_name()
    {
        Secrets.Save("network/exits/a", "first");
        Secrets.Save("network/exits/a", "second");

        Assert.Equal("second", Secrets.Find("network/exits/a"));
        Assert.Equal(["network/exits/a"], Secrets.List(""));
    }

    [Fact]
    public void List_gives_the_names_under_a_prefix_in_ordinal_order()
    {
        Secrets.Save("network/exits/b", "2");
        Secrets.Save("network/exits/a", "1");
        Secrets.Save("network/selected-exit", "a");
        Secrets.Save("other/x", "3");

        Assert.Equal(["network/exits/a", "network/exits/b"], Secrets.List("network/exits/"));
        Assert.Equal(4, Secrets.List("").Count);
    }

    [Fact]
    public void Remove_removes_only_the_named_secret()
    {
        Secrets.Save("a", "1");
        Secrets.Save("b", "2");

        Assert.True(Secrets.Remove("a"));
        Assert.False(Secrets.Remove("a"));

        Assert.Equal(["b"], Secrets.List(""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("/leading")]
    [InlineData("space here")]
    [InlineData("åäö")]
    public void Save_refuses_a_name_outside_the_rule(string name)
    {
        Assert.Throws<ArgumentException>(() => Secrets.Save(name, "value"));
    }

    [Fact]
    public void Save_refuses_an_empty_or_too_long_value_without_naming_it()
    {
        Assert.Throws<ArgumentException>(() => Secrets.Save("a", ""));
        var exception = Assert.Throws<ArgumentException>(
            () => Secrets.Save("a", "secret-" + new string('x', IVaultSecrets.MaximumValueLength)));

        Assert.DoesNotContain("secret-", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(
            () => Secrets.Save(new string('a', IVaultSecrets.MaximumNameLength + 1), "value"));
    }

    [Fact]
    public void Secrets_are_encrypted_in_the_file_and_survive_a_restart()
    {
        Secrets.Save("network/exits/marker-name", "marker-value-1234");

        var text = Encoding.UTF8.GetString(_fixture.FileBytes());
        Assert.DoesNotContain("marker-name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("marker-value-1234", text, StringComparison.Ordinal);

        using var restarted = _fixture.NewVault();
        restarted.Unlock(VaultFixture.MasterKey);
        Assert.Equal("marker-value-1234", new VaultSecrets(restarted).Find("network/exits/marker-name"));
    }

    [Fact]
    public void A_locked_vault_refuses_every_member()
    {
        _fixture.Vault.Lock();

        VaultFixture.AssertFails(VaultError.Locked, () => Secrets.List(""));
        VaultFixture.AssertFails(VaultError.Locked, () => Secrets.Find("a"));
        VaultFixture.AssertFails(VaultError.Locked, () => Secrets.Save("a", "1"));
        VaultFixture.AssertFails(VaultError.Locked, () => Secrets.Remove("a"));
    }

    [Fact]
    public void Secrets_do_not_disturb_the_api_keys()
    {
        var keys = new ApiKeyStore(_fixture.Vault);
        keys.Save(new ApiKey("esplora", "", "key-secret"));

        Secrets.Save("a", "1");

        Assert.Equal("key-secret", keys.Find("esplora")?.Secret);
        Assert.Equal("1", Secrets.Find("a"));
    }
}

public sealed class VaultContentSecretsTests
{
    [Fact]
    public void Content_from_before_the_secrets_has_none()
    {
        var content = System.Text.Json.JsonSerializer.Deserialize<VaultContent>("""{"ApiKeys":[],"Operator":null}""")!;

        Assert.Null(content.Secrets);
        Assert.Empty(content.SecretsOrEmpty);
    }

    [Fact]
    public void Each_secret_is_serialised_once()
    {
        var content = VaultContent.Empty with
        {
            Secrets = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "marker-value" },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(content);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "marker-value"));
        Assert.DoesNotContain("SecretsOrEmpty", json, StringComparison.Ordinal);
    }
}
