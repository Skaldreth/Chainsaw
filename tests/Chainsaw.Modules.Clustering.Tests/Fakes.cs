using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>A vault that is always unlocked.</summary>
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

/// <summary>Purpose keys from a fixed secret.</summary>
internal sealed class FakePurposeKeys : IPurposeKeys
{
    public void DeriveKey(string purpose, Span<byte> destination)
    {
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes("clustering-tests"));
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, destination, [], Encoding.ASCII.GetBytes(purpose));
    }
}

/// <summary>An operator profile in memory, or none.</summary>
internal sealed class FakeProfiles : IOperatorProfileStore
{
    public OperatorProfile? Profile { get; set; } = new("Testa Testsson", "Utredare", "Enheten");

    public OperatorProfile? Get() => Profile;

    public void Save(OperatorProfile profile) => Profile = profile;
}

/// <summary>A clock the test moves.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 1, 2, 3, TimeSpan.Zero);
}

/// <summary>An analysis that always fails, to show that a failure stops the whole transaction.</summary>
internal sealed class FailingHeuristic : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new("failing", 1);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context) =>
        throw new InvalidOperationException("The analysis failed.");
}

/// <summary>
/// A second version of the CoinJoin analysis that only ever sees a possible CoinJoin, to show that it replaces
/// version 1 in what counts.
/// </summary>
internal sealed class SilentCoinJoinNext : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new(HeuristicNames.CoinJoin, 3);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context) =>
        new(Id, transaction.Txid, [],
            [new CoinJoinFinding("Kanske.", CoinJoinPattern.Generic, CoinJoinCertainty.Possible,
                Chainsaw.SharedKernel.Satoshi.Zero, 2, transaction.Inputs.Count)]);
}

/// <summary>
/// A second version of common-input-ownership that concludes nothing, as a stricter version would on a transaction
/// version 1 merged, to show that its empty result takes version 1's merge back.
/// </summary>
internal sealed class SilentCommonInputV2 : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new(HeuristicNames.CommonInputOwnership, 2);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context) =>
        HeuristicResult.Nothing(Id, transaction.Txid);
}

/// <summary>An analysis that gives a proposal it must not: one address only.</summary>
internal sealed class MalformedHeuristic : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new("malformed", 1);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context) =>
        new(Id, transaction.Txid,
            [new OwnershipProposal([], EvidenceStrength.Merge, "Fel.")], []);
}

/// <summary>A finding of a kind the contracts do not declare.</summary>
internal sealed record UndeclaredFinding(string Reason) : HeuristicFinding(Reason);

/// <summary>An analysis that gives a finding of an undeclared kind.</summary>
internal sealed class UndeclaredFindingHeuristic : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new("undeclared", 1);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context) =>
        new(Id, transaction.Txid, [], [new UndeclaredFinding("Ny sort.")]);
}

/// <summary>
/// A probe in two versions for the holding basis (sketch HB1a, KV1a): both suggest that a transaction's first two
/// input addresses have one owner, version 2 resting on <see cref="Basis"/> and version 1 on nothing.
/// </summary>
/// <param name="version">1 or 2.</param>
internal sealed class BasisProbe(int version) : IUtxoHeuristic
{
    /// <summary>The probe's name.</summary>
    public const string Name = "basis-probe";

    /// <summary>The transaction version 2 rests on.</summary>
    public static Txid Basis { get; } = Synthetic.Txid("probe-basis");

    public HeuristicId Id { get; } = new(Name, version);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        var addresses = transaction.Inputs.Select(input => input.Address).OfType<BitcoinAddress>().Distinct().ToList();
        if (addresses.Count < 2)
        {
            return HeuristicResult.Nothing(Id, transaction.Txid);
        }

        return new HeuristicResult(
            Id, transaction.Txid, [new OwnershipProposal([.. addresses.Take(2)], EvidenceStrength.Suggestion, "Prov.")],
            [])
        {
            Basis = version == 2 ? [Basis] : [],
        };
    }
}

/// <summary>An analysis that concludes nothing but cites <see cref="BasisProbe.Basis"/>.</summary>
internal sealed class NoOpinionCiting : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new("no-opinion", 1);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context) =>
        HeuristicResult.Nothing(Id, transaction.Txid) with { Basis = [BasisProbe.Basis] };
}

/// <summary>
/// Two analyses that disagree about the change of a transaction with two outputs (sketch HB1a): one takes output 0
/// as change and rests on <see cref="BasisProbe.Basis"/>, the other takes output 1 with a merge. Together the
/// disagreement lowers the merge to a suggestion.
/// </summary>
/// <param name="citing">Whether this is the one that rests on the probe's basis and takes output 0.</param>
internal sealed class DisagreeingChange(bool citing) : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new(citing ? "change-citing" : "peel-like", 1);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        if (transaction.Outputs.Count != 2 || transaction.Inputs[0].Address is not { } input)
        {
            return HeuristicResult.Nothing(Id, transaction.Txid);
        }

        var change = citing ? 0 : 1;
        var proposal = new OwnershipProposal(
            [input, transaction.Outputs[change].Address!],
            citing ? EvidenceStrength.Suggestion : EvidenceStrength.Merge,
            "Prov.",
            change);
        return new HeuristicResult(Id, transaction.Txid, [proposal], [])
        {
            Basis = citing ? [BasisProbe.Basis] : [],
        };
    }
}

/// <summary>
/// An analysis that first merges a transaction's first two inputs on nothing else, and, once told to, concludes
/// nothing while citing <see cref="BasisProbe.Basis"/> (sketch HB1a).
/// </summary>
internal sealed class TurningProbe : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new("turning", 1);

    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <summary>Gets or sets whether it now concludes nothing and cites the probe's basis.</summary>
    public bool Turned { get; set; }

    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        var addresses = transaction.Inputs.Select(input => input.Address).OfType<BitcoinAddress>().Distinct().ToList();
        if (addresses.Count < 2)
        {
            return HeuristicResult.Nothing(Id, transaction.Txid);
        }

        return Turned
            ? HeuristicResult.Nothing(Id, transaction.Txid) with { Basis = [BasisProbe.Basis] }
            : new HeuristicResult(
                Id, transaction.Txid, [new OwnershipProposal([.. addresses.Take(2)], EvidenceStrength.Merge, "Prov.")],
                []);
    }
}
