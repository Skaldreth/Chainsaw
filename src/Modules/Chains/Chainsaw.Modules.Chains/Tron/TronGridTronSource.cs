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

    /// <summary>The time now, to decide whether the known height is fresh.</summary>
    private readonly IClock _clock;

    /// <summary>The settings.</summary>
    private readonly TronSourceOptions _options;

    /// <summary>Answers the lookups through the endpoints and the case.</summary>
    private readonly SourceLookups _lookups;

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
    public async Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
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
                var page = await _lookups.LookupAsync(@case, HistoryQuery(address, atOrBelow), mode, cancellationToken)
                    .ConfigureAwait(false);
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
                return await TokenHistoryAsync(@case, address, atOrBelow, mode, cancellationToken)
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

        var raw = await _lookups.LookupAsync(@case, TransactionQuery(hash), mode, cancellationToken)
            .ConfigureAwait(false);
        if (raw.Value is not { } transaction)
        {
            return Without<EthereumTransaction, TronTransactionRaw>(raw);
        }

        var parts = new LookupParts(raw.Status, raw.Origins, raw.Notes, raw.Problem);
        var infoLookup = await _lookups.LookupAsync(@case, InfoQuery(hash), mode, cancellationToken)
            .ConfigureAwait(false);
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
            reader => SourceLookups.StoredAs(_lookups.Store.Latest(reader, TronResponseKinds.Block, subject), Parse),
            block => block.Height);
        return _lookups.LookupAsync(@case, query, mode, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, CancellationToken cancellationToken = default)
    {
        var tip = await GetTipAsync(@case, cancellationToken).ConfigureAwait(false);
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
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.Account, address.Value), Parse),
            _ => null);
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
    /// place, so that the page holds the same movements a whole transaction gives. A transfer whose info could not be
    /// had, or whose event the info does not hold, is not counted now and keeps the page from being final.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/> for the newest.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    private async Task<BitcoinLookup<EthereumAddressPage>> TokenHistoryAsync(
        ICase @case, EthereumAddress address, long? atOrBelow, LookupMode mode, CancellationToken cancellationToken)
    {
        var lookup = await _lookups.LookupAsync(@case, TokenHistoryQuery(address, atOrBelow), mode, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is not { } raw)
        {
            return Without<EthereumAddressPage, TronTokenPage>(lookup);
        }

        var parts = new LookupParts(lookup.Status, lookup.Origins, lookup.Notes, lookup.Problem);
        var transfers = new List<EthereumTransfer>();
        var unread = raw.Unread;
        foreach (var group in raw.Entries.GroupBy(entry => entry.Transaction))
        {
            var info = await _lookups.LookupAsync(@case, InfoQuery(group.Key), mode, cancellationToken)
                .ConfigureAwait(false);
            parts.Take(info);
            if (info.Value is null)
            {
                unread += group.Count();
                continue;
            }

            var placed = Place(group, info.Value);
            transfers.AddRange(placed);
            unread += group.Count() - placed.Count;
        }

        var page = new EthereumAddressPage(
            address, EthereumHistory.Tokens, atOrBelow, transfers, [], raw.NextAtOrBelow, raw.IsCutShort, false)
        {
            Skipped = raw.Skipped,
            Unread = unread,
        };
        return parts.Page(page);
    }

    /// <summary>
    /// Gives the token history's transfers of one transaction their block and place: each is matched, in order, with an
    /// unused <c>Transfer</c> event in the transaction's info from the same contract, between the same addresses and
    /// of the same amount, and takes the event's index.
    /// </summary>
    /// <param name="entries">The transaction's transfers on the page.</param>
    /// <param name="info">The transaction's info.</param>
    /// <returns>The transfers that matched an event.</returns>
    private static List<EthereumTransfer> Place(IEnumerable<TronTokenEntry> entries, TronTransactionInfo info)
    {
        var block = new EthereumBlockReference(info.Height, null, info.Time);
        var unused = info.Tokens.ToList();
        var placed = new List<EthereumTransfer>();
        foreach (var entry in entries)
        {
            var log = unused.FirstOrDefault(log => log.Contract.Value == entry.Asset.Contract
                && log.From == entry.From && log.To == entry.To && log.Units == entry.Units);
            if (log is null)
            {
                continue;
            }

            unused.Remove(log);
            placed.Add(new EthereumTransfer(entry.Transaction, EthereumTransferKind.Token, entry.From, entry.To,
                new AssetAmount(entry.Asset, entry.Units), log.Position, block));
        }

        return placed;
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
    /// <returns>The path.</returns>
    private string HistoryPath(EthereumAddress address, string list, long? atOrBelow) =>
        string.Create(CultureInfo.InvariantCulture,
            $"v1/accounts/{address.Value}/{list}?order_by=block_timestamp,desc&limit={_options.PageSize}")
        + (atOrBelow is { } start ? "&max_timestamp=" + start.ToString(CultureInfo.InvariantCulture) : string.Empty);

    /// <summary>The subject of a page: the address, where it starts and the page size.</summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/>.</param>
    /// <returns>The subject.</returns>
    private string PageSubject(EthereumAddress address, long? atOrBelow) =>
        // The page size is part of the subject, so that a page read with another size is never taken for a full one.
        string.Create(CultureInfo.InvariantCulture,
            $"{address.Value}/{atOrBelow?.ToString(CultureInfo.InvariantCulture) ?? "newest"}/{_options.PageSize}");

    /// <summary>The lookup of a page of an address's transactions.</summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/>.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TronHistoryPage> HistoryQuery(EthereumAddress address, long? atOrBelow)
    {
        var subject = PageSubject(address, atOrBelow);
        TronHistoryPage Parse(byte[] body) =>
            TronGridParser.ParseHistory(body, address, atOrBelow, _options.PageSize);
        return new LookupQuery<TronHistoryPage>(
            TronResponseKinds.AddressTransactions,
            subject,
            HistoryPath(address, "transactions", atOrBelow),
            Parse,
            page => Index(page.Page.Transfers.Select(transfer => transfer.Block?.Height)
                .Concat(page.Page.Fees.Select(fee => fee.Block?.Height))),
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.AddressTransactions, subject), Parse),
            page => page.Page.IsCutShort ? null : SettledPage(atOrBelow, page.ServerTime));
    }

    /// <summary>The lookup of a page of an address's TRC-20 transfers.</summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/>.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TronTokenPage> TokenHistoryQuery(EthereumAddress address, long? atOrBelow)
    {
        var subject = PageSubject(address, atOrBelow);
        TronTokenPage Parse(byte[] body) =>
            TronGridParser.ParseTokenHistory(body, address, atOrBelow, _options.PageSize);
        return new LookupQuery<TronTokenPage>(
            TronResponseKinds.AddressTokens,
            subject,
            HistoryPath(address, "transactions/trc20", atOrBelow),
            Parse,
            _ => ResponseIndex.None,
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.AddressTokens, subject), Parse),
            page => page.IsCutShort || page.Unread > 0 ? null : SettledPage(atOrBelow, page.ServerTime));
    }

    /// <summary>
    /// The lookup of a transaction as it was signed, which cannot change once the chain has said how it ended; its
    /// block is in its info, which is asked again until the block is final.
    /// </summary>
    /// <param name="hash">The transaction.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TronTransactionRaw> TransactionQuery(EthereumTxHash hash)
    {
        TronTransactionRaw Parse(byte[] body) => TronGridParser.ParseTransaction(body, hash);
        return new LookupQuery<TronTransactionRaw>(
            TronResponseKinds.Transaction,
            hash.Value,
            "wallet/gettransactionbyid?value=" + hash.Value,
            Parse,
            _ => ResponseIndex.None,
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.Transaction, hash.Value), Parse),
            raw => raw.Result is null ? null : 0L);
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
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, TronResponseKinds.TransactionInfo, hash.Value), Parse),
            info => info.Height);
    }

    /// <summary>The lookup of the newest block, which always changes.</summary>
    /// <returns>The query.</returns>
    private LookupQuery<ChainTip> TipQuery() => new(
        TronResponseKinds.Tip,
        string.Empty,
        TronGridApi.NewestBlockPath,
        TronGridParser.ParseTip,
        tip => new ResponseIndex([], tip.Height),
        reader => SourceLookups.StoredAs(
            _lookups.Store.Latest(reader, TronResponseKinds.Tip, string.Empty), TronGridParser.ParseTip),
        _ => null);

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
