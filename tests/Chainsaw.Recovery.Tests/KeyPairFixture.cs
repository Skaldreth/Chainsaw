using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Recovery.Tests;

/// <summary>One key pair made with the tool's keygen, shared by a test class so that PBKDF2 runs once.</summary>
public sealed class KeyPairFixture : IDisposable
{
    public const string Password = "administratörens lösenord för nyckeln";

    private readonly TemporaryFolder _folder = new();

    public KeyPairFixture()
    {
        var console = new FakeConsole(Password, Password);
        ExitCode = RecoveryTool.Run(["keygen", _folder.Path], console);
        Output = console.All;
        PublicKey = RecoveryPublicKey.FromPem(File.ReadAllText(PublicPath))!;
    }

    public int ExitCode { get; }

    public string Output { get; }

    public string Folder => _folder.Path;

    public string PrivatePath => Path.Combine(_folder.Path, KeyFiles.PrivateFileName);

    public string PublicPath => Path.Combine(_folder.Path, KeyFiles.PublicFileName);

    public RecoveryPublicKey PublicKey { get; }

    public void Dispose() => _folder.Dispose();
}
