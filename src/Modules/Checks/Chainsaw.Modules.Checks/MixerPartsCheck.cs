using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Suggests that a mixer paid a deposit out in parts (<em>Delarna tillsammans</em>, the user's decision 2026-11-08):
/// for each deposit into a mixer, the combinations of two to <see cref="MostParts"/> payouts in the graph within the
/// window that together are the deposit minus a usual fee, scored by the amount, the time, how spread over blocks the
/// parts are and the address type. The best combination is given as one suggestion whose candidates are its parts,
/// each approved or dismissed on its own. A combination is a judgement, never a fact (principle 2): with many payouts
/// in the graph, some combination may fit by chance, and the text says how many did.
/// </summary>
/// <remarks>
/// The search is bounded, since the number of combinations grows fast: only the <see cref="MostPool"/> payouts
/// soonest after the deposit are tried, and at most <see cref="MostParts"/> of them together, which is at most
/// 6 175 combinations per deposit. Each part is at least <see cref="MixerOutputsCheck.MostFee"/> of the deposit, so
/// that no part alone could be a whole output (<c>mixer-outputs</c> weighs those).
/// </remarks>
internal sealed class MixerPartsCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "mixer-parts";

    /// <summary>The most parts a combination has.</summary>
    public const int MostParts = 4;

    /// <summary>The most payouts tried per deposit: those soonest after it.</summary>
    public const int MostPool = 20;

    /// <summary>The lowest score a combination is suggested at.</summary>
    public const double LowestScore = 0.5;

    /// <summary>The weight of the amount (the sum against the deposit minus a fee) in the score.</summary>
    private const double AmountWeight = 0.45;

    /// <summary>The weight of the time (the parts' mean time score) in the score.</summary>
    private const double TimeWeight = 0.2;

    /// <summary>The weight of the spread (the share of the parts in blocks of their own) in the score.</summary>
    private const double SpreadWeight = 0.15;

    /// <summary>The weight of the address type (the parts' mean type score) in the score.</summary>
    private const double TypeWeight = 0.2;

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Delarna tillsammans";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <summary>Gives the smallest part weighed: a tenth of the deposit, so that no part alone is a whole output.</summary>
    /// <param name="deposited">The deposit.</param>
    /// <returns>The smallest part, in satoshi.</returns>
    public static long SmallestPart(Satoshi deposited) =>
        (long)Math.Ceiling(deposited.Value * MixerOutputsCheck.MostFee);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var mixer = MixerDeposits.AddressesOf(input);
        return
        [
            .. MixerDeposits.Of(input, mixer).Select(deposit => Describe(input, deposit, mixer)).OfType<CheckFinding>(),
        ];
    }

    /// <summary>
    /// Finds the best combination of parts for a deposit, and how many combinations fit at all.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="deposit">The deposit.</param>
    /// <param name="mixer">The mixers' known addresses.</param>
    /// <returns>The best combination, or <see langword="null"/> when none scores at least
    /// <see cref="LowestScore"/>.</returns>
    internal static PartsCombination? Best(CheckInput input, MixerDeposit deposit, IReadOnlySet<BitcoinAddress> mixer)
    {
        var smallest = SmallestPart(deposit.Output.Amount);
        var pool = MixerDeposits.PayoutsAfter(input, deposit, mixer)
            .Where(payout => payout.Output.Amount.Value >= smallest)
            .OrderBy(payout => payout.After)
            .ThenBy(payout => payout.Key, StringComparer.Ordinal)
            .Take(MostPool)
            .ToList();
        PartsCombination? best = null;
        var fitting = 0;
        foreach (var parts in Combinations(pool, deposit.Output.Amount.Value))
        {
            if (Weigh(deposit, parts) is not { } combination)
            {
                continue;
            }

            fitting++;
            if (best is null || Better(combination, best))
            {
                best = combination;
            }
        }

        return best is null ? null : best with { Fitting = fitting };
    }

    /// <summary>
    /// Gives every combination of two to <see cref="MostParts"/> payouts whose sum is at most the deposit, without
    /// following a branch past the deposit.
    /// </summary>
    /// <param name="pool">The payouts to combine.</param>
    /// <param name="most">The deposit, in satoshi.</param>
    /// <returns>The combinations, each in the pool's order.</returns>
    private static IEnumerable<IReadOnlyList<MixerPayout>> Combinations(IReadOnlyList<MixerPayout> pool, long most)
    {
        var chosen = new List<MixerPayout>();
        return Extend(0, 0);

        IEnumerable<IReadOnlyList<MixerPayout>> Extend(int from, long sum)
        {
            for (var index = from; index < pool.Count; index++)
            {
                var next = sum + pool[index].Output.Amount.Value;
                if (next > most)
                {
                    continue;
                }

                chosen.Add(pool[index]);
                if (chosen.Count >= 2)
                {
                    yield return [.. chosen];
                }

                if (chosen.Count < MostParts)
                {
                    foreach (var longer in Extend(index + 1, next))
                    {
                        yield return longer;
                    }
                }

                chosen.RemoveAt(chosen.Count - 1);
            }
        }
    }

    /// <summary>Scores a combination, or nothing when its sum is not the deposit minus a fee it weighs.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="parts">The parts.</param>
    /// <returns>The combination, or <see langword="null"/>.</returns>
    private static PartsCombination? Weigh(MixerDeposit deposit, IReadOnlyList<MixerPayout> parts)
    {
        var deposited = deposit.Output.Amount.Value;
        var sum = parts.Sum(part => part.Output.Amount.Value);
        var fee = (double)(deposited - sum) / deposited;
        var feeScore = MixerOutputsCheck.FeeScore(fee);
        if (feeScore <= 0)
        {
            return null;
        }

        var time = parts.Average(part => MixerOutputsCheck.TimeScore(part.After));
        var spread = (double)parts.Select(part => part.Transaction.Block!.Height).Distinct().Count() / parts.Count;
        var type = parts.Average(part => MixerOutputsCheck.TypeScore(deposit, part.Address));
        var score = Math.Round(
            (AmountWeight * feeScore) + (TimeWeight * time) + (SpreadWeight * spread) + (TypeWeight * type),
            2,
            MidpointRounding.AwayFromZero);
        return score < LowestScore
            ? null
            : new PartsCombination([.. parts.OrderBy(p => p.After).ThenBy(p => p.Key, StringComparer.Ordinal)],
                new Satoshi(sum), fee, spread, score, 0);
    }

    /// <summary>
    /// Says whether a combination is better than another: a higher score, then fewer parts (a simpler explanation),
    /// then the parts' keys in order, so that the choice is stable.
    /// </summary>
    /// <param name="candidate">The combination found now.</param>
    /// <param name="best">The best so far.</param>
    /// <returns><see langword="true"/> when the new one is better.</returns>
    private static bool Better(PartsCombination candidate, PartsCombination best)
    {
        if (candidate.Score != best.Score)
        {
            return candidate.Score > best.Score;
        }

        if (candidate.Parts.Count != best.Parts.Count)
        {
            return candidate.Parts.Count < best.Parts.Count;
        }

        return string.CompareOrdinal(candidate.KeyText, best.KeyText) < 0;
    }

    /// <summary>Describes a deposit's best combination as a suggestion, or nothing when it has none.</summary>
    /// <param name="input">The input.</param>
    /// <param name="deposit">The deposit.</param>
    /// <param name="mixer">The mixers' known addresses.</param>
    /// <returns>The suggestion, or <see langword="null"/>.</returns>
    private CheckFinding? Describe(CheckInput input, MixerDeposit deposit, IReadOnlySet<BitcoinAddress> mixer)
    {
        if (Best(input, deposit, mixer) is not { } best)
        {
            return null;
        }

        var deposited = deposit.Output.Amount;
        var count = CheckText.Count(best.Parts.Count);
        var first = best.Parts[0].After;
        var last = best.Parts[^1].After;
        var candidates = best.Parts.Select((part, index) => new CheckCandidate(
            part.Key,
            part.Address,
            part.Transaction.Txid,
            part.Output.Index,
            part.Output.Amount,
            best.Score,
            $"Del {CheckText.Count(index + 1)} av {count}: {CheckText.Btc(part.Output.Amount)} av "
                + $"{CheckText.Btc(deposited)} in; {CheckText.Span(part.After)} senare; "
                + MixerOutputsCheck.TypeText(deposit, part.Address)));
        var why = $"{Id} prövar kombinationer av två till {CheckText.Count(MostParts)} utbetalningar i grafen, var och "
            + $"en minst {CheckText.Percent(MixerOutputsCheck.MostFee)} av insättningen, inom "
            + $"{CheckText.Span(MixerOutputsCheck.Window)} efter insättningen på {CheckText.Btc(deposited)} BTC i block "
            + $"{deposit.Block.Height}. De {CheckText.Count(best.Parts.Count)} delarna blir tillsammans "
            + $"{CheckText.Btc(best.Sum)} BTC, insättningen minus {CheckText.Percent(best.Fee)} i trolig avgift, och kom "
            + $"mellan {CheckText.Span(first)} och {CheckText.Span(last)} efter insättningen i "
            + $"{CheckText.Count(best.Parts.Select(part => part.Transaction.Block!.Height).Distinct().Count())} "
            + "olika block. Poängen väger summan "
            + "mot insättningen minus avgift, tiden, hur utspridda delarna är och adresstypen; den bästa av "
            + $"{CheckText.Count(best.Fitting)} kombinationer som passar visas. Bara de "
            + $"{CheckText.Count(MostPool)} utbetalningarna närmast efter insättningen prövas, och bara det som finns "
            + "i grafen. Fler kombinationer som "
            + "passar betyder större risk att en passar av en slump. Det är en bedömning, aldrig ett faktum: mixern "
            + "betalar ut från andra mynt, så ingen tx på kedjan knyter delarna till insättningen.";
        return new CheckFinding(
            deposit.Key,
            CheckKind.Suggestion,
            AreaSort.Obfuscation,
            "Delarna tillsammans",
            $"{count} delar · {CheckText.Btc(best.Sum)} av {CheckText.Btc(deposited)} in · avgift "
                + CheckText.Percent(best.Fee),
            why,
            [deposit.Output.Address!, .. best.Parts.Select(part => part.Address).Distinct()],
            [],
            [
                CheckBasis.Of(deposit.Transaction),
                .. best.Parts.Select(part => part.Transaction).Distinct().Select(CheckBasis.Of),
            ],
            [.. candidates]);
    }
}

/// <summary>A combination of payouts that together may be a deposit's money after the mixer.</summary>
/// <param name="Parts">The parts, soonest first.</param>
/// <param name="Sum">Their sum.</param>
/// <param name="Fee">The share of the deposit that did not come out.</param>
/// <param name="Spread">The share of the parts in blocks of their own, from 0 to 1.</param>
/// <param name="Score">The combination's score, from 0 to 1, rounded to two decimals.</param>
/// <param name="Fitting">How many combinations for the deposit scored high enough, this one included.</param>
internal sealed record PartsCombination(
    IReadOnlyList<MixerPayout> Parts, Satoshi Sum, double Fee, double Spread, double Score, int Fitting)
{
    /// <summary>Gets the parts' keys in order, for a stable choice between equal combinations.</summary>
    public string KeyText => string.Join(",", Parts.Select(part => part.Key));
}
