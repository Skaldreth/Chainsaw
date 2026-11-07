#if DEBUG
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.TestData;

/// <summary>
/// The made-up source behind the test case (<c>/testdata</c>, Debug only): Esplora's API in form, under its own code
/// and name, so that every sealed response says that it is made up (principle 1 must not lie about where data came
/// from). It has one endpoint of its own, which is never one of the user's and is never asked through the exit.
/// </summary>
internal sealed class TestDataApi : ISourceApi
{
    /// <summary>The endpoint's fixed id, the same in every run so that the sealed responses can be compared.</summary>
    private static readonly Guid EndpointId = Guid.Parse("7e57da7a-0000-4000-8000-000000000001");

    /// <summary>The Esplora form that the requests and the probe's answers follow.</summary>
    private readonly EsploraApi _esplora = new(new BitcoinSourceOptions { DefaultEndpoints = [] });

    /// <summary>Creates the API with its one endpoint, unpaced, since nothing leaves the computer.</summary>
    public TestDataApi()
    {
        Defaults =
        [
            new SourceEndpoint(
                EndpointId, Chain.Bitcoin, TestDataBitcoin.EndpointName, TestDataBitcoin.Address,
                TestDataBitcoin.SourceCode, null, Enabled: true)
            {
                Interval = SourcePace.Local,
            },
        ];
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; } = new(TestDataBitcoin.SourceCode, TestDataBitcoin.SourceName, Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => _esplora.HeightRequest(address);

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body) => _esplora.ReadHeight(body);

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) => _esplora.HashRequest(address, height);

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) => _esplora.ReadHash(body);

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path) => _esplora.Request(address, path);

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => _esplora.Credential(secret);
}
#endif
