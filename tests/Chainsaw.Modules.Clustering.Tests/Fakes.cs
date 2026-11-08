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
internal sealed class SilentCoinJoinV2 : IUtxoHeuristic
{
    public HeuristicId Id { get; } = new(HeuristicNames.CoinJoin, 2);

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
