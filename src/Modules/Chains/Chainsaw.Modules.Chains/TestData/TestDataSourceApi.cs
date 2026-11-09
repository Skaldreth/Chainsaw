#if DEBUG
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.TestData;

/// <summary>
/// A real sort of API in form (its requests, its probe's answers), under the made-up source's code and name
/// (<see cref="TestDataBitcoin.SourceCode"/>), with one endpoint of its own that is never one of the user's and is
/// never asked through the exit: the made-up sources of the chains after Bitcoin, TRON and Ethereum (<c>/testdata</c>,
/// Debug only) use it, so that every sealed response says that it is made up.
/// </summary>
/// <param name="form">The real API whose requests and answers the made-up source follows.</param>
/// <param name="chain">The chain.</param>
/// <param name="address">The endpoint's address, under <c>.invalid</c>, which never resolves.</param>
/// <param name="endpointId">The endpoint's fixed id, the same in every run so that sealed responses compare.</param>
internal sealed class TestDataSourceApi(ISourceApi form, Chain chain, Uri address, Guid endpointId) : ISourceApi
{
    /// <inheritdoc/>
    public SourceApiInfo Info { get; } = new(TestDataBitcoin.SourceCode, TestDataBitcoin.SourceName, chain);

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; } =
    [
        new SourceEndpoint(
            endpointId, chain, TestDataBitcoin.EndpointName, address, TestDataBitcoin.SourceCode, null, Enabled: true)
        {
            Interval = SourcePace.Local,
        },
    ];

    /// <summary>Gets its own name, so that its made-up chain is never checked against the real chain's.</summary>
    public string ChainName => TestDataBitcoin.SourceCode;

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => form.HeightRequest(address);

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body) => form.ReadHeight(body);

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) => form.HashRequest(address, height);

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) => form.ReadHash(body);

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path) => form.Request(address, path);

    /// <inheritdoc/>
    public ExitRequest Post(Uri address, string path, string body) => form.Post(address, path, body);

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => form.Credential(secret);

    /// <inheritdoc/>
    public bool Answers(string path) => form.Answers(path);

    /// <inheritdoc/>
    public SourceVerdict? Judge(ExitResponse response) => form.Judge(response);
}
#endif
