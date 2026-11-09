using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises a likely mixer that no label names, from how it behaves (<see cref="LikelyMixers"/>): many independent
/// senders pay in, many recipients are paid, and most deposits come out again soon after as the deposit minus a usual
/// mixer fee, paid with other coins. A check with an area of the sort Obfuscation, which says to set the label
/// <em>Mixer</em> if the user agrees, so that the mixer analyses weigh its outputs; a judgement, never a fact
/// (principle 2). <c>mixer-chain</c> follows a chain through it.
/// </summary>
internal sealed class MixerBehaviourCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "mixer-behaviour";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Trolig mixer";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return [.. LikelyMixers.Of(input).Select(Describe)];
    }

    /// <summary>Describes a likely mixer.</summary>
    /// <param name="mixer">The likely mixer.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(LikelyMixer mixer)
    {
        var matched = mixer.Matched.ToList();
        var (count, of) = (CheckText.Count(matched.Count), CheckText.Count(mixer.Deposits.Count));
        var fees = matched.Select(deposit => Fee(deposit.Deposit, deposit.Matches[0])).ToList();
        var range = $"{CheckText.Percent(fees.Min())}–{CheckText.Percent(fees.Max())}";
        var why = $"Analysen {this.Named()} ser en ägare utan etikett, som klustringen räknar den, som tar emot "
            + $"insättningar från {CheckText.Count(mixer.Senders)} olika avsändare och betalar ut till "
            + $"{CheckText.Count(mixer.Recipients)} olika mottagare. {count} av de {of} insättningarna kommer ut igen "
            + $"inom {CheckText.Span(MixerOutputsCheck.Window)}, till någon som inte satte in, som insättningen minus "
            + $"{range} och betalda med andra mynt än insättningens egna. Så arbetar en central mixer: den tar en "
            + "avgift och betalar tillbaka ur en gemensam pott, så att ingen tx knyter ihop in och ut. Håller du med, "
            + "sätt etiketten Mixer med L, så väger mixerns förslag dess utgångar. Det är en bedömning, inte ett "
            + "faktum: en växlare eller en tjänst som betalar tillbaka nästan samma belopp kan se likadan ut, och "
            + "klustret kan ha fler ägare.";
        var addresses = mixer.Deposits.Select(deposit => deposit.Deposit.Output.Address!)
            .Concat(matched.SelectMany(deposit => deposit.Matches[0].Transaction.InputAddresses()))
            .Distinct();
        var basis = matched.SelectMany(deposit => new[] { deposit.Deposit.Transaction, deposit.Matches[0].Transaction })
            .DistinctBy(transaction => transaction.Txid)
            .Select(CheckBasis.Of);
        return new CheckFinding(
            "likely-mixer:" + mixer.Owner.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            "Trolig mixer utan etikett",
            $"{count} av {of} insättningar ut igen minus {range}, från {CheckText.Count(mixer.Senders)} avsändare",
            why,
            [.. addresses],
            [],
            [.. basis],
            []);
    }

    /// <summary>Gives the share of a deposit that did not come out in a payout.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="payout">The payout.</param>
    /// <returns>The fee, from 0 to 1.</returns>
    private static double Fee(MixerDeposit deposit, LikelyPayout payout) =>
        (double)(deposit.Output.Amount.Value - payout.Output.Amount.Value) / deposit.Output.Amount.Value;
}
