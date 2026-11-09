using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Bitcoin from an Esplora API (the API of mempool.space and Blockstream, which can also be run in-house), asked only
/// through <see cref="IExitHttp"/>. Every response is sealed in the case before it is used; an answer that cannot
/// have changed is taken from the case without asking; without an exit the case answers and says what is missing.
/// </summary>
internal sealed class EsploraBitcoinSource : IBitcoinSource
{
    /// <summary>
    /// How old the newest known height may be before a lookup that depends on depth asks for a new one.
    /// </summary>
    private static readonly TimeSpan TipFreshness = TimeSpan.FromMinutes(1);

    /// <summary>How many times the newest block's hash is asked for when its block cannot be had.</summary>
    private const int TipAttempts = 2;

    /// <summary>The longest script that can run, in hex characters (10 000 bytes).</summary>
    private const int MaximumScriptHex = 20_000;

    /// <summary>The time now, to decide whether the known height is fresh.</summary>
    private readonly IClock _clock;

    /// <summary>The settings.</summary>
    private readonly BitcoinSourceOptions _options;

    /// <summary>Reads Esplora's JSON.</summary>
    private readonly EsploraParser _parser;

    /// <summary>Seals and finds the raw responses.</summary>
    private readonly RawResponseStore _store;

    /// <summary>Answers the lookups through the endpoints and the case.</summary>
    private readonly SourceLookups _lookups;

    /// <summary>The chains' endpoints, for which endpoint gave an answer.</summary>
    private readonly ChainSources _sources;

    /// <summary>Creates the source with the default endpoints in memory only, as a test or a tool does.</summary>
    /// <param name="http">The only way out.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings, with the default endpoints.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <exception cref="ArgumentException">A default endpoint is not an absolute <c>https</c> address of its
    /// own.</exception>
    public EsploraBitcoinSource(
        IExitHttp http, IOperatorProfileStore operators, IClock clock, BitcoinSourceOptions options,
        TimeZoneInfo timeZone)
        : this(
            new ChainSources([new EsploraApi(options)], http, null, null, clock, new ChainSourcesOptions()), operators,
            clock, options, timeZone)
    {
    }

    /// <summary>Creates the source.</summary>
    /// <param name="sources">
    /// The chains' endpoints, of which Bitcoin's are asked, each the only way it may be: through the exit, or straight
    /// to the user's own node.
    /// </param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    public EsploraBitcoinSource(
        ChainSources sources, IOperatorProfileStore operators, IClock clock, BitcoinSourceOptions options,
        TimeZoneInfo timeZone)
    {
        _sources = sources;
        _clock = clock;
        _options = options;
        _parser = new EsploraParser(options.Network);
        _store = new RawResponseStore(timeZone, options.Network);
        if (options.SafeConfirmations < 1)
        {
            throw new ArgumentException("The depth must be at least 1.", nameof(options));
        }

        _lookups = new SourceLookups(
            Chain.Bitcoin, "Bitcoin", operators, sources, _store, options.SafeConfirmations,
            async (_, @case, cancellationToken) => await RefreshHeightAsync(@case, cancellationToken)
                .ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public BitcoinNetwork Network => _options.Network;

    /// <summary>A transaction's size that could pass for an inner node of the merkle tree (two hashes).</summary>
    private const int InnerNodeSize = 64;

    /// <summary>Gets the network whose work limit a header must meet, or none when the check is turned off.</summary>
    private BitcoinNetwork? WorkLimit => _options.CheckWorkLimit ? _options.Network : null;

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
        GetAddressTransactionsAsync(@case, address, after, LookupMode.AsNeeded, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after, LookupMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Network != Network)
        {
            return Task.FromResult(SourceLookups.Refused<BitcoinAddressPage>(
                "Adressen hör till ett annat Bitcoin-nät än källan. Inget skickades."));
        }

        var segment = Uri.EscapeDataString(address.Value);
        var path = after is null ? $"address/{segment}/txs" : $"address/{segment}/txs/chain/{after.Value}";
        var subject = after is null ? address.Value : $"{address.Value}/{after.Value}";
        var query = new LookupQuery<BitcoinAddressPage>(
            ResponseKinds.AddressTransactions,
            subject,
            path,
            body => Page(address, after, _parser.ParseAddressTransactions(body, address)),
            page => IndexOf(page.Transactions),
            page => after is null || page.Transactions.Count == 0 ? null : Shallowest(page.Transactions));
        return _lookups.LookupAsync(@case, query, mode, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        var query = new LookupQuery<BitcoinTransaction>(
            ResponseKinds.Transaction,
            txid.Value,
            $"tx/{txid.Value}",
            body => _parser.ParseTransaction(body, txid),
            transaction => IndexOf([transaction]),
            transaction => transaction.Block?.Height,
            Changed: BlockChange,
            FindStored: reader => StoredTransaction(reader, txid));
        var lookup = await _lookups.LookupAsync(@case, query, mode, cancellationToken).ConfigureAwait(false);
        if (lookup.Value is null)
        {
            return lookup;
        }

        var withSpends = await WithSpendsAsync(@case, lookup, cancellationToken).ConfigureAwait(false);
        var bytes = await _lookups.LookupAsync(@case, HexQuery(txid), LookupMode.CaseOnly, cancellationToken)
            .ConfigureAwait(false);
        return bytes.Value is null ? withSpends : CheckedAgainst(withSpends, bytes);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default)
    {
        var lookup = await GetTransactionAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is not { BytesVerified: false })
        {
            return lookup;
        }

        var bytes = await _lookups.LookupAsync(@case, HexQuery(txid), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (bytes.Value is not null)
        {
            return CheckedAgainst(lookup, bytes);
        }

        var note = $"Tx:ens egna bytes kunde inte hämtas, så den är inte kontrollerad mot dem. {bytes.Problem}";
        return lookup with
        {
            Origins = [.. lookup.Origins.Concat(bytes.Origins).OrderBy(origin => origin.Sequence)],
            Notes = [.. lookup.Notes, note.TrimEnd()],
        };
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinTransaction>> VerifyInBlockAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);

        // The bytes first: the txid must be the transaction's own, and a 64-byte transaction could pass for an inner
        // node of the tree, so it is never checked this way.
        var lookup = await VerifyTransactionAsync(@case, txid, cancellationToken).ConfigureAwait(false);
        if (lookup.Value is not { Block: { } at } transaction)
        {
            return lookup;
        }

        if (!transaction.BytesVerified || StrippedSize(transaction) == InnerNodeSize)
        {
            return Unchecked(lookup, transaction.BytesVerified
                ? "Tx:en är 64 bytes och kan förväxlas med en nod i trädet, så den kontrolleras inte mot blocket."
                : "Tx:ens egna bytes kunde inte kontrolleras, så den är inte kontrollerad mot blocket.");
        }

        var block = await GetBlockAsync(@case, at.Hash, cancellationToken).ConfigureAwait(false);
        var proof = await _lookups.LookupAsync(@case, MerkleQuery(txid), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        var origins = Merged(lookup.Origins, block.Origins, proof.Origins);
        if (block.Value is not { HeaderVerified: true } read || proof.Value is not { } path)
        {
            var problem = block.Value is { HeaderVerified: true } ? proof.Problem : block.Problem;
            return Unchecked(lookup with { Origins = origins },
                $"Beviset för att tx:en ligger i blocket kunde inte hämtas, så det är inte kontrollerat. {problem}");
        }

        if (path.Height != at.Height || !path.Fits(read.TransactionCount) || path.RootFrom(txid) != read.MerkleRoot)
        {
            return new BitcoinLookup<BitcoinTransaction>(LookupStatus.Failed, null, origins,
                "Källans bevis för att tx:en ligger i blocket leder inte till blockets eget huvud, som är sparat i "
                + "ärendet; tx:ens block används inte som kontrollerat.");
        }

        var answeredBy = block.Origins.LastOrDefault()?.EndpointId;
        var confirmation = await ConfirmBlockAsync(@case, read, answeredBy, cancellationToken).ConfigureAwait(false);
        origins = Merged(origins, confirmation.Origins);
        return lookup with
        {
            Value = transaction with { InBlock = confirmation.Level },
            Origins = origins,
            Notes = confirmation.Note is { } note ? [.. lookup.Notes, note] : lookup.Notes,
        };
    }

    /// <summary>
    /// Asks another endpoint in step with the chain for the block's hash at its height, so that a block is not one
    /// source's alone: a header that meets only the network's easiest target says nothing about the chain.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="block">The block, checked against its header.</param>
    /// <param name="answeredBy">The endpoint that gave the block, which is not asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>How far the block is checked, the responses, and what to say when it is not confirmed.</returns>
    private async Task<(InBlockCheck Level, IReadOnlyList<ResponseOrigin> Origins, string? Note)> ConfirmBlockAsync(
        ICase @case, BitcoinBlock block, Guid? answeredBy, CancellationToken cancellationToken)
    {
        var height = block.Height.ToString(CultureInfo.InvariantCulture);
        var query = new LookupQuery<string>(
            ResponseKinds.BlockAtHeight,
            height,
            $"block-height/{height}",
            body => EsploraParser.ParseTipHash(body).Value,
            _ => new ResponseIndex([], block.Height),
            _ => null);
        var other = await _lookups.FetchAsync(
                @case, query, null, cancellationToken, inStep: true, except: answeredBy ?? Guid.Empty)
            .ConfigureAwait(false);
        // Another endpoint, by id and by host: two addresses of one service are not two answers. It is still not an
        // independent source (it may read the same node), so it is said as another endpoint, by name.
        var confirmedBy = other.Origins.LastOrDefault(origin => origin.Url.Host != AnsweringHost(block, answeredBy));
        if (other.Value == block.Hash.Value && confirmedBy is not null)
        {
            return (InBlockCheck.ConfirmedElsewhere, other.Origins,
                $"Blockets hash vid höjden bekräftades av en annan endpoint, {confirmedBy.Endpoint}.");
        }

        var note = other.Value is null
            ? "Blocket vilar bara på en källas svar: ingen annan endpoint i fas kunde bekräfta det."
            : "En annan endpoint i fas har ett annat block på samma höjd, så blocket vilar bara på en källas svar.";
        return (InBlockCheck.SourceOnly, other.Origins, note);
    }

    /// <summary>Gives a transaction's size without its witness data, the bytes its txid is the hash of.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The size: weight is four times it, less what the witness adds once.</returns>
    private static int StrippedSize(BitcoinTransaction transaction) => (transaction.Weight - transaction.Size) / 3;

    /// <summary>Gives the host of the endpoint that gave a block, so that another address of it is not taken as another.</summary>
    /// <param name="block">The block.</param>
    /// <param name="answeredBy">The endpoint that gave it, if known.</param>
    /// <returns>The host, or an empty text when it is not known.</returns>
    private string AnsweringHost(BitcoinBlock block, Guid? answeredBy)
    {
        _ = block;
        return _sources.Get(Chain.Bitcoin).Endpoints
            .FirstOrDefault(each => each.Endpoint.Id == answeredBy)?.Endpoint.Address.Host ?? string.Empty;
    }

    /// <summary>Gives a lookup as it is, with a note on why it is not checked against its block.</summary>
    /// <param name="lookup">The lookup.</param>
    /// <param name="note">Why.</param>
    /// <returns>The lookup with the note.</returns>
    private static BitcoinLookup<BitcoinTransaction> Unchecked(BitcoinLookup<BitcoinTransaction> lookup, string note) =>
        lookup with { Notes = [.. lookup.Notes, note.TrimEnd()] };

    /// <summary>Merges responses, once each, in their order in the case.</summary>
    /// <param name="parts">The responses.</param>
    /// <returns>The merged list.</returns>
    private static List<ResponseOrigin> Merged(params IEnumerable<ResponseOrigin>[] parts) =>
        [.. parts.SelectMany(part => part).DistinctBy(origin => origin.Sequence).OrderBy(origin => origin.Sequence)];

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
        ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Network != Network)
        {
            return Task.FromResult(SourceLookups.Refused<BitcoinAddressSummary>(
                "Adressen hör till ett annat Bitcoin-nät än källan. Inget skickades."));
        }

        var query = new LookupQuery<BitcoinAddressSummary>(
            ResponseKinds.AddressSummary,
            address.Value,
            $"address/{Uri.EscapeDataString(address.Value)}",
            body => EsploraParser.ParseAddressSummary(body, address),
            _ => ResponseIndex.None,
            _ => null);
        return _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        // The transaction's own outputs say which can never be spent (OP_RETURN), which never wait for a spend.
        var transaction = await Task.Run(
            () => @case.Read(reader => StoredTransaction(reader, txid)?.Value), cancellationToken)
            .ConfigureAwait(false);

        // One entry per output: a list of another length is not this transaction's, and is never taken as settled.
        IReadOnlyList<BitcoinOutputSpend> Parse(byte[] body)
        {
            var spends = EsploraParser.ParseOutputSpends(body);
            return transaction is null || spends.Count == transaction.Outputs.Count
                ? spends
                : throw new EsploraFormatException("The spends are not one per output of the transaction.");
        }

        var query = new LookupQuery<IReadOnlyList<BitcoinOutputSpend>>(
            ResponseKinds.OutputSpends,
            txid.Value,
            $"tx/{txid.Value}/outspends",
            Parse,
            _ => ResponseIndex.None,
            spends => SettledAt(spends, transaction),
            Changed: SpendChange);
        return await _lookups.LookupAsync(@case, query, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<TransactionReplacement>> GetReplacementAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.CaseOnly, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);

        // Everything the case holds is read at once: the transaction and the newest answer about each transaction
        // whose coins it spent.
        var read = await Task.Run(() => @case.Read(reader => ReadReplacement(reader, txid)), cancellationToken)
            .ConfigureAwait(false);
        if (read is not { } known)
        {
            return new BitcoinLookup<TransactionReplacement>(LookupStatus.NotInCase, null, [],
                "Ärendet har inte tx:en, så det går inte att säga vad som spenderar dess mynt. Inget skickades.");
        }

        var answers = new Dictionary<Txid, BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>>();
        var problems = new List<string>();
        var status = LookupStatus.FromCase;
        var asked = 0;
        foreach (var (previous, stored) in known.Previous)
        {
            var answer = stored is { } found
                ? new BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>(
                    LookupStatus.FromCase, found.Value, [found.Response.Origin], null)
                : null;
            var ask = mode != LookupMode.CaseOnly && !Settled(known.Transaction, previous, txid, answer?.Value);
            if (ask && asked < MostReplacedPrevious)
            {
                // Asked of another host than those that said the transaction is gone, when one can be, so that the
                // answer can confirm what they said.
                asked++;
                answer = await _lookups.FetchAsync(
                        @case, ReplacedSpendsQuery(previous), stored, cancellationToken, mode == LookupMode.InStep,
                        preferNotHosts: known.GoneHosts)
                    .ConfigureAwait(false);
                status = Worse(status, answer.Status);
                problems.AddRange(answer.Problem is { } problem ? [problem] : []);
            }

            if (answer is { Value: not null })
            {
                answers[previous] = answer;
            }
        }

        if (answers.Count < known.Previous.Count && asked == MostReplacedPrevious)
        {
            var count = known.Previous.Count.ToString(CultureInfo.InvariantCulture);
            problems.Add($"Frågat om {MostReplacedPrevious} av {count} tx som mynten kom ur; resten vid nästa uppslag.");
        }

        var coins = known.Transaction.Inputs
            .Where(input => input is { IsCoinbase: false, PreviousTxid: not null })
            .Select(input => CoinOf(input, txid, answers.GetValueOrDefault(input.PreviousTxid!)))
            .ToList();
        var origins = answers.Values.SelectMany(answer => answer.Origins).DistinctBy(origin => origin.Sequence);
        var said = problems.Distinct().ToList();
        var replacement = new TransactionReplacement(txid, coins)
        {
            PreviousCount = known.Previous.Count,
            PreviousAnswered = answers.Count,
        };
        return new BitcoinLookup<TransactionReplacement>(
            status, replacement, SourceLookups.Ordered(origins), said.Count == 0 ? null : string.Join(" ", said));
    }

    /// <summary>
    /// The most transactions whose coins a gone transaction spent that one lookup asks about, so that one with very many
    /// inputs does not send a request for each at once; the rest are asked at the next lookup.
    /// </summary>
    internal const int MostReplacedPrevious = 20;

    /// <summary>What the case holds about a gone transaction's coins, read at once.</summary>
    /// <param name="Transaction">The gone transaction.</param>
    /// <param name="Previous">
    /// Each transaction whose coins it spent, with the newest sealed answer about who spends them, those without an
    /// answer first, so that a lookup capped at <see cref="MostReplacedPrevious"/> asks about new ones each time.
    /// </param>
    /// <param name="GoneHosts">The hosts whose sealed answers said the source does not know the transaction.</param>
    private sealed record ReplacementReading(
        BitcoinTransaction Transaction,
        IReadOnlyList<(Txid Previous, (IReadOnlyList<BitcoinOutputSpend> Value, StoredResponse Response)? Stored)>
            Previous,
        IReadOnlySet<string> GoneHosts);

    /// <summary>Reads a gone transaction and the newest answers about the coins it spent, in one reading.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The gone transaction.</param>
    /// <returns>The reading, or <see langword="null"/> when the case does not hold the transaction.</returns>
    private ReplacementReading? ReadReplacement(ICaseReader reader, Txid txid)
    {
        if (StoredTransaction(reader, txid)?.Value is not { } transaction)
        {
            return null;
        }

        var previous = transaction.Inputs
            .Where(input => input is { IsCoinbase: false, PreviousTxid: not null })
            .Select(input => input.PreviousTxid!)
            .Distinct()
            .Select(each => (each, SourceLookups.StoredAs(
                _store.Latest(reader, ResponseKinds.ReplacedSpends, each.Value),
                body => EsploraParser.ParseOutputSpends(body))))
            .OrderBy(each => each.Item2 is null ? 0 : 1)
            .ToList();
        var goneHosts = _store.NotFoundHosts(reader, txid, 0).ToHashSet(StringComparer.Ordinal);
        return new ReplacementReading(transaction, previous, goneHosts);
    }

    /// <summary>
    /// The lookup of who spends a transaction's outputs, asked only to find what replaced a gone transaction that spent
    /// some of them, and sealed under its own kind, apart from the answers the graph follows.
    /// </summary>
    /// <param name="txid">The transaction whose outputs the gone one spent.</param>
    /// <returns>The query.</returns>
    private static LookupQuery<IReadOnlyList<BitcoinOutputSpend>> ReplacedSpendsQuery(Txid txid) => new(
        ResponseKinds.ReplacedSpends,
        txid.Value,
        $"tx/{txid.Value}/outspends",
        body => EsploraParser.ParseOutputSpends(body),
        _ => ResponseIndex.None,
        _ => null);

    /// <summary>
    /// Says whether the case's answer settles every coin the gone transaction spent from one transaction: another
    /// transaction in a block spends each. An unspent coin, or one whose spender is unconfirmed (it may have been
    /// replaced in turn), is asked about again.
    /// </summary>
    /// <param name="gone">The gone transaction.</param>
    /// <param name="previous">The transaction whose coins it spent.</param>
    /// <param name="txid">The gone transaction's id.</param>
    /// <param name="spends">The case's answer, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when nothing need be asked.</returns>
    private static bool Settled(
        BitcoinTransaction gone, Txid previous, Txid txid, IReadOnlyList<BitcoinOutputSpend>? spends) =>
        spends is not null
        && gone.Inputs.Where(input => input.PreviousTxid == previous)
            .All(input => spends.FirstOrDefault(spend => spend.Index == input.PreviousIndex)?.SpentBy is
                { Block: not null } by && by.Txid != txid);

    /// <summary>Gives the worse of two lookups' statuses: a fetch over the case, and a failure over both.</summary>
    /// <param name="so">The status so far.</param>
    /// <param name="next">The next lookup's status.</param>
    /// <returns>The status to give.</returns>
    private static LookupStatus Worse(LookupStatus so, LookupStatus next) => (so, next) switch
    {
        (LookupStatus.FromCase, _) => next,
        (LookupStatus.Fetched, LookupStatus.FromCase or LookupStatus.Fetched) => LookupStatus.Fetched,
        (LookupStatus.Fetched, _) => next,
        _ => so,
    };

    /// <summary>Reads what one coin of a gone transaction is now, from the answer about its transaction.</summary>
    /// <param name="input">The gone transaction's input that spent the coin.</param>
    /// <param name="gone">The gone transaction.</param>
    /// <param name="answer">The answer, or <see langword="null"/> when there is none.</param>
    /// <returns>The coin.</returns>
    private static ReplacedCoin CoinOf(
        BitcoinInput input, Txid gone, BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>? answer)
    {
        var index = input.PreviousIndex!.Value;
        var spend = answer?.Value?.FirstOrDefault(each => each.Index == index);

        // A fresh answer is sealed last; one that falls back on the case's answer gives that one first.
        long? sequence = answer switch
        {
            _ when spend is null => null,
            { Origins.Count: 0 } => null,
            { Status: LookupStatus.Fetched } => answer.Origins[^1].Sequence,
            _ => answer!.Origins[0].Sequence,
        };
        var state = spend switch
        {
            null => ReplacedCoinState.NotAsked,
            { SpentBy: null } => ReplacedCoinState.Unspent,
            { SpentBy: { } by } when by.Txid == gone => ReplacedCoinState.SpentByItself,
            _ => ReplacedCoinState.SpentByOther,
        };
        var spender = state == ReplacedCoinState.SpentByOther ? spend!.SpentBy : null;
        var host = sequence is { } at
            ? answer!.Origins.First(origin => origin.Sequence == at).Url.Host.ToLowerInvariant()
            : null;
        return new ReplacedCoin(input.Index, input.PreviousTxid!, index, state, spender, sequence) { Host = host };
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
        ICase @case, BlockHash hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(hash);
        var query = new LookupQuery<BitcoinBlock>(
            ResponseKinds.Block,
            hash.Value,
            $"block/{hash.Value}",
            body => EsploraParser.ParseBlock(body, hash),
            // A block at a height says that the chain has come at least that far, as the chain's height does.
            block => new ResponseIndex([], block.Height),
            block => block.Height,
            NeverChanges: true);
        var lookup = await _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is not { } block)
        {
            return lookup;
        }

        var header = await _lookups.LookupAsync(@case, HeaderQuery(hash), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        var origins = SourceLookups.Ordered(lookup.Origins.Concat(header.Origins));
        if (header.Value is null)
        {
            var note = $"Blockets huvud kunde inte hämtas, så blocket är inte kontrollerat mot det. {header.Problem}";
            return lookup with { Origins = origins, Notes = [.. lookup.Notes, note.TrimEnd()] };
        }

        if (BlockHeaderReader.Compare(block, header.Value) is null)
        {
            return lookup with { Value = block with { HeaderVerified = true }, Origins = origins };
        }

        if (lookup.Status == LookupStatus.FromCase)
        {
            // The description in the case contradicts the header, and neither ever changes: the description is
            // fetched again rather than refused for good (granskningen etapp 15, M3), and checked once more.
            var again = await _lookups.FetchAsync(@case, query, null, cancellationToken).ConfigureAwait(false);
            origins = [.. origins.Concat(again.Origins).DistinctBy(origin => origin.Sequence)
                .OrderBy(origin => origin.Sequence)];
            if (again.Value is { } fresh && BlockHeaderReader.Compare(fresh, header.Value) is null)
            {
                return again with { Value = fresh with { HeaderVerified = true }, Origins = origins };
            }
        }

        return new BitcoinLookup<BitcoinBlock>(LookupStatus.Failed, null, origins,
            "Källans uppgifter om blocket stämmer inte med blockets eget huvud, som är sparat i ärendet; de "
            + "används inte.");
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(ICase @case, CancellationToken cancellationToken = default)
        => GetTipHeightAsync(@case, LookupMode.AsNeeded, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
        ICase @case, LookupMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return _lookups.LookupAsync(@case, TipQuery(), mode, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var origins = new List<ResponseOrigin>();
        BitcoinLookup<ChainTip>? failed = null;

        // The endpoints are spread over, so the block may be asked of another than the one that gave the hash, and
        // one a block behind does not have it yet; the hash is then asked for once more, and its block with it.
        for (var attempt = 0; attempt < TipAttempts; attempt++)
        {
            var tip = await _lookups.LookupAsync(@case, TipHashQuery(), LookupMode.AsNeeded, cancellationToken)
                .ConfigureAwait(false);
            origins.AddRange(tip.Origins);
            if (tip.Value is not { } hash)
            {
                return new BitcoinLookup<ChainTip>(tip.Status, null, SourceLookups.Ordered(origins), tip.Problem);
            }

            var block = await GetBlockAsync(@case, hash, cancellationToken).ConfigureAwait(false);
            origins.AddRange(block.Origins);
            if (block.Value is { } newest)
            {
                return new BitcoinLookup<ChainTip>(
                    tip.Status, new ChainTip(newest.Height, newest.Hash.Value, newest.Time),
                    SourceLookups.Ordered(origins), tip.Problem)
                {
                    Notes = [.. tip.Notes, .. block.Notes],
                };
            }

            var problem = $"Det nyaste blockets höjd kunde inte läsas. {block.Problem}".Trim();
            failed = new BitcoinLookup<ChainTip>(LookupStatus.Failed, null, [], problem);
        }

        return failed! with { Origins = SourceLookups.Ordered(origins) };
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<ChainTip>> GetTipAsync(
        ICase @case, LookupMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return mode == LookupMode.CaseOnly
            ? Task.Run(() => @case.Read(StoredTip), cancellationToken)
            : GetTipAsync(@case, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BlockChange>> GetBlockChangesAsync(
        ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run<IReadOnlyList<BlockChange>>(
            () => @case.Read(reader => _store.BlockChangeCandidates(reader)
                .Select(txid => BlockChangeOf(reader, txid))
                .OfType<BlockChange>()
                .ToList()),
            cancellationToken);
    }

    /// <summary>
    /// Reads the newest block the case has fetched as the chain's newest: the newest sealed hash of the chain's tip
    /// and its block, without asking the source.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The block, or <see cref="LookupStatus.NotInCase"/> when the case has none.</returns>
    private BitcoinLookup<ChainTip> StoredTip(ICaseReader reader)
    {
        const string NotRead = "Läst bara ur ärendet; inget skickades.";
        var hash = SourceLookups.StoredAs(
            _store.Latest(reader, ResponseKinds.TipHash, string.Empty), body => EsploraParser.ParseTipHash(body));
        if (hash is not { } tip)
        {
            return new BitcoinLookup<ChainTip>(LookupStatus.NotInCase, null, [], NotRead);
        }

        var block = SourceLookups.StoredAs(
            _store.Latest(reader, ResponseKinds.Block, tip.Value.Value), body => EsploraParser.ParseBlock(body, tip.Value));
        if (block is not { } newest)
        {
            return new BitcoinLookup<ChainTip>(LookupStatus.NotInCase, null, [tip.Response.Origin], NotRead);
        }

        var fetched = SourceLookups.LocalText(tip.Response.Origin.ReceivedLocal);
        return new BitcoinLookup<ChainTip>(
            LookupStatus.FromCaseMayBeOutdated,
            new ChainTip(newest.Value.Height, newest.Value.Hash.Value, newest.Value.Time),
            SourceLookups.Ordered([tip.Response.Origin, newest.Response.Origin]),
            $"Det nyaste blocket i ärendet, hämtat {fetched}; kedjan kan ha vuxit sedan.");
    }

    /// <summary>
    /// Compares a transaction's sealed answers by the rule every chain shares (<see cref="BlockChangeRule"/>): the
    /// answers that had it, and those the lookup took as the source not knowing it.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The change, or <see langword="null"/> when its block is the same or it is not confirmed.</returns>
    private BlockChange? BlockChangeOf(ICaseReader reader, Txid txid)
    {
        var notFound = _store.NotFound(reader, txid);
        var gone = notFound
            .Select(each => new BlockAnswer(
                new BlockSighting(null, null, each.ReceivedLocal, each.Sequence) { Endpoint = each.Endpoint },
                each.EndpointId,
                IsGone: true));
        var answers = Sightings(reader, txid)
            .Select(each => new BlockAnswer(each.Sighting, each.EndpointId, IsGone: false))
            .Concat(gone);
        if (BlockChangeRule.Compare(answers, _store, reader) is not { } change)
        {
            return null;
        }

        if (change.Kind != BlockChangeKind.Gone)
        {
            return new BlockChange(txid, change.Kind, change.Before, change.Now);
        }

        // The hosts that said it is gone since the case last saw it in a block, and every host the case has heard
        // from on the chain, both from the sealed addresses: what the case knew, not what is switched on now.
        var goneHosts = _store.NotFoundHosts(reader, txid, change.Before.Sequence);
        var single = _store.AnsweringHosts(reader).Count <= 1;
        return new BlockChange(txid, change.Kind, change.Before, change.Now)
        {
            GoneHosts = goneHosts,
            IsSingleSource = single,
            IsCorroborated = goneHosts.Count >= 2 || single,
        };
    }

    /// <summary>Reads every sealed answer that has a transaction, oldest first, with where it put it.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The sightings, each with its endpoint's id as sealed.</returns>
    private List<(BlockSighting Sighting, string EndpointId)> Sightings(ICaseReader reader, Txid txid)
    {
        var sightings = new List<(BlockSighting, string)>();
        foreach (var response in _store.AllWithTransaction(reader, txid))
        {
            var found = SourceLookups.StoredAs(response, body => response.Kind == ResponseKinds.Transaction
                ? _parser.ParseTransaction(body, txid)
                : _parser.ParseTransactionInList(body, txid));
            if (found is { } transaction)
            {
                var block = transaction.Value.Block;
                var origin = response.Origin;
                var sighting = new BlockSighting(block?.Height, block?.Hash.Value, origin.ReceivedLocal, origin.Sequence)
                {
                    Endpoint = origin.Endpoint.Length > 0 ? origin.Endpoint : origin.Source,
                };
                sightings.Add((sighting, origin.EndpointId.ToString("D")));
            }
        }

        // The answers come newest first.
        sightings.Reverse();
        return sightings;
    }

    /// <summary>The lookup of the newest block's hash, which always changes.</summary>
    /// <returns>The query.</returns>
    private LookupQuery<BlockHash> TipHashQuery() => new(
        ResponseKinds.TipHash,
        string.Empty,
        "blocks/tip/hash",
        body => EsploraParser.ParseTipHash(body),
        _ => ResponseIndex.None,
        _ => null);

    /// <summary>The lookup of a transaction's own bytes, which never change in the parts its txid commits to.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The query.</returns>
    private LookupQuery<RawTransaction> HexQuery(Txid txid) => new(
        ResponseKinds.TransactionHex,
        txid.Value,
        $"tx/{txid.Value}/hex",
        body => RawTransactionReader.ParseHex(body, txid),
        _ => ResponseIndex.None,
        _ => null,
        NeverChanges: true);

    /// <summary>
    /// The lookup of a transaction's merkle proof, which holds once its block is deep, like the transaction itself.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The query.</returns>
    private LookupQuery<MerkleProof> MerkleQuery(Txid txid) => new(
        ResponseKinds.MerkleProof,
        txid.Value,
        $"tx/{txid.Value}/merkle-proof",
        body => MerkleProof.Parse(body),
        proof => new ResponseIndex([], proof.Height),
        proof => proof.Height);

    /// <summary>The lookup of a block's header, which never changes.</summary>
    /// <param name="hash">The block.</param>
    /// <returns>The query.</returns>
    private LookupQuery<BlockHeader> HeaderQuery(BlockHash hash) => new(
        ResponseKinds.BlockHeader,
        hash.Value,
        $"block/{hash.Value}/header",
        body => BlockHeaderReader.ParseHex(body, hash, WorkLimit),
        _ => ResponseIndex.None,
        _ => null,
        NeverChanges: true);

    /// <summary>
    /// Checks a transaction against its bytes: marks it checked when they agree, and refuses it when they do not,
    /// since the bytes hash to the txid and the description therefore is not that transaction.
    /// </summary>
    /// <param name="lookup">The transaction's lookup, with a value.</param>
    /// <param name="bytes">The bytes' lookup, with a value.</param>
    /// <returns>The checked lookup, or a failed one.</returns>
    private static BitcoinLookup<BitcoinTransaction> CheckedAgainst(
        BitcoinLookup<BitcoinTransaction> lookup, BitcoinLookup<RawTransaction> bytes)
    {
        var origins = SourceLookups.Ordered(lookup.Origins.Concat(bytes.Origins));
        return RawTransactionReader.Compare(lookup.Value!, bytes.Value!) is null
            ? lookup with { Value = lookup.Value! with { BytesVerified = true }, Origins = origins }
            : new BitcoinLookup<BitcoinTransaction>(LookupStatus.Failed, null, origins,
                "Källans uppgifter om tx:en stämmer inte med tx:ens egna bytes, som är sparade i ärendet; de "
                + "används inte.");
    }

    /// <summary>
    /// Says that a transaction has left the block the case saw it in: the block is no longer part of the chain the
    /// source follows (a reorganisation), and the transaction is now in another block or unconfirmed.
    /// </summary>
    /// <param name="before">The case's earlier answer.</param>
    /// <param name="now">The fresh answer.</param>
    /// <returns>The text for the user, or <see langword="null"/> when the block is the same.</returns>
    private static string? BlockChange(BitcoinTransaction before, BitcoinTransaction now)
    {
        if (before.Block is not { } earlier || now.Block?.Hash == earlier.Hash)
        {
            return null;
        }

        var where = now.Block is { } later
            ? string.Create(CultureInfo.InvariantCulture, $"ligger nu i block {later.Height} ({later.Hash})")
            : "är nu obekräftad";
        var earlierText = string.Create(
            CultureInfo.InvariantCulture, $"Tx:en låg tidigare i block {earlier.Height} ({earlier.Hash})");
        return $"{earlierText} men {where}: blocket är inte längre en del av kedjan enligt källan (en omorganisation). "
            + "Båda svaren är sparade i ärendet.";
    }

    /// <summary>
    /// Says when an output the case knew to be spent is now spent by another transaction, or by the same one in
    /// another block, or is unspent again: the chain was reorganised or the spending transaction was replaced (etapp
    /// 15, the answer about spent outputs compared with the last). An output first seen spent is no change.
    /// </summary>
    /// <param name="before">The case's last answer.</param>
    /// <param name="now">The new answer.</param>
    /// <returns>What changed, in Swedish, or <see langword="null"/>.</returns>
    internal static string? SpendChange(IReadOnlyList<BitcoinOutputSpend> before, IReadOnlyList<BitcoinOutputSpend> now)
    {
        var changes = new List<string>();
        foreach (var earlier in before)
        {
            if (earlier.SpentBy is not { } was)
            {
                continue;
            }

            var later = now.FirstOrDefault(each => each.Index == earlier.Index)?.SpentBy;
            if (later is null)
            {
                changes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"utgång {earlier.Index} spenderades av {was.Txid} men är nu ospenderad"));
            }
            else if (later.Txid != was.Txid)
            {
                changes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"utgång {earlier.Index} spenderades av {was.Txid} men nu av {later.Txid}"));
            }
            else if (was.Block is { } block && later.Block?.Hash != block.Hash)
            {
                changes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"utgång {earlier.Index} spenderas av samma tx, men den låg i block {block.Height} och ligger nu "
                    + $"{(later.Block is { } moved ? $"i block {moved.Height}" : "inte i något block")}"));
            }
        }

        return changes.Count == 0
            ? null
            : "Enligt källan har " + string.Join("; ", changes)
                + " (en omorganisation eller en ersatt tx). Båda svaren är sparade i ärendet.";
    }

    /// <summary>The lookup of the chain's height, which always changes.</summary>
    /// <returns>The query.</returns>
    private LookupQuery<BitcoinTip> TipQuery() => new(
        ResponseKinds.TipHeight,
        string.Empty,
        "blocks/tip/height",
        body => EsploraParser.ParseTipHeight(body),
        tip => new ResponseIndex([], tip.Height),
        _ => null);

    /// <summary>
    /// Asks for the chain's height when the newest known one is older than <see cref="TipFreshness"/>, so that an
    /// answer whose block has become deep enough can be taken from the case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns><see langword="true"/> when a new height was fetched.</returns>
    private async Task<bool> RefreshHeightAsync(ICase @case, CancellationToken cancellationToken)
    {
        var tip = TipQuery();
        var (stored, _) = await _lookups.ReadCaseAsync(@case, tip, cancellationToken).ConfigureAwait(false);
        if (stored is { } known && _clock.UtcNow - known.Response.Origin.ReceivedUtc < TipFreshness)
        {
            return false;
        }

        var fresh = await _lookups.FetchAsync(@case, tip, stored, cancellationToken).ConfigureAwait(false);
        return fresh.Status == LookupStatus.Fetched;
    }

    /// <summary>Adds who spends the outputs, from the case's newest answer, without asking the source.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="lookup">The transaction's lookup.</param>
    /// <param name="cancellationToken">Stops the lookup before it starts.</param>
    /// <returns>The lookup with the spends and their origin, or the same lookup when the case has none.</returns>
    private async Task<BitcoinLookup<BitcoinTransaction>> WithSpendsAsync(
        ICase @case, BitcoinLookup<BitcoinTransaction> lookup, CancellationToken cancellationToken)
    {
        var transaction = lookup.Value!;
        var (stored, heightThen) = await Task.Run(
            () => @case.Read(reader =>
            {
                var found = SourceLookups.StoredAs(
                    _store.Latest(reader, ResponseKinds.OutputSpends, transaction.Txid.Value),
                    body => EsploraParser.ParseOutputSpends(body));
                var height = found is { } spends ? _store.KnownHeight(reader, spends.Response.Origin.Sequence) : null;
                return (found, height);
            }),
            cancellationToken).ConfigureAwait(false);
        if (stored is not { } spends || spends.Value.Count != transaction.Outputs.Count)
        {
            return lookup;
        }

        var outputs = transaction.Outputs.Select(
            output => output with { SpentBy = spends.Value[output.Index].SpentBy });
        return lookup with
        {
            Value = transaction with { Outputs = [.. outputs] },
            Origins = [.. lookup.Origins.Append(spends.Response.Origin).OrderBy(origin => origin.Sequence)],
            Problem = Join(lookup.Problem, SpendsProblem(spends.Value, transaction, spends.Response, heightThen)),
        };
    }

    /// <summary>
    /// Says that the spends are as of when they were fetched, unless every output is spent deep in the chain: an
    /// output shown as unspent may have been spent since.
    /// </summary>
    /// <param name="spends">The spends.</param>
    /// <param name="transaction">The transaction, whose outputs say which can never be spent.</param>
    /// <param name="response">The response they come from.</param>
    /// <param name="heightThen">The highest block the case knew of when the response was sealed.</param>
    /// <returns>The text for the user, or <see langword="null"/> when the spends cannot have changed.</returns>
    private string? SpendsProblem(
        IReadOnlyList<BitcoinOutputSpend> spends, BitcoinTransaction transaction, StoredResponse response,
        long? heightThen)
    {
        if (SettledAt(spends, transaction) is { } height
            && SourceLookups.Confirmations(height, heightThen) >= _options.SafeConfirmations)
        {
            return null;
        }

        var fetchedAt = SourceLookups.LocalText(response.Origin.ReceivedLocal);
        return $"Vilka utgångar som är spenderade är hämtat {fetchedAt} och kan ha ändrats sedan.";
    }

    /// <summary>Joins two texts for the user, either of which may be missing.</summary>
    /// <param name="first">The first text.</param>
    /// <param name="second">The second text.</param>
    /// <returns>Both, one, or <see langword="null"/>.</returns>
    private static string? Join(string? first, string? second) =>
        first is null || second is null ? first ?? second : $"{first} {second}";

    /// <summary>Finds a transaction in the case, alone or on an address's page.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The transaction and its response, or <see langword="null"/>.</returns>
    private (BitcoinTransaction Value, StoredResponse Response)? StoredTransaction(ICaseReader reader, Txid txid)
    {
        // A description that contradicts the transaction's sealed bytes is passed over, so that one bad answer from
        // the source never hides an earlier good one, nor keeps the transaction from being asked for again.
        var bytes = SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.TransactionHex, txid.Value),
            body => RawTransactionReader.ParseHex(body, txid))?.Value;
        foreach (var response in _store.AllWithTransaction(reader, txid))
        {
            var transaction = SourceLookups.StoredAs(response, body => response.Kind == ResponseKinds.Transaction
                ? _parser.ParseTransaction(body, txid)
                : _parser.ParseTransactionInList(body, txid));
            if (transaction is { } found && (bytes is null || RawTransactionReader.Compare(found.Value, bytes) is null))
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Gives the height of the shallowest of some transactions, when they are all confirmed.</summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The highest block height, or <see langword="null"/> when one is unconfirmed.</returns>
    private static long? Shallowest(IEnumerable<BitcoinTransaction> transactions)
    {
        long highest = -1;
        foreach (var transaction in transactions)
        {
            if (transaction.Block is null)
            {
                return null;
            }

            highest = Math.Max(highest, transaction.Block.Height);
        }

        return highest < 0 ? null : highest;
    }

    /// <summary>
    /// Gives the height of the shallowest spend, when every output that can be spent is spent in a block; an unspent
    /// output can still be spent, so the answer can change. An output that can never be spent (OP_RETURN, or a script
    /// too large to run) never waits for a spend, so it counts from the transaction's own block.
    /// </summary>
    /// <param name="spends">The spends.</param>
    /// <param name="transaction">
    /// The transaction, when the case has it; without it every output counts as one that can be spent.
    /// </param>
    /// <returns>The highest block height, or <see langword="null"/>.</returns>
    private static long? SettledAt(IReadOnlyList<BitcoinOutputSpend> spends, BitcoinTransaction? transaction)
    {
        if (spends.Count == 0)
        {
            return null;
        }

        var outputs = transaction?.Outputs.Count == spends.Count ? transaction.Outputs : null;
        var highest = outputs is null ? null : transaction!.Block?.Height;
        foreach (var spend in spends)
        {
            if (spend.SpentBy is null && outputs is not null && IsUnspendable(outputs[spend.Index]))
            {
                continue;
            }

            if (spend.SpentBy?.Block is not { } block)
            {
                return null;
            }

            highest = Math.Max(highest ?? block.Height, block.Height);
        }

        return highest;
    }

    /// <summary>
    /// Says whether an output can never be spent: an OP_RETURN script, or a script larger than the 10 000 bytes a
    /// script may have when it runs.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> when no transaction can ever spend it.</returns>
    public static bool IsUnspendable(BitcoinOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.ScriptType == BitcoinScriptType.OpReturn || output.Script.Length > MaximumScriptHex;
    }

    /// <summary>
    /// Makes a page of an address's transactions and works out where the next page starts: after the last confirmed
    /// transaction, as long as the page has one. The page size is the source's own, so only an empty page ends the
    /// history; a short page could just be a source with smaller pages.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="after">The txid the page continues after.</param>
    /// <param name="transactions">The transactions on the page.</param>
    /// <returns>The page.</returns>
    private static BitcoinAddressPage Page(BitcoinAddress address, Txid? after, List<BitcoinTransaction> transactions)
    {
        var lastConfirmed = transactions.LastOrDefault(transaction => transaction.IsConfirmed);
        return new BitcoinAddressPage(address, after, transactions, lastConfirmed?.Txid);
    }

    /// <summary>Gives what some transactions add to the indexes.</summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The index entry.</returns>
    private static ResponseIndex IndexOf(IEnumerable<BitcoinTransaction> transactions) =>
        new([.. transactions.Select(transaction => (transaction.Txid, transaction.Block?.Height,
            transaction.Block?.Hash.Value))], null);
}
