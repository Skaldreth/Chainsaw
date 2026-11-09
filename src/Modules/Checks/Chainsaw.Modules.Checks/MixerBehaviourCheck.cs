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

    /// <summary>The analysis's name and version, as <c>mixer-chain</c> names it.</summary>
    public static readonly HeuristicId Identity = new(Name, 1);

    /// <summary>The title when both the fee and the delay vary.</summary>
    public const string MixerTitle = "Trolig mixer utan etikett";

    /// <summary>The title when the fee or the delay is mostly steady, as a forwarding service's are.</summary>
    public const string AmbiguousTitle = "Trolig mixer eller vidarebetalande tjänst";

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

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
        var fees = matched.Select(deposit => deposit.Fee).Order().ToList();
        var median = $"{CheckText.Percent(LikelyMixers.Percentile(fees, 0.5))} i median";
        var inParts = matched.Count(deposit => deposit.Match!.IsParts);
        var parts = inParts > 0 ? $", {CheckText.Count(inParts)} av dem i delar" : string.Empty;
        var why = $"Analysen {this.Named()} ser en ägare utan etikett, som klustringen räknar den, som tar emot "
            + $"insättningar från {CheckText.Count(mixer.Senders)} olika avsändare och betalar ut till "
            + $"{CheckText.Count(mixer.Recipients)} olika mottagare. {count} av de {of} insättningarna kommer ut igen "
            + $"inom {CheckText.Span(MixerOutputsCheck.Window)}{parts}, till någon som inte satte in, som insättningen "
            + $"minus {median}, betalda med andra mynt än insättningens egna och oftast till adresser som bara tar "
            + "emot en gång; varje utbetalning räknas för högst en insättning, och de flesta utbetalningarna förklaras "
            + "så. Med beloppen förskjutna, där bara slumpen kan träffa, förklaras "
            + $"{Average(mixer.Baseline)} i snitt. Så arbetar en central mixer: den tar en avgift och betalar tillbaka "
            + "ur en gemensam pott, så att ingen tx knyter ihop in och ut. " + Steadiness(mixer)
            + "Det är en bedömning, inte ett faktum, och klustret kan ha fler ägare.";
        var addresses = mixer.Deposits.Select(deposit => deposit.Deposit.Output.Address!)
            .Concat(matched.SelectMany(deposit => deposit.Match!.Outputs)
                .SelectMany(payout => payout.Transaction.InputAddresses()))
            .Distinct();
        var basis = matched.SelectMany(deposit => deposit.Match!.Outputs.Select(payout => payout.Transaction)
                .Prepend(deposit.Deposit.Transaction))
            .DistinctBy(transaction => transaction.Txid)
            .Select(CheckBasis.Of);
        return new CheckFinding(
            "likely-mixer:" + mixer.Owner.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            mixer.MayForward ? AmbiguousTitle : MixerTitle,
            $"{count} av {of} insättningar ut igen minus {median}, från {CheckText.Count(mixer.Senders)} avsändare",
            why,
            [.. addresses],
            [],
            [.. basis],
            []);
    }

    /// <summary>
    /// Writes how steady the payouts are, and what that means: a mixer varies its fee and its delay, while a payment
    /// service that forwards each payment to a fresh address keeps its rate or its schedule and looks just like it.
    /// </summary>
    /// <param name="mixer">The likely mixer.</param>
    /// <returns>The sentences, with a space last.</returns>
    private static string Steadiness(LikelyMixer mixer)
    {
        if (!mixer.MayForward)
        {
            return "Både avgiften och fördröjningen varierar, som när en mixer vill bryta kopplingen. Håller du med, "
                + "sätt etiketten Mixer med L, så väger mixerns förslag dess utgångar. ";
        }

        var steady = (mixer.FeeIsSteady, mixer.DelayIsSteady) switch
        {
            (true, true) => "Både avgiften och fördröjningen är mest jämna",
            (true, false) => "Avgiften är mest jämn (en procentsats och ett fast belopp för de flesta)",
            _ => "Fördröjningen är mest jämn (de flesta kommer ut inom samma tre timmar)",
        };
        return $"{steady}, och så ser också en betaltjänst ut som skickar varje betalning vidare, minus sin avgift, "
            + "till en ny adress hos handlaren: att skilja dem åt går inte ur kedjan. Sätt en etikett med L när du vet "
            + "vilket det är, Mixer eller tjänsten. ";
    }

    /// <summary>Writes an average count with one decimal, the Swedish way.</summary>
    /// <param name="average">The average.</param>
    /// <returns>For example <c>2,5</c>.</returns>
    private static string Average(double average) =>
        average.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("sv-SE"));
}
