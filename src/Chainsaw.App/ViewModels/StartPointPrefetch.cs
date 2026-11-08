using System.Globalization;
using System.Text.Json;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Fetches the start points of Nytt ärende into a case in memory (see <see cref="IStartPointPrefetch"/>): the
/// chains' sources answer, pace and seal exactly as in a real case, and <see cref="SealInto"/> brings every raw
/// response into the new case through <see cref="IResponseTransfer"/>, the same path the background watching uses.
/// </summary>
public sealed class StartPointPrefetch : IStartPointPrefetch, IDisposable
{
    /// <summary>
    /// How many start points are looked up at once: the endpoints pace every request anyway, and a pasted list of
    /// fifty must not queue a hundred and fifty requests at the same moment.
    /// </summary>
    private const int Parallel = 2;

    /// <summary>What a row says when the source does not know the value.</summary>
    private const string NotFoundText = "Källan känner inte till den";

    /// <summary>What a row says when the imported sanction list could not be read.</summary>
    private const string SanctionUnreadText = "Sanktionslistan kunde inte läsas just nu";

    /// <summary>What a row says when the lookup failed without a reason.</summary>
    private const string FailedText = "Hämtningen misslyckades";

    /// <summary>Swedish dates and numbers.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Makes the case in memory.</summary>
    private readonly IMemoryCases _memory;

    /// <summary>Reads and seals the raw responses again.</summary>
    private readonly IResponseTransfer _transfer;

    /// <summary>Bitcoin's source.</summary>
    private readonly IBitcoinSource _bitcoin;

    /// <summary>Ethereum's source.</summary>
    private readonly IEthereumSource _ethereum;

    /// <summary>The imported sanction lists.</summary>
    private readonly ISanctionLists _sanctions;

    /// <summary>The clock, which says whether a date is this year.</summary>
    private readonly IClock _clock;

    /// <summary>Lets <see cref="Parallel"/> lookups run at once.</summary>
    private readonly SemaphoreSlim _gate = new(Parallel);

    /// <summary>Guards <see cref="_case"/>.</summary>
    private readonly Lock _lock = new();

    /// <summary>The case in memory the lookups seal into, made at the first lookup; none before.</summary>
    private IMemoryCase? _case;

    /// <summary>Creates the prefetch.</summary>
    /// <param name="memory">Makes the case in memory.</param>
    /// <param name="transfer">Reads and seals the raw responses again.</param>
    /// <param name="bitcoin">Bitcoin's source.</param>
    /// <param name="ethereum">Ethereum's source.</param>
    /// <param name="sanctions">The imported sanction lists.</param>
    /// <param name="clock">The clock.</param>
    public StartPointPrefetch(
        IMemoryCases memory,
        IResponseTransfer transfer,
        IBitcoinSource bitcoin,
        IEthereumSource ethereum,
        ISanctionLists sanctions,
        IClock clock)
    {
        _memory = memory;
        _transfer = transfer;
        _bitcoin = bitcoin;
        _ethereum = ethereum;
        _sanctions = sanctions;
        _clock = clock;
    }

    /// <inheritdoc/>
    public async Task<StartPointPreview> PreviewAsync(
        ChainStartPoint startPoint, bool mayFetch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startPoint);
        if (!startPoint.IsRecognised)
        {
            return StartPointPreview.Only(PreviewState.NotRecognised);
        }

        string sanction;
        try
        {
            sanction = await Task.Run(() => SanctionTip(startPoint), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The list's file is held by something else (an antivirus scan): the row says so instead of fetching on,
            // so that a sanction is never left out in silence.
            return StartPointPreview.Failure(SanctionUnreadText);
        }
        if (!startPoint.IsSupported)
        {
            return StartPointPreview.Only(PreviewState.NotBuilt, sanction);
        }

        if (!mayFetch)
        {
            return StartPointPreview.Only(PreviewState.WaitingForExit, sanction);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var preview = await Task.Run(() => FetchAsync(Case(), startPoint, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            return preview with { SanctionTip = sanction };
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            // The case in memory was dropped while the lookup ran (the panel closed), or Chainsaw locked: the row says
            // that nothing came, and the panel never falls over a lookup.
            return StartPointPreview.Failure(FailedText, sanction);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public int SealInto(ICase target)
    {
        ArgumentNullException.ThrowIfNull(target);
        IMemoryCase? memory;
        lock (_lock)
        {
            memory = _case;
        }

        if (memory is null)
        {
            return 0;
        }

        var sequences = memory.SealedSince(SealedResponses.Chain, 0);
        var (responses, unread) = memory.Read(reader => ReadEach(reader, sequences));
        if (responses.Count == 0 && unread == 0)
        {
            return 0;
        }

        return target.Write(transaction =>
        {
            foreach (var response in responses)
            {
                _transfer.Seal(transaction, response);
            }

            var text = $"{responses.Count} råsvar som hämtades i Nytt ärende innan ärendet fanns förseglades i ärendet.";
            if (unread > 0)
            {
                text += $" {unread} kunde inte läsas och förseglades inte.";
            }

            transaction.AppendJournal(new JournalDraft(
                AppJournalKinds.PrefetchSealed,
                text,
                JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    ["responses"] = responses.Count,
                    ["unread"] = unread,
                })));
            return responses.Count;
        });
    }

    /// <summary>
    /// Reads every response sealed in the case in memory, one at a time, so that one that cannot be read (its hash no
    /// longer matches) never keeps the others out of the new case.
    /// </summary>
    /// <param name="reader">The case in memory's reader.</param>
    /// <param name="sequences">The sealed responses' numbers.</param>
    /// <returns>The responses read, and how many could not be.</returns>
    private (List<SealedResponse> Responses, int Unread) ReadEach(ICaseReader reader, IReadOnlyList<long> sequences)
    {
        var responses = new List<SealedResponse>();
        var unread = 0;
        foreach (var sequence in sequences)
        {
            try
            {
                if (_transfer.Read(reader, sequence) is { } response)
                {
                    responses.Add(response);
                }
                else
                {
                    unread++;
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or CaseException)
            {
                unread++;
            }
        }

        return (responses, unread);
    }

    /// <inheritdoc/>
    public void Forget()
    {
        IMemoryCase? dropped;
        lock (_lock)
        {
            (dropped, _case) = (_case, null);
        }

        dropped?.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Forget();
        _gate.Dispose();
    }

    /// <summary>Gives the case in memory, made at the first lookup.</summary>
    /// <returns>The case.</returns>
    private IMemoryCase Case()
    {
        lock (_lock)
        {
            return _case ??= _memory.Create(SealedResponses.Chain);
        }
    }

    /// <summary>Looks a start point up in its chain's source.</summary>
    /// <param name="memory">The case in memory.</param>
    /// <param name="startPoint">The supported start point.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What to show.</returns>
    private Task<StartPointPreview> FetchAsync(
        IMemoryCase memory, ChainStartPoint startPoint, CancellationToken cancellationToken)
    {
        if (startPoint.Chain == Chain.Ethereum)
        {
            return startPoint.Kind == StartPointKind.Address
                && EthereumAddress.TryParse(startPoint.Value, out var address)
                ? EthereumAddressAsync(memory, address, cancellationToken)
                : EthereumTransactionAsync(memory, startPoint.Value, cancellationToken);
        }

        return startPoint.Bitcoin switch
        {
            { Address: { } address } => BitcoinAddressAsync(memory, address, cancellationToken),
            { Txid: { } txid } => BitcoinTransactionAsync(memory, txid, cancellationToken),
            _ => Task.FromResult(StartPointPreview.Only(PreviewState.NotRecognised)),
        };
    }

    /// <summary>
    /// A Bitcoin address: the balance and number of transactions from the address's summary, the first and last
    /// activity from its newest page of transactions (the first only when the page holds every confirmed one), and
    /// the chain's height when it was fetched.
    /// </summary>
    /// <param name="memory">The case in memory.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What to show.</returns>
    private async Task<StartPointPreview> BitcoinAddressAsync(
        IMemoryCase memory, BitcoinAddress address, CancellationToken cancellationToken)
    {
        var summary = await _bitcoin.GetAddressSummaryAsync(memory, address, cancellationToken).ConfigureAwait(false);
        if (Unanswered(summary) is { } failed)
        {
            return failed;
        }

        var page = await _bitcoin.GetAddressTransactionsAsync(memory, address, null, cancellationToken)
            .ConfigureAwait(false);
        var tip = await _bitcoin.GetTipHeightAsync(memory, cancellationToken).ConfigureAwait(false);
        var stats = summary.Value!;
        var balance = stats.Confirmed.Funded - stats.Confirmed.Spent + stats.Mempool.Funded - stats.Mempool.Spent;
        var facts = new List<PreviewFact>
        {
            new(AmountText.Number(balance.ToAmount()), " " + Asset.Bitcoin.Symbol),
            new(Count(stats.TransactionCount), " tx"),
        };
        var blocks = page.Value?.Transactions.Select(tx => tx.Block).OfType<BlockReference>().ToList() ?? [];
        var isWhole = blocks.Count == stats.Confirmed.TransactionCount;
        if (Activity(blocks.Select(block => block.Time).ToList(), isWhole) is { } activity)
        {
            facts.Add(activity);
        }

        var newest = blocks.MaxBy(block => block.Height);
        return Fetched(
            facts,
            tip.Value?.Height,
            newest is null ? null : (newest.Height, newest.Hash.Value),
            BlockOf.NewestTransaction);
    }

    /// <summary>
    /// A Bitcoin transaction: what its outputs pay, how many inputs and outputs it has, its block's date, height and
    /// hash, and the chain's height when it was fetched.
    /// </summary>
    /// <param name="memory">The case in memory.</param>
    /// <param name="txid">The transaction.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What to show.</returns>
    private async Task<StartPointPreview> BitcoinTransactionAsync(
        IMemoryCase memory, Txid txid, CancellationToken cancellationToken)
    {
        var lookup = await _bitcoin.GetTransactionAsync(memory, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (Unanswered(lookup) is { } failed)
        {
            return failed;
        }

        var tip = await _bitcoin.GetTipHeightAsync(memory, cancellationToken).ConfigureAwait(false);
        var tx = lookup.Value!;
        var paid = tx.Outputs.Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount);
        var facts = new List<PreviewFact>
        {
            new(AmountText.Number(paid.ToAmount()), " " + Asset.Bitcoin.Symbol),
            new(Count(tx.Inputs.Count), " in"),
            new(Count(tx.Outputs.Count), " ut"),
            tx.Block is { } block ? new(Day(block.Time), string.Empty) : new("Obekräftad", string.Empty),
        };
        var at = tx.Block is { } confirmed ? (confirmed.Height, confirmed.Hash.Value) : default((long, string)?);
        return Fetched(facts, tip.Value?.Height, at, BlockOf.Transaction);
    }

    /// <summary>
    /// An Ethereum address: the number of transactions on its newest page of transactions (with <c>+</c> when there
    /// are older ones), their first and last activity, and the chain's height. Ethereum's source gives no balance, so
    /// none is shown rather than one made up.
    /// </summary>
    /// <param name="memory">The case in memory.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What to show.</returns>
    private async Task<StartPointPreview> EthereumAddressAsync(
        IMemoryCase memory, EthereumAddress address, CancellationToken cancellationToken)
    {
        var lookup = await _ethereum.GetAddressHistoryAsync(
            memory, address, EthereumHistory.Transactions, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (Unanswered(lookup) is { } failed)
        {
            return failed;
        }

        var tip = await _ethereum.GetTipHeightAsync(memory, cancellationToken).ConfigureAwait(false);
        var page = lookup.Value!;
        var transactions = page.Transfers.Select(each => (each.Transaction, each.Block))
            .Concat(page.Fees.Select(each => (each.Transaction, each.Block)))
            .DistinctBy(each => each.Transaction)
            .ToList();
        var isWhole = page.NextAtOrBelow is null && !page.IsCutShort;
        var count = Count(transactions.Count) + (isWhole ? string.Empty : "+");
        var facts = new List<PreviewFact> { new(count, " tx") };
        var blocks = transactions.Select(each => each.Block).OfType<EthereumBlockReference>().ToList();
        if (Activity(blocks.Select(block => block.Time).ToList(), isWhole) is { } activity)
        {
            facts.Add(activity);
        }

        var newest = blocks.MaxBy(block => block.Height);
        return Fetched(
            facts, tip.Value?.Height, newest is null ? null : (newest.Height, newest.Hash), BlockOf.NewestTransaction);
    }

    /// <summary>
    /// An Ethereum transaction: the ether it sent, how many movements of value it holds, its block and the chain's
    /// height.
    /// </summary>
    /// <param name="memory">The case in memory.</param>
    /// <param name="value">The transaction's hash as written.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>What to show.</returns>
    private async Task<StartPointPreview> EthereumTransactionAsync(
        IMemoryCase memory, string value, CancellationToken cancellationToken)
    {
        if (!EthereumTxHash.TryParse(value, out var hash))
        {
            return StartPointPreview.Only(PreviewState.NotRecognised);
        }

        var lookup = await _ethereum.GetTransactionAsync(memory, hash, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (Unanswered(lookup) is { } failed)
        {
            return failed;
        }

        var tip = await _ethereum.GetTipHeightAsync(memory, cancellationToken).ConfigureAwait(false);
        var tx = lookup.Value!;
        var facts = new List<PreviewFact>
        {
            new(AmountText.Number(tx.Value), " " + tx.Value.Asset.Symbol),
            new(Count(tx.Transfers.Count), tx.Transfers.Count == 1 ? " överföring" : " överföringar"),
            tx.Block is { } block ? new(Day(block.Time), string.Empty) : new("Obekräftad", string.Empty),
        };
        var at = tx.Block is { } confirmed ? (confirmed.Height, confirmed.Hash) : default((long, string?)?);
        return Fetched(facts, tip.Value?.Height, at, BlockOf.Transaction);
    }

    /// <summary>Says why a lookup has no value, or nothing when it has one.</summary>
    /// <typeparam name="T">What was looked up.</typeparam>
    /// <param name="lookup">The answer.</param>
    /// <returns>The preview to show instead, or <see langword="null"/> when there is a value.</returns>
    private static StartPointPreview? Unanswered<T>(BitcoinLookup<T> lookup)
        where T : class => lookup switch
    {
        { HasValue: true } => null,
        { Status: LookupStatus.Offline } => StartPointPreview.Only(PreviewState.WaitingForExit),
        { Status: LookupStatus.NotFound } => StartPointPreview.Failure(NotFoundText),
        _ => StartPointPreview.Failure(lookup.Problem ?? FailedText),
    };

    /// <summary>
    /// Gives a fetched preview with the chain's height and the block the facts rest on. The chip shows only the
    /// chain's height; a transaction's block is never shown as it, so the number never changes meaning in silence.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <param name="tip">
    /// The chain's height when it was fetched, or <see langword="null"/> when the source did not say.
    /// </param>
    /// <param name="block">The block the facts rest on, with its hash, or <see langword="null"/>.</param>
    /// <param name="blockOf">Which block that is, for the tooltip's words.</param>
    /// <returns>The preview.</returns>
    private static StartPointPreview Fetched(
        IReadOnlyList<PreviewFact> facts, long? tip, (long Height, string? Hash)? block, BlockOf blockOf)
    {
        var lines = new List<string>();
        if (tip is { } height)
        {
            lines.Add($"Hämtat vid block {Count(height)}");
        }

        if (block is { } newest)
        {
            var words = blockOf == BlockOf.Transaction ? "I block" : "Senaste tx i block";
            lines.Add($"{words} {Count(newest.Height)}");
            if (newest.Hash is { Length: > 0 } hash)
            {
                lines.Add(hash);
            }
        }

        return new StartPointPreview(
            PreviewState.Fetched, facts, tip, string.Join(Environment.NewLine, lines), string.Empty,
            string.Empty);
    }

    /// <summary>
    /// Writes when an address was active, from the blocks of the transactions fetched: <c>mar 2024 → 29 okt</c> when
    /// they are all of them, otherwise only the last, <c>senast 29 okt</c>, since the first is not known.
    /// </summary>
    /// <param name="times">The blocks' times.</param>
    /// <param name="isWhole">Whether the transactions fetched are all the address has.</param>
    /// <returns>The fact, or <see langword="null"/> when no transaction is in a block.</returns>
    private PreviewFact? Activity(IReadOnlyList<DateTimeOffset> times, bool isWhole)
    {
        if (times.Count == 0)
        {
            return null;
        }

        var last = Day(times.Max());
        var text = isWhole ? Month(times.Min()) + " → " + last : "senast " + last;
        return new PreviewFact(text, string.Empty);
    }

    /// <summary>Writes a day in local time, with the year only when it is not this year: <c>29 okt</c>.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The day.</returns>
    private string Day(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        var day = local.Day.ToString(CultureInfo.InvariantCulture) + " " + MonthName(local);
        return local.Year == _clock.UtcNow.ToLocalTime().Year ? day : day + " " + Year(local);
    }

    /// <summary>Writes a month and year in local time: <c>mars 2024</c>.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The month.</returns>
    private static string Month(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        return MonthName(local) + " " + Year(local);
    }

    /// <summary>
    /// Gives a month's short Swedish name without the full stop some cultures give it: <c>okt</c>, not <c>okt.</c>.
    /// </summary>
    /// <param name="time">The local time.</param>
    /// <returns>The name.</returns>
    private static string MonthName(DateTimeOffset time) =>
        Swedish.DateTimeFormat.GetAbbreviatedMonthName(time.Month).TrimEnd('.');

    /// <summary>Writes a year with four digits.</summary>
    /// <param name="time">The local time.</param>
    /// <returns>The year.</returns>
    private static string Year(DateTimeOffset time) => time.Year.ToString(CultureInfo.InvariantCulture);

    /// <summary>Writes a count with a space between thousands: <c>23 118 406</c>.</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    private static string Count(long count) => count.ToString("N0", Swedish);

    /// <summary>
    /// Finds a Bitcoin address on the imported sanction list (on the computer; nothing is sent) and says which entry
    /// names it and from which list, for the tooltip of the tag <em>Sanktionerad</em>.
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The tooltip, or empty when the list does not name it or none is imported.</returns>
    private string SanctionTip(ChainStartPoint startPoint)
    {
        if (startPoint.Bitcoin?.Address is not { } address)
        {
            return string.Empty;
        }

        SanctionMatch? match;
        try
        {
            match = _sanctions.Find(address);
        }
        catch (SanctionListException)
        {
            // A list that no longer has its hash is said when it is imported or checked; no tag is shown on it.
            return string.Empty;
        }

        // The same hits the case is labelled with when it is created (IAttributionService.CheckSanctions).
        var hit = match?.Hits.FirstOrDefault(each => each.Currency == SanctionCurrency.Bitcoin);
        if (match is null || hit is null)
        {
            return string.Empty;
        }

        return $"Sanktionerad enligt {match.List.Name} {match.List.PublishDate:yyyy-MM-dd} · {hit.EntryName}"
            + " · en uppgift ur en öppen källa";
    }
}

/// <summary>Which block a preview's tooltip names besides the chain's height.</summary>
internal enum BlockOf
{
    /// <summary>The block the transaction itself is in.</summary>
    Transaction,

    /// <summary>The block of the address's newest transaction fetched.</summary>
    NewestTransaction,
}
