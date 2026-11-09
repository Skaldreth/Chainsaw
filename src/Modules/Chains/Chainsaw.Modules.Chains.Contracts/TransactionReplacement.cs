namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What the source says now about one coin a transaction spent.</summary>
public enum ReplacedCoinState
{
    /// <summary>The case has no answer about the coin since the transaction was last seen.</summary>
    NotAsked,

    /// <summary>Another transaction spends the coin: it replaced this one (replace-by-fee or a double spend).</summary>
    SpentByOther,

    /// <summary>The coin is unspent: the transaction left the mempool and nothing spends the money yet.</summary>
    Unspent,

    /// <summary>The source still names the transaction as the coin's spender, though it no longer knows it.</summary>
    SpentByItself,
}

/// <summary>
/// One coin a transaction spent, and what the source says about it now (<c>/tx/:txid/outspends</c> of the coin's
/// transaction, sealed in the case). The answer is a fact as the source gave it (principle 1); that the spender
/// continues the gone transaction's money is the user's judgement (principle 2).
/// </summary>
/// <param name="InputIndex">The input's place in the transaction that is gone.</param>
/// <param name="PreviousTxid">The transaction whose output the input spent.</param>
/// <param name="PreviousIndex">The output's index.</param>
/// <param name="State">What the source says now.</param>
/// <param name="SpentBy">The input that spends the coin now, for <see cref="ReplacedCoinState.SpentByOther"/>.</param>
/// <param name="Sequence">The sealed answer's number in the case, or <see langword="null"/> when not asked.</param>
public sealed record ReplacedCoin(
    int InputIndex, Txid PreviousTxid, int PreviousIndex, ReplacedCoinState State, BitcoinSpend? SpentBy,
    long? Sequence)
{
    /// <summary>
    /// Gets the host the answer came from, in lower case, as sealed, or <see langword="null"/> when not asked; an
    /// answer from another host than the one that said the transaction is gone confirms it.
    /// </summary>
    public string? Host { get; init; }
}

/// <summary>What the coins of a transaction the source no longer knows are, taken together.</summary>
public enum ReplacementKind
{
    /// <summary>Not every coin has been asked about; nothing can be said yet.</summary>
    Unknown,

    /// <summary>Other transactions spend every coin: the transaction was replaced.</summary>
    Replaced,

    /// <summary>Another transaction spends some of the coins, and the rest are unspent.</summary>
    PartlyReplaced,

    /// <summary>
    /// Every coin is unspent: the transaction left the mempool without anything taking its place, and the money is
    /// back where it was.
    /// </summary>
    Dropped,

    /// <summary>The source still names the transaction as the spender: its answers do not agree.</summary>
    Conflicting,
}

/// <summary>
/// Which transaction spends the coins of a transaction that the source no longer knows (sketch GR9a,
/// <em>Ersatt av</em>): read from sealed answers about each coin the transaction spent. A replacer is a fact as the
/// source answered (an input spends the same coin); that it carries the same money on is a judgement (principle 2).
/// </summary>
/// <param name="Transaction">The transaction that is gone.</param>
/// <param name="Coins">Every coin it spent, in its inputs' order; a coinbase has none.</param>
public sealed record TransactionReplacement(Txid Transaction, IReadOnlyList<ReplacedCoin> Coins)
{
    /// <summary>Gets how many transactions its coins came from.</summary>
    public int PreviousCount { get; init; }

    /// <summary>
    /// Gets how many of those the case has an answer about; fewer than <see cref="PreviousCount"/> when a lookup asks
    /// about at most a set number at a time, and the rest are asked at the next.
    /// </summary>
    public int PreviousAnswered { get; init; }

    /// <summary>Gets the hosts the answers about its coins came from, each once.</summary>
    public IReadOnlyList<string> Hosts =>
        [.. Coins.Select(coin => coin.Host).OfType<string>().Distinct(StringComparer.Ordinal)];

    /// <summary>Gets the transactions that spend its coins now, each once, in the order of the inputs.</summary>
    public IReadOnlyList<Txid> Replacers =>
        [.. Coins.Where(coin => coin.SpentBy is not null).Select(coin => coin.SpentBy!.Txid).Distinct()];

    /// <summary>Gets what the coins are, taken together.</summary>
    public ReplacementKind Kind
    {
        get
        {
            if (Coins.Count == 0 || Coins.Any(coin => coin.State == ReplacedCoinState.NotAsked))
            {
                return ReplacementKind.Unknown;
            }

            if (Coins.Any(coin => coin.State == ReplacedCoinState.SpentByItself))
            {
                return ReplacementKind.Conflicting;
            }

            var replaced = Coins.Count(coin => coin.State == ReplacedCoinState.SpentByOther);
            return replaced switch
            {
                0 => ReplacementKind.Dropped,
                _ when replaced == Coins.Count => ReplacementKind.Replaced,
                _ => ReplacementKind.PartlyReplaced,
            };
        }
    }

    /// <summary>Gives the input that spends a coin of the gone transaction now, when another does.</summary>
    /// <param name="previousTxid">The coin's transaction.</param>
    /// <param name="previousIndex">The coin's output.</param>
    /// <returns>The spending input, or <see langword="null"/>.</returns>
    public BitcoinSpend? SpenderOf(Txid previousTxid, int previousIndex) =>
        Coins.FirstOrDefault(coin => coin.PreviousTxid == previousTxid && coin.PreviousIndex == previousIndex
            && coin.State == ReplacedCoinState.SpentByOther)?.SpentBy;
}
