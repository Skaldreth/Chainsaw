using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Recovery.Tests;

/// <summary><c>check --key</c>, which tries the private key for real, and <c>revoke</c>, which writes the line.</summary>
public sealed class CheckAndRevokeTests(KeyPairFixture keys) : IClassFixture<KeyPairFixture>
{
    [Fact]
    public void Check_with_the_private_key_opens_the_envelope_and_finds_the_file_whole_without_changing_it()
    {
        using var computer = new OperatorComputer(keys.PublicKey);
        var path = computer.CreateCase("A1");
        var before = File.ReadAllBytes(path);
        var console = new FakeConsole(KeyPairFixture.Password);

        var exitCode = RecoveryTool.Run(["check", path, "--key", keys.PrivatePath], console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Ja", console.All, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Check_with_the_private_key_says_no_for_a_case_without_its_envelope()
    {
        using var computer = new OperatorComputer(recoveryKey: null);
        var path = computer.CreateCase("A1");
        var console = new FakeConsole(KeyPairFixture.Password);

        var exitCode = RecoveryTool.Run(["check", path, "--key", keys.PrivatePath], console);

        Assert.Equal(ExitCodes.NotCovered, exitCode);
        Assert.Contains("Nej", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_with_the_private_key_finds_a_damaged_file()
    {
        using var computer = new OperatorComputer(keys.PublicKey);
        var path = computer.CreateCase("A1");
        var bytes = File.ReadAllBytes(path);
        bytes[^100] ^= 1;
        File.WriteAllBytes(path, bytes);
        var console = new FakeConsole(KeyPairFixture.Password);

        var exitCode = RecoveryTool.Run(["check", path, "--key", keys.PrivatePath], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains("skadad", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_refuses_both_a_public_and_a_private_key()
    {
        var console = new FakeConsole();

        var exitCode = RecoveryTool.Run(
            ["check", "A1.cut", "--key", keys.PrivatePath, "--public", keys.PublicPath], console);

        Assert.Equal(ExitCodes.Usage, exitCode);
    }

    [Fact]
    public void Revoke_writes_the_line_from_the_key_itself()
    {
        var console = new FakeConsole();

        var exitCode = RecoveryTool.Run(["revoke", keys.PublicPath], console);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([$"REVOKED {keys.PublicKey.Fingerprint}"], console.Output);
    }

    [Fact]
    public void Revoke_adds_the_line_to_the_file_once()
    {
        using var folder = new TemporaryFolder();
        var target = Path.Combine(folder.Path, "organization-recovery-key.pem");
        File.WriteAllText(target, "# Organisationens återställningsnyckel");

        Assert.Equal(ExitCodes.Success, RecoveryTool.Run(["revoke", keys.PublicPath, "--to", target], new FakeConsole()));
        Assert.Equal(ExitCodes.Success, RecoveryTool.Run(["revoke", keys.PublicPath, "--to", target], new FakeConsole()));

        var lines = File.ReadAllLines(target);
        Assert.Equal("# Organisationens återställningsnyckel", lines[0]);
        Assert.Single(lines, line => line == $"REVOKED {keys.PublicKey.Fingerprint}");
    }

    [Fact]
    public void Revoke_refuses_the_files_own_key()
    {
        using var folder = new TemporaryFolder();
        var target = Path.Combine(folder.Path, "organization-recovery-key.pem");
        var own = keys.PublicKey.ExportPem();
        File.WriteAllText(target, own);
        var console = new FakeConsole();

        var exitCode = RecoveryTool.Run(["revoke", keys.PublicPath, "--to", target], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal(own, File.ReadAllText(target));
    }

    [Fact]
    public void Revoke_refuses_a_private_key_file()
    {
        var console = new FakeConsole();

        var exitCode = RecoveryTool.Run(["revoke", keys.PrivatePath], console);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.DoesNotContain("REVOKED", console.All, StringComparison.Ordinal);
    }

    [Fact]
    public void A_point_with_a_valid_encoding_off_the_curve_is_refused()
    {
        var point = keys.PublicKey.ExportSubjectPublicKeyInfo();
        point[^1] ^= 1;

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => RecoveryPublicKey.FromSubjectPublicKeyInfo(point));
    }
}
