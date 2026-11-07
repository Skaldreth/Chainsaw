using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security.Tests;

public sealed class PurposeKeysTests : IDisposable
{
    private readonly VaultFixture _fixture = new VaultFixture().Created();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void The_same_purpose_gives_the_same_key_also_after_a_restart()
    {
        var first = Derive(_fixture.Vault, "cases/file-key");
        using var restarted = _fixture.NewVault();
        restarted.Unlock(VaultFixture.MasterKey);

        Assert.Equal(first, Derive(restarted, "cases/file-key"));
    }

    [Fact]
    public void Different_purposes_give_different_keys_and_none_is_the_data_key()
    {
        var cases = Derive(_fixture.Vault, "cases/file-key");
        var monitoring = Derive(_fixture.Vault, "monitoring/queue");

        Assert.NotEqual(cases, monitoring);
        Assert.NotEqual(_fixture.Vault.DataKeyBuffer, cases);
        Assert.NotEqual(_fixture.Vault.DataKeyBuffer, monitoring);
    }

    [Fact]
    public void Two_vaults_give_different_keys_for_the_same_purpose()
    {
        using var other = new VaultFixture().Created();

        Assert.NotEqual(Derive(_fixture.Vault, "cases/file-key"), Derive(other.Vault, "cases/file-key"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Cases")]
    [InlineData("/cases")]
    [InlineData("cases key")]
    [InlineData("cases:key")]
    [InlineData("ärende")]
    public void An_invalid_purpose_is_refused(string purpose)
    {
        Assert.Throws<ArgumentException>(() => new PurposeKeys(_fixture.Vault).DeriveKey(purpose, new byte[32]));
    }

    [Fact]
    public void A_purpose_that_is_too_long_is_refused()
    {
        var purpose = new string('a', IPurposeKeys.MaximumPurposeLength + 1);

        Assert.Throws<ArgumentException>(() => new PurposeKeys(_fixture.Vault).DeriveKey(purpose, new byte[32]));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(65)]
    public void A_key_length_outside_sixteen_to_sixty_four_bytes_is_refused(int length)
    {
        Assert.Throws<ArgumentException>(
            () => new PurposeKeys(_fixture.Vault).DeriveKey("cases/file-key", new byte[length]));
    }

    private static byte[] Derive(Vault vault, string purpose)
    {
        var key = new byte[32];
        new PurposeKeys(vault).DeriveKey(purpose, key);
        return key;
    }
}
