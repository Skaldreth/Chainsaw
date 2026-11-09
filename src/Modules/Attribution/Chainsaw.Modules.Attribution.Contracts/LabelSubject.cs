using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>Whether a label is on an address alone or on the cluster that holds it.</summary>
public enum LabelSubjectKind
{
    /// <summary>The address alone.</summary>
    Address,

    /// <summary>
    /// The cluster that holds the address, as Clustering concludes it when the label is read. The label then rests on
    /// the clustering too, and follows the cluster when it grows or is split.
    /// </summary>
    Cluster,
}

/// <summary>
/// What a label is on: an address on a chain Chainsaw can label (Bitcoin and Ethereum so far), or the Bitcoin cluster
/// that holds an address. The address always carries its chain (<see cref="Target"/>), so that the same text on two
/// chains is never taken for one actor.
/// </summary>
public sealed record LabelSubject
{
    /// <summary>Creates a subject on a Bitcoin address or the cluster that holds it.</summary>
    /// <param name="kind">The address alone or its cluster.</param>
    /// <param name="address">The address, or for a cluster the address the label was set on.</param>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not known.</exception>
    public LabelSubject(LabelSubjectKind kind, BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Kind = Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), "A label is on an address or a cluster.");
        Address = address;
        Target = address.ToChainAddress();
    }

    /// <summary>What the stored form of a subject on BSC begins with (<see cref="Stored"/>).</summary>
    public const string BscPrefix = "bsc:";

    /// <summary>Creates a subject on an account alone, on Ethereum, BSC or TRON.</summary>
    /// <param name="account">The checked account.</param>
    private LabelSubject(EthereumAddress account)
    {
        Kind = LabelSubjectKind.Address;
        Target = account.ToChainAddress();
        Display = account.Checksummed;
    }

    /// <summary>Gets whether the label is on the address alone or on its cluster.</summary>
    public LabelSubjectKind Kind { get; }

    /// <summary>
    /// Gets the address with its chain, or for a cluster the address the label was set on. Its value is what is
    /// stored and compared: for Bitcoin the address as the chain writes it, for Ethereum the hex in lower case, for TRON
    /// the Base58Check form.
    /// </summary>
    public ChainAddress Target { get; }

    /// <summary>
    /// Gets the subject as a label stores it, whose form says its chain: the address as <see cref="Target"/> gives
    /// it, and on BSC, whose addresses are written as Ethereum's, the same with <see cref="BscPrefix"/> before it.
    /// </summary>
    public string Stored => Target.Chain == Chain.Bsc ? BscPrefix + Target.Value : Target.Value;

    /// <summary>
    /// Gets the Bitcoin address, or <see langword="null"/> when the subject is on another chain. A cluster is always
    /// on Bitcoin, so it always has one.
    /// </summary>
    public BitcoinAddress? Address { get; }

    /// <summary>Gets how an address on another chain is shown, for Ethereum with its EIP-55 checksum.</summary>
    private string? Display { get; }

    /// <summary>Gives a label's subject that is the address alone.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The subject.</returns>
    public static LabelSubject ForAddress(BitcoinAddress address) => new(LabelSubjectKind.Address, address);

    /// <summary>Gives a label's subject that is the cluster that holds the address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The subject.</returns>
    public static LabelSubject ForCluster(BitcoinAddress address) => new(LabelSubjectKind.Cluster, address);

    /// <summary>
    /// Gives a label's subject that is an address alone on a chain that can be labelled: a Bitcoin address or an
    /// Ethereum or TRON account. A label on a cluster is for Bitcoin only, since only Bitcoin is clustered (see
    /// <see cref="ForCluster"/>).
    /// </summary>
    /// <param name="address">The address with its chain.</param>
    /// <returns>The subject.</returns>
    /// <exception cref="ArgumentException">
    /// The address is not valid on its chain, or its chain cannot carry labels yet.
    /// </exception>
    public static LabelSubject ForAccount(ChainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Chain switch
        {
            Chain.Bitcoin or Chain.Litecoin or Chain.Zcash
                when BitcoinAddress.TryParse(address.Chain, address.Value, out var utxo) =>
                ForAddress(utxo),
            Chain.Ethereum or Chain.Tron or Chain.Bsc
                when EthereumAddress.TryParse(address.Chain, address.Value, out var account) => new(account),

            _ => throw new ArgumentException("The address is not valid on its chain.", nameof(address)),

        };
    }

    /// <summary>
    /// Writes the subject as the journal and the explanations show it: the address (an Ethereum account with its
    /// EIP-55 checksum), or <c>kluster med</c> and the address.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => Kind == LabelSubjectKind.Cluster
        ? $"kluster med {Target.Value}"
        : Display ?? Target.Value;
}
