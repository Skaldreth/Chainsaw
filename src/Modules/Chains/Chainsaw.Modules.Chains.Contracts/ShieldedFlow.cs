using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// What a Zcash transaction moves between its transparent part and the shielded pools, as the chain states it: the
/// pools' value balances (Sapling's and Orchard's <c>valueBalance</c>) and Sprout's <c>vpub_old</c> and
/// <c>vpub_new</c> in its joinsplits. These are facts on the chain (principle 2); who owns what inside the pools, and
/// which shielded notes the money went to, cannot be read. Money that goes into a pool is where the trail ends, and
/// money that comes out of one is where a trail starts that cannot be followed back.
/// </summary>
/// <param name="IntoPool">
/// What the transaction puts into the pools, net, in zatoshi: zero when it takes more out than it puts in.
/// </param>
/// <param name="OutOfPool">
/// What the transaction takes out of the pools, net, in zatoshi: zero when it puts more in than it takes out.
/// </param>
/// <param name="Pools">The pools the transaction touches, in the order Sprout, Sapling, Orchard.</param>
public sealed record ShieldedFlow(Satoshi IntoPool, Satoshi OutOfPool, IReadOnlyList<ShieldedPool> Pools)
{
    /// <summary>Gets the pools the transaction touches.</summary>
    public IReadOnlyList<ShieldedPool> Pools { get; } = Pools ?? throw new ArgumentNullException(nameof(Pools));

    /// <summary>Gets what goes into the pools, net.</summary>
    public Satoshi IntoPool { get; } = IntoPool.Value >= 0
        ? IntoPool
        : throw new ArgumentOutOfRangeException(nameof(IntoPool), "An amount into the pools is never negative.");

    /// <summary>Gets what comes out of the pools, net.</summary>
    public Satoshi OutOfPool { get; } = OutOfPool.Value >= 0
        ? OutOfPool
        : throw new ArgumentOutOfRangeException(nameof(OutOfPool), "An amount out of the pools is never negative.");

    /// <summary>
    /// Gives the flow from the net amount that leaves the transparent part for the pools: positive into the pools,
    /// negative out of them.
    /// </summary>
    /// <param name="netIntoPool">The net amount into the pools, in zatoshi.</param>
    /// <param name="pools">The pools the transaction touches.</param>
    /// <returns>The flow.</returns>
    public static ShieldedFlow FromNet(long netIntoPool, IReadOnlyList<ShieldedPool> pools) => netIntoPool >= 0
        ? new ShieldedFlow(new Satoshi(netIntoPool), Satoshi.Zero, pools)
        : new ShieldedFlow(Satoshi.Zero, new Satoshi(checked(-netIntoPool)), pools);
}

/// <summary>Zcash's shielded pools, each with its own kind of notes and its own value balance.</summary>
public enum ShieldedPool
{
    /// <summary>Sprout, the first pool (2016), moved with joinsplits (<c>vjoinsplit</c>); closed to new money.</summary>
    Sprout,

    /// <summary>Sapling (2018), moved with shielded spends and outputs and a value balance.</summary>
    Sapling,

    /// <summary>Orchard (2022), moved with actions and a value balance.</summary>
    Orchard,
}
