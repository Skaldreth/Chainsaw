using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>
/// The recovery envelope as a function of the file key: Chainsaw makes it again on every opening and replaces one
/// that is missing or does not match, also when the header has been filled up.
/// </summary>
public sealed class RecoveryEnvelopeCheckTests : IDisposable
{
    private readonly CaseFixture _owner = new();

    private readonly CaseFixture _administrator = new("vault-admin");

    private readonly ECDiffieHellman _privateKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

    public RecoveryEnvelopeCheckTests()
    {
        _owner.RecoveryKey = RecoveryPublicKey.FromPrivateKey(_privateKey);
        _administrator.Profiles.Profile = new("Ada Administratör", "Administratör", "Enheten");
    }

    public void Dispose()
    {
        _owner.Dispose();
        _administrator.Dispose();
        _privateKey.Dispose();
    }

    [Fact]
    public void The_same_file_key_case_and_recovery_key_give_the_same_envelope()
    {
        var caseId = Guid.NewGuid();
        var fileKey = RandomNumberGenerator.GetBytes(CaseCrypto.KeySize);

        var first = RecoveryEnvelopes.Create(caseId, fileKey, _owner.RecoveryKey!);
        var second = RecoveryEnvelopes.Create(caseId, fileKey, _owner.RecoveryKey!);

        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.True(RecoveryEnvelopes.Matches(first, second));
    }

    [Fact]
    public void Another_case_file_key_or_recovery_key_gives_another_ephemeral_key_and_nonce()
    {
        var caseId = Guid.NewGuid();
        var fileKey = RandomNumberGenerator.GetBytes(CaseCrypto.KeySize);
        using var otherKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        JsonObject[] envelopes =
        [
            RecoveryEnvelopes.Create(caseId, fileKey, _owner.RecoveryKey!),
            RecoveryEnvelopes.Create(Guid.NewGuid(), fileKey, _owner.RecoveryKey!),
            RecoveryEnvelopes.Create(caseId, RandomNumberGenerator.GetBytes(CaseCrypto.KeySize), _owner.RecoveryKey!),
            RecoveryEnvelopes.Create(caseId, fileKey, RecoveryPublicKey.FromPrivateKey(otherKey)),
        ];

        foreach (var field in new[] { "ephemeral", "nonce" })
        {
            Assert.Equal(
                envelopes.Length, envelopes.Select(item => item[field]!.GetValue<string>()).Distinct().Count());
        }

        Assert.False(RecoveryEnvelopes.Matches(envelopes[0], envelopes[1]));
    }

    [Fact]
    public void The_ephemeral_key_is_imported_from_the_scalar_alone_and_has_its_public_point()
    {
        var scalar = new byte[48];
        RecoveryEnvelopes.DeriveEphemeralScalar(
            Guid.NewGuid(), RandomNumberGenerator.GetBytes(CaseCrypto.KeySize), new string('a', 64), scalar);
        using var imported = ECDiffieHellman.Create();
        imported.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP384, D = scalar });

        var parameters = imported.ExportParameters(includePrivateParameters: true);

        Assert.Equal(scalar, parameters.D);
        Assert.Equal(48, parameters.Q.X!.Length);
        using var check = ECDiffieHellman.Create();
        check.ImportParameters(parameters);
    }

    [Fact]
    public void The_order_is_that_of_p384()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

        Assert.Equal(key.ExportExplicitParameters(false).Curve.Order, RecoveryEnvelopes.Order);
    }

    [Fact]
    public void A_well_formed_envelope_that_wraps_another_key_is_replaced_on_opening_and_journalled()
    {
        CreateCase();
        var caseId = HeaderOf(_owner.CaseFile("A1")).CaseId;
        var garbage = RecoveryEnvelopes.Create(
            caseId, RandomNumberGenerator.GetBytes(CaseCrypto.KeySize), _owner.RecoveryKey!);
        RewriteHeader(header => header.WithEnvelopes(
            header.Envelopes.Select(item => KeyEnvelopes.IsKind(item, RecoveryEnvelopes.Kind) ? garbage : item)));

        using (var opened = _owner.CreateStore().Open("A1"))
        {
            var entry = opened.ReadJournal().Last(item => item.Kind == CaseJournalKinds.RecoveryAccessChanged);
            Assert.Equal(
                "Kuvertet för organisationens återställningsnyckel höll inte och skrevs på nytt", entry.Summary);
        }

        var recovery = Assert.Single(HeaderOf(_owner.CaseFile("A1")).Envelopes, IsRecovery);
        Assert.False(RecoveryEnvelopes.Matches(recovery, garbage));
        using var recovered = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, "Ada Administratör");
        Assert.Equal("A1", recovered.Info.CaseNumber);
    }

    [Fact]
    public void An_envelope_of_the_older_construction_is_replaced_as_an_upgrade_not_as_tampering()
    {
        CreateCase();
        RewriteHeader(header => header.WithEnvelopes(header.Envelopes.Select(item =>
        {
            if (!KeyEnvelopes.IsKind(item, RecoveryEnvelopes.Kind))
            {
                return item;
            }

            // The older construction used a random ephemeral key and carried a "check" field.
            var older = (System.Text.Json.Nodes.JsonObject)item.DeepClone();
            var key = Convert.FromBase64String(older["key"]!.GetValue<string>());
            key[0] ^= 1;
            older["key"] = Convert.ToBase64String(key);
            older["check"] = Convert.ToBase64String(new byte[32]);
            return older;
        })));

        using var opened = _owner.CreateStore().Open("A1");
        var entry = opened.ReadJournal().Last(item => item.Kind == CaseJournalKinds.RecoveryAccessChanged);
        Assert.Equal(
            "Kuvertet för organisationens återställningsnyckel var i den äldre formen och skrevs på nytt",
            entry.Summary);
    }

    [Fact]
    public void An_envelope_that_wraps_another_key_does_not_hide_the_sound_one_from_the_administrator()
    {
        CreateCase();
        var caseId = HeaderOf(_owner.CaseFile("A1")).CaseId;
        var garbage = RecoveryEnvelopes.Create(
            caseId, RandomNumberGenerator.GetBytes(CaseCrypto.KeySize), _owner.RecoveryKey!);
        RewriteHeader(header => header.WithEnvelopes(header.Envelopes.Prepend(garbage)));

        Assert.True(_administrator.CreateStore().TryRecoveryKey(_owner.CaseFile("A1"), _privateKey));
        using var recovered = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, "Ada Administratör");
        Assert.Equal("A1", recovered.Info.CaseNumber);
    }

    [Fact]
    public void Only_an_envelope_that_wraps_another_key_makes_the_file_corrupt_for_the_administrator()
    {
        CreateCase();
        var caseId = HeaderOf(_owner.CaseFile("A1")).CaseId;
        var garbage = RecoveryEnvelopes.Create(
            caseId, RandomNumberGenerator.GetBytes(CaseCrypto.KeySize), _owner.RecoveryKey!);
        RewriteHeader(header => header.WithEnvelopes(
            header.Envelopes.Select(item => IsRecovery(item) ? garbage : item)));

        var error = Assert.Throws<CaseException>(
            () => _administrator.CreateStore().TryRecoveryKey(_owner.CaseFile("A1"), _privateKey));
        Assert.Equal(CaseError.Corrupt, error.Error);
    }

    [Fact]
    public void A_removed_envelope_in_a_header_padded_with_unknown_kinds_is_written_again()
    {
        CreateCase();
        RewriteHeader(header => header.WithEnvelopes(header.Envelopes.Where(item => !IsRecovery(item)).Concat(
            Enumerable.Range(0, CaseContainer.MaximumEnvelopes - 1)
                .Select(index => new JsonObject { ["kind"] = "okänd", ["n"] = index }))));
        Assert.Equal(CaseContainer.MaximumEnvelopes, HeaderOf(_owner.CaseFile("A1")).Envelopes.Count);

        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Equal([_owner.RecoveryKey!.Fingerprint], opened.RecoveryFingerprints);
            var entry = opened.ReadJournal().Last(item => item.Kind == CaseJournalKinds.RecoveryAccessChanged);
            Assert.Contains("saknades och skrevs på nytt", entry.Summary, StringComparison.Ordinal);
            Assert.Contains("1 kuvert som Chainsaw inte kan använda", entry.Summary, StringComparison.Ordinal);
            Assert.Contains("\"dropped\":1", entry.Details, StringComparison.Ordinal);
        }

        var header = HeaderOf(_owner.CaseFile("A1"));
        Assert.Equal(CaseContainer.MaximumEnvelopes, header.Envelopes.Count);
        var last = CaseContainer.MaximumEnvelopes - 2;
        Assert.DoesNotContain(header.Envelopes, item => item["n"]?.GetValue<int>() == last);
        using var recovered = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, "Ada Administratör");
        Assert.Equal("A1", recovered.Info.CaseNumber);
    }

    [Fact]
    public void A_header_padded_with_foreign_vault_envelopes_still_gets_the_envelope_and_keeps_the_own_one()
    {
        CreateCase();
        RewriteHeader(header => header.WithEnvelopes(header.Envelopes.Where(item => !IsRecovery(item)).Concat(
            Enumerable.Range(0, CaseContainer.MaximumEnvelopes - 1).Select(_ => KeyEnvelopes.CreateOperator(
                header.CaseId,
                RandomNumberGenerator.GetBytes(CaseCrypto.KeySize),
                RandomNumberGenerator.GetBytes(CaseCrypto.KeySize),
                null)))));

        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Equal([_owner.RecoveryKey!.Fingerprint], opened.RecoveryFingerprints);
        }

        using var again = _owner.CreateStore().Open("A1");
        Assert.Equal("A1", again.Info.CaseNumber);
        Assert.Single(HeaderOf(_owner.CaseFile("A1")).Envelopes, IsRecovery);
    }

    [Fact]
    public void A_header_padded_with_copies_of_the_sharing_and_own_envelopes_still_gets_the_envelope()
    {
        using (var created = _owner.CreateStore().Create("A1"))
        {
            created.SetSharingPassword(CaseFixture.Password);
        }

        RewriteHeader(header =>
        {
            var copies = header.Envelopes
                .Where(item => KeyEnvelopes.IsKind(item, KeyEnvelopes.PasswordKind)
                    || KeyEnvelopes.IsKind(item, KeyEnvelopes.OperatorKind))
                .ToList();
            var kept = header.Envelopes.Where(item => !IsRecovery(item)).ToList();
            return header.WithEnvelopes(kept.Concat(Enumerable.Range(0, CaseContainer.MaximumEnvelopes - kept.Count)
                .Select(index => (JsonObject)copies[index % copies.Count].DeepClone())));
        });
        Assert.Equal(CaseContainer.MaximumEnvelopes, HeaderOf(_owner.CaseFile("A1")).Envelopes.Count);

        using (var opened = _owner.CreateStore().Open("A1"))
        {
            Assert.Equal([_owner.RecoveryKey!.Fingerprint], opened.RecoveryFingerprints);
            Assert.True(opened.HasSharingPassword);
        }

        using var recovered = _administrator.CreateStore()
            .OpenWithRecoveryKey(_owner.CaseFile("A1"), _privateKey, "Ada Administratör");
        Assert.Equal("A1", recovered.Info.CaseNumber);
    }

    [Fact]
    public void Copies_of_the_sound_envelope_are_reduced_to_one()
    {
        CreateCase();
        RewriteHeader(header => header.WithEnvelopes(
            header.Envelopes.Concat(header.Envelopes.Where(IsRecovery).Select(item => (JsonObject)item.DeepClone()))));

        using (var opened = _owner.CreateStore().Open("A1"))
        {
            var entry = opened.ReadJournal().Last(item => item.Kind == CaseJournalKinds.RecoveryAccessChanged);
            Assert.Contains("som inte höll togs bort", entry.Summary, StringComparison.Ordinal);
        }

        Assert.Single(HeaderOf(_owner.CaseFile("A1")).Envelopes, IsRecovery);
    }

    private static bool IsRecovery(JsonObject envelope) => KeyEnvelopes.IsKind(envelope, RecoveryEnvelopes.Kind);

    private static CaseHeader HeaderOf(string path)
    {
        using var stream = File.OpenRead(path);
        return CaseContainer.ReadHeader(stream);
    }

    private void CreateCase()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }
    }

    private void RewriteHeader(Func<CaseHeader, CaseHeader> change) =>
        CaseFileSurgery.RewriteHeader(_owner, _owner.CaseFile("A1"), change);
}
