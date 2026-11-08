using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises coins out of recognised CoinJoins that are spent together again: two or more outputs of CoinJoins (the same round
/// or different ones), or a mixed output together with the round's change (toxic change). Whoever does that shows
/// that the coins probably have one owner, and the mix no longer hides them, so the trail can go on there. A check
/// with an area of the sort Obfuscation; a judgement, never a fact (principle 2).
/// </summary>
internal sealed class CoinJoinMergeCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "coinjoin-merge";

    /// <summary>How many coins out of CoinJoins a transaction must spend together to count.</summary>
    public const int MinimumMixedInputs = 2;

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Mixade mynt slås ihop";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Only a recognised CoinJoin mixes; a possible one may be a batch of payouts, whose outputs have different
        // owners anyway.
        var all = input.CoinJoins();
        var coinJoins = all.Where(entry => entry.Value.EndsTrail).ToDictionary(entry => entry.Key, entry => entry.Value);
        if (coinJoins.Count == 0)
        {
            return [];
        }

        var byId = input.ById();
        var findings = new List<CheckFinding>();
        foreach (var transaction in input.Transactions.Where(transaction => !all.ContainsKey(transaction.Txid)))
        {
            var mixed = transaction.Inputs
                .Where(spent => spent.PreviousTxid is { } txid && coinJoins.ContainsKey(txid))
                .ToList();
            if (mixed.Count >= MinimumMixedInputs)
            {
                findings.Add(Describe(transaction, mixed, coinJoins, byId));
            }
        }

        return findings;
    }

    /// <summary>Describes a transaction that spends coins out of CoinJoins together.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="mixed">Its inputs that spend outputs of CoinJoins.</param>
    /// <param name="coinJoins">The CoinJoins the clustering knows of.</param>
    /// <param name="byId">The graph's transactions.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(
        BitcoinTransaction transaction,
        IReadOnlyList<BitcoinInput> mixed,
        Dictionary<Txid, CoinJoinFinding> coinJoins,
        Dictionary<Txid, BitcoinTransaction> byId)
    {
        var rounds = mixed.Select(spent => spent.PreviousTxid!).Distinct().ToList();
        var toxic = mixed.Any(spent => spent.Amount is { } amount
            && amount != coinJoins[spent.PreviousTxid!].Denomination);
        var count = CheckText.Count(mixed.Count);
        var from = rounds.Count == 1
            ? $"samma CoinJoin ({CheckText.Short(rounds[0])})"
            : $"{CheckText.Count(rounds.Count)} olika CoinJoin";
        var change = toxic
            ? " Minst en av dem är inte mixens belopp utan växeln ur den (toxic change), som pekar på den som "
                + "betalade in."
            : string.Empty;
        var why = $"Analysen {this.Named()} ser att tx {CheckText.Short(transaction.Txid)} spenderar {count} "
            + $"utgångar ur {from} tillsammans.{change} Den som slår ihop mixade mynt visar att de troligen har samma "
            + "ägare, och mixen döljer dem inte längre: spåret kan gå vidare här. Det är en bedömning, inte ett "
            + "faktum: en tjänst som tar emot mynt från flera kunder kan också spendera dem tillsammans.";
        return new CheckFinding(
            transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            toxic ? "Mixade mynt slås ihop med växeln" : "Mixade mynt slås ihop",
            $"{count} utgångar ur {from}",
            why,
            [.. transaction.InputAddresses()],
            [transaction.Txid],
            [
                CheckBasis.Of(transaction),
                .. rounds.Select(txid => byId.TryGetValue(txid, out var round) ? CheckBasis.Of(round) : null)
                    .OfType<CheckBasis>(),
            ],
            []);
    }
}
