using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Solana from its JSON-RPC, asked only through <see cref="IExitHttp"/> by <see cref="SourceLookups"/>: every response
/// is sealed in the case before it is used, everything is asked with the commitment <c>finalized</c> (what the cluster
/// has rooted cannot be rolled back, so a transaction, a block or an older page is never fetched again), and without an
/// exit the case answers and says what is missing.
/// </summary>
/// <remarks>
/// <para>
/// An address's history is its signatures (<c>getSignaturesForAddress</c>), which say nothing of what moved, so each
/// transaction on a page is fetched too, sealed and kept (<c>getTransaction</c>); its movements are read from its
/// balances (<see cref="SolanaMovements"/>). A page costs as many lookups as it has entries, so pages are small.
/// </para>
/// <para>
/// A token is held in a token account, not by its owner's address, and a transfer to an existing token account does
/// not name the owner, so it is not in the owner's own signatures. The token history therefore reads the owner's token
/// accounts of the tokens Chainsaw knows (USDT and USDC, by their mints) as the node lists them now
/// (<c>getTokenAccountsByOwner</c>) and pages each account's signatures; a token account that has been closed is no
/// longer listed, and its history is then only found through the owner's own signatures.
/// </para>
/// </remarks>
internal sealed class SolanaRpcSource : ISolanaSource
{
    /// <summary>The chain and network in the sealed table.</summary>
    public const string ChainName = "solana-mainnet";

    /// <summary>The SPL Token program, which holds the classic tokens (USDT and USDC among them).</summary>
    public const string TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";

    /// <summary>What a page says when the page before it has not been read, so it cannot be asked for.</summary>
    private const string PageBeforeText =
        "Sidan går inte att be om innan sidan före den har lästs (Solana bläddrar efter signatur). Inget skickades.";

    /// <summary>The settings.</summary>
    private readonly SolanaSourceOptions _options;

    /// <summary>Answers the lookups through the endpoints and the case.</summary>
    private readonly SourceLookups _lookups;

    /// <summary>The pages read, so that an older page can be asked for after the newer.</summary>
    private readonly SolanaPageMemory _pages = new();

    /// <summary>Creates the source.</summary>
    /// <param name="operators">The operator profile.</param>
    /// <param name="options">The settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <param name="sources">The chains' endpoints, of which Solana's are asked.</param>
    /// <exception cref="ArgumentException">The depth or the page size is less than 1.</exception>
    public SolanaRpcSource(
        IOperatorProfileStore operators, SolanaSourceOptions options, TimeZoneInfo timeZone, ChainSources sources)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PageSize < 1)
        {
            throw new ArgumentException("A page has at least one entry.", nameof(options));
        }

        _options = options;

        // Every answer is finalized, so none is fetched again for its depth and the height never needs refreshing.
        _lookups = new SourceLookups(
            Chain.Solana, "Solana", operators, sources, new RawResponseStore(timeZone, ChainName),
            options.SafeConfirmations, (_, _, _) => Task.CompletedTask);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        CheckOnSolana(address);
        if (atOrBelow is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(atOrBelow), atOrBelow, "A slot is not negative.");
        }

        return history switch
        {
            EthereumHistory.Transactions =>
                await OwnHistoryAsync(@case, address, atOrBelow, mode, cancellationToken).ConfigureAwait(false),

            // A transfer of SOL by a program is in the balances of the transaction it is in, which the owner's own
            // history reads whole; there is no list of internal movements to ask for.
            EthereumHistory.Internal => new BitcoinLookup<EthereumAddressPage>(
                LookupStatus.FromCase, Empty(address, history, atOrBelow), [], null),
            EthereumHistory.Tokens =>
                await TokenHistoryAsync(@case, address, atOrBelow, mode, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(history), history, "Unknown history."),
        };
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        CheckOnSolana(hash);
        var raw = await _lookups.LookupAsync(@case, TransactionQuery(hash), mode, cancellationToken)
            .ConfigureAwait(false);
        if (raw.Value is not { } transaction)
        {
            return Without<EthereumTransaction, SolanaTransactionRaw>(raw);
        }

        var parts = Parts.Of(raw);
        var block = parts.Add(await GetBlockAsync(@case, transaction.Slot, mode, cancellationToken)
                .ConfigureAwait(false), "Slottens blockhash")
            ?? new EthereumBlockReference(transaction.Slot, null, transaction.Time);
        block = block with { TransactionIndex = transaction.Index };
        var moves = SolanaMovements.Read(transaction, block);

        foreach (var problem in moves.Problems)
        {
            parts.Missing(problem);
        }

        if (moves.Skipped > 0)
        {
            parts.Note("Tx:en flyttar tokens vars decimaler Chainsaw inte kan räkna i; de räknas inte.");
        }

        var sent = moves.Transfers
            .Where(transfer => transfer.Kind == EthereumTransferKind.Native && transfer.From == transaction.Payer)
            .Aggregate(AssetAmount.Zero(Asset.Sol), (sum, transfer) => sum + transfer.Amount);
        var result = new EthereumTransaction(
            hash, block, transaction.Payer, null, sent, new AssetAmount(Asset.Sol, transaction.Fee),
            transaction.Failed ? EthereumOutcome.Failed : EthereumOutcome.Succeeded, moves.Transfers);
        return parts.Result(result);
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
        ICase @case, long height, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var subject = height.ToString(CultureInfo.InvariantCulture);
        EthereumBlockReference Parse(byte[] body) => SolanaRpcParser.ParseBlock(body, height);
        var query = new LookupQuery<EthereumBlockReference>(
            SolanaResponseKinds.Block,
            subject,
            string.Empty,
            Parse,
            block => new ResponseIndex([], block.Height),
            block => block.Height,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, SolanaResponseKinds.Block, subject), Parse),
            Body: SolanaRpcApi.BlockCall(height));
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
            tip.Status, tip.Value is { } slot ? new EthereumTip(slot.Value) : null, tip.Origins, tip.Problem)
        {
            Notes = tip.Notes,
        };
    }

    /// <summary>
    /// Gets the newest finalized slot with its hash and time: the slot (<c>getSlot</c>), then its block
    /// (<c>getBlock</c>), since one call does not give both.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The newest block.</returns>
    public async Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var tip = await _lookups.LookupAsync(@case, TipQuery(), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (tip.Value is not { } slot)
        {
            return Without<ChainTip, WholeNumber>(tip);
        }

        var block = await GetBlockAsync(@case, slot.Value, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        var origins = SourceLookups.Ordered(tip.Origins.Concat(block.Origins));
        return block.Value is { Hash: { } hash } found
            ? new BitcoinLookup<ChainTip>(tip.Status, new ChainTip(found.Height, hash, found.Time), origins, null)
            : new BitcoinLookup<ChainTip>(LookupStatus.Failed, null, origins, block.Problem);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
        ICase @case, EthereumAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        CheckOnSolana(address);
        var lamports = await _lookups.LookupAsync(@case, BalanceQuery(address), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (lamports.Value is not { } sol)
        {
            return Without<EthereumBalance, WholeNumber>(lamports);
        }

        var parts = Parts.Of(lamports);
        var holdings = parts.Add(
            await _lookups.LookupAsync(
                    @case, TokenAccountsQuery(address, "programId", TokenProgram), LookupMode.AsNeeded,
                    cancellationToken)
                .ConfigureAwait(false),
            "Adressens tokens");
        var (tokens, skipped) = Holdings(holdings?.Accounts ?? []);
        if (skipped > 0)
        {
            parts.Note("Adressen har tokens vars decimaler Chainsaw inte kan räkna i; de visas inte.");
        }

        if (holdings?.Accounts.Any(account => account.IsFrozen) == true)

        {
            parts.Note("Ett av adressens tokenkonton är fryst: ägaren kan inte flytta tokenen.");
        }

        var balance = new EthereumBalance(address, new AssetAmount(Asset.Sol, sol.Value), tokens)
        {
            Skipped = skipped,
            IsComplete = holdings is not null,
        };
        return parts.Answer(balance);
    }

    /// <summary>
    /// Reads a page of the owner's own signatures and what each of its transactions moved to or from the owner, with
    /// the fees the owner paid.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The owner.</param>
    /// <param name="atOrBelow">The slot the page goes back from, or <see langword="null"/> for the newest.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    private async Task<BitcoinLookup<EthereumAddressPage>> OwnHistoryAsync(
        ICase @case, EthereumAddress address, long? atOrBelow, LookupMode mode, CancellationToken cancellationToken)
    {
        var signatures = await SignaturesAsync(@case, address, atOrBelow, mode, cancellationToken)
            .ConfigureAwait(false);
        if (signatures.Value is not { } page)
        {
            return Without<EthereumAddressPage, SolanaSignaturePage>(signatures);
        }

        var parts = Parts.Of(signatures);
        var read = await MovementsAsync(@case, page.Entries, mode, parts, cancellationToken).ConfigureAwait(false);
        var transfers = read.Transfers.Where(transfer => transfer.From == address || transfer.To == address).ToList();
        var fees = read.Paid.Where(fee => fee.Payer == address).ToList();
        var result = new EthereumAddressPage(
            address, EthereumHistory.Transactions, atOrBelow, transfers, fees, page.NextAtOrBelow, page.IsCutShort,
            read.Waiting.Count > 0)
        {
            Skipped = read.Skipped,
            Waiting = read.Waiting,
        };
        return parts.Answer(result);
    }

    /// <summary>
    /// Reads a page of the owner's token history: the signatures of each of its token accounts of a token Chainsaw
    /// knows, down to the slot the fullest account's page reaches (so that the page holds every entry above it), and
    /// the token movements to or from the owner in those transactions.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="owner">The owner.</param>
    /// <param name="atOrBelow">The slot the page goes back from, or <see langword="null"/> for the newest.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    private async Task<BitcoinLookup<EthereumAddressPage>> TokenHistoryAsync(
        ICase @case, EthereumAddress owner, long? atOrBelow, LookupMode mode, CancellationToken cancellationToken)
    {
        var parts = new Parts(LookupStatus.FromCase);
        var accounts = new List<EthereumAddress>();
        foreach (var token in KnownSolanaTokens)
        {
            var listed = parts.Add(
                await _lookups.LookupAsync(
                        @case, TokenAccountsQuery(owner, "mint", token.Contract!), mode, cancellationToken)
                    .ConfigureAwait(false),
                $"Adressens tokenkonton för {token.Symbol}");
            accounts.AddRange(listed?.Accounts.Select(account => account.Account) ?? []);
        }

        var pages = new List<SolanaSignaturePage>();
        var waiting = new List<EthereumTxHash>();
        foreach (var account in accounts.Distinct())
        {
            var page = parts.Add(
                await SignaturesAsync(@case, account, atOrBelow, mode, cancellationToken).ConfigureAwait(false),
                "Tokenkontots signaturer");
            if (page is null)
            {
                parts.Incomplete();
                continue;
            }

            pages.Add(page);
        }

        var full = pages.Where(page => page.IsFull).ToList();
        long? next = full.Count == 0 ? null : full.Max(page => page.Entries[^1].Slot);
        var cutShort = full.Any(page => page.IsCutShort) || (next is { } lowest && lowest == atOrBelow);
        var entries = pages.SelectMany(page => page.Entries)
            .Where(entry => next is not { } floor || entry.Slot >= floor)
            .DistinctBy(entry => entry.Transaction)
            .ToList();
        var read = await MovementsAsync(@case, entries, mode, parts, cancellationToken).ConfigureAwait(false);
        waiting.AddRange(read.Waiting);
        var transfers = read.Transfers
            .Where(transfer => transfer.Kind == EthereumTransferKind.Token
                && (transfer.From == owner || transfer.To == owner))
            .ToList();
        var result = new EthereumAddressPage(
            owner, EthereumHistory.Tokens, atOrBelow, transfers, [], cutShort ? null : next, cutShort,
            waiting.Count > 0 || parts.IsIncomplete)
        {
            Skipped = read.Skipped,
            Waiting = waiting,
        };
        return parts.Answer(result);
    }

    /// <summary>
    /// Gets a page of an address's signatures: the newest, or one that goes back from a slot, asked after the signature
    /// the page before it ends with (<see cref="SolanaPageMemory"/>); from the case alone when that page has not been
    /// read.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address (an owner or a token account).</param>
    /// <param name="atOrBelow">The slot the page goes back from, or <see langword="null"/>.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    private async Task<BitcoinLookup<SolanaSignaturePage>> SignaturesAsync(
        ICase @case, EthereumAddress address, long? atOrBelow, LookupMode mode, CancellationToken cancellationToken)
    {
        EthereumTxHash? before = null;
        var askable = true;
        if (atOrBelow is { } slot)
        {
            var (known, after, empty) = _pages.Locate(address, slot);
            if (empty)
            {
                var none = new SolanaSignaturePage(address, atOrBelow, _options.PageSize, []);
                return new BitcoinLookup<SolanaSignaturePage>(LookupStatus.FromCase, none, [], null);
            }

            (before, askable) = (after, known);
        }

        var query = SignaturesQuery(address, atOrBelow, before);
        var lookup = await _lookups.LookupAsync(
                @case, query, askable ? mode : LookupMode.CaseOnly, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is { } page)
        {
            _pages.Record(page, before);
        }
        else if (!askable && mode != LookupMode.CaseOnly)
        {
            return SourceLookups.Refused<SolanaSignaturePage>(PageBeforeText);
        }

        return lookup;
    }

    /// <summary>
    /// Reads what the transactions of a page moved, each fetched (or taken from the case) and read from its balances;
    /// one that cannot be had now is waited for.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="entries">The page's entries.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="parts">Gathers the responses and notes.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The movements, the fees, what was waited for and how many were not counted.</returns>
    private async Task<PageMovements> MovementsAsync(
        ICase @case, IEnumerable<SolanaSignature> entries, LookupMode mode, Parts parts,
        CancellationToken cancellationToken)
    {
        var read = new PageMovements();
        foreach (var entry in entries)
        {
            var lookup = await _lookups.LookupAsync(@case, TransactionQuery(entry.Transaction), mode, cancellationToken)
                .ConfigureAwait(false);
            parts.Take(lookup);
            if (lookup.Value is not { } raw)
            {
                read.Waiting.Add(entry.Transaction);
                continue;
            }

            var block = new EthereumBlockReference(raw.Slot, null, raw.Time) { TransactionIndex = raw.Index };
            var moves = SolanaMovements.Read(raw, block);
            read.Transfers.AddRange(moves.Transfers);
            var fee = new AssetAmount(Asset.Sol, raw.Fee);
            read.Paid.Add(new EthereumFee(raw.Hash, raw.Payer, fee, !raw.Failed, block));
            if (!moves.IsComplete || moves.Skipped > 0)
            {
                read.Skipped++;
                foreach (var problem in moves.Problems)
                {
                    parts.Note($"{raw.Hash.Brief()}: {problem}");
                }
            }
        }

        if (read.Waiting.Count > 0)
        {
            parts.Note("Väntar på källan för tx " + EthereumTxHash.BriefList(read.Waiting) + ".");
        }

        return read;
    }

    /// <summary>The tokens on Solana Chainsaw knows by their mints, whose token accounts are read.</summary>
    private static IReadOnlyList<Asset> KnownSolanaTokens { get; } = [KnownTokens.SolanaUsdt, KnownTokens.SolanaUsdc];

    /// <summary>
    /// Sums an owner's token accounts per token, leaving out empty ones, wrapped SOL (already counted as SOL) and
    /// tokens Chainsaw cannot count in, with USDT and USDC first.
    /// </summary>
    /// <param name="accounts">The token accounts.</param>
    /// <returns>The tokens, and how many were not counted.</returns>
    private static (IReadOnlyList<AssetAmount> Tokens, int Skipped) Holdings(IReadOnlyList<SolanaTokenAccount> accounts)
    {
        var held = accounts.Where(account => !account.Amount.IsZero
            && account.Mint != KnownTokens.SolanaWrappedSolMint).ToList();
        var countable = held.Where(account => account.Decimals <= Asset.MaximumDecimals).ToList();
        var tokens = countable
            .GroupBy(account => account.Mint, StringComparer.Ordinal)
            .Select(group => new AssetAmount(
                KnownTokens.Canonical(Asset.Token(Chain.Solana, group.Key, "SPL " + group.Key[..4] + "…",
                    group.First().Decimals)),
                group.Aggregate(BigInteger.Zero, (sum, account) => sum + account.Amount)))
            .OrderBy(amount => KnownTokens.IsKnown(amount.Asset) ? 0 : 1)
            .ThenBy(amount => amount.Asset.Symbol, StringComparer.Ordinal)
            .ToList();
        return (tokens, held.Count - countable.Count);
    }

    /// <summary>Gives an empty page.</summary>
    /// <param name="address">The address.</param>
    /// <param name="history">Which history.</param>
    /// <param name="atOrBelow">Where it starts.</param>
    /// <returns>The page.</returns>
    private static EthereumAddressPage Empty(EthereumAddress address, EthereumHistory history, long? atOrBelow) =>
        new(address, history, atOrBelow, [], [], null, false, false);

    /// <summary>The lookup of a transaction, which cannot change once it is finalized.</summary>
    /// <param name="hash">The transaction.</param>
    /// <returns>The query.</returns>
    private LookupQuery<SolanaTransactionRaw> TransactionQuery(EthereumTxHash hash)
    {
        SolanaTransactionRaw Parse(byte[] body) => SolanaRpcParser.ParseTransaction(body, hash);
        return new LookupQuery<SolanaTransactionRaw>(
            SolanaResponseKinds.Transaction,
            hash.Value,
            string.Empty,
            Parse,
            raw => new ResponseIndex([], raw.Slot),
            raw => raw.Slot,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, SolanaResponseKinds.Transaction, hash.Value), Parse),
            Body: SolanaRpcApi.TransactionCall(hash));
    }

    /// <summary>
    /// The lookup of a page of an address's signatures. The newest page always changes; one that goes back from a slot
    /// rests only on finalized slots and cannot.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="atOrBelow">The slot it goes back from, or <see langword="null"/>.</param>
    /// <param name="before">The signature it is asked after, or <see langword="null"/>.</param>
    /// <returns>The query.</returns>
    private LookupQuery<SolanaSignaturePage> SignaturesQuery(
        EthereumAddress address, long? atOrBelow, EthereumTxHash? before)
    {
        var size = _options.PageSize;

        // The page size is part of the subject, so that a page read with another size is never taken for a full one.
        var subject = string.Create(CultureInfo.InvariantCulture,
            $"{address.Value}/{atOrBelow?.ToString(CultureInfo.InvariantCulture) ?? "newest"}/{size}");
        SolanaSignaturePage Parse(byte[] body) => SolanaRpcParser.ParseSignatures(body, address, atOrBelow, size);
        return new LookupQuery<SolanaSignaturePage>(
            SolanaResponseKinds.Signatures,
            subject,
            string.Empty,
            Parse,
            page => new ResponseIndex([], page.Entries.Count == 0 ? null : page.Entries.Max(entry => entry.Slot)),
            _ => atOrBelow,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, SolanaResponseKinds.Signatures, subject), Parse),
            Body: SolanaRpcApi.SignaturesCall(address, size, before));
    }

    /// <summary>
    /// The lookup of an owner's token accounts now, of one mint or of a token program; it always changes.
    /// </summary>
    /// <param name="owner">The owner.</param>
    /// <param name="filter">The filter: <c>mint</c> or <c>programId</c>.</param>
    /// <param name="value">The mint or the program.</param>
    /// <returns>The query.</returns>
    private LookupQuery<TokenAccountList> TokenAccountsQuery(EthereumAddress owner, string filter, string value)
    {
        var subject = $"{owner.Value}/{filter}/{value}";
        TokenAccountList Parse(byte[] body) => new(SolanaRpcParser.ParseTokenAccounts(body, owner));
        return new LookupQuery<TokenAccountList>(
            SolanaResponseKinds.TokenAccounts,
            subject,
            string.Empty,
            Parse,
            _ => ResponseIndex.None,
            _ => null,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, SolanaResponseKinds.TokenAccounts, subject), Parse),
            Body: SolanaRpcApi.TokenAccountsCall(owner, filter, value));
    }

    /// <summary>The lookup of an address's SOL now, which always changes.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The query.</returns>
    private LookupQuery<WholeNumber> BalanceQuery(EthereumAddress address)
    {
        static WholeNumber Parse(byte[] body) => new((long)SolanaRpcParser.ParseBalance(body));
        return new LookupQuery<WholeNumber>(
            SolanaResponseKinds.Balance,
            address.Value,
            string.Empty,
            Parse,
            _ => ResponseIndex.None,
            _ => null,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, SolanaResponseKinds.Balance, address.Value), Parse),
            Body: SolanaRpcApi.BalanceCall(address));
    }

    /// <summary>The lookup of the newest finalized slot, which always changes.</summary>
    /// <returns>The query.</returns>
    private LookupQuery<WholeNumber> TipQuery()
    {
        static WholeNumber Parse(byte[] body) => new(SolanaRpcParser.ParseSlot(body));
        return new LookupQuery<WholeNumber>(
            SolanaResponseKinds.Tip,
            string.Empty,
            string.Empty,
            Parse,
            slot => new ResponseIndex([], slot.Value),
            _ => null,
            FindStored: reader => SourceLookups.StoredAs(
                _lookups.Store.Latest(reader, SolanaResponseKinds.Tip, string.Empty), Parse),
            Body: SolanaRpcApi.SlotCall());
    }

    /// <summary>Checks that an address is on Solana.</summary>
    /// <param name="address">The address.</param>
    /// <exception cref="ArgumentException">It is on another chain.</exception>
    private static void CheckOnSolana(EthereumAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Chain != Chain.Solana)
        {
            throw new ArgumentException("The address is not on Solana.", nameof(address));
        }
    }

    /// <summary>Checks that a transaction is on Solana.</summary>
    /// <param name="hash">The transaction.</param>
    /// <exception cref="ArgumentException">It is on another chain.</exception>
    private static void CheckOnSolana(EthereumTxHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Chain != Chain.Solana)
        {
            throw new ArgumentException("The transaction is not on Solana.", nameof(hash));
        }
    }

    /// <summary>Carries a lookup without a value over to another type.</summary>
    /// <typeparam name="T">The type asked for.</typeparam>
    /// <typeparam name="TFrom">The type looked up.</typeparam>
    /// <param name="lookup">The lookup.</param>
    /// <returns>The same answer without a value.</returns>
    private static BitcoinLookup<T> Without<T, TFrom>(BitcoinLookup<TFrom> lookup)
        where T : class
        where TFrom : class =>
        new(lookup.Status, null, lookup.Origins, lookup.Problem) { Notes = lookup.Notes };

    /// <summary>A whole number an answer gives: a slot, or a balance in lamports.</summary>
    /// <param name="Value">The number.</param>
    private sealed record WholeNumber(long Value);

    /// <summary>An owner's token accounts, as one lookup's value.</summary>
    /// <param name="Accounts">The accounts.</param>
    private sealed record TokenAccountList(IReadOnlyList<SolanaTokenAccount> Accounts);

    /// <summary>What the transactions of a page moved.</summary>
    private sealed class PageMovements
    {
        /// <summary>Gets the movements, each once per transaction and place.</summary>
        public List<EthereumTransfer> Transfers { get; } = [];

        /// <summary>Gets the fees, one per transaction.</summary>
        public List<EthereumFee> Paid { get; } = [];

        /// <summary>Gets the transactions that could not be had now.</summary>
        public List<EthereumTxHash> Waiting { get; } = [];

        /// <summary>Gets or sets how many transactions could not be read whole from their balances.</summary>
        public int Skipped { get; set; }
    }

    /// <summary>
    /// Gathers the parts of an answer built of several lookups: their sealed responses, what is missing and what may be
    /// outdated, so that an answer that could not be read whole says so instead of looking complete.
    /// </summary>
    private sealed class Parts
    {
        /// <summary>The sealed responses of every part.</summary>
        private readonly List<ResponseOrigin> _origins = [];

        /// <summary>What the user should know.</summary>
        private readonly List<string> _notes = [];

        /// <summary>Why a part may be outdated.</summary>
        private string? _problem;

        /// <summary>The status: the main one, or outdated when a part is.</summary>
        private LookupStatus _status;

        /// <summary>Creates the parts of an answer that has no main lookup.</summary>
        /// <param name="status">The status to start from.</param>
        public Parts(LookupStatus status) => _status = status;

        /// <summary>Creates the parts of an answer from its main lookup.</summary>
        /// <typeparam name="T">The main lookup's value.</typeparam>
        /// <param name="main">The main lookup.</param>
        /// <returns>The parts.</returns>
        public static Parts Of<T>(BitcoinLookup<T> main)
            where T : class
        {
            var parts = new Parts(main.Status);
            parts.Take(main);
            return parts;
        }

        /// <summary>Gets whether a part is missing.</summary>
        public bool IsIncomplete { get; private set; }

        /// <summary>Takes a part's responses and notes, and whether it may be outdated or was fetched.</summary>
        /// <typeparam name="T">The part.</typeparam>
        /// <param name="lookup">The part's lookup.</param>
        public void Take<T>(BitcoinLookup<T> lookup)
            where T : class
        {
            _origins.AddRange(lookup.Origins);
            _notes.AddRange(lookup.Notes);
            if (lookup.Value is null)
            {
                return;
            }

            if (lookup.Status == LookupStatus.FromCaseMayBeOutdated)
            {
                _status = LookupStatus.FromCaseMayBeOutdated;
                _problem ??= lookup.Problem;
            }
            else if (lookup.Status == LookupStatus.Fetched && _status == LookupStatus.FromCase)
            {
                _status = LookupStatus.Fetched;
            }
        }

        /// <summary>Adds a part's lookup and gives its value, noting it as missing when there is none.</summary>
        /// <typeparam name="T">The part.</typeparam>
        /// <param name="lookup">The part's lookup.</param>
        /// <param name="what">What the part is, in Swedish.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
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
            IsIncomplete = true;
        }

        /// <summary>Marks the answer incomplete without a note of its own (the part's lookup said why).</summary>
        public void Incomplete() => IsIncomplete = true;

        /// <summary>Notes something the user should know that leaves the answer complete.</summary>
        /// <param name="text">The note, in Swedish.</param>
        public void Note(string text) => _notes.Add(text);

        /// <summary>Gives the transaction's answer, complete only when every part was read.</summary>
        /// <param name="transaction">The transaction.</param>
        /// <returns>The answer.</returns>
        public BitcoinLookup<EthereumTransaction> Result(EthereumTransaction transaction) =>
            Answer(transaction with { IsComplete = !IsIncomplete });

        /// <summary>Gives an answer with every part's responses and notes.</summary>
        /// <typeparam name="T">The value.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The answer.</returns>
        public BitcoinLookup<T> Answer<T>(T value)
            where T : class =>
            new(_status, value, SourceLookups.Ordered(_origins), _problem) { Notes = [.. _notes.Distinct()] };
    }
}

/// <summary>The kinds of Solana request, stored with each response so that it can be found again.</summary>
internal static class SolanaResponseKinds
{
    /// <summary>A transaction with its balances, <c>getTransaction</c>.</summary>
    public const string Transaction = "solana.tx";

    /// <summary>A page of an address's signatures, <c>getSignaturesForAddress</c>.</summary>
    public const string Signatures = "solana.signatures";

    /// <summary>A slot's block, <c>getBlock</c> without transactions.</summary>
    public const string Block = "solana.block";

    /// <summary>The newest finalized slot, <c>getSlot</c>.</summary>
    public const string Tip = "solana.tip";

    /// <summary>An address's SOL now, <c>getBalance</c>.</summary>
    public const string Balance = "solana.balance";

    /// <summary>An owner's token accounts now, <c>getTokenAccountsByOwner</c>.</summary>
    public const string TokenAccounts = "solana.token-accounts";
}
