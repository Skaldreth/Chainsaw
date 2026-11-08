using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// Chainsaw's list of known contracts as labels: a mixer's pool becomes a mixer and a bridge a swap or bridge, both
/// ending the trail with the list as the reason; an exchange's router is not labelled; nothing is labelled twice, also
/// after it was taken back. The contracts are taken from the list itself, never written out here.
/// </summary>
public sealed class KnownContractLabelTests : IDisposable
{
    private static readonly KnownContract Pool = Of(KnownContractCategory.Mixer);

    private static readonly KnownContract Bridge = Of(KnownContractCategory.Bridge);

    private static readonly KnownContract Router = Of(KnownContractCategory.Dex);

    private static readonly ChainAddress Stranger = new(Chain.Ethereum, "0x" + new string('7', 40));

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_pool_is_a_mixer_and_a_bridge_a_swap_or_bridge_with_the_list_as_origin()
    {
        var added = _harness.KnownContracts.Check(
            _harness.Case, [AddressOf(Pool), AddressOf(Bridge), AddressOf(Router), Stranger]);

        Assert.Equal(2, added.Count);
        var mixer = added.Single(label => label.Subject.Target == AddressOf(Pool));
        Assert.Equal(LabelKind.Mixer, mixer.Kind);
        Assert.Equal(Pool.Name, mixer.Name);
        Assert.Equal(LabelSource.OpenSource, mixer.Origin.Source);
        Assert.Equal(KnownContracts.Name, mixer.Origin.SourceName);
        Assert.Equal(KnownContracts.Version, mixer.Origin.SourceVersion);
        Assert.Contains(Pool.Source.AbsoluteUri, mixer.Origin.Reason, StringComparison.Ordinal);
        Assert.Equal(LabelKind.SwapBridge, added.Single(label => label.Subject.Target == AddressOf(Bridge)).Kind);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(AttributionJournalKinds.KnownContractsMatched, journal.Kind);
        Assert.Contains(Pool.Address.Value, journal.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trail_ends_at_a_labelled_pool_and_bridge()
    {
        _harness.KnownContracts.Check(_harness.Case, [AddressOf(Pool), AddressOf(Bridge)]);

        var ends = _harness.Classifier.ClassifyAccounts(_harness.Case, [AddressOf(Pool), AddressOf(Bridge)]);

        Assert.Equal(TrailEndReason.Mixer, Assert.Single(ends[AddressOf(Pool)]).Reason);
        Assert.Equal(TrailEndReason.OtherChain, Assert.Single(ends[AddressOf(Bridge)]).Reason);
    }

    [Fact]
    public void A_contract_is_labelled_once_and_never_again_after_it_is_taken_back()
    {
        var first = Assert.Single(_harness.KnownContracts.Check(_harness.Case, [AddressOf(Pool)]));
        var journalLength = _harness.Case.ReadJournal().Count;

        Assert.Empty(_harness.KnownContracts.Check(_harness.Case, [AddressOf(Pool)]));
        Assert.True(_harness.Labels.Retract(_harness.Case, first.Sequence, null));
        Assert.Empty(_harness.KnownContracts.Check(_harness.Case, [AddressOf(Pool)]));
        Assert.Equal(journalLength + 1, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void Nothing_is_written_when_no_address_is_a_labelled_contract()
    {
        var before = _harness.Case.ReadJournal().Count;

        Assert.Empty(_harness.KnownContracts.Check(_harness.Case, [AddressOf(Router), Stranger]));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void The_labels_are_sealed_so_that_the_case_verifies()
    {
        _harness.KnownContracts.Check(_harness.Case, [AddressOf(Pool)]);

        Assert.True(_harness.Verify().IsIntact);
    }

    private static KnownContract Of(KnownContractCategory category) =>
        KnownContracts.All.First(contract => contract.Category == category);

    private static ChainAddress AddressOf(KnownContract contract) => contract.Address.ToChainAddress();
}
