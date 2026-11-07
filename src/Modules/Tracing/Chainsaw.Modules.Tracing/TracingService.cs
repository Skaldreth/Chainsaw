using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The trace as a model: keeps the graph's state in the case, expands it through <see cref="IBitcoinSource"/>, builds
/// it with the clusters and the ends of the trail, and traces money through it by a registered method.
/// </summary>
internal sealed class TracingService : ITracingService
{
    /// <summary>The longest reason the user may give.</summary>
    private const int MaximumReasonLength = 1_000;

    /// <summary>Bitcoin's facts.</summary>
    private readonly IBitcoinSource _source;

    /// <summary>The clusters and the analyses.</summary>
    private readonly IClusteringService _clustering;

    /// <summary>What recognises the ends of the trail, in registration order.</summary>
    private readonly IReadOnlyList<ITrailEndClassifier> _classifiers;

    /// <summary>The newest version of each tracing method.</summary>
    private readonly IReadOnlyDictionary<TracingMethodKind, ITracingMethod> _methods;

    /// <summary>The clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="source">Bitcoin's facts.</param>
    /// <param name="clustering">The clusters and the analyses.</param>
    /// <param name="classifiers">What recognises the ends of the trail.</param>
    /// <param name="methods">The tracing methods.</param>
    /// <param name="clock">The clock.</param>
    public TracingService(
        IBitcoinSource source,
        IClusteringService clustering,
        IEnumerable<ITrailEndClassifier> classifiers,
        IEnumerable<ITracingMethod> methods,
        IClock clock)
    {
        _source = source;
        _clustering = clustering;
        _classifiers = [.. classifiers];
        _methods = methods.GroupBy(method => method.Id.Kind)
            .ToDictionary(group => group.Key, group => group.MaxBy(method => method.Id.Version)!);
        _clock = clock;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TracingMethodId> Methods =>
        [.. _methods.Values.Select(method => method.Id).OrderBy(id => id.Kind)];

    /// <inheritdoc/>
    public Task<TraceGraph> LoadAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(
            async () =>
            {
                var data = await ReadDataAsync(@case, cancellationToken).ConfigureAwait(false);
                return Build(@case, data);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public bool AddStartPoint(ICase @case, TraceStartPoint startPoint)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(startPoint);
        return @case.Write(transaction =>
        {
            if (GraphStateStore.HasStartPoint(transaction, startPoint))
            {
                return false;
            }

            var journal = transaction.AppendJournal(StartPointDraft(
                TracingJournalKinds.StartPointAdded, "Startpunkt för spårningen: {0}", startPoint));
            GraphStateStore.InsertStartPoint(transaction, startPoint, journal);
            return true;
        });
    }

    /// <inheritdoc/>
    public bool RemoveStartPoint(ICase @case, TraceStartPoint startPoint)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(startPoint);
        return @case.Write(transaction =>
        {
            if (!GraphStateStore.HasStartPoint(transaction, startPoint))
            {
                return false;
            }

            transaction.AppendJournal(StartPointDraft(
                TracingJournalKinds.StartPointRemoved, "Startpunkten togs bort: {0}", startPoint));
            GraphStateStore.DeleteStartPoint(transaction, startPoint);
            return true;
        });
    }

    /// <inheritdoc/>
    public Task<ExpansionResult> ExpandAsync(
        ICase @case, ExpansionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Address is null) == (request.Txid is null))
        {
            throw new ArgumentException("Expand either an address or a transaction.", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumTransactions, 1, nameof(request));
        return Task.Run(() => ExpandCoreAsync(@case, request, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public CrossChainLink LinkCrossChain(ICase @case, Outpoint from, ChainTransactionRef to, string reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        CheckReason(reason);
        if (to.Chain == Chain.Bitcoin)
        {
            throw new ArgumentException("A link to another chain leads off Bitcoin.", nameof(to));
        }

        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.CrossChainLinked,
                string.Format(CultureInfo.InvariantCulture, "Fortsätter på annan kedja: {0} → {1}", from, to),
                JsonSerializer.Serialize(
                    new { from = from.ToString(), chain = to.Chain.ToString(), to = to.Id, reason })));
            return GraphStateStore.SealLink(transaction, from, to, reason, journal);
        });
    }

    /// <inheritdoc/>
    public bool UnlinkCrossChain(ICase @case, CrossChainLink link, string reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(link);
        CheckReason(reason);
        return @case.Write(transaction =>
        {
            if (!GraphStateStore.Read(transaction).Links.Any(current => current.Sequence == link.Sequence))
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.CrossChainUnlinked,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Länken till annan kedja togs tillbaka: {0} → {1}",
                    link.From,
                    link.To),
                JsonSerializer.Serialize(new { link = link.Sequence, reason })));
            GraphStateStore.SealUnlink(transaction, link, reason, journal);
            return true;
        });
    }

    /// <inheritdoc/>
    public void SavePosition(ICase @case, string nodeKey, GraphLayout layout, NodePosition position)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentException.ThrowIfNullOrEmpty(nodeKey);
        if (!Enum.IsDefined(layout) || !double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentException("A place is a known layout and finite coordinates.", nameof(position));
        }

        var utc = _clock.UtcNow;
        @case.Write(transaction =>
        {
            GraphStateStore.SavePosition(transaction, NodeKeys.Position(nodeKey), layout, position, utc);
            return true;
        });
    }

    /// <inheritdoc/>
    public TraceResult Trace(TraceGraph graph, TraceRequest request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);
        if (!_methods.TryGetValue(request.Method, out var method))
        {
            throw new ArgumentException("The tracing method is not registered.", nameof(request));
        }

        return TraceCalculator.Trace(graph, method, request.Seeds);
    }

    /// <summary>Expands the graph, records what came in, runs the analyses and builds the graph.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="request">What to expand.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What came in and the graph.</returns>
    private async Task<ExpansionResult> ExpandCoreAsync(
        ICase @case, ExpansionRequest request, CancellationToken cancellationToken)
    {
        var before = @case.Read(GraphStateStore.Read).Transactions.ToHashSet();
        var step = request.Address is { } address
            ? await ExpandAddressAsync(@case, address, request.MaximumTransactions, cancellationToken)
                .ConfigureAwait(false)
            : await ExpandTransactionAsync(@case, request.Txid!, cancellationToken).ConfigureAwait(false);
        if (!step.Answered)
        {
            var unchanged = await LoadAsync(@case, cancellationToken).ConfigureAwait(false);
            return new ExpansionResult(ExpansionOutcome.NotExpanded, [], step.Problem, unchanged);
        }

        var expansion = new Expansion(
            request.Address, request.Txid, step.Transactions, step.Problem is null, _clock.UtcNow);
        @case.Write(transaction => GraphStateStore.SaveExpansion(transaction, expansion));
        await FetchSpendsAsync(@case, step.Transactions, cancellationToken).ConfigureAwait(false);
        var data = await ReadDataAsync(@case, cancellationToken).ConfigureAwait(false);
        if (data.Transactions.Count > 0)
        {
            _clustering.Analyse(@case, [.. data.Transactions.Values.Select(entry => entry.Transaction)]);
        }

        return new ExpansionResult(
            expansion.IsComplete ? ExpansionOutcome.Expanded : ExpansionOutcome.Partly,
            [.. step.Transactions.Where(txid => !before.Contains(txid))],
            step.Problem,
            Build(@case, data));
    }

    /// <summary>Brings in an address's history, newest first, up to the limit.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="maximum">The most transactions.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The transactions and what went wrong.</returns>
    private async Task<StepResult> ExpandAddressAsync(
        ICase @case, BitcoinAddress address, int maximum, CancellationToken cancellationToken)
    {
        var found = new List<Txid>();
        Txid? after = null;
        while (true)
        {
            var page = await _source.GetAddressTransactionsAsync(@case, address, after, cancellationToken)
                .ConfigureAwait(false);
            if (page.Value is null)
            {
                return new StepResult(found, Problem(page.Status, page.Problem), found.Count > 0);
            }

            var countBefore = found.Count;
            foreach (var transaction in page.Value.Transactions.Where(tx => !found.Contains(tx.Txid)))
            {
                if (found.Count == maximum)
                {
                    return new StepResult(
                        found,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Adressen har fler än {0} tx; de nyaste {0} togs med.",
                            maximum),
                        true);
                }

                found.Add(transaction.Txid);
            }

            if (page.Value.NextAfter is null)
            {
                var outdated = page.Status == LookupStatus.FromCaseMayBeOutdated ? page.Problem : null;
                return new StepResult(found, outdated, true);
            }

            if (page.Value.NextAfter == after || found.Count == countBefore)
            {
                // A source that does not move on would otherwise be asked for ever.
                return new StepResult(
                    found, "Källan gav samma sida igen; historiken togs med så långt den gick.", true);
            }

            after = page.Value.NextAfter;
        }
    }

    /// <summary>Brings in a transaction and the transactions that spend its outputs.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The transactions and what went wrong.</returns>
    private async Task<StepResult> ExpandTransactionAsync(ICase @case, Txid txid, CancellationToken cancellationToken)
    {
        var lookup = await _source.GetTransactionAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is null)
        {
            return new StepResult([], Problem(lookup.Status, lookup.Problem), false);
        }

        var found = new List<Txid> { txid };
        var spends = await _source.GetOutputSpendsAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (spends.Value is null)
        {
            return new StepResult(found, Problem(spends.Status, spends.Problem), true);
        }

        string? problem = spends.Status == LookupStatus.FromCaseMayBeOutdated ? spends.Problem : null;
        foreach (var spender in spends.Value.Select(spend => spend.SpentBy?.Txid).OfType<Txid>().Distinct())
        {
            var next = await _source.GetTransactionAsync(@case, spender, LookupMode.AsNeeded, cancellationToken)
                .ConfigureAwait(false);
            if (next.Value is null)
            {
                problem ??= Problem(next.Status, next.Problem);
                continue;
            }

            found.Add(spender);
        }

        return new StepResult(found, problem, true);
    }

    /// <summary>
    /// Asks where the outputs of the transactions an expansion brought in went, so that the graph knows which coins
    /// are spent and which are still there. A failure here is not an error: those coins stay not expanded yet.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="transactions">The transactions.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when every answer is in the case.</returns>
    private async Task FetchSpendsAsync(
        ICase @case, IReadOnlyList<Txid> transactions, CancellationToken cancellationToken)
    {
        foreach (var txid in transactions)
        {
            await _source.GetOutputSpendsAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the state and every transaction in it and where its outputs went, from the case only: showing the graph
    /// again never sends anything (principles 4 and 6). What may have changed says so; expanding asks again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The data.</returns>
    private async Task<GraphData> ReadDataAsync(ICase @case, CancellationToken cancellationToken)
    {
        var state = @case.Read(GraphStateStore.Read);
        var transactions = new Dictionary<Txid, GraphTransaction>();
        var spends = new Dictionary<Txid, OutputSpendsAnswer>();
        var problems = new List<string>();
        foreach (var txid in state.Transactions)
        {
            var lookup = await _source.GetTransactionAsync(@case, txid, LookupMode.CaseOnly, cancellationToken)
                .ConfigureAwait(false);
            if (lookup.Value is null)
            {
                problems.Add($"Tx {txid}: {Problem(lookup.Status, lookup.Problem)}");
                continue;
            }

            transactions.Add(txid, new GraphTransaction(lookup.Value, lookup.Status, lookup.Problem));
            var answer = await _source.GetOutputSpendsAsync(@case, txid, LookupMode.CaseOnly, cancellationToken)
                .ConfigureAwait(false);
            if (answer.Value is null)
            {
                // Not asked yet: its coins are not expanded yet, which the trace says where it matters.
                continue;
            }

            spends.Add(txid, new OutputSpendsAnswer(
                answer.Value, answer.Status == LookupStatus.FromCaseMayBeOutdated));
        }

        return new GraphData(state, transactions, spends, problems);
    }

    /// <summary>Builds the graph with the clusters and the ends of the trail.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="data">The data.</param>
    /// <returns>The graph.</returns>
    private TraceGraph Build(ICase @case, GraphData data) => GraphBuilder.Build(data, new GraphKnowledge(
        address => [.. _clustering.GetCluster(@case, address).Addresses],
        address => [.. _classifiers.SelectMany(classifier => classifier.ClassifyAddress(@case, address))],
        transaction => [.. _classifiers.SelectMany(classifier => classifier.ClassifyTransaction(@case, transaction))]));

    /// <summary>Gives the journal draft for a start point.</summary>
    /// <param name="kind">The journal code.</param>
    /// <param name="format">The line, with the start point at <c>{0}</c>.</param>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The draft.</returns>
    private static JournalDraft StartPointDraft(string kind, string format, TraceStartPoint startPoint)
    {
        var (startKind, subject) = GraphStateStore.Key(startPoint);
        return new JournalDraft(
            kind,
            string.Format(CultureInfo.InvariantCulture, format, subject),
            JsonSerializer.Serialize(new { kind = startKind, subject }));
    }

    /// <summary>Checks a reason the user gives.</summary>
    /// <param name="reason">The reason.</param>
    /// <exception cref="ArgumentException">It is empty or too long.</exception>
    private static void CheckReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumReasonLength)
        {
            throw new ArgumentException("A reason of at most 1 000 characters is needed.", nameof(reason));
        }
    }

    /// <summary>Says what went wrong with a lookup, for the user.</summary>
    /// <param name="status">How it was answered.</param>
    /// <param name="problem">The source's own text, if any.</param>
    /// <returns>The text.</returns>
    private static string Problem(LookupStatus status, string? problem) => problem ?? status switch
    {
        LookupStatus.Offline => "Offline: ingen utgång är ansluten och ärendet har inte uppgiften.",
        LookupStatus.NotInCase => "Ärendet har inte uppgiften; expandera för att hämta den.",
        LookupStatus.NotFound => "Källan känner inte till den.",
        _ => "Uppslaget misslyckades.",
    };

    /// <summary>What one expansion step brought in.</summary>
    /// <param name="Transactions">The transactions.</param>
    /// <param name="Problem">What went wrong, or <see langword="null"/> when it is complete.</param>
    /// <param name="Answered">
    /// Whether the source answered at all; an address with no history is answered with no transactions.
    /// </param>
    private sealed record StepResult(IReadOnlyList<Txid> Transactions, string? Problem, bool Answered);
}
