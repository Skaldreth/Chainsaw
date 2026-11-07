using Chainsaw.Modules.Chains.Contracts;

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

/// <summary>What a label is on.</summary>
/// <param name="Kind">The address alone or its cluster.</param>
/// <param name="Address">The address, or for a cluster the address the label was set on.</param>
public sealed record LabelSubject(LabelSubjectKind Kind, BitcoinAddress Address)
{
    /// <summary>Gets whether the label is on the address alone or on its cluster.</summary>
    public LabelSubjectKind Kind { get; } = Enum.IsDefined(Kind)
        ? Kind
        : throw new ArgumentOutOfRangeException(nameof(Kind), "A label is on an address or a cluster.");

    /// <summary>Gets the address.</summary>
    public BitcoinAddress Address { get; } = Address ?? throw new ArgumentNullException(nameof(Address));

    /// <summary>Gives a label's subject that is the address alone.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The subject.</returns>
    public static LabelSubject ForAddress(BitcoinAddress address) => new(LabelSubjectKind.Address, address);

    /// <summary>Gives a label's subject that is the cluster that holds the address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The subject.</returns>
    public static LabelSubject ForCluster(BitcoinAddress address) => new(LabelSubjectKind.Cluster, address);

    /// <inheritdoc/>
    public override string ToString() => Kind == LabelSubjectKind.Cluster ? $"kluster med {Address}" : Address.Value;
}
