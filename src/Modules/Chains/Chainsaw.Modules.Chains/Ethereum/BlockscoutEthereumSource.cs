using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Ethereum from Blockscout's API, asked only through <see cref="IExitHttp"/> by <see cref="SourceLookups"/>: every
/// response is sealed in the case before it is used, what rests on a final block (64 deep) is taken from the case
/// without asking, and without an exit the case answers and says what is missing.
/// </summary>
internal sealed class BlockscoutEthereumSource : IEthereumSource
{
    /// <summary>The chain and network in the sealed table.</summary>
    public const string ChainName = "ethereum-mainnet";

    /// <summary>How old the newest known height may be before a lookup that depends on depth asks again.</summary>
    private static readonly TimeSpan TipFreshness = TimeSpan.FromMinutes(1);

    /// <summary>The time now, to decide whether the known height is fresh.</summary>
    private readonly IClock _clock;

    /// <summary>The settings.</summary>
    private readonly EthereumSourceOptions _options;

    /// <summary>Answers the lookups through the endpoints and the case.</summary>
    private readonly SourceLookups _lookups;

    /// <summary>Creates the source.</summary>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <param name="sources">
    /// The chains' endpoints, of which Ethereum's are asked, each the only way it may be: through the exit, or straight
    /// to the user's own node.
    /// </param>
    /// <exception cref="ArgumentException">The depth or the page size is less than 1.</exception>
    public BlockscoutEthereumSource(
        IOperatorProfileStore operators, IClock clock, EthereumSourceOptions options,
        TimeZoneInfo timeZone, ChainSources sources)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PageSize < 1)
        {
            throw new ArgumentException("A page has at least one entry.", nameof(options));
        }

        _clock = clock;
        _options = options;
        _lookups = new SourceLookups(
            Chain.Ethereum, "Ethereum", operators, sources, new RawResponseStore(timeZone, ChainName),
            options.SafeConfirmations,
            async (_, @case, cancellationToken) => await RefreshHeightAsync(@case, cancellationToken)
                .ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        if (!Enum.IsDefined(history))
        {
            throw new ArgumentOutOfRangeException(nameof(history), history, "Unknown history.");
        }

        if (atOrBelow is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(atOrBelow), atOrBelow, "A block number is not negative.");
        }

        var (kind, action) = history switch
        {
            EthereumHistory.Transactions => (EthereumResponseKinds.AddressTransactions, "txlist"),
            EthereumHistory.Internal => (EthereumResponseKinds.AddressInternal, "txlistinternal"),
            _ => (EthereumResponseKinds.AddressTokens, "tokentx"),
        };
        var below = atOrBelow?.ToString(CultureInfo.InvariantCulture);
        // The page size is part of the subject, so that a page read with another size is never taken for a full one.
        var subject = string.Create(CultureInfo.InvariantCulture,
            $"{address.Value}/{below ?? "newest"}/{_options.PageSize}");
        var path = string.Create(CultureInfo.InvariantCulture,
            $"?module=account&action={action}&address={address.Value}&sort=desc&page=1&offset={_options.PageSize}")
            + (below is null ? string.Empty : "&endblock=" + below);
        EthereumAddressPage Parse(byte[] body) =>
            BlockscoutParser.ParseHistory(body, address, history, atOrBelow, _options.PageSize);
        var query = new LookupQuery<EthereumAddressPage>(
            kind,
            subject,
            path,
            Parse,
            page => Index(page.Transfers.Select(transfer => transfer.Block?.Height)),
            reader => SourceLookups.StoredAs(_lookups.Store.Latest(reader, kind, subject), Parse),
            page => atOrBelow is null || page.IsIncomplete || page.IsCutShort || page.Unread > 0 ? null : atOrBelow);
        return _lookups.LookupAsync(@case, query, mode, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(hash);
        var info = await _lookups.LookupAsync(@case, TransactionQuery(hash), mode, cancellationToken)
            .ConfigureAwait(false);
        if (info.Value is not { } transaction)
        {
            return Without<EthereumTransaction, TransactionInfo>(info);
        }

        var parts = new TransactionParts(info);
        if (transaction.Outcome == EthereumOutcome.Pending)
        {
            parts.Missing(
                "Tx:en är obekräftad: om den lyckas och vad den flyttar avgörs först när den körs i ett block.");
        }

        if (transaction.LogsCut)
        {
            parts.Missing("Källan gav inte alla tx:ens loggar, så tokenöverföringar kan saknas. Tx:en frågas om igen.");
        }

        if (transaction.Unread > 0)
        {
            parts.Missing(string.Create(CultureInfo.InvariantCulture,
                $"{transaction.Unread} Transfer-händelser i tx:ens logg gick inte att läsa som ERC-20; ")
                + "de räknas inte.");
        }

        if (transaction.Outcome != EthereumOutcome.Failed && !transaction.Value.IsZero && transaction.Receiver is null)
        {
            parts.Missing("Källan sa inte vem som tog emot tx:ens ether (ett kontrakt som skapades); den räknas inte.");
        }

        var calls = transaction.Outcome == EthereumOutcome.Succeeded
            ? parts.Add(await _lookups.LookupAsync(@case, InternalQuery(hash, transaction.Height), mode,
                cancellationToken).ConfigureAwait(false), "Tx:ens interna tx")
            : new InternalCalls([], false);
        if (calls?.IsIncomplete == true)
        {
            parts.Missing("Källan har inte läst alla tx:ens interna tx än.");
        }

        EthereumBlockReference? block = null;
        if (transaction.Height is { } height)
        {
            block = parts.Add(await GetBlockAsync(@case, height, mode, cancellationToken).ConfigureAwait(false),
                "Blockets hash");
        }

        var tokens = new Dictionary<EthereumAddress, Asset>();
        foreach (var contract in transaction.Tokens.Select(log => log.Contract).Distinct())
        {
            if (Known(contract) is { } known)
            {
                tokens[contract] = known;
            }
            else if (parts.Add(await _lookups.LookupAsync(@case, TokenQuery(contract), mode, cancellationToken)
                .ConfigureAwait(false), $"Tokenen {contract}") is { } token)
            {
                if (token.Asset is { } asset)
                {
                    tokens[contract] = asset;
                }
                else
                {
                    parts.Missing($"Tokenen {contract} har inga decimaler Chainsaw kan räkna i; den räknas inte.");
                }
            }
        }

        return parts.Result(Assemble(transaction, calls?.Calls ?? [], tokens, block
            ?? (transaction.Height is { } at ? new EthereumBlockReference(at, null, transaction.Time!.Value) : null)));
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
        ICase @case, long height, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var subject = height.ToString(CultureInfo.InvariantCulture);
        EthereumBlockReference Parse(byte[] body) => BlockscoutParser.ParseBlock(body, height);
        var query = new LookupQuery<EthereumBlockReference>(
            EthereumResponseKinds.Block,
            subject,
            BlockscoutApi.BlockPath(height),
            Parse,
            block => new ResponseIndex([], block.Height),
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, EthereumResponseKinds.Block, subject), Parse),
            block => block.Height);
        return _lookups.LookupAsync(@case, query, mode, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return _lookups.LookupAsync(@case, TipQuery(), LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var query = new LookupQuery<ChainTip>(
            EthereumResponseKinds.TipBlock,
            string.Empty,
            BlockscoutApi.NewestBlockPath,
            BlockscoutParser.ParseNewestBlock,
            tip => new ResponseIndex([], tip.Height),
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, EthereumResponseKinds.TipBlock, string.Empty),
                BlockscoutParser.ParseNewestBlock),
            _ => null);
        return _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
        ICase @case, EthereumAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        var ether = await _lookups.LookupAsync(@case, BalanceQuery(address), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (ether.Value is not { } wei)
        {
            return Without<EthereumBalance, EtherBalance>(ether);
        }

        var tokens = await _lookups.LookupAsync(
            @case, TokenBalancesQuery(address), LookupMode.AsNeeded, cancellationToken).ConfigureAwait(false);
        var origins = ether.Origins.Concat(tokens.Origins).DistinctBy(origin => origin.Sequence)
            .OrderBy(origin => origin.Sequence).ToList();
        var balance = new EthereumBalance(address, new AssetAmount(Asset.Ether, wei.Wei), tokens.Value?.Tokens ?? [])
        {
            Skipped = tokens.Value?.Skipped ?? 0,
            IsComplete = tokens.Value is { IsIncomplete: false },
        };
        if (tokens.Value is null)
        {
            var note = $"Adressens tokens kunde inte läsas, så bara dess ether visas. {tokens.Problem}".TrimEnd();
            return new BitcoinLookup<EthereumBalance>(ether.Status, balance, origins, ether.Problem)
            {
                Notes = [.. ether.Notes, .. tokens.Notes, note],
            };
        }

        IReadOnlyList<string> notes = tokens.Value.IsIncomplete
            ? [.. ether.Notes, .. tokens.Notes, "Källan har inte läst alla adressens tokens än; fler kan finnas."]
            : [.. ether.Notes, .. tokens.Notes];

        // Either answer taken from the case makes the whole balance one that may be outdated.
        var outdated = ether.Status == LookupStatus.FromCaseMayBeOutdated
            || tokens.Status == LookupStatus.FromCaseMayBeOutdated;
        return new BitcoinLookup<EthereumBalance>(
            outdated ? LookupStatus.FromCaseMayBeOutdated : ether.Status, balance, origins,
            ether.Problem ?? tokens.Problem)
        {
            Notes = notes,
        };
    }

    /// <summary>The lookup of an address's ether, which always changes.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The query.</returns>
    private LookupQuery<EtherBalance> BalanceQuery(EthereumAddress address) => new(
        EthereumResponseKinds.Balance,
        address.Value,
        "?module=account&action=balance&address=" + address.Value,
        BlockscoutParser.ParseBalance,
        _ => ResponseIndex.None,
        reader => SourceLookups.StoredAs(
            _lookups.Store.Latest(reader, EthereumResponseKinds.Balance, address.Value), BlockscoutParser.ParseBalance),
        _ => null);

    /// <summary>The lookup of an address's tokens, which always change.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TokenBalances> TokenBalancesQuery(EthereumAddress address) => new(
        EthereumResponseKinds.TokenBalances,
        address.Value,
        "?module=account&action=tokenlist&address=" + address.Value,
        BlockscoutParser.ParseTokenBalances,
        _ => ResponseIndex.None,
        reader => SourceLookups.StoredAs(
            _lookups.Store.Latest(reader, EthereumResponseKinds.TokenBalances, address.Value),
            BlockscoutParser.ParseTokenBalances),
        _ => null);

    /// <summary>Gives a token Chainsaw knows by its contract, without asking the source.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns>USDT or USDC, or <see langword="null"/>.</returns>
    private static Asset? Known(EthereumAddress contract)
    {
        var asset = KnownTokens.Canonical(Asset.Token(Chain.Ethereum, contract.Value, "token", 0));
        return KnownTokens.IssuerOf(asset) is null ? null : asset;
    }

    /// <summary>
    /// Puts a transaction together: its ether, its internal transactions and its tokens in the chain's order. A token
    /// whose decimals could not be had is left out (and the answer says the transaction is incomplete).
    /// </summary>
    /// <param name="info">The transaction as the source gave it.</param>
    /// <param name="calls">Its internal transactions that moved ether.</param>
    /// <param name="tokens">The tokens whose decimals are known, by contract.</param>
    /// <param name="block">Its block, or <see langword="null"/> while unconfirmed.</param>
    /// <returns>The transaction.</returns>
    private static EthereumTransaction Assemble(
        TransactionInfo info, IReadOnlyList<InternalCall> calls, IReadOnlyDictionary<EthereumAddress, Asset> tokens,
        EthereumBlockReference? block)
    {
        var transfers = new List<EthereumTransfer>();
        if (info.Outcome != EthereumOutcome.Failed && !info.Value.IsZero && info.Receiver is { } receiver)
        {
            // While pending this is only what the transaction would send; the lookup says it is unconfirmed.
            transfers.Add(new EthereumTransfer(info.Hash, EthereumTransferKind.Native, info.From, receiver,
                new AssetAmount(Asset.Ether, info.Value), 0, block));
        }

        if (info.Outcome == EthereumOutcome.Succeeded)
        {
            transfers.AddRange(calls.Select(call => new EthereumTransfer(info.Hash, EthereumTransferKind.Internal,
                call.From, call.To, new AssetAmount(Asset.Ether, call.Value), call.Position, block)));
            transfers.AddRange(info.Tokens.Where(log => tokens.ContainsKey(log.Contract)).Select(log =>
                new EthereumTransfer(info.Hash, EthereumTransferKind.Token, log.From, log.To,
                    new AssetAmount(tokens[log.Contract], log.Units), log.Position, block)));
        }

        return new EthereumTransaction(info.Hash, block, info.From, info.To, new AssetAmount(Asset.Ether, info.Value),
            new AssetAmount(Asset.Ether, info.Fee), info.Outcome, transfers);
    }

    /// <summary>The lookup of a transaction, which cannot change once its block is final.</summary>
    /// <param name="hash">The transaction.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TransactionInfo> TransactionQuery(EthereumTxHash hash)
    {
        TransactionInfo Parse(byte[] body) => BlockscoutParser.ParseTransaction(body, hash);
        return new LookupQuery<TransactionInfo>(
            EthereumResponseKinds.Transaction,
            hash.Value,
            "?module=transaction&action=gettxinfo&txhash=" + hash.Value,
            Parse,
            info => Index([info.Height]),
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, EthereumResponseKinds.Transaction, hash.Value), Parse),
            info => info.LogsCut ? null : info.Height);
    }

    /// <summary>The lookup of a transaction's internal transactions, which cannot change once its block is
    /// final.</summary>
    /// <param name="hash">The transaction.</param>
    /// <param name="height">Its block, or <see langword="null"/> while unconfirmed.</param>
    /// <returns>The query.</returns>
    private LookupQuery<InternalCalls> InternalQuery(EthereumTxHash hash, long? height)
    {
        InternalCalls Parse(byte[] body) => BlockscoutParser.ParseInternal(body, hash);
        return new LookupQuery<InternalCalls>(
            EthereumResponseKinds.TransactionInternal,
            hash.Value,
            "?module=account&action=txlistinternal&txhash=" + hash.Value,
            Parse,
            _ => Index([height]),
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, EthereumResponseKinds.TransactionInternal, hash.Value), Parse),
            calls => calls.IsIncomplete ? null : height);
    }

    /// <summary>
    /// The lookup of a token's decimals and symbol, which its contract never changes once the source has them.
    /// </summary>
    /// <param name="contract">The token's contract.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TokenInfo> TokenQuery(EthereumAddress contract)
    {
        TokenInfo Parse(byte[] body) => BlockscoutParser.ParseToken(body, contract);
        return new LookupQuery<TokenInfo>(
            EthereumResponseKinds.Token,
            contract.Value,
            "?module=token&action=getToken&contractaddress=" + contract.Value,
            Parse,
            _ => ResponseIndex.None,
            reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, EthereumResponseKinds.Token, contract.Value), Parse),
            // Decimals never change once the source has them; an answer without them (the source may not have read
            // a new token yet) is asked again. Block 0 is always deep.
            info => info.Asset is null ? null : 0L);
    }

    /// <summary>The lookup of the chain's height, which always changes.</summary>
    /// <returns>The query.</returns>
    private LookupQuery<EthereumTip> TipQuery() => new(
        EthereumResponseKinds.TipHeight,
        string.Empty,
        BlockscoutApi.HeightPath,
        BlockscoutParser.ParseHeight,
        tip => new ResponseIndex([], tip.Height),
        reader => SourceLookups.StoredAs(
            _lookups.Store.Latest(reader, EthereumResponseKinds.TipHeight, string.Empty), BlockscoutParser.ParseHeight),
        _ => null);

    /// <summary>
    /// Asks for the chain's height when the newest known one is older than <see cref="TipFreshness"/>, so that an
    /// answer whose block has become final can be taken from the case.
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
    /// Gives what an answer adds to the indexes: its highest block, which the chain has at least reached, so that the
    /// depth of what rests on it can be counted.
    /// </summary>
    /// <param name="heights">The answer's blocks; <see langword="null"/> for an unconfirmed one.</param>
    /// <returns>The index entry.</returns>
    private static ResponseIndex Index(IEnumerable<long?> heights) =>
        new([], heights.Max());

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
    /// Gathers the parts of a transaction's answer: their sealed responses, and what is missing or may be outdated,
    /// so that a transaction that could not be read whole says so instead of looking complete.
    /// </summary>
    /// <param name="main">The transaction's own lookup, with a value.</param>
    private sealed class TransactionParts(BitcoinLookup<TransactionInfo> main)
    {
        /// <summary>The sealed responses of every part.</summary>
        private readonly List<ResponseOrigin> _origins = [.. main.Origins];

        /// <summary>What the user should know: missing parts and notes.</summary>
        private readonly List<string> _notes = [.. main.Notes];

        /// <summary>Why a part may be outdated.</summary>
        private string? _problem = main.Problem;

        /// <summary>The status: the main one, or outdated when a part is.</summary>
        private LookupStatus _status = main.Status;

        /// <summary>Whether every part could be read.</summary>
        private bool _complete = true;

        /// <summary>Adds a part's lookup and gives its value.</summary>
        /// <typeparam name="T">The part.</typeparam>
        /// <param name="lookup">The part's lookup.</param>
        /// <param name="what">What the part is, in Swedish, for the note when it is missing.</param>
        /// <returns>The value, or <see langword="null"/> when it is missing.</returns>
        public T? Add<T>(BitcoinLookup<T> lookup, string what)
            where T : class
        {
            _origins.AddRange(lookup.Origins);
            _notes.AddRange(lookup.Notes);
            if (lookup.Value is null)
            {
                Missing($"{what} kunde inte läsas: {lookup.Problem}".TrimEnd(' ', ':'));
                return null;
            }

            if (lookup.Status == LookupStatus.FromCaseMayBeOutdated)
            {
                _status = _status == LookupStatus.NotInCase ? _status : LookupStatus.FromCaseMayBeOutdated;
                _problem ??= lookup.Problem;
            }

            return lookup.Value;
        }

        /// <summary>Notes a part that is missing, which makes the transaction incomplete.</summary>
        /// <param name="text">What is missing, in Swedish.</param>
        public void Missing(string text)
        {
            _notes.Add(text);
            _complete = false;
        }

        /// <summary>Gives the answer with every part's responses and notes.</summary>
        /// <param name="transaction">The transaction as it could be put together.</param>
        /// <returns>The answer.</returns>
        public BitcoinLookup<EthereumTransaction> Result(EthereumTransaction transaction) =>
            new(_status, transaction with { IsComplete = _complete },
                [.. _origins.DistinctBy(origin => origin.Sequence).OrderBy(origin => origin.Sequence)], _problem)
            {
                Notes = [.. _notes.Distinct()],
            };
    }
}

/// <summary>The kinds of Ethereum request, stored with each response so that it can be found again.</summary>
internal static class EthereumResponseKinds
{
    /// <summary>A transaction, <c>gettxinfo</c>.</summary>
    public const string Transaction = "ethereum.tx";

    /// <summary>A transaction's internal transactions, <c>txlistinternal&amp;txhash=</c>.</summary>
    public const string TransactionInternal = "ethereum.tx-internal";

    /// <summary>A token's decimals and symbol, <c>getToken</c>.</summary>
    public const string Token = "ethereum.token";

    /// <summary>A page of an address's transactions, <c>txlist</c>.</summary>
    public const string AddressTransactions = "ethereum.address-txs";

    /// <summary>A page of an address's internal transactions, <c>txlistinternal</c>.</summary>
    public const string AddressInternal = "ethereum.address-internal";

    /// <summary>A page of an address's token transfers, <c>tokentx</c>.</summary>
    public const string AddressTokens = "ethereum.address-tokens";

    /// <summary>The block at a height, <c>v2/blocks/:height</c>.</summary>
    public const string Block = "ethereum.block";

    /// <summary>The chain's height, <c>eth_block_number</c>.</summary>
    public const string TipHeight = "ethereum.tip-height";

    /// <summary>The newest block the source has read, with its hash, <c>v2/blocks?type=block</c>.</summary>
    public const string TipBlock = "ethereum.tip-block";

    /// <summary>An address's ether, <c>balance</c>.</summary>
    public const string Balance = "ethereum.balance";

    /// <summary>An address's ERC-20 tokens, <c>tokenlist</c>.</summary>
    public const string TokenBalances = "ethereum.token-balances";
}
