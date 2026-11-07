using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Attribution.Tests.Synthetic;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>Labels on Ethereum accounts: synthetic addresses only, <c>0x</c> and one hex digit repeated.</summary>
public sealed class EthereumLabelTests : IDisposable
{
    private const string OtherCaseNumber = "2026-0303";

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static ChainAddress Account(char digit) => new(Chain.Ethereum, "0x" + new string(digit, 40));

    private static string Checksummed(ChainAddress account) => EthereumAddress.Parse(account.Value).Checksummed;

    private static IReadOnlyList<string> StoredSubjects(ICase @case) => @case.Read(reader =>
    {
        using var command = reader.CreateCommand("SELECT subject FROM attribution_labels ORDER BY sequence");
        using var rows = command.ExecuteReader();
        var subjects = new List<string>();
        while (rows.Read())
        {
            subjects.Add(rows.GetString(0));
        }

        return (IReadOnlyList<string>)subjects;
    });

    [Fact]
    public void An_ethereum_account_is_a_subject_without_a_bitcoin_address()
    {
        var account = Account('a');

        var subject = LabelSubject.ForAccount(account);

        Assert.Equal(LabelSubjectKind.Address, subject.Kind);
        Assert.Equal(account, subject.Target);
        Assert.Null(subject.Address);
        Assert.Equal(Checksummed(account), subject.ToString());
        Assert.Equal(subject, LabelSubject.ForAccount(new ChainAddress(Chain.Ethereum, Checksummed(account))));
    }

    [Fact]
    public void A_bitcoin_address_as_a_chain_address_is_the_same_subject_as_before()
    {
        var address = Wpkh("chain-address");

        var subject = LabelSubject.ForAccount(address.ToChainAddress());

        Assert.Equal(LabelSubject.ForAddress(address), subject);
        Assert.Equal(address, subject.Address);
        Assert.Equal(address.Value, subject.ToString());
    }

    [Fact]
    public void Only_bitcoin_and_ethereum_addresses_that_are_valid_can_be_labelled()
    {
        Assert.Throws<ArgumentException>(() => LabelSubject.ForAccount(new ChainAddress(Chain.Tron, "TXYZ123")));
        Assert.Throws<ArgumentException>(() => LabelSubject.ForAccount(new ChainAddress(Chain.Ethereum, "0x12")));
        Assert.Throws<ArgumentException>(() => LabelSubject.ForAccount(new ChainAddress(Chain.Bitcoin, "0x" +
            new string('a', 40))));
    }

    [Fact]
    public void An_ethereum_label_is_added_read_and_retracted_with_journal_lines()
    {
        var account = Account('b');

        var label = _harness.Own(LabelSubject.ForAccount(account), LabelKind.Exchange, "Börsen AB");
        _harness.Reopen();

        var stored = Assert.Single(_harness.Labels.GetEntries(_harness.Case));
        Assert.Equal(label, stored);
        Assert.Equal([account.Value], StoredSubjects(_harness.Case));
        var added = _harness.Case.ReadJournal().Single(entry => entry.Sequence == stored.JournalSequence);
        Assert.Equal(AttributionJournalKinds.LabelAdded, added.Kind);
        Assert.Contains(Checksummed(account), added.Summary, StringComparison.Ordinal);
        Assert.Contains(Checksummed(account), Assert.Single(_harness.Labels.GetLabels(_harness.Case)).Explain(),
            StringComparison.Ordinal);

        Assert.True(_harness.Labels.Retract(_harness.Case, stored.Sequence, "Fel konto"));

        Assert.Empty(_harness.Labels.GetEntries(_harness.Case));
        Assert.Empty(_harness.Labels.GetLabels(_harness.Case, [account]));
        var retracted = _harness.Case.ReadJournal().Last();
        Assert.Equal(AttributionJournalKinds.LabelRetracted, retracted.Kind);
        Assert.Contains(Checksummed(account), retracted.Summary, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void The_history_of_an_ethereum_account_keeps_a_label_taken_back()
    {
        var account = Account('c');
        var label = _harness.Own(LabelSubject.ForAccount(account), LabelKind.Mixer, "Blandaren");
        _harness.Own(LabelSubject.ForAccount(Account('d')), LabelKind.Exchange);
        Assert.True(_harness.Labels.Retract(_harness.Case, label.Sequence, null));

        _harness.Reopen();

        var entry = Assert.Single(_harness.Labels.GetHistory(_harness.Case, [account]));
        Assert.Equal(label, entry.Label);
        Assert.True(entry.IsRetracted);
        Assert.False(entry.CanRetract);
    }

    [Fact]
    public void Labels_are_read_by_chain_address_on_both_chains_at_once()
    {
        var account = Account('e');
        var address = Wpkh("both-chains");
        _harness.Own(LabelSubject.ForAccount(account), LabelKind.Exchange, "Börsen AB");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Mixer, "Blandaren");

        var labels = _harness.Labels.GetLabels(
            _harness.Case, [account, address.ToChainAddress(), Account('f')]);

        Assert.Equal(2, labels.Count);
        Assert.Equal(LabelKind.Exchange, Assert.Single(labels[account]).Kind);
        Assert.Equal(LabelKind.Mixer, Assert.Single(labels[address.ToChainAddress()]).Kind);
        Assert.Equal(LabelKind.Mixer, Assert.Single(_harness.Labels.GetLabels(_harness.Case, address)).Kind);
    }

    [Fact]
    public void A_bitcoin_address_still_gets_the_labels_of_its_cluster_by_chain_address()
    {
        var address = Wpkh("clustered-chain");
        _harness.Own(LabelSubject.ForCluster(address), LabelKind.Exchange, "Börsen AB");

        var labels = _harness.Labels.GetLabels(_harness.Case, [address.ToChainAddress()]);

        Assert.Equal(LabelSubjectKind.Cluster, Assert.Single(labels[address.ToChainAddress()]).Subject.Kind);
    }

    [Fact]
    public void The_same_text_on_another_chain_is_another_address()
    {
        var account = Account('a');
        _harness.Own(LabelSubject.ForAccount(account), LabelKind.Exchange);
        var sameTextOnBitcoin = new ChainAddress(Chain.Bitcoin, account.Value);
        var sameTextOnTron = new ChainAddress(Chain.Tron, account.Value);

        var labels = _harness.Labels.GetLabels(_harness.Case, [sameTextOnBitcoin, sameTextOnTron]);

        Assert.Empty(labels);
        Assert.Empty(_harness.Labels.GetHistory(_harness.Case, [sameTextOnBitcoin, sameTextOnTron]));
        Assert.Empty(_harness.Classifier.ClassifyAccounts(_harness.Case, [sameTextOnBitcoin, sameTextOnTron]));
    }

    [Fact]
    public void An_ethereum_label_spreads_through_the_library_into_another_case()
    {
        var account = Account('9');
        var original = _harness.Own(LabelSubject.ForAccount(account), LabelKind.Exchange, "Börsen AB");

        Assert.Equal(new LibraryAbsorption(1, 0), _harness.Library.Absorb(_harness.Case));
        Assert.Equal(LabelSubject.ForAccount(account), Assert.Single(_harness.Library.List()).Subject);
        using var other = _harness.Create(OtherCaseNumber);
        Assert.Empty(_harness.Library.ShareInto(other, [Wpkh("not-the-account")]));
        Assert.Empty(_harness.Library.ShareInto(other, [new ChainAddress(Chain.Tron, account.Value)]));

        var shared = Assert.Single(_harness.Library.ShareInto(other, [account, Account('8')]));

        Assert.Equal(LabelSubject.ForAccount(account), shared.Subject);
        Assert.Equal(LabelSource.OtherCase, shared.Origin.Source);
        Assert.Equal(original.Origin.OriginId, shared.Origin.OriginId);
        Assert.True(Assert.Single(_harness.Labels.GetLabels(other, [account])[account]).IsOnlyFromOtherCases);
        Assert.Empty(_harness.Library.ShareInto(other, [account]));
        Assert.True(_harness.Verify(other).IsIntact);
    }

    [Fact]
    public void An_ethereum_label_taken_back_where_it_was_set_is_taken_back_in_the_other_case()
    {
        var account = Account('7');
        var original = _harness.Own(LabelSubject.ForAccount(account), LabelKind.Exchange, "Börsen AB");
        _harness.Library.Absorb(_harness.Case);
        using var other = _harness.Create(OtherCaseNumber);
        Assert.Single(_harness.Library.ShareInto(other, [account]));

        Assert.True(_harness.Labels.Retract(_harness.Case, original.Sequence, null));
        _harness.Library.Absorb(_harness.Case);
        _harness.Library.ShareInto(other, [account]);

        Assert.Empty(_harness.Labels.GetLabels(other, [account]));
        Assert.Empty(_harness.Library.List());
    }

    [Fact]
    public void An_exchange_label_on_an_ethereum_account_ends_the_trail()
    {
        var account = Account('6');
        var address = Wpkh("bitcoin-exchange");
        _harness.Own(LabelSubject.ForAccount(account), LabelKind.Exchange, "Börsen AB");
        _harness.Own(LabelSubject.ForAddress(address), LabelKind.Mixer, "Blandaren");
        _harness.Own(LabelSubject.ForAccount(Account('5')), LabelKind.Victim, "Offret");

        var hints = _harness.Classifier.ClassifyAccounts(
            _harness.Case, [account, address.ToChainAddress(), Account('5'), Account('4')]);

        Assert.Equal(2, hints.Count);
        var hint = Assert.Single(hints[account]);
        Assert.Equal(TrailEndReason.Exchange, hint.Reason);
        Assert.Contains("egen v1", hint.Source, StringComparison.Ordinal);
        Assert.Contains(Checksummed(account), hint.Explanation, StringComparison.Ordinal);
        Assert.Equal(TrailEndReason.Mixer, Assert.Single(hints[address.ToChainAddress()]).Reason);
    }

    [Fact]
    public void A_bitcoin_label_is_stored_and_journalled_as_before()
    {
        var address = Wpkh("unchanged");

        var label = _harness.Own(LabelSubject.ForAddress(address), LabelKind.Exchange, "Börsen AB");

        Assert.Equal([address.Value], StoredSubjects(_harness.Case));
        var journal = _harness.Case.ReadJournal().Single(entry => entry.Sequence == label.JournalSequence);
        Assert.Contains($"\"subject\":\"{address.Value}\"", journal.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("chain", journal.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"Etikett: {address.Value} är börs (Börsen AB)", journal.Summary);
        _harness.Reopen();
        Assert.Equal(label, Assert.Single(_harness.Labels.GetEntries(_harness.Case)));
    }
}
