using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The account heuristics' proposals in the panel (sketch KE1): one suggestion per proposal, sealed with its accounts;
/// approving it merges them as the user's judgement, and taking the approval back undoes the merge.
/// </summary>
public sealed class AccountWalletTests : IDisposable
{
    private static readonly ChainAddress First = Account('2');

    private static readonly ChainAddress Second = Account('8');

    private readonly Harness _harness = new();

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_proposal_is_a_suggestion_about_its_accounts()
    {
        var check = Proposal();

        Assert.Equal(CheckKind.Suggestion, check.Finding.Kind);
        Assert.Equal(AreaSort.Wallet, check.Finding.Area);
        Assert.Equal([First, Second], check.Finding.Accounts);
        Assert.Empty(check.Finding.Addresses);
        Assert.Contains("user v1", check.Finding.Why, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Approving_merges_the_accounts_and_taking_it_back_undoes_the_merge()
    {
        var decision = _harness.Service.Decide(_harness.Case, new DecisionRequest(
            Proposal().Analysis, Proposal().Finding.Key, null, CheckVerdict.Approved, "Samma gas."));

        var merged = Read();
        Assert.Equal([First, Second], Assert.Single(merged.Clusters).Members);
        var made = Assert.Single(_harness.Case.Read(reader => Accounts.Decisions(reader, Chain.Ethereum)));
        Assert.Equal("checks.decisions:" + decision.Sequence, made.Origin);

        _harness.Service.Decide(_harness.Case, DecisionRequest.TakeBack(decision));

        Assert.Empty(Read().Clusters);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void An_approved_proposal_is_still_in_the_panel_after_the_next_run_and_can_be_taken_back()
    {
        var decision = _harness.Service.Decide(_harness.Case, new DecisionRequest(
            Proposal().Analysis, Proposal().Finding.Key, null, CheckVerdict.Approved));

        var again = Proposal();

        Assert.Equal([First, Second], again.Finding.Accounts);
        Assert.Contains(again.Decisions, entry => entry.Sequence == decision.Sequence);
        _harness.Service.Decide(_harness.Case, DecisionRequest.TakeBack(decision));
        Assert.Empty(Read().Clusters);
    }

    private IAccountClustering Accounts => _harness.Provider.GetRequiredService<IAccountClustering>();

    private AccountClusteringRun Read() => _harness.Case.Read(reader => Accounts.Read(reader, Chain.Ethereum));

    private StoredCheck Proposal()
    {
        var graph = Harness.GraphOf([]) with
        {
            AccountProposals =
            [
                new GraphAccountProposal("gas-funding:1", Chain.Ethereum, "gas-funding v1", [First, Second],
                    ["0x" + new string('b', 64)], "Samma betalare av gasen."),
            ],
        };
        return Assert.Single(_harness.Service.Run(_harness.Case, graph).Checks,
            check => check.Analysis.Name == CheckAnalysisNames.AccountWallets);
    }

    private static ChainAddress Account(char digit) => new(Chain.Ethereum, "0x" + new string(digit, 40));
}
