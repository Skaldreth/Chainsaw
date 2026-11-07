using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// Heuristics are registered per model and chain: one for accounts never runs on, or is named in, Bitcoin's analysis,
/// and one for Bitcoin must implement the UTXO contract.
/// </summary>
public sealed class ScopeTests
{
    [Fact]
    public void Bitcoins_heuristics_are_registered_for_bitcoin_alone()
    {
        using var harness = new CaseHarness();

        Assert.Equal(4, harness.Service.Heuristics.Count);
        var heuristics = new ServiceCollection().AddClusteringModule().BuildServiceProvider()
            .GetServices<IClusteringHeuristic>().ToList();
        Assert.All(heuristics, heuristic => Assert.Equal(ChainScope.Only(Chain.Bitcoin), heuristic.Scope));
        Assert.All(heuristics, heuristic => Assert.IsAssignableFrom<IUtxoHeuristic>(heuristic));
    }

    [Fact]
    public void A_heuristic_for_accounts_changes_nothing_for_bitcoin()
    {
        using var plain = new CaseHarness();
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic>(new AccountHeuristic(HeuristicNames.CoinJoin)));

        var before = plain.Analyse(Patterns.Whirlpool());
        var after = harness.Analyse(Patterns.Whirlpool());

        Assert.Equal(plain.Service.Heuristics, harness.Service.Heuristics);
        Assert.Equal(before.Journal!.Summary, after.Journal!.Summary);
        Assert.Equal(before.Journal.Details, after.Journal.Details);
        Assert.Equal(plain.Count("clustering_evidence"), harness.Count("clustering_evidence"));
    }

    [Fact]
    public void A_heuristic_for_bitcoin_without_the_utxo_contract_is_refused()
    {
        IClusteringHeuristic[] heuristics = [new AccountHeuristic("misplaced", ChainScope.Only(Chain.Bitcoin))];

        Assert.Throws<ArgumentException>(() => new ClusteringService(heuristics));
    }

    /// <summary>A heuristic registered for a model or chain; it has no contract to analyse anything with.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="scope">Its scope; every account chain when not given.</param>
    private sealed class AccountHeuristic(string name, ChainScope? scope = null) : IClusteringHeuristic
    {
        public HeuristicId Id { get; } = new(name, 1);

        public ChainScope Scope { get; } = scope ?? ChainScope.AllOf(LedgerModel.Account);
    }
}
