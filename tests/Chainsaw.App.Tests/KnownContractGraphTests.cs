using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph asks Chainsaw's list of known contracts about its Ethereum addresses each time it is read, reads the case
/// again only when a label was set, and is shown also when the labels cannot be written.
/// </summary>
public sealed class KnownContractGraphTests
{
    [Fact]
    public async Task The_graph_s_accounts_are_checked_and_the_graph_read_again_after_a_new_label()
    {
        var tracing = new TestTracing { Graph = EthSamples.Graph() };
        var known = new TestKnownContracts { Answer = 1 };
        var source = new GraphSource(
            tracing, new TestAttribution(), new TestClustering(), knownContracts: known);

        await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Contains(EthSamples.M.ToChainAddress(), Assert.Single(known.Asked));
        Assert.Equal(2, tracing.Loads);
    }

    [Fact]
    public async Task Nothing_new_is_read_once_and_a_failure_never_keeps_the_graph_from_being_shown()
    {
        var quiet = new TestTracing { Graph = EthSamples.Graph() };
        var none = new TestKnownContracts();
        await new GraphSource(quiet, new TestAttribution(), new TestClustering(), knownContracts: none)
            .LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);
        var failing = new TestTracing { Graph = EthSamples.Graph() };
        var broken = new TestKnownContracts { Failure = new InvalidOperationException("no profile") };

        var scene = await new GraphSource(failing, new TestAttribution(), new TestClustering(), knownContracts: broken)
            .LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Equal(1, quiet.Loads);
        Assert.Equal(1, failing.Loads);
        Assert.NotEmpty(scene.Nodes);
    }

    /// <summary>A list of known contracts that says how many labels it set, or fails.</summary>
    private sealed class TestKnownContracts : IKnownContractLabels
    {
        public int Answer { get; init; }

        public Exception? Failure { get; init; }

        public List<IReadOnlyCollection<ChainAddress>> Asked { get; } = [];

        public IReadOnlyList<LabelEntry> Check(ICase @case, IReadOnlyCollection<ChainAddress> addresses)
        {
            Asked.Add(addresses);
            if (Failure is not null)
            {
                throw Failure;
            }

            return [.. Enumerable.Range(0, Answer).Select(_ => new LabelEntry(
                1, LabelSubject.ForAccount(addresses.First()), LabelKind.Mixer, "Pool",
                new LabelOrigin("known-contract/x", LabelSource.OpenSource, LabelSource.OpenSource, "list", "v1",
                    new OperatorProfile("Testa", "Utredare", "Enheten"), DateTimeOffset.UnixEpoch, null, null),
                false, 1))];
        }
    }
}
