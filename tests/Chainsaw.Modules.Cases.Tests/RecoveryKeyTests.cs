using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class RecoveryKeyTests : IDisposable
{
    /// <summary>A P-384 public key made by OpenSSL, and the SHA-256 of its DER as OpenSSL computes it.</summary>
    private const string OpenSslPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAENNvuKFSD8sfn9Pflyhz00eA6Hcv2/Psb
        9N2sLLhGXBlYjF69SjJ1BuB7wME6B6j8qxxccC2NaKdMSiqykGletVQo2PmHlaST
        D90TqCXpsDeipeydLtXt19rcdu/VoSHo
        -----END PUBLIC KEY-----
        """;

    private const string OpenSslFingerprint = "e6526d5662e6673c2fdb97cf6d66c37bd96beee28711dc95c6361c27f37089b7";

    /// <summary>The same key with a compressed point, which is refused.</summary>
    private const string CompressedPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MEYwEAYHKoZIzj0CAQYFK4EEACIDMgACNNvuKFSD8sfn9Pflyhz00eA6Hcv2/Psb
        9N2sLLhGXBlYjF69SjJ1BuB7wME6B6j8
        -----END PUBLIC KEY-----
        """;

    /// <summary>A P-256 public key, which is refused.</summary>
    private const string P256PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1HYEK95d7OPwBe6lfkKvSLu6FLxK
        oz1OaHtlvYeV8G00CW7wPWz+tGAXCLBuHQNbxWgQesHD7X2eFekIHiFlMA==
        -----END PUBLIC KEY-----
        """;

    private const string Administrator = "Ada Administratör";

    private readonly CaseFixture _owner = new();

    private readonly CaseFixture _administrator;

    private readonly ECDiffieHellman _privateKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

    private readonly ECDiffieHellman _otherKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

    public RecoveryKeyTests()
    {
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_privateKey);
        _administrator = new CaseFixture("vault-admin");
        _administrator.Profiles.Profile = new(Administrator, "Administratör", "Enheten");
    }

    public void Dispose()
    {
        _owner.Dispose();
        _administrator.Dispose();
        _privateKey.Dispose();
        _otherKey.Dispose();
    }

    [Fact]
    public void A_new_case_has_an_envelope_for_the_recovery_key_and_the_journal_says_which()
    {
        var store = _owner.CreateStore();
        using var created = store.Create("A1");

        Assert.Equal([_owner.RecoveryKey!.Fingerprint], created.RecoveryFingerprints);
        Assert.Equal([_owner.RecoveryKey.Fingerprint], store.ReadRecoveryFingerprints(created.Path));
        using var details = JsonDocument.Parse(created.ReadJournal()[0].Details!);
        Assert.Equal(_owner.RecoveryKey.Fingerprint, details.RootElement.GetProperty("recoveryKey").GetString());
    }

    [Fact]
    public void Without_a_configured_key_the_case_has_no_envelope_and_the_journal_says_so()
    {
        _owner.RecoveryKey = null;
        var store = _owner.CreateStore();
        using var created = store.Create("A1");

        Assert.Null(store.RecoveryKey);
        Assert.Empty(created.RecoveryFingerprints);
        using var details = JsonDocument.Parse(created.ReadJournal()[0].Details!);
        Assert.Equal(JsonValueKind.Null, details.RootElement.GetProperty("recoveryKey").ValueKind);
    }

    [Fact]
    public void An_older_case_without_an_envelope_gets_one_when_it_is_opened()
    {
        var key = _owner.RecoveryKey!;
        _owner.RecoveryKey = null;
        using (_owner.CreateStore().Create("A1"))
        {
        }

        _owner.RecoveryKey = key;
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Equal([key.Fingerprint], opened.RecoveryFingerprints);
            var entry = opened.ReadJournal()[^1];
            Assert.Equal(CaseJournalKinds.RecoveryAccessChanged, entry.Kind);
            Assert.Contains(key.Fingerprint, entry.Details, StringComparison.Ordinal);
        }

        Assert.Equal([key.Fingerprint], _owner.CreateStore().ReadRecoveryFingerprints(_owner.CaseFile("A1")));
        using var again = _owner.CreateStore().Open("A1");
        Assert.Single(again.ReadJournal(), entry => entry.Kind == CaseJournalKinds.RecoveryAccessChanged);
    }

    [Fact]
    public void A_replaced_key_gets_a_new_envelope_and_the_old_one_stays_until_it_is_revoked()
    {
        var oldKey = _owner.RecoveryKey!;
        using (_owner.CreateStore().Create("A1"))
        {
        }

        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_otherKey);
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Equal([oldKey.Fingerprint, _owner.RecoveryKey.Fingerprint], opened.RecoveryFingerprints);
            Assert.Equal(CaseJournalKinds.RecoveryAccessChanged, opened.ReadJournal()[^1].Kind);
        }

        using (_administrator.CreateStore().OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator))
        {
        }

        var copyBefore = _owner.CaseFile("A1") + ".kopia";
        File.Copy(_owner.CaseFile("A1"), copyBefore);
        _owner.Revoked = new HashSet<string>([oldKey.Fingerprint]);
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Equal([_owner.RecoveryKey.Fingerprint], opened.RecoveryFingerprints);
            var entry = opened.ReadJournal()[^1];
            Assert.Equal(CaseJournalKinds.RecoveryAccessChanged, entry.Kind);
            Assert.Contains(oldKey.Fingerprint, entry.Details, StringComparison.Ordinal);
        }

        var error = Assert.Throws<CaseException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator));
        Assert.Equal(CaseError.NoAccess, error.Error);
        using (_administrator.CreateStore().OpenWithRecoveryKey(_owner.CaseFile("A1"), _otherKey, Administrator))
        {
        }

        // A copy made before the key was revoked still opens with the old key.
        using var old = _administrator.CreateStore().OpenWithRecoveryKey(copyBefore, _privateKey, Administrator);
        Assert.Equal("A1", old.Info.CaseNumber);
    }

    [Fact]
    public void An_older_or_foreign_build_never_removes_the_envelope_of_a_key_it_does_not_know()
    {
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_otherKey);
        using (_owner.CreateStore().Create("A1"))
        {
        }

        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_privateKey);
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Contains(RecoveryPublicKey.FromPrivateKey(_otherKey).Fingerprint, opened.RecoveryFingerprints);
        }

        using var newer = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _otherKey, Administrator);
        Assert.Equal("A1", newer.Info.CaseNumber);
    }

    [Fact]
    public void The_built_in_text_gives_the_key_and_the_revoked_keys()
    {
        var key = RecoveryPublicKey.FromPrivateKey(_privateKey);
        var text = "Förklaring\n" + key.ExportPem() + "\nREVOKED " + RecoveryPublicKey.FormatFingerprint(
            OpenSslFingerprint) + "\n";

        var parsed = EmbeddedRecoveryKey.Parse(text);

        Assert.Equal(key, parsed.Key);
        Assert.Equal([OpenSslFingerprint], parsed.Revoked);
    }

    [Theory]
    [InlineData("REVOKED {0}")]
    [InlineData("revoked {0}")]
    [InlineData("Revoked: {0}")]
    [InlineData("REVOKED\t{0}")]
    [InlineData("REVOKED\u00A0{0}")]
    [InlineData("- REVOKED {0}")]
    [InlineData("  * revoked:{0}\r")]
    [InlineData("`REVOKED {0}`")]
    [InlineData("– REVOKED {0}")]
    [InlineData("REVOKED={0}")]
    [InlineData("**REVOKED** {0}")]
    [InlineData("REVOKED E652-6D56-62E6-673C-2FDB-97CF-6D66-C37B-D96B-EEE2-8711-DC95-C636-1C27-F370-89B7")]
    [InlineData("\uFEFF\u200BREVOKED\u200B {0}")]
    public void Every_spelling_of_a_revocation_is_read(string line)
    {
        var parsed = EmbeddedRecoveryKey.Parse(
            "Förklaring\r" + string.Format(System.Globalization.CultureInfo.InvariantCulture, line,
                RecoveryPublicKey.FormatFingerprint(OpenSslFingerprint)) + "\rSlut");

        Assert.Equal([OpenSslFingerprint], parsed.Revoked);
    }

    [Fact]
    public void Two_public_keys_in_the_built_in_text_are_refused()
    {
        var key = RecoveryPublicKey.FromPrivateKey(_privateKey);

        Assert.Throws<InvalidOperationException>(
            () => EmbeddedRecoveryKey.Parse(key.ExportPem() + "\n" + OpenSslPublicKey));
    }

    [Fact]
    public void The_fingerprint_can_be_written_without_groups_and_in_uppercase()
    {
        var parsed = EmbeddedRecoveryKey.Parse("REVOKED " + OpenSslFingerprint.ToUpperInvariant());

        Assert.Equal([OpenSslFingerprint], parsed.Revoked);
    }

    [Theory]
    [InlineData("REVOKED 1234")]
    [InlineData("revoked")]
    [InlineData("REVOKED: inte ett fingeravtryck")]
    [InlineData("Revoked keys are listed below.")]
    [InlineData("1. REVOKED e6526d5662e6673c2fdb97cf6d66c37bd96beee28711dc95c6361c27f37089b7")]
    [InlineData("REVOKED e6526d5662e6673c2fdb97cf6d66c37bd96beee28711dc95c6361c27f37089b7 # röjd")]
    [InlineData("REVOKED_KEY e6526d5662e6673c2fdb97cf6d66c37bd96beee28711dc95c6361c27f37089b7")]
    [InlineData("REVOKED e6526d5662e6673c2fdb97cf6d66c37bd96beee28711dc95c6361c27f37089b7 ersatt av "
        + "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("ÅTERKALLAD E652.6D56.62E6.673C.2FDB.97CF.6D66.C37B.D96B.EEE2.8711.DC95.C636.1C27.F370.89B7")]
    [InlineData("REVOKE e6526d5662e6673c2fdb97cf6d66c37bd96beee28711dc95c6361c27f37089b7")]
    [InlineData("ÅTERKALLAD e6526d5662e6673c2fdb97cf6d66c37bd96beee28711dc95c6361c27f37089b7")]
    [InlineData("-----begin private key-----")]
    [InlineData("REVOKED 2026 E652 6D56 62E6 673C 2FDB 97CF 6D66 C37B D96B EEE2 8711 DC95 C636 1C27 F370 89B7")]
    [InlineData("REVOKED E652 6D56 62E6 673C 2FDB 97CF 6D66 C37B D96B EEE2 8711 DC95 C636 1C27 F370 89B7 2026")]
    [InlineData("Röjd: E652-6D56-62E6-673C-2FDB-97CF-6D66-C37B-D96B-EEE2-8711-DC95-C636-1C27-F370-89B7")]
    [InlineData("E6:52:6D:56:62:E6:67:3C:2F:DB:97:CF:6D:66:C3:7B:D9:6B:EE:E2:87:11:DC:95:C6:36:1C:27:F3:70:89:B7")]
    [InlineData("-----BEGIN ENCRYPTED PRIVATE KEY-----")]
    public void Built_in_text_that_does_not_hold_stops_the_build_from_running(string line)
    {
        Assert.Throws<InvalidOperationException>(() => EmbeddedRecoveryKey.Parse("Förklaring\n" + line));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void A_key_block_on_one_line_does_not_hide_a_revocation_after_it(string separator)
    {
        var key = RecoveryPublicKey.FromPrivateKey(_privateKey);
        var oneLine = key.ExportPem().Replace("\n", separator, StringComparison.Ordinal);

        var parsed = EmbeddedRecoveryKey.Parse(oneLine + "\nKommentar\nREVOKED " + OpenSslFingerprint + "\n");

        Assert.Equal(key, parsed.Key);
        Assert.Equal([OpenSslFingerprint], parsed.Revoked);
    }

    [Fact]
    public void A_block_that_is_never_closed_does_not_hide_a_revocation()
    {
        Assert.Equal(
            [OpenSslFingerprint],
            EmbeddedRecoveryKey.Parse("-----BEGIN NOTE-----\nREVOKED " + OpenSslFingerprint).Revoked);
    }

    [Fact]
    public void A_build_cannot_revoke_its_own_key()
    {
        var key = RecoveryPublicKey.FromPrivateKey(_privateKey);

        Assert.Throws<InvalidOperationException>(
            () => EmbeddedRecoveryKey.Parse(key.ExportPem() + "\nREVOKED " + key.Fingerprint));
    }

    [Fact]
    public void A_recovery_envelope_of_a_later_format_does_not_stop_the_case_from_opening_and_is_kept()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        var future = new System.Text.Json.Nodes.JsonObject
        {
            ["kind"] = "recovery",
            ["alg"] = "v2",
            ["fingerprint"] = "ABCDEF",
        };
        RewriteHeader(_owner.CaseFile("A1"), header => header.WithEnvelopes(header.Envelopes.Append(future)));
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_otherKey);

        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Contains(_owner.RecoveryKey.Fingerprint, opened.RecoveryFingerprints);
        }

        using var stream = File.OpenRead(_owner.CaseFile("A1"));
        Assert.Contains(
            CaseContainer.ReadHeader(stream).Envelopes,
            envelope => envelope["alg"]?.GetValue<string>() == "v2");
    }

    [Fact]
    public void A_damaged_envelope_does_not_stop_a_good_one_for_the_same_key()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        var damaged = RecoveryEnvelope(_owner.CaseFile("A1"));
        using (var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
        {
            damaged["ephemeral"] = Convert.ToBase64String(p256.ExportSubjectPublicKeyInfo());
        }

        RewriteHeader(_owner.CaseFile("A1"), header => header.WithEnvelopes(header.Envelopes.Prepend(damaged)));

        using var opened = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator);
        Assert.Equal("A1", opened.Info.CaseNumber);
    }

    [Fact]
    public void A_header_full_of_copies_of_the_vaults_own_envelope_gives_one_up_for_the_recovery_envelope()
    {
        _owner.RecoveryKey = null;
        using (_owner.CreateStore().Create("A1"))
        {
        }

        RewriteHeader(_owner.CaseFile("A1"), header => header.WithEnvelopes(header.Envelopes.Concat(
            Enumerable.Range(0, CaseContainer.MaximumEnvelopes - 1)
                .Select(_ => (System.Text.Json.Nodes.JsonObject)header.Envelopes[0].DeepClone()))));
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_privateKey);

        using (var opened = _owner.CreateStore().Open("A1"))
        {
            // A copy is never read, so it cannot keep the recovery envelope out of a full header.
            Assert.Equal([_owner.RecoveryKey.Fingerprint], opened.RecoveryFingerprints);
            var entry = opened.ReadJournal().Last(item => item.Kind == CaseJournalKinds.RecoveryAccessChanged);
            Assert.Contains("1 kuvert som Chainsaw inte kan använda", entry.Summary, StringComparison.Ordinal);
        }

        using var again = _owner.CreateStore().Open("A1");
        Assert.Equal("A1", again.Info.CaseNumber);
    }

    [Fact]
    public void The_administrator_opens_a_case_with_the_private_key_and_the_journal_says_who_and_which_key()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        using var opened = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator);

        var entry = opened.ReadJournal()[^1];
        Assert.Equal(CaseJournalKinds.OpenedWithRecoveryKey, entry.Kind);
        Assert.Contains(Administrator, entry.Summary, StringComparison.Ordinal);
        using var details = JsonDocument.Parse(entry.Details!);
        Assert.Equal(Administrator, details.RootElement.GetProperty("administrator").GetString());
        Assert.Equal(_owner.RecoveryKey!.Fingerprint, details.RootElement.GetProperty("fingerprint").GetString());
        Assert.True(new CaseVerifier(_administrator.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void The_administrator_gives_the_case_a_sharing_password_that_the_operator_then_uses()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        using (var opened = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator))
        {
            opened.SetSharingPassword(CaseFixture.Password);
        }

        var newVault = new CaseFixture("vault-a-new", _owner.Directory);
        using var mine = newVault.CreateStore().OpenWithPassword(_owner.CaseFile("A1"), CaseFixture.Password);
        mine.AddOperatorAccess();
        mine.RemoveSharingPassword();
        Assert.False(mine.HasSharingPassword);
    }

    [Fact]
    public void A_wrong_private_key_is_refused_and_leaves_nothing_behind()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        var error = Assert.Throws<CaseException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _otherKey, Administrator));

        Assert.Equal(CaseError.NoAccess, error.Error);
        Assert.False(File.Exists(_owner.CaseFile("A1") + ".work"));
        Assert.False(File.Exists(_owner.CaseFile("A1") + ".lock"));
    }

    [Fact]
    public void A_case_without_a_recovery_envelope_does_not_open_with_the_private_key()
    {
        _owner.RecoveryKey = null;
        using (_owner.CreateStore().Create("A1"))
        {
        }

        var error = Assert.Throws<CaseException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator));

        Assert.Equal(CaseError.NoAccess, error.Error);
    }

    [Fact]
    public void A_private_key_on_another_curve_is_refused()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        using var p256 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Assert.ThrowsAny<CryptographicException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), p256, Administrator));
    }

    [Fact]
    public void An_envelope_moved_to_another_case_does_not_open_it()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        _owner.RecoveryKey = null;
        using (_owner.CreateStore().Create("B2"))
        {
        }

        var moved = RecoveryEnvelope(_owner.CaseFile("A1"));
        RewriteHeader(_owner.CaseFile("B2"), header => header.WithEnvelopes(header.Envelopes.Append(moved)));

        var error = Assert.Throws<CaseException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("B2"), _privateKey, Administrator));
        Assert.Equal(CaseError.NoAccess, error.Error);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("tag")]
    [InlineData("nonce")]
    public void A_changed_envelope_does_not_open(string field)
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        ChangeRecoveryEnvelope(_owner.CaseFile("A1"), envelope =>
        {
            var bytes = Convert.FromBase64String(envelope[field]!.GetValue<string>());
            bytes[0] ^= 1;
            envelope[field] = Convert.ToBase64String(bytes);
        });

        var error = Assert.Throws<CaseException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator));
        Assert.Equal(CaseError.NoAccess, error.Error);
    }

    [Fact]
    public void An_ephemeral_key_replaced_by_another_valid_key_does_not_open()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        ChangeRecoveryEnvelope(_owner.CaseFile("A1"), envelope =>
        {
            using var other = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
            envelope["ephemeral"] = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        });

        var error = Assert.Throws<CaseException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator));
        Assert.Equal(CaseError.NoAccess, error.Error);
    }

    [Fact]
    public void An_ephemeral_key_on_another_curve_makes_the_file_corrupt()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        ChangeRecoveryEnvelope(_owner.CaseFile("A1"), envelope =>
        {
            using var other = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            envelope["ephemeral"] = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo());
        });

        var error = Assert.Throws<CaseException>(() => _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, Administrator));
        Assert.Equal(CaseError.Corrupt, error.Error);
    }

    [Fact]
    public void The_operator_still_opens_the_case_with_the_vault()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        using var opened = _owner.CreateStore().Open("A1");
        Assert.Equal("A1", opened.Info.CaseNumber);
        Assert.True(new CaseVerifier(_owner.Clock).Verify(opened).IsIntact);
    }

    [Fact]
    public void The_fingerprint_is_the_sha256_of_the_der_key_as_openssl_computes_it()
    {
        var key = RecoveryPublicKey.FromPem(OpenSslPublicKey)!;

        Assert.Equal(OpenSslFingerprint, key.Fingerprint);
        Assert.Equal(16, key.DisplayFingerprint.Split(' ').Length);
        Assert.Equal(OpenSslFingerprint.ToUpperInvariant(), key.DisplayFingerprint.Replace(" ", ""));
    }

    [Fact]
    public void The_fingerprint_is_the_same_from_the_private_key_the_der_and_the_pem()
    {
        var fromPrivate = RecoveryPublicKey.FromPrivateKey(_privateKey);
        var fromDer = RecoveryPublicKey.FromSubjectPublicKeyInfo(_privateKey.ExportSubjectPublicKeyInfo());
        var fromPem = RecoveryPublicKey.FromPem("En kommentar först\n" + fromPrivate.ExportPem() + "\nOch sist.")!;

        Assert.Equal(fromPrivate.Fingerprint, fromDer.Fingerprint);
        Assert.Equal(fromPrivate.Fingerprint, fromPem.Fingerprint);
        Assert.Equal(fromPrivate, fromPem);
    }

    [Fact]
    public void Text_without_a_public_key_means_that_no_key_is_configured()
    {
        Assert.Null(RecoveryPublicKey.FromPem("Ingen nyckel är konfigurerad ännu."));
    }

    [Theory]
    [InlineData(CompressedPublicKey)]
    [InlineData(P256PublicKey)]
    public void Other_curves_and_compressed_points_are_refused(string pem)
    {
        Assert.ThrowsAny<CryptographicException>(() => RecoveryPublicKey.FromPem(pem));
    }

    [Fact]
    public void A_private_key_block_is_not_taken_for_a_public_key()
    {
        var pem = _privateKey.ExportPkcs8PrivateKeyPem().Replace("PRIVATE KEY", "PUBLIC KEY", StringComparison.Ordinal);

        Assert.ThrowsAny<CryptographicException>(() => RecoveryPublicKey.FromPem(pem));
    }

    [Fact]
    public void The_built_in_key_is_either_absent_or_a_valid_p384_key()
    {
        var key = EmbeddedRecoveryKey.Load().Key;

        if (key is not null)
        {
            Assert.Equal(64, key.Fingerprint.Length);
        }
    }

    [Fact]
    public void The_built_in_resource_holds_no_private_key()
    {
        using var stream = typeof(EmbeddedRecoveryKey).Assembly
            .GetManifestResourceStream(EmbeddedRecoveryKey.ResourceName)!;
        using var reader = new StreamReader(stream);

        Assert.DoesNotContain("PRIVATE", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    private static System.Text.Json.Nodes.JsonObject RecoveryEnvelope(string path)
    {
        using var stream = File.OpenRead(path);
        return (System.Text.Json.Nodes.JsonObject)CaseContainer.ReadHeader(stream).Envelopes
            .Single(envelope => KeyEnvelopes.IsKind(envelope, RecoveryEnvelopes.Kind))
            .DeepClone();
    }

    private void ChangeRecoveryEnvelope(string path, Action<System.Text.Json.Nodes.JsonObject> change) =>
        RewriteHeader(path, header => header.WithEnvelopes(header.Envelopes.Select(envelope =>
        {
            var copy = (System.Text.Json.Nodes.JsonObject)envelope.DeepClone();
            if (KeyEnvelopes.IsKind(copy, RecoveryEnvelopes.Kind))
            {
                change(copy);
            }

            return copy;
        })));

    private void RewriteHeader(string path, Func<CaseHeader, CaseHeader> change) =>
        CaseFileSurgery.RewriteHeader(_owner, path, change);
}
