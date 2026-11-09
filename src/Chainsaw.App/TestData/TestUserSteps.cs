#if DEBUG
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// Checks the graph's addresses against the sanction lists while the test case is built, when the made-up list
/// (<see cref="TestDataSanctionLists"/>) has the story's sanctioned actor on it: a sanction hit for the view Etiketter
/// (sketch ET1a) without a real list being imported.
/// </summary>
public sealed record SanctionCheckStep : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var graph = await run.Services.Tracing.LoadAsync(run.Case, cancellationToken).ConfigureAwait(false);
        var addresses = graph.Nodes.SelectMany(node => node.GraphAddresses).Distinct().ToList();
        using var scope = TestDataScope.Building();
        if (run.Services.Attribution.CheckSanctions(run.Case, addresses).Added.Count == 0)
        {
            run.Note("Sanktionskontrollen gav ingen träff på den påhittade listan.");
        }
    }
}

/// <summary>
/// Checks the graph's addresses against the open sources while the test case is built, when the made-up source
/// (<see cref="TestDataOpenSources"/>) names the scammer's second address: a <em>Rapporterad</em> label from an open
/// source in Ransomwhere's form, without a real file being imported.
/// </summary>
public sealed record OpenSourceCheckStep : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var graph = await run.Services.Tracing.LoadAsync(run.Case, cancellationToken).ConfigureAwait(false);
        var addresses = graph.Nodes.SelectMany(node => node.GraphAddresses)
            .Select(address => address.ToChainAddress())
            .Distinct()
            .ToList();
        using var scope = TestDataScope.Building();
        if (run.Services.Attribution.CheckOpenSources(run.Case, addresses).Added.Count == 0)
        {
            run.Note("Kontrollen mot de öppna källorna gav ingen träff på den påhittade källan.");
        }
    }
}

/// <summary>
/// Labels a transaction, as the user does where no address says what it is (round 5): a label that says where the
/// money goes ends the trail at the transaction.
/// </summary>
/// <param name="Transaction">The story's label for the transaction.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Name">The service's name.</param>
public sealed record TransactionLabelStep(string Transaction, LabelKind Kind, string Name) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.TransactionLabels is not { } labels)
        {
            run.Note("Etiketten på tx:en kunde inte sättas: etiketterna på tx finns inte.");
            return Task.CompletedTask;
        }

        labels.Add(run.Case, new TransactionLabelDraft(
            Chain.Bitcoin, run.TxidOf(Transaction).Value, Kind, Name, TestStory.LabelReason, null));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives a transaction a changed block (sketch GR9a), as a reorganisation does: the source first answers it, in an
/// address's list, in an orphaned block at the same height, and then, asked for the transaction itself, in its own
/// block. Both answers are sealed in the case (principle 1), and the graph marks the transaction as moved.
/// </summary>
/// <param name="Transaction">The story's label for the transaction; it must not have been looked up yet.</param>
/// <param name="Receiver">An address the transaction pays, whose list is asked for first.</param>
public sealed record ReorganisationStep(string Transaction, TestAddress Receiver) : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.Bitcoin is not { } bitcoin || Receiver.Address is not { } receiver)
        {
            run.Note("Det bytta blocket kunde inte göras: Bitcoin-källan finns inte.");
            return;
        }

        using (run.Chain.Orphaning(Transaction))
        {
            await bitcoin.GetAddressTransactionsAsync(run.Case, receiver, null, cancellationToken)
                .ConfigureAwait(false);
        }

        await bitcoin.GetTransactionAsync(run.Case, run.TxidOf(Transaction), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var changes = await bitcoin.GetBlockChangesAsync(run.Case, cancellationToken).ConfigureAwait(false);
        if (!changes.Any(change => change.TransactionId == run.TxidOf(Transaction).Value))
        {
            run.Note($"Blocket för {Transaction} syns inte som bytt.");
        }
    }
}

/// <summary>
/// Makes the made-up source lag a block behind the newest (<see cref="TestChain.Behind"/>) until
/// <see cref="ReplacedStep"/> has had the first answer that its transaction is not known, so that the case learns of
/// the newest block between the two answers that say so (the second is <see cref="GoneStep"/>'s). It comes before
/// anything is looked up.
/// </summary>
public sealed record LagStep : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Chain.Behind = 1;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Lets the source see a transaction that is later replaced (sketch GR9a, <em>Ersatt av</em>), as a replacement by fee
/// (RBF) after a reorganisation does: while the source lags a block behind (<see cref="LagStep"/>), the transaction is
/// expanded from an orphaned block, and then the source no longer knows it, once. One such answer does not make it
/// gone, so the expansions after it still know it, and conclusions can rest on it; <see cref="GoneStep"/> gives the
/// second answer after the newest block. Every answer is sealed in the case (principle 1).
/// </summary>
/// <param name="Transaction">The story's label for the replaced transaction.</param>
public sealed record ReplacedStep(string Transaction) : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        try
        {
            await SeenAsync(run, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            run.Chain.Behind = 0;
        }
    }

    /// <summary>Lets the source see the transaction in an orphaned block, and then no more.</summary>
    /// <param name="run">The build.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when the source has answered.</returns>
    private async Task SeenAsync(TestRun run, CancellationToken cancellationToken)
    {
        if (run.Services.Bitcoin is not { } bitcoin)
        {
            run.Note("Den ersatta tx:en kunde inte göras: Bitcoin-källan finns inte.");
            return;
        }

        await bitcoin.GetTipHeightAsync(run.Case, cancellationToken).ConfigureAwait(false);
        using (run.Chain.Orphaning(Transaction))
        {
            await new ExpandStep(Transaction).RunAsync(run, cancellationToken).ConfigureAwait(false);
        }

        await bitcoin.GetTransactionAsync(run.Case, run.TxidOf(Transaction), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Makes the transaction that <see cref="ReplacedStep"/> let the source see gone and replaced (sketch GR9a): after the
/// newest block the source again does not know it, which confirms the first answer, and the transaction its coin came
/// from is asked who spends it now. The next expansion then leaves it out of the clustering, so that a conclusion that
/// rests on it is held back by itself (sketch HB1a). Every answer is sealed in the case (principle 1).
/// </summary>
/// <param name="Transaction">The story's label for the replaced transaction.</param>
public sealed record GoneStep(string Transaction) : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.Bitcoin is not { } bitcoin)
        {
            run.Note("Den ersatta tx:en kunde inte göras: Bitcoin-källan finns inte.");
            return;
        }

        var txid = run.TxidOf(Transaction);
        await bitcoin.GetTipHeightAsync(run.Case, cancellationToken).ConfigureAwait(false);
        await bitcoin.GetTransactionAsync(run.Case, txid, cancellationToken: cancellationToken).ConfigureAwait(false);
        var replacement = await bitcoin.GetReplacementAsync(run.Case, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        var changes = await bitcoin.GetBlockChangesAsync(run.Case, cancellationToken).ConfigureAwait(false);
        if (!changes.Any(change => change.TransactionId == txid.Value && change.Kind == BlockChangeKind.Gone)
            || replacement.Value?.Kind != ReplacementKind.Replaced)
        {
            run.Note($"{Transaction} syns inte som borta och ersatt.");
        }
    }
}

/// <summary>Changes a comment's text, as the user does in the note, so that its history has something.</summary>
/// <param name="About">The actor the comment is tied to.</param>
/// <param name="Text">The new Markdown.</param>
public sealed record EditCommentStep(TestAddress About, string Text) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var anchor = About.Address is { } address ? GraphPile.MemberKey(address) : null;
        var comment = run.Services.Comments?.GetComments(run.Case).FirstOrDefault(each => each.Anchor == anchor);
        if (comment is null || run.Services.Comments!.EditComment(run.Case, comment.Id, Text) is null)
        {
            run.Note("Kommentaren kunde inte ändras.");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Writes a comment and throws it in the trash, as the user does by dragging the note to the bin (sketch SO1a): the
/// note is hidden in the graph and stands in the trash's view, and stays in the case as it was written.
/// </summary>
/// <param name="Text">The comment's Markdown.</param>
/// <param name="Place">Where it stood on the free surface.</param>
public sealed record ThrownCommentStep(string Text, NodePosition Place) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.Comments is not { } comments || run.Services.Trash is not { } trash)
        {
            run.Note("Den slängda kommentaren kunde inte göras: kommentarerna eller soptunnan finns inte.");
            return Task.CompletedTask;
        }

        var comment = comments.AddComment(run.Case, new GraphCommentDraft(Text, null, Place));
        trash.Throw(run.Case, [TrashKeys.Comment(comment.Id, comment.Text)]);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Draws an own area and throws it in the trash, as the user does from the area's menu (sketch SO1a): only the area is
/// hidden, its actors stay, and it stays in the case as it was drawn.
/// </summary>
/// <param name="Name">The area's name.</param>
/// <param name="Members">The addresses in it.</param>
/// <param name="Note">Why it was drawn.</param>
public sealed record ThrownAreaStep(string Name, IReadOnlyList<TestAddress> Members, string Note) : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.Trash is not { } trash)
        {
            run.Note("Den slängda ytan kunde inte göras: soptunnan finns inte.");
            return;
        }

        await new OwnAreaStep(Name, Members, Note).RunAsync(run, cancellationToken).ConfigureAwait(false);
        var area = run.Services.Checks.GetAreas(run.Case).Last(each => each.Name == Name);
        trash.Throw(run.Case, [TrashKeys.Area(area.Id, area.Name)]);
    }
}

/// <summary>
/// Breaks an address out of its cluster, as the user does with Bryt upp (U): one split per merged link that joins it
/// to the rest, so that the inspector's history (sketch AN1b) has a split to undo.
/// </summary>
/// <param name="Leaving">The address that leaves its cluster.</param>
/// <param name="Reason">Why, as the user would write it.</param>
public sealed record SplitStep(TestAddress Leaving, string Reason) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.Clustering is not { } clustering || Leaving.Address is not { } address)
        {
            run.Note("Uppbrytningen kunde inte göras: klustringen finns inte.");
            return Task.CompletedTask;
        }

        var requests = InspectorForms.BreakOut(clustering.GetCluster(run.Case, address), [address], Reason);
        if (requests.Count == 0)
        {
            run.Note($"{Leaving.Label} var inte sammanslagen med något att bryta upp.");
            return Task.CompletedTask;
        }

        clustering.Split(run.Case, requests);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Decides some of the suggestions, as the user does in the panel (sketch KF2b): approves a proposed wallet, so that
/// an approved wallet stands in the graph, and approves the mixer's likeliest way out while the other candidates still
/// wait, so that a suggestion shows an approval next to what is left to decide. The checks run again afterwards, so
/// that what follows from the approvals is sealed.
/// </summary>
/// <param name="Wallet">An address in the proposed wallet to approve.</param>
/// <param name="Deposit">
/// The story's label for the transaction whose deposit into the mixer has its likeliest way out approved, since the
/// mixer has more deposits than one.
/// </param>
/// <param name="Reason">The reason given with the decisions.</param>
public sealed record DecideStep(TestAddress Wallet, string Deposit, string Reason) : ITestStep
{
    /// <summary>The name of the analysis that suggests the mixer's likely ways out.</summary>
    public const string MixerOutputs = "mixer-outputs";

    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var checks = run.Services.Checks;
        var graph = await run.Services.Tracing.LoadAsync(run.Case, cancellationToken).ConfigureAwait(false);
        var suggestions = checks.Run(run.Case, graph).Checks
            .Where(check => check.Finding.Kind == CheckKind.Suggestion)
            .ToList();
        var requests = new List<DecisionRequest>();
        if (suggestions.FirstOrDefault(check => check.Analysis.Name == CheckAnalysisNames.Wallets
                && check.Finding.Addresses.Any(address => address == Wallet.Address)) is { } wallet)
        {
            requests.Add(new DecisionRequest(wallet.Analysis, wallet.Finding.Key, null, CheckVerdict.Approved, Reason));
        }

        var deposit = run.TxidOf(Deposit).Value + ":";
        if (suggestions.FirstOrDefault(check => check.Analysis.Name == MixerOutputs
                && check.Finding.Key.StartsWith(deposit, StringComparison.Ordinal)) is { } outputs
            && outputs.Finding.Candidates.MaxBy(candidate => candidate.Score) is { } likeliest)
        {
            requests.Add(new DecisionRequest(
                outputs.Analysis, outputs.Finding.Key, likeliest.Key, CheckVerdict.Approved, Reason));
        }

        if (requests.Count < 2)
        {
            run.Note($"Kontrollerna gav inte både plånboken med {Wallet.Label} och mixerns utgångar att godkänna.");
        }

        checks.DecideAll(run.Case, requests);
        var after = await run.Services.Tracing.LoadAsync(run.Case, cancellationToken).ConfigureAwait(false);
        checks.Run(run.Case, after);
    }
}
#endif
