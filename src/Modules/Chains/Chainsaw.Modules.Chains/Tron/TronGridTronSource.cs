using System.Collections.Concurrent;
using System.Globalization;

using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// TRON from TronGrid's API, asked only through <see cref="IExitHttp"/> by <see cref="SourceLookups"/>: every response
/// is sealed in the case before it is used, what rests on a final block (20 deep) is taken from the case without
/// asking, and without an exit the case answers and says what is missing.
/// </summary>
/// <remarks>
/// TRON's histories are paged by time, not by block (<see cref="PageStartOf"/>). The token history does not give the
/// block or the event's place in the transaction, so each of its transactions' info (<c>gettransactioninfobyid</c>)
/// is fetched too, sealed and kept: it gives the block and the log's index, so that a movement is the same whether it
/// came from the history or from the whole transaction.
/// </remarks>
internal sealed class TronGridTronSource : ITronSource
{
    /// <summary>The chain and network in the sealed table.</summary>
    public const string ChainName = "tron-mainnet";

    /// <summary>How old the newest known height may be before a lookup that depends on depth asks again.</summary>
    private static readonly TimeSpan TipFreshness = TimeSpan.FromSeconds(30);

    /// <summary>How long a doubted info waits before its endpoint is asked again: 1, 5, 15 min, then hourly.</summary>
    private static readonly TimeSpan[] AskAgainWaits =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    /// <summary>The time now, to decide whether the known height is fresh.</summary>
    private readonly IClock _clock;

    /// <summary>The settings.</summary>
    private readonly TronSourceOptions _options;

    /// <summary>Answers the lookups through the endpoints and the case.</summary>
    private readonly SourceLookups _lookups;

    /// <summary>The time zone the user reads times in.</summary>
    private readonly TimeZoneInfo _timeZone;

    /// <summary>
    /// When a doubted info was last asked for again, by transaction, so that an ask that fails and seals nothing is not
    /// repeated at every read.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _askedAgain = new(StringComparer.Ordinal);

    /// <summary>Creates the source.</summary>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <param name="sources">The chains' endpoints, of which TRON's are asked.</param>
    /// <exception cref="ArgumentException">The depth or the page size is less than 1.</exception>
    public TronGridTronSource(
        IOperatorProfileStore operators, IClock clock, TronSourceOptions options, TimeZoneInfo timeZone,
        ChainSources sources)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PageSize < 1)
        {
            throw new ArgumentException("A page has at least one entry.", nameof(options));
        }

        _clock = clock;
        _options = options;
        _timeZone = timeZone;
        _lookups = new SourceLookups(
            Chain.Tron, "TRON", operators, sources, new RawResponseStore(timeZone, ChainName),
            options.SafeConfirmations,
            async (_, @case, cancellationToken) => await RefreshHeightAsync(@case, cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>Gives where a page that goes back from a block starts: its time in milliseconds since 1970.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The block's time in milliseconds.</returns>
    public long PageStartOf(EthereumBlockReference block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Time.ToUnixTimeMilliseconds();
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        HistoryAsync(@case, address, history, atOrBelow, mode, null, cancellationToken);

    /// <summary>
    /// Gets a page of an address's history for a check that needs nothing older than a time: the token history's
    /// entries older than it, and those in a token Chainsaw does not know (which a check never takes as hits), are
    /// neither looked up nor counted, so that a check reads only what it can use and a stranger's token never holds
    /// it back.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="history">Which history.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/> for the newest.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="notBefore">The oldest block time the check needs.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    public Task<BitcoinLookup<EthereumAddressPage>> GetRecentHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow, LookupMode mode,
        DateTimeOffset notBefore, CancellationToken cancellationToken = default) =>
        HistoryAsync(@case, address, history, atOrBelow, mode, notBefore, cancellationToken);

    /// <summary>Gets a page of an address's history, leaving the token history's older entries out when asked.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="history">Which history.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/> for the newest.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="notBefore">
    /// For a check, the oldest block time needed; the token history then keeps only the known tokens. For a read of the
    /// whole history (the graph), <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    private async Task<BitcoinLookup<EthereumAddressPage>> HistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow, LookupMode mode,
        DateTimeOffset? notBefore, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        CheckOnTron(address);
        if (atOrBelow is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(atOrBelow), atOrBelow, "A time is not negative.");
        }

        switch (history)
        {
            case EthereumHistory.Transactions:
                var page = await WholeBlockAsync(
                    @case, size => HistoryQuery(address, atOrBelow, size), raw => raw.Page.IsCutShort, mode,
                    cancellationToken).ConfigureAwait(false);
                return new BitcoinLookup<EthereumAddressPage>(page.Status, page.Value?.Page, page.Origins, page.Problem)
                {
                    Notes = page.Notes,
                };
            case EthereumHistory.Internal:
                // TronGrid lists the TRX a contract sent with the transaction it is in, in the address's transactions;
                // there is no list of its own to ask for.
                return new BitcoinLookup<EthereumAddressPage>(
                    LookupStatus.FromCase,
                    new EthereumAddressPage(address, history, atOrBelow, [], [], null, false, false),
                    [],
                    null);
            case EthereumHistory.Tokens:
                return await TokenHistoryAsync(@case, address, atOrBelow, mode, notBefore, cancellationToken)
                    .ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(history), history, "Unknown history.");
        }
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Chain != Chain.Tron)
        {
            throw new ArgumentException("The transaction is not on TRON.", nameof(hash));
        }

        // The info first: its block decides when the transaction's own answer (and its result) is final, so that a
        // result from a block that was still shallow is asked for again.
        var infoLookup = await _lookups.LookupAsync(@case, InfoQuery(hash), mode, cancellationToken)
            .ConfigureAwait(false);
        var raw = await _lookups.LookupAsync(
                @case, TransactionQuery(hash, infoLookup.Value?.Height), mode, cancellationToken)
            .ConfigureAwait(false);
        if (raw.Value is not { } transaction)
        {
            return Without<EthereumTransaction, TronTransactionRaw>(raw);
        }

        var parts = new LookupParts(raw.Status, raw.Origins, raw.Notes, raw.Problem);
        TronTransactionInfo? info;
        if (infoLookup.Status == LookupStatus.NotFound)
        {
            parts.Take(infoLookup);
            parts.Missing(
                "Tx:en är obekräftad: om den lyckas och vad den flyttar avgörs först när den körs i ett block.");
            info = null;
        }
        else
        {
            info = parts.Add(infoLookup, "Tx:ens block, avgift och händelser");
        }

        EthereumBlockReference? block = null;
        if (info is not null)
        {
            block = parts.Add(await GetBlockAsync(@case, info.Height, mode, cancellationToken).ConfigureAwait(false),
                    "Blockets hash")
                ?? new EthereumBlockReference(info.Height, null, info.Time);
        }

        if (transaction.IsTrc10)
        {
            parts.Note("Tx:en flyttar en TRC-10-token, som Chainsaw inte räknar.");
        }

        return parts.Result(Assemble(transaction, info, block, parts));
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
        ICase @case, long height, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var subject = height.ToString(CultureInfo.InvariantCulture);
        EthereumBlockReference Parse(byte[] body) => TronGridParser.ParseBlock(body, height);
        var query = new LookupQuery<EthereumBlockReference>(
            TronResponseKinds.Block,
            subject,
            TronGridApi.BlockPath(height),
            Parse,
            block => new ResponseIndex([], block.Height),
            block => block.Height,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.Block, subject), Parse));
        return _lookups.LookupAsync(@case, query, mode, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        GetTipHeightAsync(@case, LookupMode.AsNeeded, cancellationToken);

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, LookupMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var tip = await _lookups.LookupAsync(@case, TipQuery(), mode, cancellationToken).ConfigureAwait(false);
        return new BitcoinLookup<EthereumTip>(
            tip.Status, tip.Value is { } known ? new EthereumTip(known.Height) : null, tip.Origins, tip.Problem)
        {
            Notes = tip.Notes,
        };
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return _lookups.LookupAsync(@case, TipQuery(), LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
        ICase @case, EthereumAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        CheckOnTron(address);
        TronAccount Parse(byte[] body) => TronGridParser.ParseAccount(body, address);
        var query = new LookupQuery<TronAccount>(
            TronResponseKinds.Account,
            address.Value,
            "v1/accounts/" + address.Value,
            Parse,
            _ => ResponseIndex.None,
            _ => null,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.Account, address.Value), Parse));
        var account = await _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (account.Value is not { } found)
        {
            return Without<EthereumBalance, TronAccount>(account);
        }

        var balance = new EthereumBalance(address, new AssetAmount(Asset.Tronix, found.Sun), found.Tokens)
        {
            Skipped = found.Skipped,
        };
        IReadOnlyList<string> notes = found.Skipped > 0
            ? [.. account.Notes, "Adressen har tokens vars decimaler Chainsaw inte känner; de visas inte."]
            : account.Notes;
        return new BitcoinLookup<EthereumBalance>(account.Status, balance, account.Origins, account.Problem)
        {
            Notes = notes,
        };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BlockChange>> GetBlockChangesAsync(
        ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run<IReadOnlyList<BlockChange>>(
            () => @case.Read(reader => _lookups.Store.SubjectsAskedAgain(reader, TronResponseKinds.TransactionInfo)
                .Select(subject => EthereumTxHash.TryParse(Chain.Tron, subject, out var hash)
                    ? BlockChangeOf(reader, hash)
                    : null)
                .OfType<BlockChange>()
                .ToList()),
            cancellationToken);
    }

    /// <summary>Checks that an address is on TRON.</summary>
    /// <param name="address">The address.</param>
    /// <exception cref="ArgumentException">It is on another chain.</exception>
    private static void CheckOnTron(EthereumAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Chain != Chain.Tron)
        {
            throw new ArgumentException("The address is not on TRON.", nameof(address));
        }
    }

    /// <summary>
    /// Reads a page of an address's TRC-20 transfers, and each of its transactions' info for the block and the event's
    /// place, so that the page holds the same movements a whole transaction gives. Two kinds of gap are kept apart:
    /// <list type="bullet">
    /// <item>A transfer whose info could not be had now (the source asked Chainsaw to wait, did not answer, or has not
    /// indexed it yet), or whose info lacks the event while its block is still shallow, waits: the page is incomplete,
    /// says which transactions it waits for, and is read again, so that a check never moves past it.</item>
    /// <item>A transfer of a token Chainsaw does not know whose info is final but holds no event it can be matched with
    /// (a non-standard event, common among scam tokens) is left out for good: counted as skipped and as unplaced, and
    /// said, but it never keeps the page incomplete, so that no stranger's token can hold a check back. A known token's
    /// transfer is never left out this way: the info is asked again of another endpoint, and it waits until placed
    /// (<see cref="PlaceGroupAsync"/>).</item>
    /// </list>
    /// An entry without decimals, which the source will not give for that page, counts as unread.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/> for the newest.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="notBefore">
    /// For a check, the oldest block time needed (only known tokens are then looked up); <see langword="null"/> for
    /// all.
    /// </param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    private async Task<BitcoinLookup<EthereumAddressPage>> TokenHistoryAsync(
        ICase @case, EthereumAddress address, long? atOrBelow, LookupMode mode, DateTimeOffset? notBefore,
        CancellationToken cancellationToken)
    {
        var lookup = await WholeBlockAsync(
            @case, size => TokenHistoryQuery(address, atOrBelow, size), raw => raw.IsCutShort, mode,
            cancellationToken).ConfigureAwait(false);
        if (lookup.Value is not { } raw)
        {
            return Without<EthereumAddressPage, TronTokenPage>(lookup);
        }

        var parts = new LookupParts(lookup.Status, lookup.Origins, lookup.Notes, lookup.Problem);
        var transfers = new List<EthereumTransfer>();
        var gaps = new TokenGaps();
        var needed = raw.Entries.Where(entry => notBefore is not { } oldest
            || (entry.Time >= oldest && IsKnown(entry)));
        foreach (var group in needed.GroupBy(entry => entry.Transaction))
        {
            transfers.AddRange(
                await PlaceGroupAsync(@case, [.. group], mode, parts, gaps, cancellationToken).ConfigureAwait(false));
        }

        gaps.Note(parts, DisputedText);

        var waiting = gaps.Waiting;
        var page = new EthereumAddressPage(
            address, EthereumHistory.Tokens, atOrBelow, transfers, [], raw.NextAtOrBelow, raw.IsCutShort,
            waiting.Count > 0)
        {
            Skipped = raw.Skipped + gaps.Unplaced,
            Unread = raw.Unread,
            Unplaced = gaps.Unplaced,
            Waiting = waiting,
            Disputed = gaps.Disputed,
        };
        return parts.Page(page);
    }

    /// <summary>
    /// Places one transaction's entries of a token page by its info. What cannot be placed waits while the block is
    /// shallow. In a final block, an entry of a token Chainsaw does not know is left out for good (unplaced); an entry of
    /// a known token never is: the answer that lacks its event is not believed, the info is asked again of another
    /// endpoint than the one that gave it, and the transaction waits until an endpoint places the movement.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="entries">The transaction's entries on the page.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="parts">The page's answer, which takes every response.</param>
    /// <param name="gaps">What the page waits for and what it left out.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The transfers that could be placed.</returns>
    private async Task<IReadOnlyList<EthereumTransfer>> PlaceGroupAsync(
        ICase @case, IReadOnlyList<TronTokenEntry> entries, LookupMode mode, LookupParts parts, TokenGaps gaps,
        CancellationToken cancellationToken)
    {
        var hash = entries[0].Transaction;
        var info = await _lookups.LookupAsync(@case, InfoQuery(hash), mode, cancellationToken).ConfigureAwait(false);
        parts.Take(info);
        if (info.Value is not { } found)
        {
            gaps.Wait(hash);
            return [];
        }

        var (placed, left) = Place(entries, found);
        if (left.Count == 0)
        {
            return placed;
        }

        if (!await IsFinalAsync(@case, found.Height, cancellationToken).ConfigureAwait(false))
        {
            gaps.Wait(hash);
            return placed;
        }

        gaps.Unplace(left.Count(entry => !IsKnown(entry)));
        if (!left.Any(IsKnown))
        {
            return placed;
        }

        // A final answer without a known token's event is more likely an endpoint's fault than the chain's: asked
        // again, without the stored answer, and believed only when an endpoint places it.
        var (again, disputed) = await AskAgainAsync(
                @case, hash, info.Origins.Count > 0 ? info.Origins[^1] : null, mode, cancellationToken)
            .ConfigureAwait(false);
        if (again is not null)
        {
            parts.Take(again);
        }

        if (again?.Value is { } other && Place(entries, other) is var second
            && second.Left.Count(IsKnown) < left.Count(IsKnown))
        {
            (placed, left) = (second.Placed, second.Left);
        }

        if (left.Any(IsKnown))
        {
            gaps.Dispute(disputed);
        }

        return placed;
    }

    /// <inheritdoc />
    public async Task<BitcoinLookup<EthereumTransaction>> GetDoubtedTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(hash);
        var info = await _lookups.LookupAsync(@case, InfoQuery(hash), mode, cancellationToken).ConfigureAwait(false);
        string? note = null;
        if (info.Value is { } found && await IsFinalAsync(@case, found.Height, cancellationToken).ConfigureAwait(false))
        {
            var (_, disputed) = await AskAgainAsync(
                    @case, hash, info.Origins.Count > 0 ? info.Origins[^1] : null, mode, cancellationToken)
                .ConfigureAwait(false);
            note = DisputedText(disputed);
        }

        var lookup = await GetTransactionAsync(@case, hash, mode, cancellationToken).ConfigureAwait(false);
        return note is null ? lookup : lookup with { Notes = [.. lookup.Notes, note] };
    }

    /// <summary>
    /// Asks a doubted final info again, without the stored answer: of another endpoint than the one that gave it (and
    /// not at its host) when one is switched on, at once the first time; otherwise of the same, after 1, 5 and 15
    /// minutes and then every hour, counted from the newest sealed answer. Never more than once a minute per
    /// transaction, also when an ask fails and seals nothing.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="hash">The transaction.</param>
    /// <param name="doubted">The response not believed, or <see langword="null"/> when unknown.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The new answer when one was asked for, and what is doubted and when it is asked again.</returns>
    private async Task<(BitcoinLookup<TronTransactionInfo>? Again, EthereumDisputedAnswer Disputed)> AskAgainAsync(
        ICase @case, EthereumTxHash hash, ResponseOrigin? doubted, LookupMode mode,
        CancellationToken cancellationToken)
    {
        var other = doubted is not null && _lookups.HasOtherThan(doubted);
        var (count, newest) = await AnswersAsync(@case, hash, cancellationToken).ConfigureAwait(false);
        var due = other && count <= 1 ? DateTimeOffset.MinValue : Due(hash, count, newest);
        BitcoinLookup<TronTransactionInfo>? again = null;
        if (mode != LookupMode.CaseOnly && _clock.UtcNow >= due)
        {
            _askedAgain[hash.Value] = _clock.UtcNow;
            again = await _lookups.FetchAsync(
                    @case, InfoQuery(hash), null, cancellationToken, mode == LookupMode.InStep,
                    doubted: other ? doubted : null)
                .ConfigureAwait(false);
            (count, newest) = await AnswersAsync(@case, hash, cancellationToken).ConfigureAwait(false);
            due = Due(hash, count, newest);
        }

        var disputed = new EthereumDisputedAnswer(hash, doubted?.Endpoint ?? string.Empty)
        {
            AskedAgainAt = other ? null : due,
            AskedAgainText = other ? string.Empty : SourceLookups.WhenText(due, _clock.UtcNow, _timeZone),
        };

        return (again, disputed);
    }

    /// <summary>
    /// Counts a transaction's sealed infos that were final when they came (the doubted answers: one from a block still
    /// shallow was asked again for that reason, not because it was doubted), and gives when the newest of all came.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="hash">The transaction.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>How many, and the newest's time (<see cref="DateTimeOffset.MinValue"/> for none).</returns>
    private Task<(int Count, DateTimeOffset Newest)> AnswersAsync(
        ICase @case, EthereumTxHash hash, CancellationToken cancellationToken) =>
        Task.Run(
            () => @case.Read(reader =>
            {
                var all = _lookups.Store.All(reader, TronResponseKinds.TransactionInfo, hash.Value);
                var final = all.Count(response => WasFinal(reader, response, hash));
                return (final, all.Count == 0 ? DateTimeOffset.MinValue : all[^1].Origin.ReceivedUtc);
            }),
            cancellationToken);

    /// <summary>Says whether a sealed info's block was final when the info came.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="response">The sealed info.</param>
    /// <param name="hash">The transaction.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    private bool WasFinal(ICaseReader reader, StoredResponse response, EthereumTxHash hash)
    {
        if (SourceLookups.StoredAs(response, body => TronGridParser.ParseTransactionInfo(body, hash)) is not { } info)
        {
            return false;
        }

        var knownThen = _lookups.Store.KnownHeight(reader, response.Origin.Sequence);
        return SourceLookups.Confirmations(info.Value.Height, knownThen) >= _options.SafeConfirmations;
    }

    /// <summary>
    /// Gives when a doubted info is asked of the same endpoint again: after 1, 5 and 15 minutes and then every hour
    /// from the newest sealed answer, and never sooner than a minute after the last try.
    /// </summary>
    /// <param name="hash">The transaction.</param>
    /// <param name="count">How many infos are sealed.</param>
    /// <param name="newest">When the newest came.</param>
    /// <returns>The time.</returns>
    private DateTimeOffset Due(EthereumTxHash hash, int count, DateTimeOffset newest)
    {
        var wait = AskAgainWaits[Math.Clamp(count - 1, 0, AskAgainWaits.Length - 1)];
        var due = newest == DateTimeOffset.MinValue ? newest : newest + wait;
        return _askedAgain.TryGetValue(hash.Value, out var tried) && tried + AskAgainWaits[0] > due
            ? tried + AskAgainWaits[0]
            : due;
    }

    /// <summary>Says, in Swedish, that a final answer lacked a known token's event and what is done about it.</summary>
    /// <param name="disputed">The doubted answer.</param>
    /// <returns>The text.</returns>
    private string DisputedText(EthereumDisputedAnswer disputed)
    {
        var start = $"Källan {disputed.Endpoint} gav ingen Transfer-händelse för en känd token i tx "
            + $"{disputed.Transaction.Brief()} fast blocket är slutligt; svaret tros inte, ";
        if (disputed.AskedAgainAt is not { } at)
        {
            return start + "tx:en frågas hos en annan källa och väntar tills överföringen kan placeras.";
        }

        return start + $"ingen annan källa för TRON är påslagen; frågas igen hos {disputed.Endpoint} "
            + $"{disputed.AskedAgainText}, och tx:en väntar tills överföringen kan placeras.";
    }

    /// <summary>Says whether an entry's token is one Chainsaw knows by its contract (USDT and the others).</summary>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> for a known token.</returns>
    private static bool IsKnown(TronTokenEntry entry) => KnownTokens.IssuerOf(entry.Asset) is not null;

    /// <summary>What a token page waits for, which answers are not believed, and what it left out for good.</summary>
    private sealed class TokenGaps
    {
        /// <summary>The transactions waited for, each once, in the page's order.</summary>
        private readonly List<EthereumTxHash> _waiting = [];

        /// <summary>The transactions whose final answer lacked a known token's event.</summary>
        private readonly List<EthereumDisputedAnswer> _disputed = [];

        /// <summary>Gets the transactions waited for.</summary>
        public IReadOnlyList<EthereumTxHash> Waiting => _waiting;

        /// <summary>Gets the transactions whose final answer lacked a known token's event.</summary>
        public IReadOnlyList<EthereumDisputedAnswer> Disputed => _disputed;

        /// <summary>Gets how many entries of unknown tokens have no readable event in their final info.</summary>
        public int Unplaced { get; private set; }

        /// <summary>Waits for a transaction.</summary>
        /// <param name="hash">The transaction.</param>
        public void Wait(EthereumTxHash hash)
        {
            if (!_waiting.Contains(hash))
            {
                _waiting.Add(hash);
            }
        }

        /// <summary>Waits for a transaction whose final answer from an endpoint lacked a known token's event.</summary>
        /// <param name="disputed">The doubted answer.</param>
        public void Dispute(EthereumDisputedAnswer disputed)
        {
            Wait(disputed.Transaction);
            _disputed.Add(disputed);
        }

        /// <summary>Counts entries left out for good.</summary>
        /// <param name="count">How many.</param>
        public void Unplace(int count) => Unplaced += count;

        /// <summary>Says what the page waits for and what it left out for good.</summary>
        /// <param name="parts">The page's answer.</param>
        /// <param name="disputedText">Says what is done about a doubted answer.</param>
        public void Note(LookupParts parts, Func<EthereumDisputedAnswer, string> disputedText)
        {
            foreach (var disputed in _disputed)
            {
                parts.Note(disputedText(disputed));
            }

            var plain = _waiting.Where(hash => _disputed.All(disputed => disputed.Transaction != hash)).ToList();
            if (plain.Count > 0)
            {
                parts.Note("Väntar på källan för tx " + EthereumTxHash.BriefList(plain)
                    + ": deras block och plats kunde inte hämtas än; överföringarna räknas när de kan hämtas.");
            }

            if (Unplaced > 0)
            {
                parts.Note(string.Create(CultureInfo.InvariantCulture,
                    $"{Unplaced} TRC-20-överföringar av okända tokens har ingen läsbar Transfer-händelse i sin ")
                    + "slutliga tx (vanligt för skräptokens); de räknas inte.");
            }
        }
    }

    /// <summary>Says whether a block is final: as deep as the source's setting asks, from the highest block known.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="height">The block.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns><see langword="true"/> when it is final.</returns>
    private Task<bool> IsFinalAsync(ICase @case, long height, CancellationToken cancellationToken) =>
        Task.Run(
            () => @case.Read(reader => SourceLookups.Confirmations(height, _lookups.Store.KnownHeight(reader))
                >= _options.SafeConfirmations),
            cancellationToken);


    /// <summary>
    /// Reads a page, and when a full page lay in one block reads it again with the most entries TronGrid gives a page
    /// (200), so that a block with more transfers than the page size is read whole as far as that reaches.
    /// </summary>
    /// <typeparam name="T">The page.</typeparam>
    /// <param name="case">The open case.</param>
    /// <param name="query">The page's lookup with a page size.</param>
    /// <param name="isCutShort">Says whether the page was cut short.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page, the wider one when it had to be read.</returns>
    private async Task<BitcoinLookup<T>> WholeBlockAsync<T>(
        ICase @case, Func<int, LookupQuery<T>> query, Func<T, bool> isCutShort, LookupMode mode,
        CancellationToken cancellationToken)
        where T : class
    {
        var page = await _lookups.LookupAsync(@case, query(_options.PageSize), mode, cancellationToken)
            .ConfigureAwait(false);
        if (page.Value is not { } first || !isCutShort(first) || _options.PageSize >= TronGridApi.MostPerPage)
        {
            return page;
        }

        var wider = await _lookups.LookupAsync(@case, query(TronGridApi.MostPerPage), mode, cancellationToken)
            .ConfigureAwait(false);
        return wider.Value is null
            ? page
            : wider with { Origins = [.. page.Origins.Concat(wider.Origins).DistinctBy(origin => origin.Sequence)] };
    }


    /// <summary>
    /// Gives the token history's transfers of one transaction their block and place: each is matched, in order, with an
    /// unused <c>Transfer</c> event in the transaction's info from the same contract, between the same addresses and
    /// of the same amount, and takes the event's index.
    /// </summary>
    /// <param name="entries">The transaction's transfers on the page.</param>
    /// <param name="info">The transaction's info.</param>
    /// <returns>The transfers that matched an event, and the entries that did not.</returns>
    private static (List<EthereumTransfer> Placed, List<TronTokenEntry> Left) Place(
        IEnumerable<TronTokenEntry> entries, TronTransactionInfo info)
    {
        var block = new EthereumBlockReference(info.Height, null, info.Time);
        var unused = info.Tokens.ToList();
        var placed = new List<EthereumTransfer>();
        var left = new List<TronTokenEntry>();
        foreach (var entry in entries)
        {
            var log = unused.FirstOrDefault(log => log.Contract.Value == entry.Asset.Contract
                && log.From == entry.From && log.To == entry.To && log.Units == entry.Units);
            if (log is null)
            {
                left.Add(entry);
                continue;
            }

            unused.Remove(log);
            placed.Add(new EthereumTransfer(entry.Transaction, EthereumTransferKind.Token, entry.From, entry.To,
                new AssetAmount(entry.Asset, entry.Units), log.Position, block));
        }

        return (placed, left);
    }

    /// <summary>
    /// Puts a transaction together: the TRX it sent of its own, the TRX its contracts sent and its TRC-20 transfers, in
    /// the chain's order. Only the tokens Chainsaw knows are counted in a whole transaction, since TRON's API gives a
    /// token's decimals only in an address's token history; another token is noted and left out.
    /// </summary>
    /// <param name="raw">The transaction.</param>
    /// <param name="info">What it did in its block, or <see langword="null"/> while it is unconfirmed.</param>
    /// <param name="block">Its block, or <see langword="null"/> while unconfirmed.</param>
    /// <param name="parts">Where a token left out is noted.</param>
    /// <returns>The transaction.</returns>
    private static EthereumTransaction Assemble(
        TronTransactionRaw raw, TronTransactionInfo? info, EthereumBlockReference? block, LookupParts parts)
    {
        var outcome = OutcomeOf(raw, info);
        var transfers = new List<EthereumTransfer>();
        if (outcome != EthereumOutcome.Failed && !raw.Value.IsZero && raw.To is { } receiver)
        {
            // While pending this is only what the transaction would send; the lookup says it is unconfirmed.
            transfers.Add(new EthereumTransfer(raw.Hash, EthereumTransferKind.Native, raw.Owner, receiver,
                new AssetAmount(Asset.Tronix, raw.Value), 0, block));
        }

        if (outcome == EthereumOutcome.Succeeded && info is not null)
        {
            transfers.AddRange(info.Calls.Select(call => new EthereumTransfer(raw.Hash, EthereumTransferKind.Internal,
                call.From, call.To, new AssetAmount(Asset.Tronix, call.Value), call.Position, block)));
            foreach (var log in info.Tokens)
            {
                if (Known(log.Contract) is { } asset)
                {
                    transfers.Add(new EthereumTransfer(raw.Hash, EthereumTransferKind.Token, log.From, log.To,
                        new AssetAmount(asset, log.Units), log.Position, block));
                }
                else
                {
                    parts.Missing($"Tokenen {log.Contract} har decimaler som källan inte ger i en hel tx; "
                        + "den räknas inte här, men i adressens tokenhistorik.");
                }
            }

            if (info.Unread > 0)
            {
                parts.Missing(string.Create(CultureInfo.InvariantCulture,
                    $"{info.Unread} Transfer-händelser i tx:ens logg gick inte att läsa som TRC-20; de räknas inte."));
            }
        }

        var fee = info?.Fee ?? System.Numerics.BigInteger.Zero;
        return new EthereumTransaction(raw.Hash, block, raw.Owner, raw.To, new AssetAmount(Asset.Tronix, raw.Value),
            new AssetAmount(Asset.Tronix, fee), outcome, transfers);
    }

    /// <summary>
    /// Says how a transaction ended: pending without info, failed when the chain says so or its contract call failed,
    /// otherwise succeeded.
    /// </summary>
    /// <param name="raw">The transaction.</param>
    /// <param name="info">Its info, or <see langword="null"/>.</param>
    /// <returns>The outcome.</returns>
    private static EthereumOutcome OutcomeOf(TronTransactionRaw raw, TronTransactionInfo? info)
    {
        if (info is null)
        {
            return EthereumOutcome.Pending;
        }

        return raw.Succeeded && !info.Failed ? EthereumOutcome.Succeeded : EthereumOutcome.Failed;
    }

    /// <summary>Gives a token Chainsaw knows by its contract (USDT), without asking the source.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns>The token, or <see langword="null"/>.</returns>
    private static Asset? Known(EthereumAddress contract)
    {
        var asset = KnownTokens.Canonical(Asset.Token(Chain.Tron, contract.Value, "token", 0));
        return KnownTokens.IssuerOf(asset) is null ? null : asset;
    }

    /// <summary>
    /// Says from a history page's own answer when it can no longer change: a page that goes back from a time far
    /// enough behind the source's clock rests only on final blocks. The newest page always changes.
    /// </summary>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/> for the newest.</param>
    /// <param name="serverTime">The source's clock when it answered, or <see langword="null"/>.</param>
    /// <returns>Block 0 (always deep) when settled, otherwise <see langword="null"/>.</returns>
    private long? SettledPage(long? atOrBelow, DateTimeOffset? serverTime) =>
        atOrBelow is { } start && serverTime is { } now
            && now - DateTimeOffset.FromUnixTimeMilliseconds(start) >= _options.SettledAge
            ? 0L
            : null;

    /// <summary>The path of a page of an address's history.</summary>
    /// <param name="address">The address.</param>
    /// <param name="list">The list under the address: <c>transactions</c> or <c>transactions/trc20</c>.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/>.</param>
    /// <param name="size">How many entries the page asks for.</param>
    /// <returns>The path.</returns>
    private static string HistoryPath(EthereumAddress address, string list, long? atOrBelow, int size) =>
        string.Create(CultureInfo.InvariantCulture,
            $"v1/accounts/{address.Value}/{list}?order_by=block_timestamp,desc&limit={size}")
        + (atOrBelow is { } start ? "&max_timestamp=" + start.ToString(CultureInfo.InvariantCulture) : string.Empty);

    /// <summary>The subject of a page: the address, where it starts and the page size.</summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/>.</param>
    /// <param name="size">How many entries the page asks for.</param>
    /// <returns>The subject.</returns>
    private static string PageSubject(EthereumAddress address, long? atOrBelow, int size) =>
        // The page size is part of the subject, so that a page read with another size is never taken for a full one.
        string.Create(CultureInfo.InvariantCulture,
            $"{address.Value}/{atOrBelow?.ToString(CultureInfo.InvariantCulture) ?? "newest"}/{size}");

    /// <summary>The lookup of a page of an address's transactions.</summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/>.</param>
    /// <param name="size">How many entries the page asks for.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TronHistoryPage> HistoryQuery(EthereumAddress address, long? atOrBelow, int size)
    {
        var subject = PageSubject(address, atOrBelow, size);
        TronHistoryPage Parse(byte[] body) => TronGridParser.ParseHistory(body, address, atOrBelow, size);
        return new LookupQuery<TronHistoryPage>(
            TronResponseKinds.AddressTransactions,
            subject,
            HistoryPath(address, "transactions", atOrBelow, size),
            Parse,
            page => Index(page.Page.Transfers.Select(transfer => transfer.Block?.Height)
                .Concat(page.Page.Fees.Select(fee => fee.Block?.Height))),
            page => page.Page.IsCutShort ? null : SettledPage(atOrBelow, page.ServerTime),
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.AddressTransactions, subject), Parse));
    }

    /// <summary>The lookup of a page of an address's TRC-20 transfers.</summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/>.</param>
    /// <param name="size">How many entries the page asks for.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TronTokenPage> TokenHistoryQuery(EthereumAddress address, long? atOrBelow, int size)
    {
        var subject = PageSubject(address, atOrBelow, size);
        TronTokenPage Parse(byte[] body) => TronGridParser.ParseTokenHistory(body, address, atOrBelow, size);
        return new LookupQuery<TronTokenPage>(
            TronResponseKinds.AddressTokens,
            subject,
            HistoryPath(address, "transactions/trc20", atOrBelow, size),
            Parse,
            _ => ResponseIndex.None,
            page => page.IsCutShort || page.Unread > 0 ? null : SettledPage(atOrBelow, page.ServerTime),
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.AddressTokens, subject), Parse));
    }

    /// <summary>
    /// The lookup of a transaction as it was signed and how it ended, which is final only when the block its info
    /// puts it in is final: a result read while that block was shallow is asked for again, and so is one whose block
    /// is not known (a pending transaction, or info that could not be had).
    /// </summary>
    /// <param name="hash">The transaction.</param>
    /// <param name="height">The block its info puts it in, or <see langword="null"/> when not known.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TronTransactionRaw> TransactionQuery(EthereumTxHash hash, long? height)
    {
        TronTransactionRaw Parse(byte[] body) => TronGridParser.ParseTransaction(body, hash);
        return new LookupQuery<TronTransactionRaw>(
            TronResponseKinds.Transaction,
            hash.Value,
            "wallet/gettransactionbyid?value=" + hash.Value,
            Parse,
            _ => ResponseIndex.None,
            raw => raw.Result is null ? null : height,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.Transaction, hash.Value), Parse));

    }

    /// <summary>The lookup of what a transaction did in its block, which cannot change once the block is final.</summary>
    /// <param name="hash">The transaction.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TronTransactionInfo> InfoQuery(EthereumTxHash hash)
    {
        TronTransactionInfo Parse(byte[] body) => TronGridParser.ParseTransactionInfo(body, hash);
        return new LookupQuery<TronTransactionInfo>(
            TronResponseKinds.TransactionInfo,
            hash.Value,
            "wallet/gettransactioninfobyid?value=" + hash.Value,
            Parse,
            info => Index([info.Height]),
            info => info.Height,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.TransactionInfo, hash.Value), Parse));
    }

    /// <summary>The lookup of the newest block, which always changes.</summary>
    /// <returns>The query.</returns>
    private LookupQuery<ChainTip> TipQuery() => new(
        TronResponseKinds.Tip,
        string.Empty,
        TronGridApi.NewestBlockPath,
        TronGridParser.ParseTip,
        tip => new ResponseIndex([], tip.Height),
        _ => null,
        FindStored: reader => SourceLookups.StoredAs(
            _lookups.Store.Latest(reader, TronResponseKinds.Tip, string.Empty), TronGridParser.ParseTip));

    /// <summary>
    /// Asks for the newest block when the newest known one is older than <see cref="TipFreshness"/>, so that an answer
    /// whose block has become final can be taken from the case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>A task that completes when the height is fresh or could not be had.</returns>
    private async Task RefreshHeightAsync(ICase @case, CancellationToken cancellationToken)
    {
        var tip = TipQuery();
        var (stored, _) = await _lookups.ReadCaseAsync(@case, tip, cancellationToken).ConfigureAwait(false);
        if (stored is { } known && _clock.UtcNow - known.Response.Origin.ReceivedUtc < TipFreshness)
        {
            return;
        }

        await _lookups.FetchAsync(@case, tip, stored, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compares a transaction's sealed info answers by the rule every chain shares (<see cref="BlockChangeRule"/>):
    /// each answer with its block's height and, from the block fetched with it, the block's id, and the answers that the
    /// source did not know it in a block.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="hash">The transaction.</param>
    /// <returns>The change, or <see langword="null"/> when its block is the same or it is not confirmed.</returns>
    private BlockChange? BlockChangeOf(ICaseReader reader, EthereumTxHash hash)
    {
        var store = _lookups.Store;
        var seen = store.All(reader, TronResponseKinds.TransactionInfo, hash.Value)
            .Select(response => SourceLookups.StoredAs(
                response, body => TronGridParser.ParseTransactionInfo(body, hash)))
            .OfType<(TronTransactionInfo Value, StoredResponse Response)>()
            .Select(found => Answer(found.Response.Origin, found.Value.Height, BlockId(reader, found), false));
        var gone = store.NotFoundAnswers(reader, TronResponseKinds.TransactionInfo, hash.Value)
            .Where(response => SaysNotFound(response, hash))
            .Select(response => Answer(response.Origin, null, null, true));
        return BlockChangeRule.Compare(seen.Concat(gone), store, reader) is { } change
            ? new BlockChange(hash.Value, change.Kind, change.Before, change.Now) { Chain = Chain.Tron }
            : null;
    }

    /// <summary>Reads the id of the block a transaction's info put it in, from the block fetched with it.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="found">The transaction's info.</param>
    /// <returns>The id, or <see langword="null"/> when the case has no answer for the block.</returns>
    private string? BlockId(ICaseReader reader, (TronTransactionInfo Value, StoredResponse Response) found)
    {
        var height = found.Value.Height;
        var subject = height.ToString(CultureInfo.InvariantCulture);
        var block = _lookups.Store.Nearest(reader, TronResponseKinds.Block, subject, found.Response.Origin.Sequence);
        return SourceLookups.StoredAs(block, body => TronGridParser.ParseBlock(body, height))?.Value.Hash;
    }

    /// <summary>Says whether a noted answer really says that the source does not know the transaction in a block.</summary>
    /// <param name="response">The sealed answer.</param>
    /// <param name="hash">The transaction asked about.</param>
    /// <returns><see langword="true"/> when it says so.</returns>
    private static bool SaysNotFound(StoredResponse response, EthereumTxHash hash)
    {
        if (response.Origin.StatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }

        try
        {
            TronGridParser.ParseTransactionInfo(response.Body, hash);
            return false;
        }
        catch (SourceNotFoundException)
        {
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Makes one answer for the comparison.</summary>
    /// <param name="origin">The sealed answer.</param>
    /// <param name="height">The block's height it gave, or <see langword="null"/>.</param>
    /// <param name="blockId">The block's id, or <see langword="null"/> when not known.</param>
    /// <param name="isGone">Whether it said the source does not know the transaction in a block.</param>
    /// <returns>The answer.</returns>
    private static BlockAnswer Answer(ResponseOrigin origin, long? height, string? blockId, bool isGone) =>
        new(
            new BlockSighting(height, blockId, origin.ReceivedLocal, origin.Sequence)
            {
                Endpoint = origin.Endpoint.Length > 0 ? origin.Endpoint : origin.Source,
            },
            origin.EndpointId.ToString("D"),
            isGone);

    /// <summary>Gives what an answer adds to the indexes: its highest block.</summary>
    /// <param name="heights">The answer's blocks.</param>
    /// <returns>The index entry.</returns>
    private static ResponseIndex Index(IEnumerable<long?> heights) => new([], heights.Max());

    /// <summary>Carries a lookup without a value over to another type.</summary>
    /// <typeparam name="T">The type asked for.</typeparam>
    /// <typeparam name="TFrom">The type looked up.</typeparam>
    /// <param name="lookup">The lookup.</param>
    /// <returns>The same answer without a value.</returns>
    private static BitcoinLookup<T> Without<T, TFrom>(BitcoinLookup<TFrom> lookup)
        where T : class
        where TFrom : class =>
        new(lookup.Status, null, lookup.Origins, lookup.Problem) { Notes = lookup.Notes };

    /// <summary>
    /// Gathers the parts of an answer built of several lookups: their sealed responses, and what is missing or may be
    /// outdated, so that an answer that could not be read whole says so instead of looking complete.
    /// </summary>
    /// <param name="status">The main lookup's status.</param>
    /// <param name="origins">The main lookup's sealed responses.</param>
    /// <param name="notes">The main lookup's notes.</param>
    /// <param name="problem">The main lookup's problem.</param>
    private sealed class LookupParts(
        LookupStatus status, IEnumerable<ResponseOrigin> origins, IEnumerable<string> notes, string? problem)
    {
        /// <summary>The sealed responses of every part.</summary>
        private readonly List<ResponseOrigin> _origins = [.. origins];

        /// <summary>What the user should know: missing parts and notes.</summary>
        private readonly List<string> _notes = [.. notes];

        /// <summary>Why a part may be outdated.</summary>
        private string? _problem = problem;

        /// <summary>The status: the main one, or outdated when a part is.</summary>
        private LookupStatus _status = status;

        /// <summary>Whether every part could be read.</summary>
        private bool _complete = true;

        /// <summary>Takes a part's responses and notes, and whether it may be outdated, without its value.</summary>
        /// <typeparam name="T">The part.</typeparam>
        /// <param name="lookup">The part's lookup.</param>
        public void Take<T>(BitcoinLookup<T> lookup)
            where T : class
        {
            _origins.AddRange(lookup.Origins);
            _notes.AddRange(lookup.Notes);
            if (lookup.Value is not null && lookup.Status == LookupStatus.FromCaseMayBeOutdated)
            {
                _status = _status == LookupStatus.NotInCase ? _status : LookupStatus.FromCaseMayBeOutdated;
                _problem ??= lookup.Problem;
            }
        }

        /// <summary>Adds a part's lookup and gives its value.</summary>
        /// <typeparam name="T">The part.</typeparam>
        /// <param name="lookup">The part's lookup.</param>
        /// <param name="what">What the part is, in Swedish, for the note when it is missing.</param>
        /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
        public T? Add<T>(BitcoinLookup<T> lookup, string what)
            where T : class
        {
            Take(lookup);
            if (lookup.Value is null)
            {
                Missing($"{what} kunde inte läsas: {lookup.Problem}".TrimEnd(' ', ':'));
            }

            return lookup.Value;
        }

        /// <summary>Notes a part that is missing, which makes the answer incomplete.</summary>
        /// <param name="text">What is missing, in Swedish.</param>
        public void Missing(string text)
        {
            _notes.Add(text);
            _complete = false;
        }

        /// <summary>Notes something the user should know that leaves the answer complete.</summary>
        /// <param name="text">The note, in Swedish.</param>
        public void Note(string text) => _notes.Add(text);

        /// <summary>Gives the transaction's answer with every part's responses and notes.</summary>
        /// <param name="transaction">The transaction as it could be put together.</param>
        /// <returns>The answer.</returns>
        public BitcoinLookup<EthereumTransaction> Result(EthereumTransaction transaction) =>
            new(_status, transaction with { IsComplete = _complete }, Origins(), _problem) { Notes = Notes() };

        /// <summary>Gives a page's answer with every part's responses and notes.</summary>
        /// <param name="page">The page.</param>
        /// <returns>The answer.</returns>
        public BitcoinLookup<EthereumAddressPage> Page(EthereumAddressPage page) =>
            new(_status, page, Origins(), _problem) { Notes = Notes() };

        /// <summary>Gives every part's sealed responses, each once, oldest first.</summary>
        /// <returns>The responses.</returns>
        private List<ResponseOrigin> Origins() =>
            [.. _origins.DistinctBy(origin => origin.Sequence).OrderBy(origin => origin.Sequence)];

        /// <summary>Gives the notes, each once.</summary>
        /// <returns>The notes.</returns>
        private List<string> Notes() => [.. _notes.Distinct()];
    }
}

/// <summary>The kinds of TRON request, stored with each response so that it can be found again.</summary>
internal static class TronResponseKinds
{
    /// <summary>A transaction as it was signed, <c>wallet/gettransactionbyid</c>.</summary>
    public const string Transaction = "tron.tx";

    /// <summary>What a transaction did in its block, <c>wallet/gettransactioninfobyid</c>.</summary>
    public const string TransactionInfo = "tron.tx-info";

    /// <summary>A page of an address's transactions, <c>v1/accounts/:address/transactions</c>.</summary>
    public const string AddressTransactions = "tron.address-txs";

    /// <summary>A page of an address's TRC-20 transfers, <c>v1/accounts/:address/transactions/trc20</c>.</summary>
    public const string AddressTokens = "tron.address-trc20";

    /// <summary>The block at a height, <c>wallet/getblock?id_or_num=</c>.</summary>
    public const string Block = "tron.block";

    /// <summary>The newest block, <c>wallet/getblock?detail=false</c>.</summary>
    public const string Tip = "tron.tip";

    /// <summary>What an address holds, <c>v1/accounts/:address</c>.</summary>
    public const string Account = "tron.account";
}
