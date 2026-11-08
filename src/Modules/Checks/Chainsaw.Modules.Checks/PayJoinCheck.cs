using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Says when the clustering is less sure because a transaction may be a PayJoin, which cannot be seen on the chain
/// and can only be guessed: common-input-ownership joined its inputs, but one input was not needed for either payment
/// (wallets seldom add an input they do not need, and in a PayJoin the payee adds one of its own), and more so when the
/// inputs are of different types. A check with an area of the sort Wallet; a judgement, never a fact (principle 2).
/// </summary>
internal sealed class PayJoinCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "payjoin-hint";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Möjlig PayJoin";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var coinJoins = input.CoinJoins();
        var findings = new List<CheckFinding>();
        foreach (var transaction in input.Transactions.Where(transaction => !coinJoins.ContainsKey(transaction.Txid)))
        {
            if (input.Conclusions.TryGetValue(transaction.Txid, out var conclusions)
                && conclusions.Evidence.Any(evidence => evidence.Heuristic.Name == HeuristicNames.CommonInputOwnership
                    && evidence.Strength is EvidenceStrength.Merge or EvidenceStrength.Suggestion)
                && HasUnneededInput(transaction))
            {
                findings.Add(Describe(transaction));
            }
        }

        return findings;
    }

    /// <summary>
    /// Says whether a transaction with two inputs or more and two outputs had an input it did not need: without its
    /// smallest input, the others still pay the larger output and the fee, so that whichever output is the payment, an
    /// input could have been left out.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it had.</returns>
    private static bool HasUnneededInput(BitcoinTransaction transaction)
    {
        var paying = transaction.PayingOutputs();
        if (transaction.Inputs.Count < 2 || paying.Count != 2 || transaction.Outputs.Count != 2
            || transaction.TotalIn() is not { } total)
        {
            return false;
        }

        var smallest = transaction.Inputs.Min(spent => spent.Amount!.Value.Value);
        var largest = paying.Max(output => output.Amount.Value);
        return total.Value - smallest >= largest + transaction.Fee.Value;
    }

    /// <summary>Describes a possible PayJoin.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(BitcoinTransaction transaction)
    {
        var mixedTypes = transaction.Inputs.Select(spent => spent.ScriptType).Distinct().Count() > 1;
        var types = mixedTypes
            ? " Inputs är dessutom av olika adresstyp, som när två plånböcker bidrar."
            : string.Empty;
        var why = $"Analysen {this.Named()} ser att common-input-ownership slog ihop inputs i tx "
            + $"{CheckText.Short(transaction.Txid)}, men att en input inte behövdes: utan den minsta räcker de andra "
            + $"till den större utbetalningen och avgiften.{types} En plånbok lägger sällan till en input den inte "
            + "behöver, så tx:en kan vara en PayJoin, där mottagaren lade till ett eget mynt; då har inputs två "
            + "ägare. Den kan också vara en plånbok som samlar ihop mynt. Klustringen är osäkrare här; bryt upp "
            + "sammanslagningen om det visar sig vara en PayJoin. Det är en bedömning, inte ett faktum.";
        return new CheckFinding(
            transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Wallet,
            "Möjlig PayJoin",
            mixedTypes ? "En onödig input, olika adresstyper" : "En onödig input",
            why,
            [.. transaction.InputAddresses()],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            []);
    }
}
