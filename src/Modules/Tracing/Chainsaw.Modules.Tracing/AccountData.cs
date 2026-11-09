using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The Ethereum facts the graph is built from, read from the case: every movement and fee each once, the whole
/// transactions, the accounts whose history was read, and the blocks' hashes.
/// </summary>
/// <param name="Transfers">The movements, each once, in chain order (<see cref="AccountGraph.ChainOrder"/>).</param>
/// <param name="Fees">The fees, one per transaction.</param>
/// <param name="Transactions">The transactions read whole.</param>
/// <param name="ReadAccounts">The accounts whose history was read.</param>
/// <param name="MayBeOutdated">The accounts whose read history may have changed since.</param>
internal sealed record AccountData(
    IReadOnlyList<EthereumTransfer> Transfers,
    IReadOnlyList<EthereumFee> Fees,
    IReadOnlyDictionary<EthereumTxHash, EthereumTransaction> Transactions,
    IReadOnlySet<ChainAddress> ReadAccounts,
    IReadOnlySet<ChainAddress> MayBeOutdated)
{
    /// <summary>Gets data with nothing in it, for a graph without Ethereum.</summary>
    public static AccountData Empty { get; } = new(
        [], [], new Dictionary<EthereumTxHash, EthereumTransaction>(), new HashSet<ChainAddress>(),
        new HashSet<ChainAddress>());

    /// <summary>Gets each block's hash by height, where the case has it.</summary>
    public IReadOnlyDictionary<long, string> BlockHashes { get; init; } = new Dictionary<long, string>();

    /// <summary>
    /// Gets the accounts whose history was read only in part (held back at the limit or incomplete); they do not carry
    /// money on.
    /// </summary>
    public IReadOnlySet<ChainAddress> PartlyRead { get; init; } = new HashSet<ChainAddress>();

    /// <summary>Gets what could not be read, in Swedish.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>
    /// Joins what the whole transactions and the histories say. A transaction read whole is the surest answer and
    /// gives its own movements and fee; the histories add the movements and fees of the other transactions, each once.
    /// </summary>
    /// <param name="whole">The transactions read whole.</param>
    /// <param name="history">The movements the histories gave.</param>
    /// <param name="historyFees">The fees the histories gave.</param>
    /// <param name="read">The accounts whose history was read.</param>
    /// <param name="outdated">The accounts whose read history may have changed.</param>
    /// <returns>The data.</returns>
    public static AccountData Combine(
        IReadOnlyDictionary<EthereumTxHash, EthereumTransaction> whole,
        IEnumerable<EthereumTransfer> history,
        IEnumerable<EthereumFee> historyFees,
        IReadOnlySet<ChainAddress> read,
        IReadOnlySet<ChainAddress> outdated)
    {
        var transfers = new Dictionary<TransferKey, EthereumTransfer>();
        foreach (var transfer in whole.Values.SelectMany(transaction => transaction.Transfers)
            .Concat(history.Where(transfer => !whole.ContainsKey(transfer.Transaction))))
        {
            transfers.TryAdd(TransferKey.Of(transfer), transfer);
        }

        var fees = new Dictionary<EthereumTxHash, EthereumFee>();
        foreach (var transaction in whole.Values.Where(transaction => transaction.IsConfirmed))
        {
            fees[transaction.Hash] = new EthereumFee(
                transaction.Hash, transaction.From, transaction.Fee, transaction.Succeeded, transaction.Block);
        }

        foreach (var fee in historyFees)
        {
            fees.TryAdd(fee.Transaction, fee);
        }

        var places = Places(transfers.Values.Select(transfer => (transfer.Transaction, transfer.Block))
            .Concat(fees.Values.Select(fee => (fee.Transaction, fee.Block))));
        return new AccountData(
            [.. transfers.Values.Select(transfer => WithPlace(transfer, places)).OrderBy(AccountGraph.ChainOrder)],
            [.. fees.Values.Where(fee => fee.Amount.Units.Sign > 0).Select(fee => WithPlace(fee, places))
                .OrderBy(fee => fee.Block?.Height ?? long.MaxValue)
                .ThenBy(fee => fee.Block?.TransactionIndex ?? long.MaxValue)
                .ThenBy(fee => fee.Transaction.Value, StringComparer.Ordinal)],
            whole,
            read,
            outdated);
    }

    /// <summary>
    /// Gathers each transaction's place in its block from the answers that gave it, so that every movement of one
    /// transaction is ordered the same way whichever history it came from (a list of internal transactions gives no
    /// place, the list of the address's transactions does).
    /// </summary>
    /// <param name="blocks">Each movement's or fee's transaction and block.</param>
    /// <returns>The places by transaction.</returns>
    private static Dictionary<EthereumTxHash, long> Places(
        IEnumerable<(EthereumTxHash Transaction, EthereumBlockReference? Block)> blocks)
    {
        var places = new Dictionary<EthereumTxHash, long>();
        foreach (var (transaction, block) in blocks)
        {
            if (block?.TransactionIndex is { } place)
            {
                places.TryAdd(transaction, place);
            }
        }

        return places;
    }

    /// <summary>Gives a movement with its transaction's place in the block, when an answer gave the place.</summary>
    /// <param name="transfer">The movement.</param>
    /// <param name="places">The places by transaction.</param>
    /// <returns>The movement, with the place on its block.</returns>
    private static EthereumTransfer WithPlace(EthereumTransfer transfer, Dictionary<EthereumTxHash, long> places) =>
        transfer.Block is { TransactionIndex: null } block && places.TryGetValue(transfer.Transaction, out var place)
            ? transfer with { Block = block with { TransactionIndex = place } }
            : transfer;

    /// <summary>Gives a fee with its transaction's place in the block, when an answer gave the place.</summary>
    /// <param name="fee">The fee.</param>
    /// <param name="places">The places by transaction.</param>
    /// <returns>The fee, with the place on its block.</returns>
    private static EthereumFee WithPlace(EthereumFee fee, Dictionary<EthereumTxHash, long> places) =>
        fee.Block is { TransactionIndex: null } block && places.TryGetValue(fee.Transaction, out var place)
            ? fee with { Block = block with { TransactionIndex = place } }
            : fee;

    /// <summary>
    /// Finds the swaps the transactions show (<see cref="SwapOf"/>). Only the signer is asked, since the pool on the
    /// other side does the same in reverse; when the signer is not known (a history that is not its own), no swap is
    /// shown.
    /// </summary>
    /// <param name="endpoint">
    /// Says why an address is one the trail must reach (an exchange, a mixer or a bridge by its label, with its
    /// source), or <see langword="null"/> when it is not; <see langword="null"/> knows none.
    /// </param>
    /// <returns>The swaps, in chain order.</returns>
    public IReadOnlyList<TokenSwap> Swaps(Func<EthereumAddress, string?>? endpoint = null) =>
        ReadSwaps(endpoint).Swaps;

    /// <summary>
    /// Finds the swaps the transactions show, and the transactions that would be swaps but for a counterparty the
    /// trail must reach, so that <em>Varför?</em> can say why there is none.
    /// </summary>
    /// <param name="endpoint">Says why an address is one the trail must reach, or <see langword="null"/>.</param>
    /// <returns>The swaps and the refused ones, in chain order.</returns>
    public (IReadOnlyList<TokenSwap> Swaps, IReadOnlyList<SwapRefusal> Refused) ReadSwaps(
        Func<EthereumAddress, string?>? endpoint = null)
    {
        var signers = Fees.ToDictionary(fee => fee.Transaction, fee => fee.Payer);
        foreach (var transaction in Transactions.Values)
        {
            signers[transaction.Hash] = transaction.From;
        }

        var swaps = new List<TokenSwap>();
        var refused = new List<SwapRefusal>();
        foreach (var transaction in Transfers.GroupBy(transfer => transfer.Transaction))
        {
            var moves = transaction.Where(transfer => transfer.From != transfer.To && transfer.Amount.Units.Sign > 0)
                .ToList();
            if (signers.GetValueOrDefault(transaction.Key) is not { } account
                || SwapOf(transaction.Key, account, moves) is not { } swap)
            {
                continue;
            }

            if (RefusalOf(swap, account, moves, endpoint ?? (_ => null)) is { } refusal)
            {
                refused.Add(refusal);
            }
            else
            {
                swaps.Add(swap);
            }
        }

        return (swaps, refused);
    }

    /// <summary>
    /// Reads a swap in one transaction, and when in doubt reads none: a false swap would move the traced money away
    /// from a real recipient, while a missed one only leaves the trail honestly at the pool. It is a swap when
    /// <list type="bullet">
    /// <item>the signer got exactly one asset back that it did not also send (what comes back in an asset it sent, a
    /// refund, is never a purchase);</item>
    /// <item>what came back has a value Chainsaw knows (ether, WETH or a token it knows by its contract), always, also
    /// through a known exchange's router, since anyone can open a pool there for a token of their own; a worthless
    /// token, as dust or from one's own pool, never makes a payment a swap;</item>
    /// <item>it sent exactly one asset, and every movement of it went to the trade's side
    /// (<see cref="TradeSide"/>); one movement to anyone else, a third party or a fee collector, and it is no
    /// swap.</item>
    /// </list>
    /// Whether a counterparty is one the trail must reach is asked after (<see cref="RefusalOf"/>).
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="account">The account that signed it.</param>
    /// <param name="moves">Its movements of value between two addresses.</param>
    /// <returns>The swap, or <see langword="null"/>.</returns>
    private TokenSwap? SwapOf(EthereumTxHash transaction, EthereumAddress account, List<EthereumTransfer> moves)
    {
        var outgoing = moves.Where(transfer => transfer.From == account).ToList();
        var sent = outgoing.Select(transfer => transfer.Amount.Asset).ToHashSet();
        var bought = moves.Where(transfer => transfer.To == account && !sent.Contains(transfer.Amount.Asset)).ToList();
        if (sent.Count != 1 || bought.Select(transfer => transfer.Amount.Asset).Distinct().Count() != 1)
        {
            return null;
        }

        var boughtAsset = bought[0].Amount.Asset;
        var venue = VenueOf(transaction, account, moves);
        if (!HasKnownValue(boughtAsset))
        {
            return null;
        }

        var side = TradeSide(transaction, account, moves, boughtAsset, venue);
        if (!outgoing.All(transfer => side.Contains(transfer.To)))
        {
            return null;
        }

        return new TokenSwap(
            transaction,
            account.ToChainAddress(),
            Sum(outgoing[0].Amount.Asset, outgoing),
            Sum(boughtAsset, bought))
        {
            Venue = venue,
            SoldMovements = [.. outgoing.Select(AccountSeed.Of)],
            Time = bought[0].Block?.Time,
        };
    }

    /// <summary>
    /// Says whether Chainsaw knows what an asset is worth: ether, wrapped ether, or a token it knows by its contract
    /// (<see cref="KnownTokens"/>), never by its symbol.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool HasKnownValue(Asset asset) =>
        asset.Kind == AssetKind.Native || asset == KnownTokens.EthereumWeth || KnownTokens.IssuerOf(asset) is not null;

    /// <summary>
    /// Says why a swap is no swap after all: a counterparty is a known bridge or mixer, or one the trail must reach by
    /// its label, so the money goes on to it as an ordinary payment.
    /// </summary>
    /// <param name="swap">The swap it would be.</param>
    /// <param name="account">The signer.</param>
    /// <param name="moves">The transaction's movements of value between two addresses.</param>
    /// <param name="endpoint">Says why an address is one the trail must reach, or <see langword="null"/>.</param>
    /// <returns>The refusal, or <see langword="null"/> when it is a swap.</returns>
    private static SwapRefusal? RefusalOf(
        TokenSwap swap, EthereumAddress account, List<EthereumTransfer> moves, Func<EthereumAddress, string?> endpoint)
    {
        var counterparties = moves.Where(move => move.From == account || move.To == account)
            .Select(move => move.From == account ? move.To : move.From)
            .Distinct();
        foreach (var party in counterparties)
        {
            var known = KnownContracts.Find(party);
            var why = known is { Category: KnownContractCategory.Bridge or KnownContractCategory.Mixer }
                ? $"{known.Name} ({KnownContracts.Name} {KnownContracts.Version})"
                : endpoint(party);
            if (why is not null)
            {
                return new SwapRefusal(swap, party.ToChainAddress(), why);
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the trade's side of a transaction: the contract the transaction called (when it is read whole), the
    /// addresses that sent the bought asset to the signer, the known exchange, and every address that in the same
    /// transaction demonstrably moved the bought asset (or, for ether, wrapped ether) to one of those, such as the
    /// pair behind a router. What the source does not give cannot be shown, so a pair that only an internal movement
    /// would reveal stays outside.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="account">The signer.</param>
    /// <param name="moves">Its movements of value between two addresses.</param>
    /// <param name="boughtAsset">What the signer got back.</param>
    /// <param name="venue">The known exchange, or <see langword="null"/>.</param>
    /// <returns>The addresses on the trade's side.</returns>
    private HashSet<EthereumAddress> TradeSide(
        EthereumTxHash transaction,
        EthereumAddress account,
        List<EthereumTransfer> moves,
        Asset boughtAsset,
        KnownContract? venue)
    {
        var side = moves.Where(transfer => transfer.To == account && transfer.Amount.Asset == boughtAsset)
            .Select(transfer => transfer.From)
            .ToHashSet();
        if (Transactions.GetValueOrDefault(transaction)?.To is { } called)
        {
            side.Add(called);
        }

        if (venue is not null)
        {
            side.Add(venue.Address);
        }

        var carries = boughtAsset.Kind == AssetKind.Native
            ? new HashSet<Asset> { boughtAsset, KnownTokens.EthereumWeth }
            : [boughtAsset];
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var move in moves.Where(move => carries.Contains(move.Amount.Asset) && side.Contains(move.To)))
            {
                grew |= move.From != account && side.Add(move.From);
            }
        }

        return side;
    }

    /// <summary>
    /// Finds the known exchange or aggregator a swap went through: the contract the transaction was sent to, when the
    /// whole transaction is read, or else a contract the account sent to or received from in it. Only an exchange or
    /// an aggregator names a swap; a mixer or a bridge on the other side is not one.
    /// </summary>
    /// <param name="transaction">The swap's transaction.</param>
    /// <param name="account">The account that swapped.</param>
    /// <param name="moves">The transaction's movements between two addresses.</param>
    /// <returns>The contract, or <see langword="null"/> when none is known.</returns>
    private KnownContract? VenueOf(
        EthereumTxHash transaction, EthereumAddress account, IEnumerable<EthereumTransfer> moves)
    {
        var called = Transactions.GetValueOrDefault(transaction)?.To;
        var counterparties = moves.Select(move => CounterpartyOf(move, account)).OfType<EthereumAddress>();
        return (called is null ? counterparties : counterparties.Prepend(called))
            .Select(KnownContracts.Find)
            .FirstOrDefault(known => known?.Category is KnownContractCategory.Dex or KnownContractCategory.Aggregator);
    }

    /// <summary>Gives the other side of a movement an account is on.</summary>
    /// <param name="move">The movement.</param>
    /// <param name="account">The account.</param>
    /// <returns>The other address, or <see langword="null"/> when the account is not on the movement.</returns>
    private static EthereumAddress? CounterpartyOf(EthereumTransfer move, EthereumAddress account)
    {
        if (move.From == account)
        {
            return move.To;
        }

        return move.To == account ? move.From : null;
    }

    /// <summary>Gives a movement's block with its hash, when the case has the hash.</summary>
    /// <param name="block">The movement's block.</param>
    /// <returns>The block for the graph, or <see langword="null"/> while unconfirmed or the hash is not known.</returns>
    public BlockReference? BlockOf(EthereumBlockReference? block)
    {
        if (block is null)
        {
            return null;
        }


        var text = block.Hash ?? BlockHashes.GetValueOrDefault(block.Height);
        return BlockHash.TryParseEthereum(text, out var hash) ? new BlockReference(block.Height, hash, block.Time) : null;
    }

    /// <summary>Adds the amounts of some movements of one asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="transfers">The movements.</param>
    /// <returns>The sum.</returns>
    private static AssetAmount Sum(Asset asset, IEnumerable<EthereumTransfer> transfers) =>
        transfers.Aggregate(AssetAmount.Zero(asset), (sum, transfer) => sum + transfer.Amount);
}
