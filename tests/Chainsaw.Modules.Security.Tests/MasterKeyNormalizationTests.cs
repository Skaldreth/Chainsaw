using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security.Tests;

/// <summary>
/// The master key in Unicode normalization form C: the same key typed with another input method opens the vault, and
/// a vault written before the normalization still opens and is wrapped again in form C.
/// </summary>
public sealed class MasterKeyNormalizationTests : IDisposable
{
    /// <summary>A master key with letters that have a composed and a decomposed form.</summary>
    private const string Composed = "räven på åkern är grå";

    private readonly VaultFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    /// <summary>Gets the same key with every letter decomposed, as some input methods type it.</summary>
    private static string Decomposed => Composed.Normalize(NormalizationForm.FormD);

    [Fact]
    public void A_new_vault_is_derived_in_form_c_and_opens_with_the_key_typed_decomposed()
    {
        Assert.NotEqual(Composed, Decomposed);
        _fixture.Vault.Create(Composed);

        Assert.Equal("NFC", ReadJson()["kdf"]!["normalization"]!.GetValue<string>());
        _fixture.NewVault().Unlock(Decomposed);
    }

    [Fact]
    public void A_changed_master_key_is_derived_in_form_c()
    {
        _fixture.Created();

        _fixture.Vault.ChangeMasterKey(VaultFixture.MasterKey, Decomposed);

        _fixture.NewVault().Unlock(Composed);
    }

    [Fact]
    public void A_vault_written_before_the_normalization_opens_as_typed_and_is_wrapped_again_in_form_c()
    {
        WriteLegacyVault(Decomposed);
        var saltBefore = ReadJson()["kdf"]!["salt"]!.GetValue<string>();

        _fixture.NewVault().Unlock(Decomposed);

        Assert.Equal("NFC", ReadJson()["kdf"]!["normalization"]!.GetValue<string>());
        Assert.NotEqual(saltBefore, ReadJson()["kdf"]!["salt"]!.GetValue<string>());
        _fixture.NewVault().Unlock(Composed);
    }

    [Fact]
    public void A_vault_written_before_the_normalization_with_a_composed_key_opens_with_it_typed_decomposed()
    {
        WriteLegacyVault(Composed);

        _fixture.NewVault().Unlock(Decomposed);

        Assert.Equal("NFC", ReadJson()["kdf"]!["normalization"]!.GetValue<string>());
    }

    [Fact]
    public void A_key_with_a_lone_surrogate_is_wrong_for_an_old_vault_and_never_throws()
    {
        WriteLegacyVault(Composed);

        VaultFixture.AssertFails(
            VaultError.WrongMasterKey, () => _fixture.NewVault().Unlock("nyckel med \uD800 i mitten"));
    }

    [Fact]
    public void A_key_with_a_lone_surrogate_makes_and_opens_a_new_vault()
    {
        const string Lone = "nyckel med \uD800 i mitten";

        _fixture.Vault.Create(Lone);

        _fixture.NewVault().Unlock(Lone);
    }

    [Fact]
    public void A_wrong_key_is_still_wrong_for_an_old_vault_and_leaves_the_file_alone()
    {
        WriteLegacyVault(Composed);
        var before = _fixture.FileBytes();

        VaultFixture.AssertFails(VaultError.WrongMasterKey, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));

        Assert.Equal(before, _fixture.FileBytes());
    }

    [Fact]
    public void An_unknown_normalization_makes_the_file_corrupt()
    {
        _fixture.Created();
        var json = ReadJson();
        json["kdf"]!["normalization"] = "NFKD";
        File.WriteAllText(_fixture.FilePath, json.ToJsonString());

        VaultFixture.AssertFails(VaultError.Corrupt, () => _fixture.NewVault().Unlock(VaultFixture.MasterKey));
    }

    /// <summary>
    /// Writes a vault as a build before the normalization did: the data key wrapped with the key derived from the
    /// master key exactly as typed, and no normalization in the parameters.
    /// </summary>
    /// <param name="masterKey">The master key as it was typed then.</param>
    private void WriteLegacyVault(string masterKey)
    {
        _fixture.Vault.Create(masterKey);
        var dataKey = VaultFixture.DataKeyOf(_fixture.Vault)!.ToArray();
        _fixture.Vault.Lock();
        var file = new VaultFile(_fixture.Directory);
        var document = file.Read();
        var kdf = new KdfParameters(KdfParameters.AlgorithmName, VaultFixture.TestIterations, VaultCrypto.NewSalt());
        var wrappingKey = new byte[VaultCrypto.KeySize];
        VaultCrypto.DeriveWrappingKey(masterKey, kdf, normalize: false, wrappingKey);
        var wrapped = VaultCrypto.Seal(wrappingKey, dataKey, VaultDocument.DataKeyAssociatedData);
        file.Write(document with { Kdf = kdf, DataKey = wrapped });
        CryptographicOperations.ZeroMemory(dataKey);
        Assert.Null(ReadJson()["kdf"]!["normalization"]);
    }

    private JsonNode ReadJson() => JsonNode.Parse(_fixture.FileBytes())!;
}
