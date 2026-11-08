using System.Security.Cryptography;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Recovery.Tests;

public sealed class RecoveryToolTests(KeyPairFixture keys) : IClassFixture<KeyPairFixture>
{
    private const string Administrator = "Ada Administratör";

    private const string SharingPassword = "Hemligt-Lösen-4711";

    [Fact]
    public void Keygen_writes_an_encrypted_private_key_and_a_public_key_and_shows_the_fingerprint()
    {
        Assert.Equal(ExitCodes.Success, keys.ExitCode);
        var privateText = File.ReadAllText(keys.PrivatePath);
        Assert.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", privateText, StringComparison.Ordinal);
        Assert.DoesNotContain("-----BEGIN PRIVATE KEY-----", privateText, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", File.ReadAllText(keys.PublicPath), StringComparison.Ordinal);
        Assert.Contains(keys.PublicKey.DisplayFingerprint, keys.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyPairFixture.Password, keys.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_private_key_opens_only_with_its_password()
    {
        using var key = KeyFiles.ReadPrivate(keys.PrivatePath, KeyPairFixture.Password);
        Assert.Equal(keys.PublicKey, RecoveryPublicKey.FromPrivateKey(key));

        Assert.ThrowsAny<CryptographicException>(
            () => KeyFiles.ReadPrivate(keys.PrivatePath, "ett annat långt lösenord"));
    }

    [Fact]
    public void A_key_file_that_asks_for_too_many_iterations_is_refused_before_anything_is_derived()
    {
        // A crafted file asking for int.MaxValue iterations would otherwise keep the tool busy for many minutes.
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.113549.1.5.13");
                using (writer.PushSequence())
                {
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("1.2.840.113549.1.5.12");
                        using (writer.PushSequence())
                        {
                            writer.WriteOctetString(new byte[16]);
                            writer.WriteInteger(int.MaxValue);
                        }
                    }

                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("2.16.840.1.101.3.4.1.42");
                        writer.WriteOctetString(new byte[16]);
                    }
                }
            }

            writer.WriteOctetString(new byte[64]);
        }

        var path = Path.Combine(Path.GetTempPath(), "chainsaw-crafted-" + Guid.NewGuid().ToString("N") + ".pem");
        File.WriteAllText(path, PemEncoding.WriteString("ENCRYPTED PRIVATE KEY", writer.Encode()));
        try
        {
            var error = Assert.ThrowsAny<CryptographicException>(
                () => KeyFiles.ReadPrivate(path, KeyPairFixture.Password));

            Assert.Contains("iteration", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_private_key_is_protected_with_pbkdf2_sha512_and_aes256()
    {
        var der = PemEncoding.Find(File.ReadAllText(keys.PrivatePath));
        var bytes = Convert.FromBase64String(File.ReadAllText(keys.PrivatePath)[der.Base64Data]);
        var info = new System.Formats.Asn1.AsnReader(bytes, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
        var algorithm = info.ReadSequence();

        Assert.Equal("1.2.840.113549.1.5.13", algorithm.ReadObjectIdentifier());
        var text = Convert.ToHexString(bytes);
        Assert.Contains(Convert.ToHexString(OidBytes("2.16.840.1.101.3.4.1.42")), text, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(OidBytes("1.2.840.113549.2.11")), text, StringComparison.Ordinal);
    }

    [Fact]
    public void On_unix_only_the_owner_may_read_the_private_key()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keys.PrivatePath));
    }

    [Fact]
    public void Keygen_never_overwrites_a_key()
    {
        var before = File.ReadAllBytes(keys.PrivatePath);
        var console = new FakeConsole(KeyPairFixture.Password, KeyPairFixture.Password);

        var exitCode = RecoveryTool.Run(["keygen", keys.Folder], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal(before, File.ReadAllBytes(keys.PrivatePath));
        Assert.Contains("skrivs aldrig över", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Keygen_never_overwrites_a_lone_public_key_either()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, KeyFiles.PublicFileName), "finns redan");

        var exitCode = RecoveryTool.Run(["keygen", folder.Path], new FakeConsole());

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal("finns redan", File.ReadAllText(Path.Combine(folder.Path, KeyFiles.PublicFileName)));
        Assert.False(File.Exists(Path.Combine(folder.Path, KeyFiles.PrivateFileName)));
    }

    [Theory]
    [InlineData("för kort", "för kort")]
    [InlineData("ett långt lösenord som", "ett långt lösenord men annat")]
    public void Keygen_refuses_a_short_or_mistyped_password_and_writes_nothing(string first, string second)
    {
        using var folder = new TemporaryFolder();

        var exitCode = RecoveryTool.Run(["keygen", folder.Path], new FakeConsole(first, second));

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));
    }

    [Fact]
    public void Keygen_refuses_a_folder_inside_a_git_repository()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, ".git"));
        var target = Path.Combine(folder.Path, "nycklar");
        var console = new FakeConsole(KeyPairFixture.Password, KeyPairFixture.Password);

        var exitCode = RecoveryTool.Run(["keygen", target], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.False(Directory.Exists(target));
        Assert.Contains("git-repot", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_shows_the_same_stable_fingerprint_as_keygen_and_chainsaw()
    {
        var first = new FakeConsole();
        var second = new FakeConsole();

        Assert.Equal(ExitCodes.Success, RecoveryTool.Run(["fingerprint", keys.PublicPath], first));
        Assert.Equal(ExitCodes.Success, RecoveryTool.Run(["fingerprint", keys.PublicPath], second));

        Assert.Equal([keys.PublicKey.DisplayFingerprint], first.Output);
        Assert.Equal(first.Output, second.Output);
    }

    [Fact]
    public void Fingerprint_refuses_the_private_key_file()
    {
        var console = new FakeConsole();

        Assert.Equal(ExitCodes.Failed, RecoveryTool.Run(["fingerprint", keys.PrivatePath], console));
        Assert.Empty(console.Output);
    }

    [Fact]
    public void Fingerprint_without_a_file_shows_the_built_in_key_or_says_honestly_that_there_is_none()
    {
        var console = new FakeConsole();
        var builtIn = new CasesOptions().RecoveryKey;

        var exitCode = RecoveryTool.Run(["fingerprint"], console);

        if (builtIn is null)
        {
            Assert.Equal(ExitCodes.Failed, exitCode);
            Assert.Contains("Ingen återställningsnyckel är inbyggd", console.All, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal([builtIn.DisplayFingerprint], console.Output);
        }
    }

    [Fact]
    public void Check_says_yes_for_a_case_with_an_envelope_for_the_key()
    {
        using var computer = new OperatorComputer(keys.PublicKey);
        var path = computer.CreateCase("A1");
        var console = new FakeConsole();

        var exitCode = RecoveryTool.Run(["check", path, "--public", keys.PublicPath], console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Ja", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_says_no_for_a_case_without_an_envelope_for_the_key()
    {
        using var computer = new OperatorComputer(recoveryKey: null);
        var path = computer.CreateCase("A1");
        var console = new FakeConsole();

        var exitCode = RecoveryTool.Run(["check", path, "--public", keys.PublicPath], console);

        Assert.Equal(ExitCodes.NotCovered, exitCode);
        Assert.Contains("Nej", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_of_a_missing_case_fails()
    {
        var console = new FakeConsole();

        var missing = Path.Combine(keys.Folder, "X.cut");

        var exitCode = RecoveryTool.Run(["check", missing, "--public", keys.PublicPath], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
    }

    [Fact]
    public void Open_with_share_gives_the_case_a_sharing_password_and_journals_the_administrator()
    {
        using var computer = new OperatorComputer(keys.PublicKey);
        var path = computer.CreateCase("A1");
        var console = new FakeConsole(KeyPairFixture.Password, SharingPassword, SharingPassword);

        var exitCode = RecoveryTool.Run(
            ["open", path, "--key", keys.PrivatePath, "--admin", Administrator, "--share"], console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.DoesNotContain(SharingPassword, console.All, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyPairFixture.Password, console.All, StringComparison.Ordinal);
        using var newComputer = new OperatorComputer(keys.PublicKey, "Ny Operatör");
        using var opened = newComputer.Store.OpenWithPassword(path, SharingPassword);
        var journal = opened.ReadJournal();
        var recovery = Assert.Single(journal, entry => entry.Kind == CaseJournalKinds.OpenedWithRecoveryKey);
        Assert.Equal(Administrator, recovery.Operator.Name);
        Assert.Contains(keys.PublicKey.Fingerprint, recovery.Details, StringComparison.Ordinal);
        var shared = Assert.Single(journal, entry => entry.Kind == CaseJournalKinds.SharingPasswordSet);
        Assert.Equal(Administrator, shared.Operator.Name);
    }

    [Fact]
    public void Open_with_vault_gives_the_operators_new_vault_an_envelope()
    {
        using var lost = new OperatorComputer(keys.PublicKey);
        var path = lost.CreateCase("A1");
        using var renewed = new OperatorComputer(keys.PublicKey);
        renewed.Vault.Lock();
        var console = new FakeConsole(KeyPairFixture.Password, OperatorComputer.MasterKey);

        var exitCode = RecoveryTool.Run(
            ["open", path, "--key", keys.PrivatePath, "--admin", Administrator, "--vault", renewed.Root], console);

        Assert.True(exitCode == ExitCodes.Success, console.All);
        Assert.DoesNotContain(OperatorComputer.MasterKey, console.All, StringComparison.Ordinal);
        renewed.Vault.Unlock(OperatorComputer.MasterKey);
        var copy = Path.Combine(renewed.Store.Directory, "A1.cut");
        Directory.CreateDirectory(renewed.Store.Directory);
        File.Copy(path, copy);
        using var opened = renewed.Store.Open("A1");
        var journal = opened.ReadJournal();
        var recovery = Assert.Single(journal, entry => entry.Kind == CaseJournalKinds.OpenedWithRecoveryKey);
        Assert.Contains(Administrator, recovery.Summary, StringComparison.Ordinal);
        Assert.Contains(journal, entry => entry.Kind == CaseJournalKinds.OperatorAccessAdded);
    }

    [Fact]
    public void Open_with_a_wrong_password_for_the_private_key_fails_and_leaves_the_case_as_it_was()
    {
        using var computer = new OperatorComputer(keys.PublicKey);
        var path = computer.CreateCase("A1");
        var before = File.ReadAllBytes(path);
        var console = new FakeConsole("fel lösenord för nyckeln", SharingPassword, SharingPassword);

        var exitCode = RecoveryTool.Run(
            ["open", path, "--key", keys.PrivatePath, "--admin", Administrator, "--share"], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Open_with_another_organisations_private_key_is_refused()
    {
        using var other = new TemporaryFolder();
        Assert.Equal(ExitCodes.Success, RecoveryTool.Run(
            ["keygen", other.Path], new FakeConsole(KeyPairFixture.Password, KeyPairFixture.Password)));
        using var computer = new OperatorComputer(keys.PublicKey);
        var path = computer.CreateCase("A1");
        var before = File.ReadAllBytes(path);
        var console = new FakeConsole(KeyPairFixture.Password, SharingPassword, SharingPassword);

        var exitCode = RecoveryTool.Run(
            ["open", path, "--key", Path.Combine(other.Path, KeyFiles.PrivateFileName), "--admin", Administrator,
                "--share"],
            console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains("inget kuvert", console.All, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData]
    [InlineData("okänt")]
    [InlineData("open", "A1.cut", "--key", "k.pem", "--admin", "Ada")]
    [InlineData("open", "A1.cut", "--key", "k.pem", "--admin", "Ada", "--share", "--vault", "v")]
    [InlineData("open", "A1.cut", "--key", "k.pem", "--share")]
    [InlineData("open", "A1.cut", "--key", "k.pem", "--admin", "Ada", "--share", "--password", "hemligt")]
    [InlineData("open", "A1.cut", "--key", "k.pem", "--admin", "Ada", "--org", "Enheten", "--vault", "v")]
    [InlineData("open", "A1.cut", "--key", "k.pem", "--admin", "Ada\nFörfalskad rad", "--share")]
    [InlineData("keygen")]
    [InlineData("check")]
    public void A_wrong_command_line_shows_the_usage(params string[] args)
    {
        var console = new FakeConsole();

        Assert.Equal(ExitCodes.Usage, RecoveryTool.Run(args, console));
        Assert.Contains("Chainsaw.Recovery", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_file_with_two_keys_is_refused_without_a_stack_trace()
    {
        using var folder = new TemporaryFolder();
        var doubled = Path.Combine(folder.Path, "dubbel.pem");
        var text = File.ReadAllText(keys.PrivatePath);
        File.WriteAllText(doubled, text + text);
        using var computer = new OperatorComputer(keys.PublicKey);
        var path = computer.CreateCase("A1");
        var console = new FakeConsole(KeyPairFixture.Password, SharingPassword, SharingPassword);

        var exitCode = RecoveryTool.Run(
            ["open", path, "--key", doubled, "--admin", Administrator, "--share"], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.DoesNotContain("   at ", console.All, StringComparison.Ordinal);
    }

    private static byte[] OidBytes(string oid)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        writer.WriteObjectIdentifier(oid);
        return writer.Encode();
    }
}
