#if DEBUG
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>The modules the test case is built through, by their contracts only, as the user's actions are.</summary>
/// <param name="Cases">Creates and opens the cases.</param>
/// <param name="Tracing">Start points, expansions and links to another chain.</param>
/// <param name="Attribution">Labels.</param>
/// <param name="Library">The label library, for the label from another case; none when it is not registered.</param>
/// <param name="Monitoring">Watches.</param>
/// <param name="Checks">Checks, suggestions and own areas.</param>
public sealed record TestServices(
    ICaseStore Cases,
    ITracingService Tracing,
    IAttributionService Attribution,
    ILabelLibrary? Library,
    IMonitoringService Monitoring,
    ICheckService Checks)
{
    /// <summary>Gets the graph's comments (sketch KN1a); none when they are not registered.</summary>
    public IGraphCommentService? Comments { get; init; }

    /// <summary>Gets the graph's trash (sketch SO1a); none when it is not registered.</summary>
    public IGraphTrash? Trash { get; init; }

    /// <summary>Gets the clustering, for a split the user made (sketch AN1b); none when it is not registered.</summary>
    public IClusteringService? Clustering { get; init; }

    /// <summary>Gets the Bitcoin source, for the transaction whose block changed (sketch GR9a), if any.</summary>
    public IBitcoinSource? Bitcoin { get; init; }
}

/// <summary>One build of the test case: the case, the story it is built from, and what did not go as planned.</summary>
/// <param name="services">The modules.</param>
/// <param name="case">The open case the steps work in.</param>
/// <param name="chain">The made-up chain.</param>
/// <param name="actors">The story's actors.</param>
public sealed class TestRun(TestServices services, ICase @case, TestChain chain, TestActors actors)
{
    /// <summary>What did not go as planned, in plain Swedish.</summary>
    private readonly List<string> _problems = [];

    /// <summary>Gets the modules.</summary>
    public TestServices Services => services;

    /// <summary>Gets the case.</summary>
    public ICase Case => @case;

    /// <summary>Gets the made-up chain.</summary>
    public TestChain Chain => chain;

    /// <summary>Gets the actors.</summary>
    public TestActors Actors => actors;

    /// <summary>Gets what did not go as planned.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Notes something that did not go as planned.</summary>
    /// <param name="problem">What, in plain Swedish.</param>
    public void Note(string problem) => _problems.Add(problem);

    /// <summary>Gives a made-up transaction's txid.</summary>
    /// <param name="label">The story's label for it.</param>
    /// <returns>The txid.</returns>
    public Txid TxidOf(string label) => Txid.Parse(chain[label].Txid);
}

/// <summary>
/// One step of the test case's story, done through the modules' contracts as a user's action would be. A new part of
/// Chainsaw adds a step of its own here and puts it in <see cref="TestStory"/>, so that it can be seen in the test case.
/// </summary>
public interface ITestStep
{
    /// <summary>Does the step.</summary>
    /// <param name="run">The build.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when the step is done.</returns>
    Task RunAsync(TestRun run, CancellationToken cancellationToken);
}

/// <summary>Makes a transaction a start point.</summary>
/// <param name="Transaction">The story's label for it.</param>
public sealed record StartPointStep(string Transaction) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Services.Tracing.AddStartPoint(run.Case, TraceStartPoint.ForTransaction(run.TxidOf(Transaction)));
        return Task.CompletedTask;
    }
}

/// <summary>Expands a transaction: brings it in, checked against its bytes, with the transactions that spend it.</summary>
/// <param name="Transaction">The story's label for it.</param>
public sealed record ExpandStep(string Transaction) : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var result = await run.Services.Tracing.ExpandAsync(
            run.Case, ExpansionRequest.ForTransaction(run.TxidOf(Transaction)), cancellationToken)
            .ConfigureAwait(false);
        if (result.Outcome != ExpansionOutcome.Expanded)
        {
            run.Note($"Expansionen av {Transaction} blev inte hel: {result.Problem}");
        }
    }
}

/// <summary>
/// Sets a label on an address or on its cluster. The test case's labels are marked as made up (the open source
/// <see cref="TestStory.LabelSource"/>) and are kept out of the label library, so that they never spread to a real case;
/// the victim's is the user's own, as it would be.
/// </summary>
/// <param name="At">The address.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Name">Its name.</param>
public sealed record LabelStep(TestAddress At, LabelKind Kind, string? Name) : ITestStep
{
    /// <summary>Gets whether the label is on the address's cluster rather than the address.</summary>
    public bool OnCluster { get; init; }

    /// <summary>Gets whether the label is the user's own rather than from the made-up open source.</summary>
    public bool Own { get; init; }

    /// <summary>Gets whether the label may go into the label library (a colleague's case's labels do).</summary>
    public bool Spreads { get; init; }

    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var address = At.Address ?? throw new InvalidOperationException($"{At.Label} has no address to label.");
        var subject = OnCluster ? LabelSubject.ForCluster(address) : LabelSubject.ForAddress(address);
        var draft = new LabelDraft(subject, Kind, Name, TestStory.LabelReason, DoNotSpread: !Spreads);
        run.Services.Attribution.Add(run.Case, Own ? draft : draft with
        {
            Source = LabelSource.OpenSource,
            SourceName = TestStory.LabelSource,
            SourceVersion = TestStory.Version,
        });
        return Task.CompletedTask;
    }
}

/// <summary>Brings the label library's labels into the case, as opening it does, for the label from another case.</summary>
public sealed record ShareLabelsStep : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.Library is not { } library)
        {
            run.Note("Etikettbiblioteket finns inte, så etiketten från ett annat ärende kom inte med.");
            return;
        }

        var graph = await run.Services.Tracing.LoadAsync(run.Case, cancellationToken).ConfigureAwait(false);
        var addresses = graph.Nodes.SelectMany(node => node.GraphAddresses).Distinct().ToList();
        if (library.ShareInto(run.Case, addresses).Count == 0)
        {
            run.Note("Ingen etikett från ett annat ärende kom in ur etikettbiblioteket.");
        }
    }
}

/// <summary>Links an output to the transaction it continues as on another chain (a swap).</summary>
/// <param name="Transaction">The story's label for the transaction.</param>
/// <param name="Output">The output that leaves the chain.</param>
/// <param name="Seed">What the made-up Ethereum transaction's hash is made from.</param>
public sealed record LinkOtherChainStep(string Transaction, int Output, string Seed) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var hash = "0x" + Convert.ToHexStringLower(TestAddress.Hash(Seed));
        run.Services.Tracing.LinkCrossChain(
            run.Case,
            new Outpoint(run.TxidOf(Transaction), Output),
            new ChainTransactionRef(Chain.Ethereum, hash),
            TestStory.LabelReason);
        return Task.CompletedTask;
    }
}

/// <summary>Watches a cluster for money in one direction.</summary>
/// <param name="Anchor">An address in the cluster.</param>
/// <param name="Name">The watch's name.</param>
/// <param name="Direction">Which money is watched.</param>
public sealed record WatchStep(TestAddress Anchor, string Name, WatchDirection Direction) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var anchor = Anchor.Address ?? throw new InvalidOperationException($"{Anchor.Label} has no address.");
        run.Services.Monitoring.Add(
            run.Case,
            new WatchSubject(WatchSubjectKind.Cluster, anchor.ToChainAddress(), Name),
            new WatchFilter(Chain.Bitcoin, Direction, [], [], null, null));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Checks the watches as the running Chainsaw does: once at the block before the newest, where they start, and once
/// at the newest, where the new victim's payment is their hit, left unseen so that it waits for the user.
/// </summary>
public sealed record WatchCheckStep : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        using var scope = TestDataScope.Building();
        var monitoring = run.Services.Monitoring;
        await monitoring.CheckAsync(run.Case, Chain.Bitcoin, run.Chain.Tip - 1, cancellationToken)
            .ConfigureAwait(false);
        var checkedRun = await monitoring.CheckAsync(run.Case, Chain.Bitcoin, run.Chain.Tip, cancellationToken)
            .ConfigureAwait(false);
        if (!checkedRun.Hits.Any())
        {
            var problems = checkedRun.Outcomes.SelectMany(outcome => outcome.Problems).DefaultIfEmpty("ingen träff");
            run.Note($"Bevakningen slog inte till ({string.Join("; ", problems)}).");
        }
    }
}

/// <summary>Runs the checks and suggestions on the graph, so that there is something to decide.</summary>
public sealed record ChecksStep : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var graph = await run.Services.Tracing.LoadAsync(run.Case, cancellationToken).ConfigureAwait(false);
        var checks = run.Services.Checks.Run(run.Case, graph);
        if (!checks.Checks.Any(check => check.Finding.Kind == CheckKind.Suggestion))
        {
            run.Note("Kontrollerna gav inget förslag att avgöra.");
        }

        foreach (var problem in checks.Problems)
        {
            run.Note(problem);
        }
    }
}

/// <summary>Draws an own area around some actors, as the user does with Z.</summary>
/// <param name="Name">The area's name.</param>
/// <param name="Members">The addresses in it.</param>
/// <param name="Note">Why.</param>
public sealed record OwnAreaStep(string Name, IReadOnlyList<TestAddress> Members, string Note) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var members = Members.Select(member => member.Address)
            .OfType<BitcoinAddress>()
            .Select(CheckRules.MemberOf)
            .ToList();
        run.Services.Checks.AddArea(
            run.Case, new OwnAreaDraft(Name, AreaSort.Own, AreaTint.Blue, false, Note, members));
        return Task.CompletedTask;
    }
}

/// <summary>Writes a comment on the graph about an actor, as the user does with N (sketch KN1a).</summary>
/// <param name="About">The actor its thread goes to.</param>
/// <param name="Text">Its Markdown.</param>
/// <param name="Place">Where it stands on the free surface: above the graph, which is laid out downwards.</param>
public sealed record CommentStep(TestAddress About, string Text, NodePosition Place) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.Comments is not { } comments || About.Address is not { } address)
        {
            run.Note("Kommentaren kunde inte skrivas: kommentarerna finns inte.");
            return Task.CompletedTask;
        }

        comments.AddComment(run.Case, new GraphCommentDraft(Text, GraphPile.MemberKey(address), Place));
        return Task.CompletedTask;
    }
}

#endif
