#if DEBUG
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>The token a made-up part on a Blockscout chain moves: its contract, decimals, symbol and name.</summary>
/// <param name="Contract">The token's contract, the real one, since it is how the token is known.</param>
/// <param name="Decimals">How many decimals its amounts have.</param>
/// <param name="Symbol">Its symbol.</param>
/// <param name="Name">Its name.</param>
public sealed record TestBlockscoutToken(EthereumAddress Contract, int Decimals, string Symbol, string Name);

/// <summary>
/// A made-up part of the test case on a chain that Blockscout serves (Ethereum or BSC): what
/// <see cref="TestEthereumHttp"/> answers from, so that the same made-up Blockscout answers both.
/// </summary>
public interface ITestBlockscoutChain
{
    /// <summary>Gets the chain.</summary>
    Chain Chain { get; }

    /// <summary>Gets the made-up source's address, which every request must be under.</summary>
    Uri Root { get; }

    /// <summary>Gets the first block of the made-up part.</summary>
    long First { get; }

    /// <summary>Gets the newest block.</summary>
    long Newest { get; }

    /// <summary>Gets the token the part moves.</summary>
    TestBlockscoutToken Token { get; }

    /// <summary>Gets the transactions, oldest first.</summary>
    IReadOnlyList<TestEthereumTransaction> Transactions { get; }

    /// <summary>Gives a block's made-up hash.</summary>
    /// <param name="height">The block.</param>
    /// <returns>The hash, <c>0x</c> and 64 hex digits.</returns>
    string HashAt(long height);

    /// <summary>Gives a block's time.</summary>
    /// <param name="height">The block.</param>
    /// <returns>Its time.</returns>
    DateTimeOffset TimeAt(long height);
}
#endif
