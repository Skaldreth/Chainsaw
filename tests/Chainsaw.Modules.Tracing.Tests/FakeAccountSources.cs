using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>Ethereum's fake source: blocks' hashes are <c>0x</c> and 64 hex digits.</summary>
internal sealed class FakeEthereumSource : FakeAccountSource, IEthereumSource
{
}

/// <summary>
/// TRON's fake source: the same synthetic chain, with blocks' ids as TRON writes them (64 hex digits, no prefix). It
/// pages by block like the Ethereum fake, so its pages start at a block (the default
/// <see cref="IAccountSource.PageStartOf"/>); the real source's paging by time is tested in Chains.
/// </summary>
internal sealed class FakeTronSource : FakeAccountSource, ITronSource
{
    /// <inheritdoc/>
    protected override string BlockHashOf(long height) => HashOf(height)[2..];
}
