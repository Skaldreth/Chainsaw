// The test case and its made-up source exist only in a Debug build.
#if DEBUG
using System.Security.Cryptography;
using System.Text;
using Chainsaw.App.TestData;
using Chainsaw.Modules.Attribution;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Monitoring;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing;
using Chainsaw.Modules.Requests;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.App.TestData.Tests;

/// <summary>
/// The modules as the program registers them, in a temporary folder, with the test data on top. The real sources' way
/// out is a fake that counts what would have gone out and lets nothing through, and the watches' gate is closed (no
/// exit), as on a computer whose VPN is not connected.
/// </summary>
internal sealed class Harness : IDisposable
{
    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-testdata-" + Guid.NewGuid().ToString("N"));

    public Harness()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExitHttp>(Outside);
        services.AddChainsModule();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore, FakeProfiles>();
        services.AddSingleton<IVaultSecrets>(new TestVaultSecrets());
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSingleton<IWatchGate>(new ClosedGate());
        services.AddCasesModule(options =>
        {
            options.Directory = Path.Combine(_directory, "Ärenden");
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddClusteringModule();
        services.AddAttributionModule(options => options.Directory = _directory);
        services.AddTracingModule();
        services.AddChecksModule();
        services.AddMonitoringModule();
        services.AddPricingModule();
        services.AddRequestsModule();
        services.AddSingleton<IUserDataProtector, PlainProtector>();
        services.AddBackgroundWatchFiles(Path.Combine(_directory, "Bevakning"));
        services.AddTestData();
        _provider = services.BuildServiceProvider();
    }

    /// <summary>Gets the way out of the real sources, which counts and refuses.</summary>
    public OutsideHttp Outside { get; } = new();

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

/// <summary>The real sources' way out: nothing is connected, and every attempt is counted.</summary>
internal sealed class OutsideHttp : IExitHttp
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _attempts);
        throw new ExitNotConnectedException();
    }
}

/// <summary>Leaves the background's files as they are; the tests never read them back.</summary>
internal sealed class PlainProtector : IUserDataProtector
{
    public byte[] Protect(ReadOnlySpan<byte> data, string purpose) => data.ToArray();

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose) => protectedData.ToArray();
}

internal sealed class ClosedGate : IWatchGate
{
    public WatchGate For(Chain chain) => WatchGate.NoExit;
}

internal sealed class FakeVault : IVault
{
    public VaultState State => VaultState.Unlocked;

    public event EventHandler<VaultState>? StateChanged
    {
        add { }
        remove { }
    }

    public void Create(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Unlock(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Lock() => throw new NotSupportedException();

    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey) =>
        throw new NotSupportedException();
}

internal sealed class FakePurposeKeys : IPurposeKeys
{
    public void DeriveKey(string purpose, Span<byte> destination)
    {
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes("testdata-tests"));
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, destination, [], Encoding.ASCII.GetBytes(purpose));
    }
}

internal sealed class FakeProfiles : IOperatorProfileStore
{
    private OperatorProfile? _profile = new("Testa Testsson", "Utredare", "Enheten");

    public OperatorProfile? Get() => _profile;

    public void Save(OperatorProfile profile) => _profile = profile;
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow => new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
}
#endif
