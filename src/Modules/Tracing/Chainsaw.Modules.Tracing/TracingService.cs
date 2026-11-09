using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Lookups;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The trace as a model: keeps the graph's state in the case, expands it through <see cref="IBitcoinSource"/>, builds
/// it with the clusters and the ends of the trail, and traces money through it by a registered method.
/// </summary>
internal sealed class TracingService : ITracingService
{
    /// <summary>
    /// How many lookups of where coins went are asked at the same time. The source's own pace per endpoint still
    /// decides when each request leaves (rate limits are always respected); asking a few at once only keeps one
    /// answer's way through the exit from holding up the next turn, and lets the lookups spread over the endpoints.
    /// </summary>
    internal const int SpendLookupsAtOnce = 4;

    /// <summary>Bitcoin's facts.</summary>
    private readonly IBitcoinSource _source;

    /// <summary>The clusters and the analyses.</summary>
    private readonly IClusteringService _clustering;

    /// <summary>What recognises the ends of the trail, in registration order.</summary>
    private readonly IReadOnlyList<ITrailEndClassifier> _classifiers;

    /// <summary>The newest version of each tracing method for Bitcoin's coins.</summary>
    private readonly IReadOnlyDictionary<TracingMethodKind, IUtxoTracingMethod> _methods;

    /// <summary>The newest version of each tracing method for Ethereum's accounts.</summary>
    private readonly IReadOnlyDictionary<TracingMethodKind, IAccountTracingMethod> _accountMethods;

    /// <summary>Why Ethereum has no tracing methods, when they are registered wrongly; otherwise null.</summary>
    private readonly string? _accountProblem;

    /// <summary>Ethereum's facts, read for the graph.</summary>
    private readonly EthereumReader _ethereum;

    /// <summary>TRON's facts, read for the graph, or <see langword="null"/> when no TRON source is registered.</summary>
    private readonly EthereumReader? _tron;

    /// <summary>BSC's facts, read for the graph, or <see langword="null"/> when no BSC source is registered.</summary>
    private readonly EthereumReader? _bsc;

    /// <summary>
    /// Solana's facts, read for the graph, or <see langword="null"/> when no Solana source is registered.
    /// </summary>
    private readonly EthereumReader? _solana;

    /// <summary>The clock.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="source">Bitcoin's facts.</param>
    /// <param name="ethereum">Ethereum's facts.</param>
    /// <param name="clustering">The clusters and the analyses.</param>
    /// <param name="classifiers">What recognises the ends of the trail.</param>
    /// <param name="methods">
    /// The tracing methods, for every model and chain; Bitcoin's are the UTXO methods that cover it.
    /// </param>
    /// <param name="clock">The clock.</param>
    /// <param name="tron">
    /// TRON's facts, or <see langword="null"/> where TRON is not registered (an expansion on TRON is then refused).
    /// </param>
    /// <param name="bsc">
    /// BSC's facts, or <see langword="null"/> where BSC is not registered (an expansion on BSC is then refused).
    /// </param>
    /// <param name="solana">
    /// Solana's facts, or <see langword="null"/> where Solana is not registered (an expansion on Solana is then
    /// refused).
    /// </param>
    /// <exception cref="ArgumentException">
    /// The methods are not registered properly (see <see cref="TracingMethods"/>).
    /// </exception>
    public TracingService(
        IBitcoinSource source,
        IEthereumSource ethereum,
        IClusteringService clustering,
        IEnumerable<ITrailEndClassifier> classifiers,
        IEnumerable<ITracingMethod> methods,
        IClock clock,
        ITronSource? tron = null,
        IBscSource? bsc = null,
        ISolanaSource? solana = null)
    {
        _source = source;
        _clustering = clustering;
        _classifiers = [.. classifiers];
        var all = methods.ToList();
        _methods = TracingMethods.For<IUtxoTracingMethod>(all, Chain.Bitcoin);
        _accountMethods = AccountMethodsFor(all, out _accountProblem);
        _ethereum = new EthereumReader(ethereum);
        _tron = tron is null ? null : new EthereumReader(tron);
        _bsc = bsc is null ? null : new EthereumReader(bsc);
        _solana = solana is null ? null : new EthereumReader(solana);
        _clock = clock;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TracingMethodId> Methods =>
        [.. _methods.Values.Select(method => method.Id).OrderBy(id => id.Kind)];

    /// <inheritdoc/>
    public IReadOnlyList<TracingMethodId> AccountMethods =>
        [.. _accountMethods.Values.Select(method => method.Id).OrderBy(id => id.Kind)];

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
        var subjects = new object?[]
        {
            request.Address, request.Txid, request.EthereumAddress, request.EthereumTransaction,
        }.Count(subject => subject is not null);
        if (subjects != 1)
        {
            throw new ArgumentException("Expand either an address or a transaction.", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumTransactions, 1, nameof(request));
        return request.EthereumAddress is null && request.EthereumTransaction is null
            ? Task.Run(() => ExpandCoreAsync(@case, request, cancellationToken), cancellationToken)
            : Task.Run(() => ExpandEthereumAsync(@case, request, cancellationToken), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<HeldBackList> ListHeldBackAsync(
        ICase @case,
        BitcoinAddress address,
        DateTimeOffset? around = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        return Task.Run(
            () => new HeldBackHistory(_source).ReadAsync(@case, address, around, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ExpansionResult> TakeHeldBackAsync(
        ICase @case,
        BitcoinAddress address,
        IReadOnlyCollection<Txid> transactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(transactions);
        if (transactions.Count == 0)
        {
            throw new ArgumentException("Pick at least one transaction.", nameof(transactions));
        }

        return Task.Run(
            () => TakeHeldBackCoreAsync(@case, address, transactions, cancellationToken), cancellationToken);
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

        return @case.Write(transaction => CrossChainLinks.SealCoin(transaction, from, to, reason, null));
    }

    /// <inheritdoc/>
    public CrossChainLink LinkCrossChain(ICase @case, AccountSeed from, ChainTransactionRef to, string reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        CheckReason(reason);
        if (to.Chain == from.Transaction.Chain)
        {
            throw new ArgumentException("A link to another chain leads off the movement's chain.", nameof(to));
        }

        return @case.Write(transaction => CrossChainLinks.SealMovement(transaction, from, to, reason, null));
    }

    /// <inheritdoc/>
    public bool UnlinkCrossChain(ICase @case, CrossChainLink link, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(link);
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is not null)
        {
            CheckReason(reason);
        }

        return @case.Write(transaction =>
        {
            // The stored link is what is taken back, whatever the caller's copy says.
            var stored = GraphStateStore.Read(transaction).Links
                .FirstOrDefault(current => current.Sequence == link.Sequence);
            if (stored is null)
            {
                return false;
            }

            if (stored.Origin is not null)
            {
                // Taken back alone, the approval would stand without its link; the checks take both back together.
                throw new ArgumentException(
                    "A link an approved suggestion made is taken back with its approval, in the checks.",
                    nameof(link));
            }

            CrossChainLinks.Unlink(transaction, stored, reason);
            return true;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<CrossChainLinkHistoryEntry> GetCrossChainHistory(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(GraphStateStore.ReadLinkHistory);
    }

    /// <inheritdoc/>
    public SwapStop StopAtSwap(ICase @case, TokenSwap swap, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(swap);
        reason = OptionalReason(reason);
        // Only a swap the case's own facts on the swap's chain (Ethereum or BSC) show, for that account in that
        // transaction, is chosen for, so that no choice is sealed about something the case does not hold. Only that
        // chain's part is read, not the graph. The labels are asked as when the graph is built, so that a swap a label
        // refuses is not chosen for either.
        if (!swap.Account.Chain.IsEvm())
        {
            throw new ArgumentException("The swap is not in the case's graph.", nameof(swap));
        }

        var state = @case.Read(GraphStateStore.Read);
        var facts = ReaderFor(swap.Account.Chain).ReadAsync(@case, state, CancellationToken.None)
            .GetAwaiter().GetResult();
        var ends = AccountEnds(@case, new GraphData(
            state, new Dictionary<Txid, GraphTransaction>(), new Dictionary<Txid, OutputSpendsAnswer>(), [])
        {
            Account = facts,
        });
        var swaps = facts.ReadSwaps(address => GraphBuilder.SwapEndpoint(
            account => ends.GetValueOrDefault(account) ?? [], address)).Swaps;
        if (!swaps.Contains(swap))
        {
            throw new ArgumentException("The swap is not in the case's graph.", nameof(swap));
        }

        return @case.Write(transaction =>
        {
            // One choice in force per swap; choosing again leaves the one there, so that a double click seals once.
            if (SwapValue.StopOf(swap, SwapStopStore.ReadInForce(transaction)) is { } already)
            {
                return already;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.SwapStopped,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Spåret följer inte med genom swappen: {0} ({1} → {2})",
                    swap.Transaction.Value,
                    swap.Sold.Asset.Symbol,
                    swap.Bought.Asset.Symbol),
                JsonSerializer.Serialize(new
                {
                    transaction = swap.Transaction.Value,
                    account = swap.Account.Value,
                    sold = swap.Sold.Asset.Symbol,
                    bought = swap.Bought.Asset.Symbol,
                    analysis = SwapValue.Analysis,
                    reason,
                })));
            return SwapStopStore.Seal(transaction, swap, reason, journal);
        });
    }

    /// <inheritdoc/>
    public bool FollowSwap(ICase @case, SwapStop stop, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(stop);
        reason = OptionalReason(reason);
        return @case.Write(transaction =>
        {
            // The stored choice is what is taken back, whatever the caller's copy says.
            var stored = SwapStopStore.ReadInForce(transaction)
                .FirstOrDefault(current => current.Sequence == stop.Sequence);
            if (stored is null)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.SwapFollowed,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Värdet följer med genom swappen igen: {0}",
                    stored.Transaction.Value),
                JsonSerializer.Serialize(new { stop = stored.Sequence, analysis = SwapValue.Analysis, reason })));
            SwapStopStore.SealUndo(transaction, stored, reason, journal);
            return true;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<SwapStopHistoryEntry> GetSwapStopHistory(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(SwapStopStore.ReadHistory);
    }

    /// <inheritdoc/>
    public async Task<ReplacementFollow> FollowReplacementAsync(
        ICase @case, Txid gone, Txid replacer, string? reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(gone);
        ArgumentNullException.ThrowIfNull(replacer);
        reason = OptionalReason(reason);

        // Only a transaction the case's sealed answers show gone, and a replacer they show spending its coins, is
        // chosen for, so that no choice is sealed about something the case does not hold.
        var changes = await _source.GetBlockChangesAsync(@case, cancellationToken).ConfigureAwait(false);
        if (!changes.Any(change => change is { Chain: Chain.Bitcoin, Kind: BlockChangeKind.Gone }
            && Txid.TryParse(change.TransactionId, out var txid) && txid == gone))
        {
            throw new ArgumentException(
                "The source still knows the transaction, as far as the case says.", nameof(gone));
        }

        var replacement = await _source.GetReplacementAsync(@case, gone, LookupMode.CaseOnly, cancellationToken)
            .ConfigureAwait(false);
        if (replacement.Value is not { Kind: not ReplacementKind.Conflicting } known
            || !known.Replacers.Contains(replacer))
        {
            throw new ArgumentException(
                "The case does not show the transaction spending the coins, or its answers do not agree.",
                nameof(replacer));
        }

        // The replacer comes into the graph like an expanded transaction: fetched through the exit and sealed, with
        // where its outputs went, so that the trace can go on through it.
        var fetched = await _source.GetTransactionAsync(@case, replacer, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (fetched.Value is null)
        {
            throw fetched.Problem is { } problem
                ? new ReplacementUnavailableException(problem)
                : new ReplacementUnavailableException();
        }

        var goneTransaction = await _source.GetTransactionAsync(@case, gone, LookupMode.CaseOnly, cancellationToken)
            .ConfigureAwait(false);
        if (goneTransaction.Value is not { } read || !Agrees(read, known, fetched.Value))
        {
            throw new ArgumentException(
                "The replacer's inputs do not spend the gone transaction's coins as the source said.", nameof(replacer));
        }

        await FetchSpendsAsync(@case, [replacer], cancellationToken).ConfigureAwait(false);
        return @case.Write(transaction =>
        {
            // One choice in force per replacer; choosing again leaves the one there, so that a double click seals once.
            if (ReplacementFollowStore.ReadInForce(transaction)
                .FirstOrDefault(follow => follow.Gone == gone && follow.Replacer == replacer) is { } already)
            {
                return already;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.ReplacementFollowed,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Spåret följer tx:en som ersatte en som inte finns längre: {0} i stället för {1}",
                    replacer.Value,
                    gone.Value),
                JsonSerializer.Serialize(new
                {
                    gone = gone.Value, replacer = replacer.Value, analysis = GoneTransaction.Source, reason,
                })));
            return ReplacementFollowStore.Seal(transaction, gone, replacer, reason, journal);
        });
    }

    /// <summary>
    /// Says whether a replacer's own inputs spend the gone transaction's coins as the answers about the coins say: the
    /// same coin, with the same amount, at the input named (<see cref="SpendRules.IsConflicting"/>).
    /// </summary>
    /// <param name="gone">The gone transaction.</param>
    /// <param name="replacement">What the case knows spends its coins.</param>
    /// <param name="replacer">The replacer as fetched.</param>
    /// <returns><see langword="true"/> when every coin it is said to spend agrees.</returns>
    private static bool Agrees(BitcoinTransaction gone, TransactionReplacement replacement, BitcoinTransaction replacer)
    {
        var coins = replacement.Coins.Where(coin => coin.SpentBy?.Txid == replacer.Txid).ToList();
        return coins.Count > 0 && coins.All(coin =>
        {
            var input = gone.Inputs[coin.InputIndex];
            var output = new BitcoinOutput(
                coin.PreviousIndex, input.ScriptType ?? BitcoinScriptType.NonStandard, input.Address,
                input.Amount ?? Satoshi.Zero, string.Empty, null);
            return !SpendRules.IsConflicting(
                replacer, coin.SpentBy!.InputIndex, new Outpoint(coin.PreviousTxid, coin.PreviousIndex), output);
        });
    }

    /// <inheritdoc/>
    public bool TakeBackReplacement(ICase @case, ReplacementFollow follow, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(follow);
        reason = OptionalReason(reason);
        return @case.Write(transaction =>
        {
            // The stored choice is what is taken back, whatever the caller's copy says.
            var stored = ReplacementFollowStore.ReadInForce(transaction)
                .FirstOrDefault(current => current.Sequence == follow.Sequence);
            if (stored is null)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.ReplacementTakenBack,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Spåret följer inte längre tx:en som ersatte {0}; det slutar vid den igen",
                    stored.Gone.Value),
                JsonSerializer.Serialize(new { follow = stored.Sequence, replacer = stored.Replacer.Value, reason })));
            ReplacementFollowStore.SealTakeBack(transaction, stored, reason, journal);
            return true;
        });
    }

    /// <summary>Trims an optional reason and checks its length; an empty one is none.</summary>
    /// <param name="reason">The reason, or <see langword="null"/>.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">It is too long.</exception>
    private static string? OptionalReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var trimmed = reason.Trim();
        CheckReason(trimmed);
        return trimmed;
    }

    /// <inheritdoc/>
    public void SavePosition(ICase @case, string nodeKey, GraphLayout layout, NodePosition position)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentException.ThrowIfNullOrEmpty(nodeKey);
        SavePositions(@case, layout, new Dictionary<string, NodePosition>(StringComparer.Ordinal)
        {
            [nodeKey] = position,
        });
    }

    /// <inheritdoc/>
    public void SavePositions(ICase @case, GraphLayout layout, IReadOnlyDictionary<string, NodePosition> positions)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(positions);
        if (!Enum.IsDefined(layout) || positions.Any(pair => pair.Key.Length == 0 || !IsFinite(pair.Value)))
        {
            throw new ArgumentException("A place is a node, a known layout and finite coordinates.", nameof(positions));
        }

        if (positions.Count == 0)
        {
            return;
        }

        var utc = _clock.UtcNow;
        @case.Write(transaction =>
        {
            foreach (var (nodeKey, position) in positions)
            {
                GraphStateStore.SavePosition(transaction, NodeKeys.Position(nodeKey), layout, position, utc);
            }

            return true;
        });
    }

    /// <summary>Reads whether a place has finite coordinates.</summary>
    /// <param name="position">The place.</param>
    /// <returns><see langword="true"/> when both are finite.</returns>
    private static bool IsFinite(NodePosition position) => double.IsFinite(position.X) && double.IsFinite(position.Y);

    /// <inheritdoc/>
    public void PickFromPile(ICase @case, IReadOnlyCollection<string> nodeKeys)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var keys = MemberKeys(nodeKeys);
        if (keys.Count == 0)
        {
            return;
        }

        var utc = _clock.UtcNow;
        @case.Write(transaction =>
        {
            GraphStateStore.SavePicked(transaction, keys, utc);
            return true;
        });
    }

    /// <inheritdoc/>
    public void ReturnToPile(ICase @case, IReadOnlyCollection<string> nodeKeys)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var keys = MemberKeys(nodeKeys);
        if (keys.Count == 0)
        {
            return;
        }

        @case.Write(transaction =>
        {
            GraphStateStore.DeletePicked(transaction, keys);
            return true;
        });
    }

    /// <summary>
    /// Gives the keys the case remembers picked actors by: a cluster under its representative address, as its place,
    /// and read back from any of its addresses (<see cref="PileRules"/>).
    /// </summary>
    /// <param name="nodeKeys">The actors' node keys.</param>
    /// <returns>The member keys, each once.</returns>
    /// <exception cref="ArgumentException">A key is empty.</exception>
    private static List<string> MemberKeys(IReadOnlyCollection<string> nodeKeys)
    {
        ArgumentNullException.ThrowIfNull(nodeKeys);
        if (nodeKeys.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("A node's key is never empty.", nameof(nodeKeys));
        }

        return [.. nodeKeys.Select(NodeKeys.Position).Distinct(StringComparer.Ordinal)];
    }

    /// <inheritdoc/>
    public TraceResult Trace(TraceGraph graph, TraceRequest request) =>
        TraceCalculator.Trace(graph, MethodFor(graph, request), request.Seeds);

    /// <inheritdoc/>
    public Task<TraceResult> TraceAsync(
        TraceGraph graph, TraceRequest request, CancellationToken cancellationToken = default)
    {
        var method = MethodFor(graph, request);
        return Task.Run(
            () => TraceCalculator.Trace(graph, method, request.Seeds, cancellationToken), cancellationToken);
    }

    /// <summary>Checks a UTXO trace's arguments and gives its method.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="request">The request.</param>
    /// <returns>The registered method the request names.</returns>
    /// <exception cref="ArgumentException">The method is not registered.</exception>
    private IUtxoTracingMethod MethodFor(TraceGraph graph, TraceRequest request)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);
        return _methods.TryGetValue(request.Method, out var method)
            ? method
            : throw new ArgumentException("The tracing method is not registered.", nameof(request));
    }

    /// <inheritdoc/>
    public Task<AccountTraceResult> TraceAccountsAsync(
        TraceGraph graph, AccountTraceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);
        if (!_accountMethods.TryGetValue(request.Method, out var method))
        {
            throw new ArgumentException(
                _accountProblem ?? "The tracing method is not registered for the account chains.", nameof(request));
        }

        return Task.Run(
            () => AccountTraceCalculator.Trace(graph, method, request.Seeds, cancellationToken, request.Pricing),
            cancellationToken);
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

    /// <summary>
    /// Gives Ethereum's tracing methods. A method registered wrongly for Ethereum never stops Bitcoin (as for the
    /// heuristics): Ethereum then has none, and a trace there is refused with why.
    /// </summary>
    /// <param name="methods">Every registered method.</param>
    /// <param name="problem">Why there are none, or <see langword="null"/>.</param>
    /// <returns>The newest version of each method for Ethereum, by kind.</returns>
    private static IReadOnlyDictionary<TracingMethodKind, IAccountTracingMethod> AccountMethodsFor(
        IReadOnlyList<ITracingMethod> methods, out string? problem)
    {
        try
        {
            problem = null;
            return TracingMethods.For<IAccountTracingMethod>(methods, Chain.Ethereum);
        }
        catch (ArgumentException exception)
        {
            problem = exception.Message;
            return new Dictionary<TracingMethodKind, IAccountTracingMethod>();
        }
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
            request.Address, request.Txid, step.Transactions, step.Problem is null, _clock.UtcNow)
        {
            HeldBack = step.HeldBack,
        };
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
            Join(step.Problem, step.Notes),
            Build(@case, data))
        {
            HeldBack = step.HeldBack,
        };
    }

    /// <summary>
    /// Expands an Ethereum address's history or an Ethereum transaction, records what came in and builds the graph.
    /// The clustering analyses are Bitcoin's and do not run.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="request">What to expand.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What came in and the graph.</returns>
    private async Task<ExpansionResult> ExpandEthereumAsync(
        ICase @case, ExpansionRequest request, CancellationToken cancellationToken)
    {
        var state = @case.Read(GraphStateStore.Read);
        var before = state.EthereumTransactions
            .Concat(state.EthereumHistories.SelectMany(expansion => expansion.EthereumTransactions)).ToHashSet();
        var reader = ReaderFor(request.EthereumAddress?.Chain ?? request.EthereumTransaction!.Chain);
        var step = request.EthereumAddress is { } address
            ? await reader.ExpandAddressAsync(
                    @case,
                    address,
                    request.MaximumTransactions,
                    await LowestEarlierBlockAsync(@case, state, reader, address, cancellationToken)
                        .ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false)
            : await reader.ExpandTransactionAsync(@case, request.EthereumTransaction!, cancellationToken)
                .ConfigureAwait(false);
        if (!step.Answered)
        {
            var unchanged = await LoadAsync(@case, cancellationToken).ConfigureAwait(false);
            return new ExpansionResult(ExpansionOutcome.NotExpanded, [], step.Problem, unchanged);
        }

        var expansion = new Expansion(null, null, [], step.Problem is null, _clock.UtcNow)
        {
            EthereumAddress = request.EthereumAddress,
            EthereumTransaction = request.EthereumTransaction,
            EthereumTransactions = step.Transactions,
            HeldBack = step.HeldBack,
        };
        @case.Write(transaction => GraphStateStore.SaveExpansion(transaction, expansion));
        var data = await ReadDataAsync(@case, cancellationToken).ConfigureAwait(false);
        return new ExpansionResult(
            expansion.IsComplete ? ExpansionOutcome.Expanded : ExpansionOutcome.Partly,
            [],
            Join(step.Problem, step.Notes),
            Build(@case, data))
        {
            HeldBack = step.HeldBack,
            NewEthereumTransactions = [.. step.Transactions.Where(hash => !before.Contains(hash))],
        };
    }

    /// <summary>Gives the reader of an account chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="ArgumentException">No source for the chain is registered.</exception>
    private EthereumReader ReaderFor(Chain chain) => chain switch
    {
        Chain.Ethereum => _ethereum,
        Chain.Tron when _tron is not null => _tron,
        Chain.Bsc when _bsc is not null => _bsc,
        Chain.Solana when _solana is not null => _solana,
        _ => throw new ArgumentException($"No source for {chain} is registered.", nameof(chain)),
    };

    /// <summary>
    /// Gives where the oldest page of what earlier expansions of an account brought in starts, read from the case, in
    /// the source's unit (a block on Ethereum, a block's time on TRON), so that expanding it again reads every history
    /// down past it.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="state">The graph's state.</param>
    /// <param name="reader">The account's chain.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The start, or <see langword="null"/> when the address was not expanded before.</returns>
    private static async Task<long?> LowestEarlierBlockAsync(
        ICase @case, GraphState state, EthereumReader reader, EthereumAddress address,
        CancellationToken cancellationToken)
    {
        var earlier = state.EthereumHistories.Where(expansion => expansion.EthereumAddress == address)
            .SelectMany(expansion => expansion.EthereumTransactions).ToHashSet();
        if (earlier.Count == 0)
        {
            return null;
        }

        var data = await reader.ReadAsync(@case, state, cancellationToken).ConfigureAwait(false);
        var heights = data.Transfers.Where(transfer => earlier.Contains(transfer.Transaction))
            .Select(transfer => transfer.Block)
            .Concat(data.Fees.Where(fee => earlier.Contains(fee.Transaction)).Select(fee => fee.Block))
            .OfType<EthereumBlockReference>()
            .Select(reader.PageStartOf)
            .ToList();
        return heights.Count == 0 ? null : heights.Min();
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
                outdated ??= Outdated(page);
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
    /// Brings chosen held-back transactions into the address's expansion: each is read (from the case when it is
    /// there), one that does not touch the address is left out, and what the expansion held back stays, so that its
    /// clue counts what is left.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="transactions">The chosen transactions.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What came in and the graph.</returns>
    private async Task<ExpansionResult> TakeHeldBackCoreAsync(
        ICase @case,
        BitcoinAddress address,
        IReadOnlyCollection<Txid> transactions,
        CancellationToken cancellationToken)
    {
        var state = @case.Read(GraphStateStore.Read);
        var before = state.Transactions.ToHashSet();
        var earlier = state.Expansions.FirstOrDefault(expansion => expansion.Address == address);
        var taken = new List<Txid>();
        string? problem = null;
        foreach (var txid in transactions.Distinct())
        {
            var lookup = await _source.GetTransactionAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
                .ConfigureAwait(false);
            if (lookup.Value is null)
            {
                problem = Join(problem, Problem(lookup.Status, lookup.Problem));
                continue;
            }

            if (HeldBackReader.Read(lookup.Value, address) is not null)
            {
                taken.Add(txid);
            }
        }

        if (taken.Count == 0)
        {
            var unchanged = await LoadAsync(@case, cancellationToken).ConfigureAwait(false);
            return new ExpansionResult(ExpansionOutcome.NotExpanded, [], problem, unchanged);
        }

        // Picked without an earlier expansion, the address's history was not brought in whole.
        var expansion = new Expansion(address, null, taken, earlier?.IsComplete ?? false, _clock.UtcNow)
        {
            Chosen = taken,
        };
        @case.Write(transaction => GraphStateStore.SaveExpansion(transaction, expansion));
        await FetchSpendsAsync(@case, taken, cancellationToken).ConfigureAwait(false);
        var data = await ReadDataAsync(@case, cancellationToken).ConfigureAwait(false);
        _clustering.Analyse(@case, [.. data.Transactions.Values.Select(entry => entry.Transaction)]);
        return new ExpansionResult(
            problem is null ? ExpansionOutcome.Expanded : ExpansionOutcome.Partly,
            [.. taken.Where(txid => !before.Contains(txid))],
            problem,
            Build(@case, data));
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
        var problem = Outdated(lookup);
        var notes = WithNotes(null, lookup);
        var spends = await _source.GetOutputSpendsAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (spends.Value is null)
        {
            return new StepResult(found, Join(problem, Problem(spends.Status, spends.Problem)), true)
            {
                Notes = notes,
            };
        }

        problem = Join(problem, Outdated(spends));
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

            problem = Join(problem, Outdated(next));
            notes = WithNotes(notes, next);
            found.Add(spender);
        }

        return new StepResult(found, problem, true, heldBack) { Notes = notes };
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
    /// are spent and which are still there. A failure here is not an error: those coins stay not expanded yet. A few
    /// are asked at once (<see cref="SpendLookupsAtOnce"/>), each still in its endpoint's turn.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="transactions">The transactions.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when every answer is in the case.</returns>
    private Task FetchSpendsAsync(
        ICase @case, IReadOnlyList<Txid> transactions, CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(
            transactions,
            new ParallelOptions { MaxDegreeOfParallelism = SpendLookupsAtOnce, CancellationToken = cancellationToken },
            async (txid, token) => await _source.GetOutputSpendsAsync(@case, txid, LookupMode.AsNeeded, token)
                .ConfigureAwait(false));

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

        var account = state.EthereumTransactions.Count > 0 || state.EthereumHistories.Count > 0
            ? await ReadAccountsAsync(@case, state, cancellationToken).ConfigureAwait(false)
            : AccountData.Empty;
        var gone = await GoneAsync(@case, transactions, cancellationToken).ConfigureAwait(false);
        return new GraphData(state, transactions, spends, problems) { Account = account, Gone = gone };
    }

    /// <summary>
    /// Reads which of the graph's Bitcoin transactions the source no longer knows (sketch GR9a), and what the case
    /// knows spends their coins now, from the case only.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="transactions">The graph's transactions.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The gone transactions, by txid.</returns>
    private async Task<Dictionary<Txid, GoneTransaction>> GoneAsync(
        ICase @case, IReadOnlyDictionary<Txid, GraphTransaction> transactions, CancellationToken cancellationToken)
    {
        var gone = new Dictionary<Txid, GoneTransaction>();
        var changes = await _source.GetBlockChangesAsync(@case, cancellationToken).ConfigureAwait(false);
        foreach (var change in changes.Where(change => change is { Chain: Chain.Bitcoin, Kind: BlockChangeKind.Gone }))
        {
            if (!Txid.TryParse(change.TransactionId, out var txid) || !transactions.ContainsKey(txid))
            {
                continue;
            }

            var replacement = await _source.GetReplacementAsync(@case, txid, LookupMode.CaseOnly, cancellationToken)
                .ConfigureAwait(false);
            gone[txid] = new GoneTransaction(txid, change, replacement.Value);
        }

        return gone;
    }

    /// <summary>
    /// Reads every account chain's facts the graph's state holds, from the case only, and joins them: Ethereum's, and
    /// TRON's, BSC's and Solana's when their sources are registered.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="state">The graph's state.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The account data of every chain.</returns>
    private async Task<AccountData> ReadAccountsAsync(
        ICase @case, GraphState state, CancellationToken cancellationToken)
    {
        var data = await _ethereum.ReadAsync(@case, state, cancellationToken).ConfigureAwait(false);
        foreach (var other in new[] { _tron, _bsc, _solana }.OfType<EthereumReader>())
        {
            data = data.Merge(await other.ReadAsync(@case, state, cancellationToken).ConfigureAwait(false));
        }

        return data;
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
        var accountEnds = AccountEnds(@case, data);
        return GraphBuilder.Build(data, new GraphKnowledge(
            address => clusters.TryGetValue(address, out var cluster)
                ? [.. cluster.Addresses]
                : [.. _clustering.GetCluster(@case, address).Addresses],
            address => addresses.Contains(address)
                ? ends.GetValueOrDefault(address) ?? []
                : [.. _classifiers.SelectMany(classifier => classifier.ClassifyAddress(@case, address))],
            transaction => [.. GoneEnds(data, transaction.Txid).Concat(_classifiers.SelectMany(
                classifier => classifier.ClassifyTransaction(@case, transaction)))])
        {
            AccountEnds = account => accountEnds.GetValueOrDefault(account) ?? [],
        });
    }

    /// <summary>
    /// Gives the end of the trail at a transaction the source no longer knows, first among its ends, since nothing
    /// after a transaction that is not in the chain can be followed, whatever else is said about it.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The end, or nothing when the transaction is not gone.</returns>
    private static IEnumerable<TrailEndHint> GoneEnds(GraphData data, Txid txid) =>
        data.Gone.TryGetValue(txid, out var gone) && gone.Hint is { } hint ? [hint] : [];

    /// <summary>
    /// Asks every classifier once about the Ethereum addresses the graph shows, and joins their hints per address.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="data">The data.</param>
    /// <returns>The hints of each address that has any.</returns>
    private Dictionary<ChainAddress, IReadOnlyList<TrailEndHint>> AccountEnds(ICase @case, GraphData data)
    {
        var ends = new Dictionary<ChainAddress, IReadOnlyList<TrailEndHint>>();
        var addresses = data.State.StartPoints.Select(start => start.EthereumAddress).OfType<EthereumAddress>()
            .Concat(data.Account.Transfers.SelectMany(transfer => new[] { transfer.From, transfer.To }))
            .Concat(data.Account.Transactions.Values.Select(transaction => transaction.From))
            .Select(address => address.ToChainAddress())
            .ToHashSet();
        if (addresses.Count == 0)
        {
            return ends;
        }

        foreach (var classifier in _classifiers)
        {
            foreach (var (address, hints) in classifier.ClassifyAccounts(@case, addresses))
            {
                ends[address] = ends.TryGetValue(address, out var earlier) ? [.. earlier, .. hints] : hints;
            }
        }

        return ends;
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

    /// <summary>Checks a reason the user gives; it may be empty (the user's decision 2026-11-07).</summary>
    /// <param name="reason">The reason.</param>
    /// <exception cref="ArgumentException">It is too long.</exception>
    private static void CheckReason(string reason) => CrossChainLinks.CheckReason(reason);

    /// <summary>
    /// Joins an answer's notes (a transaction that left its block, one that could not be checked against its bytes)
    /// to those already gathered; they are said, but do not make the expansion incomplete.
    /// </summary>
    /// <typeparam name="T">What was looked up.</typeparam>
    /// <param name="notes">The notes so far.</param>
    /// <param name="lookup">The answer.</param>
    /// <returns>The notes with the answer's added.</returns>
    private static string? WithNotes<T>(string? notes, BitcoinLookup<T> lookup)
        where T : class => lookup.Notes.Aggregate(notes, Join);

    /// <summary>What one expansion step brought in.</summary>
    /// <param name="Transactions">The transactions.</param>
    /// <param name="Problem">What went wrong, or <see langword="null"/> when it is complete.</param>
    /// <param name="Answered">
    /// Whether the source answered at all; an address with no history is answered with no transactions.
    /// </param>
    /// <param name="HeldBack">What was held back at the limit, if anything.</param>
    private sealed record StepResult(
        IReadOnlyList<Txid> Transactions, string? Problem, bool Answered, ExpansionHoldBack? HeldBack = null)
    {
        /// <summary>
        /// Gets what the user should know that does not make the step incomplete (a transaction that left its
        /// block, one that could not be checked against its bytes), or <see langword="null"/>.
        /// </summary>
        public string? Notes { get; init; }
    }
}
