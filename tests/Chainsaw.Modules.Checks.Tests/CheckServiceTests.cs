using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>The runs, the seals and the decisions of <see cref="ICheckService"/>.</summary>
public sealed class CheckServiceTests
{
    private static StoredCheck Mixer(CheckRun run) =>
        Assert.Single(run.Checks, check => check.Finding.Key == $"{Txid("deposit").Value}:0");

    [Fact]
    public void A_run_seals_what_is_new_with_one_line_in_the_journal_and_the_same_run_again_seals_nothing()
    {
        using var harness = new Harness();

        var first = harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther);
        var second = harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther);

        Assert.Equal(first.Checks.Count, first.Sealed);
        Assert.Equal(0, second.Sealed);
        Assert.Single(harness.Journal(CheckJournalKinds.Ran));
        Assert.Equal(first.Checks.Count, harness.Count("checks_results"));
        Assert.Equal(Mixer(first).Sequence, Mixer(second).Sequence);
        Assert.Equal(10, second.Transactions);
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public void A_changed_finding_is_sealed_again_and_the_old_stays()
    {
        using var harness = new Harness();
        var before = Mixer(harness.Run([Mixing.Deposit(), Mixing.Second()], Mixing.Mixer));

        var run = harness.Run([Mixing.Deposit(), Mixing.Second(), Mixing.Best()], Mixing.Mixer);

        Assert.True(Mixer(run).Sequence > before.Sequence);
        Assert.Equal(2, Mixer(run).Finding.Candidates.Count);
        Assert.Equal(2L, harness.Case.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT COUNT(*) FROM checks_results WHERE finding_key = $key");
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$key", before.Finding.Key));
            return (long)command.ExecuteScalar()!;
        }));
    }

    [Fact]
    public void At_most_five_candidates_wait_and_a_dismissed_one_frees_its_place()
    {
        using var harness = new Harness();
        var graph = (IReadOnlyList<BitcoinTransaction>)[Mixing.Deposit(), .. Mixing.Many(8)];
        var check = Mixer(harness.Run(graph, Mixing.Mixer));
        Assert.Equal(CheckRules.MostWaitingCandidates, check.Finding.Candidates.Count);
        var sixth = Mixer(harness.Run(graph, Mixing.Mixer)).Finding.Candidates;

        harness.Service.Decide(harness.Case, new DecisionRequest(
            check.Analysis, check.Finding.Key, check.Finding.Candidates[0].Key, CheckVerdict.Dismissed));
        var after = Mixer(harness.Run(graph, Mixing.Mixer));

        Assert.Equal(6, after.Finding.Candidates.Count);
        Assert.Equal(5, after.Waiting);
        Assert.Contains(after.Finding.Candidates, c => sixth.All(old => old.Key != c.Key));
    }

    [Fact]
    public void An_approved_candidate_stays_when_better_ones_come()
    {
        using var harness = new Harness();
        var check = Mixer(harness.Run([Mixing.Deposit(), Mixing.Third()], Mixing.Mixer));
        var third = Assert.Single(check.Finding.Candidates);
        harness.Service.Decide(harness.Case, new DecisionRequest(
            check.Analysis, check.Finding.Key, third.Key, CheckVerdict.Approved));

        var after = Mixer(harness.Run([Mixing.Deposit(), Mixing.Third(), .. Mixing.Many(6)], Mixing.Mixer));

        Assert.Contains(after.Finding.Candidates, c => c.Key == third.Key);
        Assert.Contains(third.Address, after.Finding.Addresses);
        Assert.Equal(CheckVerdict.Approved, after.DecisionFor(third.Key)!.Verdict);
        Assert.Equal(CheckRules.MostWaitingCandidates, after.Waiting);
    }

    [Fact]
    public void A_suggestion_waits_for_one_decision_per_candidate()
    {
        using var harness = new Harness();

        var check = Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));

        Assert.Equal(3, check.Waiting);
        Assert.Equal("Mixerutgångar", check.AnalysisName);
    }

    [Fact]
    public void A_decision_is_sealed_with_who_when_and_the_reason_and_counts_in_the_next_run()
    {
        using var harness = new Harness();
        var check = Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));
        var best = check.Finding.Candidates[0];

        var decision = harness.Service.Decide(
            harness.Case,
            new DecisionRequest(check.Analysis, check.Finding.Key, best.Key, CheckVerdict.Approved, "  Samma belopp. "));
        var after = Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));

        Assert.Equal("Samma belopp.", decision.Reason);
        Assert.Equal("Testa Testsson", decision.DecidedBy.Name);
        Assert.Equal(CheckVerdict.Approved, after.DecisionFor(best.Key)!.Verdict);
        Assert.Equal(2, after.Waiting);
        Assert.Single(harness.Journal(CheckJournalKinds.Decided));
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public void A_dismissed_candidate_stays_dismissed_after_the_case_is_opened_again()
    {
        using var harness = new Harness();
        var check = Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));
        var third = check.Finding.Candidates[2];
        harness.Service.Decide(
            harness.Case, new DecisionRequest(check.Analysis, check.Finding.Key, third.Key, CheckVerdict.Dismissed));

        harness.Reopen();
        var after = Mixer(harness.Service.Run(
            harness.Case, Harness.GraphOf(Mixing.All(), Mixing.Mixer, Mixing.MixerOther)));

        Assert.Equal(CheckVerdict.Dismissed, after.DecisionFor(third.Key)!.Verdict);
        Assert.Null(after.DecisionFor(third.Key)!.Reason);
    }

    [Fact]
    public void The_latest_decision_about_the_same_candidate_counts()
    {
        using var harness = new Harness();
        var check = Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));
        var key = check.Finding.Candidates[0].Key;
        harness.Service.Decide(
            harness.Case, new DecisionRequest(check.Analysis, check.Finding.Key, key, CheckVerdict.Approved));
        harness.Service.Decide(
            harness.Case, new DecisionRequest(check.Analysis, check.Finding.Key, key, CheckVerdict.Dismissed));

        var after = Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));

        Assert.Equal(CheckVerdict.Dismissed, after.DecisionFor(key)!.Verdict);
        Assert.Equal(2, after.Decisions.Count);
    }

    [Fact]
    public void A_check_an_unknown_candidate_or_an_unsealed_suggestion_cannot_be_decided()
    {
        using var harness = new Harness();
        var run = harness.Run([.. Mixing.All(), Patterns.Whirlpool()], Mixing.Mixer, Mixing.MixerOther);
        var mixer = Mixer(run);
        var coinjoin = Assert.Single(run.Checks, check => check.Analysis.Name == CoinJoinCheck.Name);

        Assert.Throws<ArgumentException>(() => harness.Service.Decide(harness.Case, new DecisionRequest(
            coinjoin.Analysis, coinjoin.Finding.Key, null, CheckVerdict.Approved)));
        Assert.Throws<ArgumentException>(() => harness.Service.Decide(harness.Case, new DecisionRequest(
            mixer.Analysis, mixer.Finding.Key, "nothing:0", CheckVerdict.Approved)));
        Assert.Throws<ArgumentException>(() => harness.Service.Decide(harness.Case, new DecisionRequest(
            mixer.Analysis, mixer.Finding.Key, null, CheckVerdict.Approved)));
        Assert.Throws<ArgumentException>(() => harness.Service.Decide(harness.Case, new DecisionRequest(
            new HeuristicId(MixerOutputsCheck.Name, 2), mixer.Finding.Key, mixer.Finding.Candidates[0].Key,
            CheckVerdict.Approved)));
        Assert.Throws<ArgumentException>(() => harness.Service.Decide(harness.Case, new DecisionRequest(
            mixer.Analysis, mixer.Finding.Key, mixer.Finding.Candidates[0].Key, CheckVerdict.Approved,
            new string('x', CheckRules.MaximumTextLength + 1))));
        Assert.Equal(0, harness.Count("checks_decisions"));
    }

    [Fact]
    public void A_dismissal_holds_only_for_its_version_so_a_new_version_asks_again()
    {
        using var harness = new Harness();
        var check = Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));
        foreach (var candidate in check.Finding.Candidates)
        {
            harness.Service.Decide(harness.Case, new DecisionRequest(
                check.Analysis, check.Finding.Key, candidate.Key, CheckVerdict.Dismissed));
        }

        var service = new CheckService(
            [new MixerOutputsV2()], harness.Clustering, new FakeClock());
        var again = Mixer(service.Run(harness.Case, Harness.GraphOf(Mixing.All(), Mixing.Mixer, Mixing.MixerOther)));

        Assert.Equal(0, Mixer(harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther)).Waiting);
        Assert.Equal(3, again.Waiting);
        Assert.Equal(2, again.Analysis.Version);
    }

    [Fact]
    public void An_analysis_that_fails_says_so_and_the_others_are_sealed_all_the_same()
    {
        using var harness = new Harness();
        var service = new CheckService(
            [new FailingAnalysis(), new MixerOutputsCheck()], harness.Clustering, new FakeClock());

        var run = service.Run(harness.Case, Harness.GraphOf(Mixing.All(), Mixing.Mixer, Mixing.MixerOther));

        Assert.Equal("Analysen failing v1 misslyckades och gav inget den här gången.", Assert.Single(run.Problems));
        Assert.True(run.Sealed > 0);
        Assert.Contains(run.Checks, check => check.Analysis.Name == MixerOutputsCheck.Name);
    }

    [Fact]
    public void Nothing_new_needs_no_operator_profile_but_something_new_does()
    {
        using var harness = new Harness();
        harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther);
        harness.Profiles.Profile = null;

        harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther);
        var error = Assert.Throws<CaseException>(() => harness.Run([.. Mixing.All(), Patterns.Whirlpool()]));

        Assert.Equal(CaseError.NoOperatorProfile, error.Error);
    }

    [Fact]
    public void Two_analyses_with_the_same_name_are_refused_and_an_analysis_for_another_chain_never_runs()
    {
        using var harness = new Harness();

        Assert.Throws<InvalidOperationException>(() => new CheckService(
            [new MixerOutputsCheck(), new MixerOutputsCheck()], harness.Clustering, new FakeClock()));
        var service = new CheckService([new EthereumOnly()], harness.Clustering, new FakeClock());
        Assert.Empty(service.Analyses);
    }

    [Fact]
    public void The_module_registers_the_four_analyses_in_order()
    {
        using var harness = new Harness();

        Assert.Equal(
            [MixerOutputsCheck.Name, CoinJoinCheck.Name, PeelChainCheck.Name, WalletCheck.Name],
            harness.Service.Analyses.Select(analysis => analysis.Name));
    }

    /// <summary>The mixer analysis in a second version, to show that a dismissal holds for its version only.</summary>
    private sealed class MixerOutputsV2 : ICheckAnalysis
    {
        private readonly MixerOutputsCheck _v1 = new();

        public HeuristicId Id { get; } = new(MixerOutputsCheck.Name, 2);

        public string DisplayName => "Mixerutgångar";

        public ChainScope Scope => _v1.Scope;

        public IReadOnlyList<CheckFinding> Run(CheckInput input) => _v1.Run(input);
    }

    /// <summary>An analysis that always fails.</summary>
    private sealed class FailingAnalysis : ICheckAnalysis
    {
        public HeuristicId Id { get; } = new("failing", 1);

        public string DisplayName => "Trasig";

        public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

        public IReadOnlyList<CheckFinding> Run(CheckInput input) => throw new InvalidOperationException("Trasig.");
    }

    /// <summary>An analysis for Ethereum only.</summary>
    private sealed class EthereumOnly : ICheckAnalysis
    {
        public HeuristicId Id { get; } = new("ethereum-only", 1);

        public string DisplayName => "Ethereum";

        public ChainScope Scope { get; } = ChainScope.Only(Chain.Ethereum);

        public IReadOnlyList<CheckFinding> Run(CheckInput input) => [];
    }
}
