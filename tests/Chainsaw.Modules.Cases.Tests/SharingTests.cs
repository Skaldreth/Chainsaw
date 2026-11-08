using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases.Tests;

public sealed class SharingTests : IDisposable
{
    private readonly CaseFixture _owner = new();

    private readonly CaseFixture _colleague;

    public SharingTests()
    {
        _colleague = new CaseFixture("vault-b");
        _colleague.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Andra enheten");
    }

    public void Dispose()
    {
        _owner.Dispose();
        _colleague.Dispose();
    }

    [Fact]
    public void A_colleague_opens_the_case_with_the_sharing_password()
    {
        var path = CreateShared();

        using var opened = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);

        Assert.Equal("A1", opened.Info.CaseNumber);
        var last = opened.ReadJournal()[^1];
        Assert.Equal(CaseJournalKinds.OpenedWithPassword, last.Kind);
        Assert.Equal("Kollega Kollegasson", last.Operator.Name);
    }

    [Fact]
    public void A_wrong_sharing_password_does_not_open_the_case()
    {
        var path = CreateShared();

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().OpenWithPassword(path, "fel lösenord, men långt"));

        Assert.Equal(CaseError.NoAccess, error.Error);
        Assert.False(File.Exists(path + ".work"));
    }

    [Fact]
    public void Without_a_sharing_envelope_the_password_opens_nothing()
    {
        using (_owner.CreateStore().Create("A1"))
        {
        }

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().OpenWithPassword(_owner.CaseFile("A1"), CaseFixture.Password));

        Assert.Equal(CaseError.NoAccess, error.Error);
    }

    [Fact]
    public void A_removed_sharing_envelope_no_longer_opens_the_case()
    {
        var path = CreateShared();
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            opened.RemoveSharingPassword();
            Assert.False(opened.HasSharingPassword);
            Assert.Equal(CaseJournalKinds.SharingPasswordRemoved, opened.ReadJournal()[^1].Kind);
        }

        var error = Assert.Throws<CaseException>(
            () => _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password));

        Assert.Equal(CaseError.NoAccess, error.Error);
        using var stillMine = _owner.CreateStore().Open("A1");
    }

    [Fact]
    public void The_sharing_envelope_cannot_be_removed_by_a_vault_without_its_own_envelope()
    {
        var path = CreateShared();
        using var opened = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password);

        Assert.Equal(CaseError.WouldLoseAccess, Assert.Throws<CaseException>(opened.RemoveSharingPassword).Error);
        Assert.True(opened.HasSharingPassword);
    }

    [Fact]
    public void A_colleague_who_adds_an_own_envelope_opens_the_case_without_the_password()
    {
        var path = CreateShared();
        using (var opened = _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password))
        {
            opened.AddOperatorAccess();
            opened.RemoveSharingPassword();
        }

        File.Copy(path, _colleague.CaseFile("A1").EnsureFolder());
        using var own = _colleague.CreateStore().Open("A1");
        using var original = _owner.CreateStore().Open("A1");

        Assert.False(own.HasSharingPassword);
        Assert.Contains(own.ReadJournal(), entry => entry.Kind == CaseJournalKinds.OperatorAccessAdded);
    }

    [Fact]
    public void A_new_sharing_password_replaces_the_old_one()
    {
        var path = CreateShared();
        using (var opened = _owner.CreateStore().Open("A1"))
        {
            opened.SetSharingPassword("ett helt annat lösenord");
        }

        using (var header = File.OpenRead(path))
        {
            Assert.Single(KeyEnvelopes.OfKind(CaseContainer.ReadHeader(header), KeyEnvelopes.PasswordKind));
        }

        Assert.Throws<CaseException>(() => _colleague.CreateStore().OpenWithPassword(path, CaseFixture.Password));
        using var reopened = _colleague.CreateStore().OpenWithPassword(path, "ett helt annat lösenord");
    }

    [Fact]
    public void A_short_sharing_password_is_refused()
    {
        using var opened = _owner.CreateStore().Create("A1");

        var error = Assert.Throws<CaseException>(() => opened.SetSharingPassword("för kort"));

        Assert.Equal(CaseError.PasswordTooShort, error.Error);
        Assert.False(opened.HasSharingPassword);
    }

    [Fact]
    public void Swapping_envelopes_between_cases_opens_nothing()
    {
        var store = _owner.CreateStore();
        using (store.Create("A1"))
        {
        }

        using (store.Create("B2"))
        {
        }

        var second = File.ReadAllBytes(_owner.CaseFile("B2"));
        using var secondStream = new MemoryStream(second);
        var secondHeader = CaseContainer.ReadHeader(secondStream);
        CaseFileSurgery.RewriteHeader(
            _owner, _owner.CaseFile("A1"), header => header.WithEnvelopes(secondHeader.Envelopes));

        Assert.Equal(CaseError.NoAccess, Assert.Throws<CaseException>(() => store.Open("A1")).Error);
    }

    private string CreateShared()
    {
        using var opened = _owner.CreateStore().Create("A1");
        opened.SetSharingPassword(CaseFixture.Password);
        Assert.True(opened.HasSharingPassword);
        return opened.Path;
    }
}

internal static class PathExtensions
{
    public static string EnsureFolder(this string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
