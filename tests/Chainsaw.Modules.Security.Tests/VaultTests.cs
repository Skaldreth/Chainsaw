using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security.Tests;

public sealed class VaultTests : IDisposable
{
    private readonly VaultFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void A_vault_without_a_file_is_not_created()
    {
        Assert.Equal(VaultState.NotCreated, _fixture.Vault.State);
        Assert.False(File.Exists(_fixture.FilePath));
    }

    [Fact]
    public void Create_writes_the_file_and_leaves_the_vault_unlocked()
    {
        var states = new List<VaultState>();
        _fixture.Vault.StateChanged += (_, state) => states.Add(state);

        _fixture.Vault.Create(VaultFixture.MasterKey);

        Assert.Equal(VaultState.Unlocked, _fixture.Vault.State);
        Assert.True(File.Exists(_fixture.FilePath));
        Assert.Equal([VaultState.Unlocked], states);
    }

    [Fact]
    public void Create_refuses_to_overwrite_an_existing_vault()
    {
        _fixture.Created();
        var before = _fixture.FileBytes();

        VaultFixture.AssertFails(VaultError.AlreadyCreated, () => _fixture.NewVault().Create(VaultFixture.OtherMasterKey));
        Assert.Equal(before, _fixture.FileBytes());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("elevenchars")]
    public void Create_refuses_a_master_key_shorter_than_twelve_characters(string masterKey)
    {
        VaultFixture.AssertFails(VaultError.MasterKeyTooShort, () => _fixture.Vault.Create(masterKey));

        Assert.Equal(VaultState.NotCreated, _fixture.Vault.State);
        Assert.False(File.Exists(_fixture.FilePath));
    }

    [Fact]
    public void Create_accepts_a_master_key_of_exactly_twelve_characters()
    {
        _fixture.Vault.Create("twelve chars");

        Assert.Equal(VaultState.Unlocked, _fixture.Vault.State);
    }

    [Fact]
    public void A_restarted_application_finds_the_vault_locked_and_opens_it_with_the_master_key()
    {
        _fixture.Created();
        new ApiKeyStore(_fixture.Vault).Save(new ApiKey("esplora", "account-1", "secret-value-1"));
        new OperatorProfileStore(_fixture.Vault).Save(new OperatorProfile("Test Operator", "Analyst", "Unit"));

        using var restarted = _fixture.NewVault();
        Assert.Equal(VaultState.Locked, restarted.State);
        restarted.Unlock(VaultFixture.MasterKey);

        Assert.Equal(VaultState.Unlocked, restarted.State);
        Assert.Equal("secret-value-1", new ApiKeyStore(restarted).Find("esplora")?.Secret);
        Assert.Equal("Test Operator", new OperatorProfileStore(restarted).Get()?.Name);
    }

    [Fact]
    public void Unlock_with_the_wrong_master_key_fails_clearly_and_stays_locked()
    {
        _fixture.Created();
        _fixture.Vault.Lock();
        var raised = false;
        _fixture.Vault.StateChanged += (_, _) => raised = true;

        var exception = VaultFixture.AssertFails(
            VaultError.WrongMasterKey, () => _fixture.Vault.Unlock(VaultFixture.OtherMasterKey));

        Assert.Equal(VaultState.Locked, _fixture.Vault.State);
        Assert.False(raised);
        Assert.DoesNotContain(VaultFixture.OtherMasterKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unlock_with_the_wrong_master_key_while_unlocked_keeps_the_vault_unlocked()
    {
        _fixture.Created();

        VaultFixture.AssertFails(VaultError.WrongMasterKey, () => _fixture.Vault.Unlock(VaultFixture.OtherMasterKey));

        Assert.Equal(VaultState.Unlocked, _fixture.Vault.State);
    }

    [Fact]
    public void Unlock_without_a_vault_says_it_is_not_created()
    {
        VaultFixture.AssertFails(VaultError.NotCreated, () => _fixture.Vault.Unlock(VaultFixture.MasterKey));
    }

    [Fact]
    public void Lock_clears_the_data_key_and_raises_the_event_once()
    {
        _fixture.Created();
        var dataKey = _fixture.Vault.DataKeyBuffer;
        Assert.NotNull(dataKey);
        Assert.Contains(dataKey, value => value != 0);
        var states = new List<VaultState>();
        _fixture.Vault.StateChanged += (_, state) => states.Add(state);

        _fixture.Vault.Lock();
        _fixture.Vault.Lock();

        Assert.All(dataKey, value => Assert.Equal(0, value));
        Assert.Null(_fixture.Vault.DataKeyBuffer);
        Assert.Equal(VaultState.Locked, _fixture.Vault.State);
        Assert.Equal([VaultState.Locked], states);
    }

    [Fact]
    public void A_locked_vault_gives_nothing()
    {
        _fixture.Created();
        new ApiKeyStore(_fixture.Vault).Save(new ApiKey("esplora", "", "secret-value-1"));
        _fixture.Vault.Lock();

        VaultFixture.AssertFails(VaultError.Locked, () => new ApiKeyStore(_fixture.Vault).Find("esplora"));
        VaultFixture.AssertFails(VaultError.Locked, () => new OperatorProfileStore(_fixture.Vault).Get());
        VaultFixture.AssertFails(
            VaultError.Locked, () => new PurposeKeys(_fixture.Vault).DeriveKey("cases/file-key", new byte[32]));
    }

    [Fact]
    public void Dispose_locks_the_vault()
    {
        _fixture.Created();
        var dataKey = _fixture.Vault.DataKeyBuffer!;

        _fixture.Vault.Dispose();

        Assert.Equal(VaultState.Locked, _fixture.Vault.State);
        Assert.All(dataKey, value => Assert.Equal(0, value));
    }

    [Fact]
    public void ChangeMasterKey_makes_the_old_key_stop_working_and_keeps_the_content()
    {
        _fixture.Created();
        new ApiKeyStore(_fixture.Vault).Save(new ApiKey("esplora", "", "secret-value-1"));
        var purposeBefore = DerivePurposeKey(_fixture.Vault);

        _fixture.Vault.ChangeMasterKey(VaultFixture.MasterKey, VaultFixture.OtherMasterKey);

        Assert.Equal(VaultState.Unlocked, _fixture.Vault.State);
        using var restarted = _fixture.NewVault();
        VaultFixture.AssertFails(VaultError.WrongMasterKey, () => restarted.Unlock(VaultFixture.MasterKey));
        restarted.Unlock(VaultFixture.OtherMasterKey);
        Assert.Equal("secret-value-1", new ApiKeyStore(restarted).Find("esplora")?.Secret);
        Assert.Equal(purposeBefore, DerivePurposeKey(restarted));
    }

    [Fact]
    public void ChangeMasterKey_while_unlocked_keeps_the_new_key_on_later_saves()
    {
        _fixture.Created();
        _fixture.Vault.ChangeMasterKey(VaultFixture.MasterKey, VaultFixture.OtherMasterKey);

        new OperatorProfileStore(_fixture.Vault).Save(new OperatorProfile("Test Operator", "", ""));

        using var restarted = _fixture.NewVault();
        VaultFixture.AssertFails(VaultError.WrongMasterKey, () => restarted.Unlock(VaultFixture.MasterKey));
        restarted.Unlock(VaultFixture.OtherMasterKey);
        Assert.Equal("Test Operator", new OperatorProfileStore(restarted).Get()?.Name);
    }

    [Fact]
    public void ChangeMasterKey_works_on_a_locked_vault_and_leaves_it_locked()
    {
        _fixture.Created();
        _fixture.Vault.Lock();

        _fixture.Vault.ChangeMasterKey(VaultFixture.MasterKey, VaultFixture.OtherMasterKey);

        Assert.Equal(VaultState.Locked, _fixture.Vault.State);
        _fixture.Vault.Unlock(VaultFixture.OtherMasterKey);
    }

    [Fact]
    public void ChangeMasterKey_uses_a_new_salt_and_a_new_nonce_and_keeps_the_content_box()
    {
        _fixture.Created();
        var before = ReadJson();

        _fixture.Vault.ChangeMasterKey(VaultFixture.MasterKey, VaultFixture.OtherMasterKey);

        var after = ReadJson();
        Assert.NotEqual(before["kdf"]!["salt"]!.GetValue<string>(), after["kdf"]!["salt"]!.GetValue<string>());
        Assert.NotEqual(
            before["dataKey"]!["nonce"]!.GetValue<string>(), after["dataKey"]!["nonce"]!.GetValue<string>());
        Assert.Equal(before["content"]!.ToJsonString(), after["content"]!.ToJsonString());
    }

    [Fact]
    public void ChangeMasterKey_refuses_a_wrong_current_key_and_leaves_the_file_unchanged()
    {
        _fixture.Created();
        var before = _fixture.FileBytes();

        VaultFixture.AssertFails(
            VaultError.WrongMasterKey,
            () => _fixture.Vault.ChangeMasterKey(VaultFixture.OtherMasterKey, "another long master key"));

        Assert.Equal(before, _fixture.FileBytes());
    }

    [Fact]
    public void ChangeMasterKey_refuses_a_short_new_key_and_leaves_the_file_unchanged()
    {
        _fixture.Created();
        var before = _fixture.FileBytes();

        VaultFixture.AssertFails(
            VaultError.MasterKeyTooShort, () => _fixture.Vault.ChangeMasterKey(VaultFixture.MasterKey, "too short"));

        Assert.Equal(before, _fixture.FileBytes());
    }

    [Fact]
    public void The_file_contains_no_plaintext_secrets()
    {
        _fixture.Created();
        new ApiKeyStore(_fixture.Vault).Save(new ApiKey("esplora", "account-identifier-7", "api-secret-value-42"));
        new OperatorProfileStore(_fixture.Vault).Save(
            new OperatorProfile("Operator Plaintextname", "Senior Analyst", "Example Unit"));
        var dataKey = _fixture.Vault.DataKeyBuffer!.ToArray();

        var bytes = _fixture.FileBytes();

        foreach (var secret in new[]
                 {
                     VaultFixture.MasterKey, "api-secret-value-42", "account-identifier-7", "esplora",
                     "Operator Plaintextname", "Senior Analyst", "Example Unit",
                 })
        {
            Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(secret)), $"UTF-8 '{secret}' found in the file.");
            Assert.False(Contains(bytes, Encoding.Unicode.GetBytes(secret)), $"UTF-16 '{secret}' found in the file.");
        }

        Assert.False(Contains(bytes, dataKey), "The raw data key was found in the file.");
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(Convert.ToBase64String(dataKey), text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_records_format_version_algorithm_iterations_and_salt()
    {
        _fixture.Created();

        var json = ReadJson();

        Assert.Equal("chainsaw-vault", json["format"]!.GetValue<string>());
        Assert.Equal(1, json["version"]!.GetValue<int>());
        Assert.Equal("PBKDF2-SHA512", json["kdf"]!["algorithm"]!.GetValue<string>());
        Assert.Equal(VaultFixture.TestIterations, json["kdf"]!["iterations"]!.GetValue<int>());
        Assert.Equal(32, Convert.FromBase64String(json["kdf"]!["salt"]!.GetValue<string>()).Length);
        Assert.Equal(12, Convert.FromBase64String(json["dataKey"]!["nonce"]!.GetValue<string>()).Length);
        Assert.Equal(16, Convert.FromBase64String(json["dataKey"]!["tag"]!.GetValue<string>()).Length);
    }

    [Fact]
    public void The_default_is_six_hundred_thousand_iterations_and_it_is_stored()
    {
        Assert.Equal(600_000, new VaultOptions().Iterations);
        using var vault = new Vault(new VaultOptions { Directory = _fixture.Directory });

        vault.Create(VaultFixture.MasterKey);

        Assert.Equal(600_000, ReadJson()["kdf"]!["iterations"]!.GetValue<int>());
    }

    [Fact]
    public void A_vault_opens_with_the_iterations_stored_in_its_file()
    {
        _fixture.Created();

        using var other = new Vault(new VaultOptions { Directory = _fixture.Directory, Iterations = 2_000 });
        other.Unlock(VaultFixture.MasterKey);

        Assert.Equal(VaultState.Unlocked, other.State);
    }

    [Fact]
    public void An_unknown_version_is_refused()
    {
        _fixture.Created();
        RewriteJson(json => json["version"] = 2);

        VaultFixture.AssertFails(
            VaultError.UnsupportedVersion, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    [Fact]
    public void Lowered_iterations_in_the_file_do_not_open_the_vault()
    {
        _fixture.Created();
        RewriteJson(json => json["kdf"]!["iterations"] = 1);

        VaultFixture.AssertFails(VaultError.WrongMasterKey, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    [Fact]
    public void Absurd_iterations_in_the_file_are_refused_before_deriving()
    {
        _fixture.Created();
        RewriteJson(json => json["kdf"]!["iterations"] = int.MaxValue);

        VaultFixture.AssertFails(VaultError.Corrupt, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    [Fact]
    public void A_changed_content_box_is_detected_by_its_tag()
    {
        _fixture.Created();
        new ApiKeyStore(_fixture.Vault).Save(new ApiKey("esplora", "", "secret-value-1"));
        RewriteJson(json => json["content"]!["ciphertext"] = FlipFirstBit(json["content"]!["ciphertext"]!));

        VaultFixture.AssertFails(VaultError.Corrupt, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    [Fact]
    public void A_changed_wrapped_key_does_not_open_the_vault()
    {
        _fixture.Created();
        RewriteJson(json => json["dataKey"]!["tag"] = FlipFirstBit(json["dataKey"]!["tag"]!));

        VaultFixture.AssertFails(VaultError.WrongMasterKey, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    [Fact]
    public void Swapping_the_boxes_is_detected_by_the_associated_data()
    {
        _fixture.Created();
        RewriteJson(json =>
        {
            var dataKey = json["dataKey"]!.DeepClone();
            json["dataKey"] = json["content"]!.DeepClone();
            json["content"] = dataKey;
        });

        Assert.Throws<VaultException>(() => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"chainsaw-vault\",\"version\":1}")]
    [InlineData("{\"format\":\"something-else\",\"version\":1}")]
    public void A_damaged_file_is_reported_as_corrupt(string content)
    {
        System.IO.Directory.CreateDirectory(_fixture.Directory);
        File.WriteAllText(_fixture.FilePath, content);

        VaultFixture.AssertFails(VaultError.Corrupt, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    [Fact]
    public void Every_save_uses_a_new_nonce()
    {
        _fixture.Created();
        var store = new ApiKeyStore(_fixture.Vault);
        var nonces = new HashSet<string> { ReadJson()["content"]!["nonce"]!.GetValue<string>() };

        for (var i = 0; i < 5; i++)
        {
            store.Save(new ApiKey("esplora", "", "same-secret"));
            nonces.Add(ReadJson()["content"]!["nonce"]!.GetValue<string>());
        }

        Assert.Equal(6, nonces.Count);
    }

    [Fact]
    public void A_write_leaves_no_temporary_file_and_replaces_a_stale_one()
    {
        System.IO.Directory.CreateDirectory(_fixture.Directory);
        var temporaryPath = _fixture.FilePath + VaultFile.TemporarySuffix;
        File.WriteAllText(temporaryPath, "left by a crash");

        _fixture.Created();
        new OperatorProfileStore(_fixture.Vault).Save(new OperatorProfile("Test Operator", "", ""));

        Assert.False(File.Exists(temporaryPath));
        Assert.Equal(VaultState.Locked, _fixture.NewVault().State);
    }

    [Fact]
    public void A_failed_write_leaves_the_old_file_and_the_content_unchanged()
    {
        _fixture.Created();
        var profiles = new OperatorProfileStore(_fixture.Vault);
        profiles.Save(new OperatorProfile("First Operator", "", ""));
        var before = _fixture.FileBytes();
        var temporaryPath = _fixture.FilePath + VaultFile.TemporarySuffix;
        System.IO.Directory.CreateDirectory(temporaryPath);

        var exception = Record.Exception(() => profiles.Save(new OperatorProfile("Second Operator", "", "")));

        Assert.NotNull(exception);
        Assert.IsNotType<VaultException>(exception);
        Assert.Equal(before, _fixture.FileBytes());
        Assert.Equal("First Operator", profiles.Get()?.Name);
        System.IO.Directory.Delete(temporaryPath);
        using var restarted = _fixture.NewVault();
        restarted.Unlock(VaultFixture.MasterKey);
        Assert.Equal("First Operator", new OperatorProfileStore(restarted).Get()?.Name);
    }

    [Fact]
    public void Exception_messages_carry_no_secrets()
    {
        foreach (var error in Enum.GetValues<VaultError>())
        {
            var message = new VaultException(error).Message;
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.DoesNotContain(VaultFixture.MasterKey, message, StringComparison.Ordinal);
        }
    }

    private static byte[] DerivePurposeKey(Vault vault)
    {
        var key = new byte[32];
        new PurposeKeys(vault).DeriveKey("cases/file-key", key);
        return key;
    }

    private static string FlipFirstBit(JsonNode node)
    {
        var bytes = Convert.FromBase64String(node.GetValue<string>());
        bytes[0] ^= 1;
        return Convert.ToBase64String(bytes);
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private JsonNode ReadJson() => JsonNode.Parse(_fixture.FileBytes())!;

    private void RewriteJson(Action<JsonNode> change)
    {
        var json = ReadJson();
        change(json);
        File.WriteAllText(_fixture.FilePath, json.ToJsonString());
    }
}
