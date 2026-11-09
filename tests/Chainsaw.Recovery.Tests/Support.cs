using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Recovery.Tests;

/// <summary>A console whose secrets the test queues and whose output the test reads.</summary>
internal sealed class FakeConsole(params string[] secrets) : IToolConsole
{
    private readonly Queue<string> _secrets = new(secrets);

    public List<string> Output { get; } = [];

    public List<string> Errors { get; } = [];

    public string All => string.Join('\n', Output.Concat(Errors));

    public void Out(string line) => Output.Add(line);

    public void Error(string line) => Errors.Add(line);

    public char[] ReadSecret(string prompt) => _secrets.Dequeue().ToCharArray();
}

/// <summary>A temporary folder that is removed afterwards.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chainsaw-recovery-test-" + Guid.NewGuid().ToString("N"));

    public TemporaryFolder() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>
/// An operator's computer with the real Security and Cases modules in a temporary folder laid out like
/// %LOCALAPPDATA%\Chainsaw: the vault in the root, the cases in Ärenden and the anchors in Ankare.
/// </summary>
internal sealed class OperatorComputer : IDisposable
{
    public const string MasterKey = "operatörens långa huvudnyckel";

    private readonly TemporaryFolder _folder = new();

    private readonly ServiceProvider _provider;

    public OperatorComputer(RecoveryPublicKey? recoveryKey, string name = "Olga Operatör")
    {
        var services = new ServiceCollection();
        services.AddSecurityModule(options => options.Directory = Root);
        services.AddCasesModule(options =>
        {
            options.Directory = System.IO.Path.Combine(Root, "Ärenden");
            options.AnchorDirectory = System.IO.Path.Combine(Root, "Ankare");
            options.RecoveryKey = recoveryKey;
            options.SharingIterations = 1_000;
            options.MinimumSharingIterations = 1_000;
        });
        _provider = services.BuildServiceProvider();
        Vault.Create(MasterKey);
        _provider.GetRequiredService<IOperatorProfileStore>().Save(new OperatorProfile(name, "Utredare", "Enheten"));
    }

    public string Root => _folder.Path;

    public IVault Vault => _provider.GetRequiredService<IVault>();

    public ICaseStore Store => _provider.GetRequiredService<ICaseStore>();

    public string CreateCase(string caseNumber)
    {
        using var created = Store.Create(caseNumber);
        return created.Path;
    }

    public void Dispose()
    {
        _provider.Dispose();
        _folder.Dispose();
    }
}
