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
            // One coin continues in one place; another link is made after the first is taken back, so that the
            // coin's amount is never on two lines at once.
            if (GraphStateStore.Read(transaction).Links.Any(link => link.From == from))
            {
                throw new ArgumentException(
                    "The coin already continues on another chain; take that link back first.", nameof(from));
            }

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
            // The stored link is what is taken back, whatever the caller's copy says.
            var stored = GraphStateStore.Read(transaction).Links
                .FirstOrDefault(current => current.Sequence == link.Sequence);
            if (stored is null)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.CrossChainUnlinked,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Länken till annan kedja togs tillbaka: {0} → {1}",
                    stored.From,
                    stored.To),
                JsonSerializer.Serialize(new { link = stored.Sequence, reason })));
            GraphStateStore.SealUnlink(transaction, stored, reason, journal);
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

    /// <inheritdoc/>
    public Task<TraceResult> TraceAsync(
        TraceGraph graph, TraceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);
        if (!_methods.TryGetValue(request.Method, out var method))
        {
            throw new ArgumentException("The tracing method is not registered.", nameof(request));
        }

        return Task.Run(() => TraceCalculator.Trace(graph, method, request.Seeds, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public GraphViewState GetView(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(GraphStateStore.ReadView) ?? GraphViewState.Default;
    }

    /// <inheritdoc/>
    public bool SaveView(ICase @case, GraphViewState view)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(view);
        if (!Enum.IsDefined(view.Method) || !Enum.IsDefined(view.Layout) || !Enum.IsDefined(view.AmountLabels)
            || (view.Viewport is { } viewport && !GraphStateStore.IsFinite(viewport)))
        {
            throw new ArgumentException("A view is known values and a finite part on screen.", nameof(view));
        }

        var utc = _clock.UtcNow;
        return @case.Write(transaction =>
        {
            var before = (GraphStateStore.ReadView(transaction) ?? GraphViewState.Default).Method;
            var changed = before != view.Method;
            if (changed)
            {
                transaction.AppendJournal(new JournalDraft(
                    TracingJournalKinds.MethodChanged,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Spårningsmetod: {0} i stället för {1}",
                        MethodName(view.Method),
                        MethodName(before)),
                    JsonSerializer.Serialize(new { method = MethodName(view.Method), before = MethodName(before) })));
            }

            GraphStateStore.SaveView(transaction, view, utc);
            return changed;
        });
    }

    /// <summary>Names a method with the version that counts by it, for example <c>FIFO v1</c>.</summary>
    /// <param name="kind">The method.</param>
    /// <returns>The name.</returns>
    private string MethodName(TracingMethodKind kind) =>
        _methods.TryGetValue(kind, out var method) ? method.Id.ToString() : new TracingMethodId(kind, 1).Name;

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
            : await ExpandTransactionAsync(@case, request.Txid!, request.MaximumTransactions, cancellationToken)
                .ConfigureAwait(false);
        if (!step.Answered)
        {
            var unchanged = await LoadAsync(@case, cancellationToken).ConfigureAwait(false);
            return new ExpansionResult(ExpansionOutcome.NotExpanded, [], step.Problem, unchanged);
        }

        var expansion = new Expansion(
            request.Address, request.Txid, step.Transactions, step.Problem is null, _clock.UtcNow);
        @case.Write(transaction => GraphStateStore.SaveExpansion(transaction, expansion));
        // The expanded transaction's own spends were asked for in the step.
        var unasked = step.Transactions.Where(txid => txid != request.Txid).ToList();
        await FetchSpendsAsync(@case, unasked, cancellationToken).ConfigureAwait(false);
        var data = await ReadDataAsync(@case, cancellationToken).ConfigureAwait(false);
        if (data.Transactions.Count > 0)
        {
            _clustering.Analyse(@case, [.. data.Transactions.Values.Select(entry => entry.Transaction)]);
        }

        return new ExpansionResult(
            expansion.IsComplete ? ExpansionOutcome.Expanded : ExpansionOutcome.Partly,
            [.. step.Transactions.Where(txid => !before.Contains(txid))],
            step.Problem,
            Build(@case, data))
        {
            HeldBack = step.HeldBack,
        };
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
        string? outdated = null;
        Txid? after = null;
        while (true)
        {
            var page = await _source.GetAddressTransactionsAsync(@case, address, after, cancellationToken)
                .ConfigureAwait(false);
            if (page.Value is null)
            {
                return new StepResult(found, Problem(page.Status, page.Problem), found.Count > 0);
            }

            if (page.Status == LookupStatus.FromCaseMayBeOutdated)
            {
                outdated ??= Note(page);
            }

            var countBefore = found.Count;
            foreach (var transaction in page.Value.Transactions.Where(tx => !found.Contains(tx.Txid)))
            {
                if (found.Count == maximum)
                {
                    return await HeldBackAtAddressAsync(@case, address, found, outdated, cancellationToken)
                        .ConfigureAwait(false);
                }

                found.Add(transaction.Txid);
            }

            if (page.Value.NextAfter is null)
            {
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

    /// <summary>
    /// Says that an address's history was held back at the limit, with how many transactions it has in all when the
    /// source's summary of the address can say (principle 7: honestly, not silently).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="found">The transactions brought in, as many as the limit.</param>
    /// <param name="outdated">Why a page came from the case, if one did.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The step's result.</returns>
    private async Task<StepResult> HeldBackAtAddressAsync(
        ICase @case, BitcoinAddress address, List<Txid> found, string? outdated, CancellationToken cancellationToken)
    {
        var summary = await _source.GetAddressSummaryAsync(@case, address, cancellationToken).ConfigureAwait(false);
        var fresh = summary is { Status: LookupStatus.Fetched, Value: { } value } ? value : null;
        long? total = fresh?.TransactionCount > found.Count ? fresh.TransactionCount : null;
        var text = total is { } all
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Adressen har {0} tx; de nyaste {1} togs med och resten hölls tillbaka.",
                all,
                found.Count)
            : string.Format(
                CultureInfo.InvariantCulture,
                "Adressen har fler än {0} tx; de nyaste {0} togs med och resten hölls tillbaka.",
                found.Count);
        return new StepResult(found, Join(text, outdated), true, new ExpansionHoldBack(found.Count, total));
    }

    /// <summary>
    /// Brings in a transaction, checked against its own bytes, and the transactions that spend its outputs, up to the
    /// limit: a batch payout's spenders are taken by how much they receive, the most first, and the rest is held back
    /// and said (principle 7).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction.</param>
    /// <param name="maximum">The most spending transactions.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The transactions and what went wrong.</returns>
    private async Task<StepResult> ExpandTransactionAsync(
        ICase @case, Txid txid, int maximum, CancellationToken cancellationToken)
    {
        var lookup = await _source.VerifyTransactionAsync(@case, txid, cancellationToken).ConfigureAwait(false);
        if (lookup.Value is null)
        {
            return new StepResult([], Problem(lookup.Status, lookup.Problem), false);
        }

        var found = new List<Txid> { txid };
        var problem = Note(lookup);
        var spends = await _source.GetOutputSpendsAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (spends.Value is null)
        {
            return new StepResult(found, Join(problem, Problem(spends.Status, spends.Problem)), true);
        }

        problem = Join(problem, Note(spends));
        var spenders = SpendersByAmount(lookup.Value, spends.Value);
        ExpansionHoldBack? heldBack = null;
        if (spenders.Count > maximum)
        {
            heldBack = new ExpansionHoldBack(maximum, spenders.Count);
            problem = Join(problem, string.Format(
                CultureInfo.InvariantCulture,
                "Tx:ens utgångar spenderas i {0} tx; de {1} som tar emot mest togs med och resten hölls tillbaka.",
                spenders.Count,
                maximum));
        }

        foreach (var spender in spenders.Take(maximum))
        {
            var next = await _source.VerifyTransactionAsync(@case, spender, cancellationToken).ConfigureAwait(false);
            if (next.Value is null)
            {
                problem = Join(problem, Problem(next.Status, next.Problem));
                continue;
            }

            problem = Join(problem, Note(next));
            found.Add(spender);
        }

        return new StepResult(found, problem, true, heldBack);
    }

    /// <summary>
    /// Orders the transactions that spend a transaction's outputs by how much of it each receives, the most first,
    /// and then by txid, so that a limit keeps where most of the money went.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="spends">Who spends each output.</param>
    /// <returns>The spending transactions, each once.</returns>
    private static List<Txid> SpendersByAmount(
        BitcoinTransaction transaction, IReadOnlyList<BitcoinOutputSpend> spends) =>
    [
        .. spends
            .Where(spend => spend.SpentBy is not null)
            .GroupBy(spend => spend.SpentBy!.Txid)
            .Select(group => (Txid: group.Key, Amount: group.Sum(spend =>
                spend.Index >= 0 && spend.Index < transaction.Outputs.Count
                    ? transaction.Outputs[spend.Index].Amount.Value
                    : 0)))
            .OrderByDescending(spender => spender.Amount)
            .ThenBy(spender => spender.Txid.Value, StringComparer.Ordinal)
            .Select(spender => spender.Txid),
    ];

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
    /// <remarks>
    /// The clusters and the ends at the graph's addresses are read once per build (each read goes through the whole
    /// case's conclusions and labels), not once per address; an address the builder asks about outside the graph is
    /// still answered, one at a time.
    /// </remarks>
    private TraceGraph Build(ICase @case, GraphData data)
    {
        var addresses = GraphAddresses(data);
        var clusters = _clustering.GetClusters(@case, addresses);
        var ends = AddressEnds(@case, addresses);
        return GraphBuilder.Build(data, new GraphKnowledge(
            address => clusters.TryGetValue(address, out var cluster)
                ? [.. cluster.Addresses]
                : [.. _clustering.GetCluster(@case, address).Addresses],
            address => addresses.Contains(address)
                ? ends.GetValueOrDefault(address) ?? []
                : [.. _classifiers.SelectMany(classifier => classifier.ClassifyAddress(@case, address))],
            transaction => [.. _classifiers.SelectMany(
                classifier => classifier.ClassifyTransaction(@case, transaction))]));
    }

    /// <summary>Gives every address the graph shows: the start addresses and those its transactions touch.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The addresses.</returns>
    private static HashSet<BitcoinAddress> GraphAddresses(GraphData data) =>
    [
        .. data.State.StartPoints.Select(start => start.Address).OfType<BitcoinAddress>(),
        .. data.Transactions.Values.SelectMany(entry => entry.Transaction.Inputs.Select(input => input.Address)
            .Concat(entry.Transaction.Outputs.Select(output => output.Address))).OfType<BitcoinAddress>(),
    ];

    /// <summary>Asks every classifier once about all the addresses, and joins their hints per address.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The hints of each address that has any, in the classifiers' order.</returns>
    private Dictionary<BitcoinAddress, IReadOnlyList<TrailEndHint>> AddressEnds(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        var ends = new Dictionary<BitcoinAddress, IReadOnlyList<TrailEndHint>>();
        foreach (var classifier in _classifiers)
        {
            foreach (var (address, hints) in classifier.ClassifyAddresses(@case, addresses))
            {
                ends[address] = ends.TryGetValue(address, out var earlier) ? [.. earlier, .. hints] : hints;
            }
        }

        return ends;
    }

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

    /// <summary>
    /// Gives what the user should know about an answer that has a value: that it came from the case and may be
    /// outdated, that the transaction has left its block, or that it could not be checked against its own bytes.
    /// </summary>
    /// <typeparam name="T">What was looked up.</typeparam>
    /// <param name="lookup">The answer.</param>
    /// <returns>The text, or <see langword="null"/> when there is nothing to say.</returns>
    private static string? Note<T>(BitcoinLookup<T> lookup)
        where T : class => lookup.Status == LookupStatus.FromCaseMayBeOutdated
        ? lookup.Problem ?? "Svaret kommer ur ärendet och kan ha ändrats."
        : lookup.Problem;

    /// <summary>Joins two texts for the user, either of which may be missing, each once.</summary>
    /// <param name="first">The first text.</param>
    /// <param name="second">The second text.</param>
    /// <returns>Both, one, or <see langword="null"/>.</returns>
    private static string? Join(string? first, string? second)
    {
        if (first is null || second is null || first.Contains(second, StringComparison.Ordinal))
        {
            return first ?? second;
        }

        return $"{first} {second}";
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
    /// <param name="HeldBack">What was held back at the limit, if anything.</param>
    private sealed record StepResult(
        IReadOnlyList<Txid> Transactions, string? Problem, bool Answered, ExpansionHoldBack? HeldBack = null);
}
