using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Suggests a mixer's likely outputs (CLAUDE.md, <em>Mixers är vanliga</em>): for each deposit into a mixer, the
/// payments in the graph that may be the same money after the mixer's fee, scored by the amount minus a fee, the time
/// after the deposit and whether the address type is the depositor's. A mixer pays out from other coins, so nothing on
/// the chain ties a candidate to the deposit; every candidate is a judgement, always dashed (principle 2).
/// </summary>
/// <remarks>Only what the graph holds is weighed; the chain around the deposit is not searched (TODO).</remarks>
internal sealed class MixerOutputsCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "mixer-outputs";

    /// <summary>The highest fee weighed: an output smaller than the deposit by more is not a candidate.</summary>
    public const double MostFee = 0.10;

    /// <summary>The lowest fee that counts in full; mixers usually take at least half a per cent.</summary>
    public const double UsualFeeLow = 0.005;

    /// <summary>The highest fee that counts in full.</summary>
    public const double UsualFeeHigh = 0.05;

    /// <summary>The lowest score a candidate is kept at.</summary>
    public const double LowestScore = 0.4;

    /// <summary>The weight of the amount in the score.</summary>
    private const double AmountWeight = 0.5;

    /// <summary>The weight of the time in the score.</summary>
    private const double TimeWeight = 0.3;

    /// <summary>The weight of the address type in the score.</summary>
    private const double TypeWeight = 0.2;

    /// <summary>What a different address type still counts.</summary>
    private const double OtherTypeScore = 0.4;

    /// <summary>What the time counts at the end of the window.</summary>
    private const double LatestTimeScore = 0.2;

    /// <summary>How long after a deposit an output is weighed.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Mixerutgångar";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var mixer = input.Mixers.SelectMany(known => known.Addresses).ToHashSet();
        if (mixer.Count == 0)
        {
            return [];
        }

        var findings = new List<CheckFinding>();
        var deposits = input.Transactions
            .Where(transaction => transaction.Block is not null)
            .OrderBy(transaction => transaction.Block!.Height)
            .ThenBy(transaction => transaction.Txid.Value, StringComparer.Ordinal);
        foreach (var deposit in deposits)
        {
            var depositors = deposit.Inputs.Select(i => i.Address).OfType<BitcoinAddress>()
                .Where(address => !mixer.Contains(address)).ToHashSet();
            if (depositors.Count == 0)
            {
                continue;
            }

            foreach (var output in deposit.Outputs.Where(o => o.Address is { } a && mixer.Contains(a)))
            {
                if (Describe(input, deposit, output, depositors, mixer) is { } finding)
                {
                    findings.Add(finding);
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Gives the score of a fee, from 0 to 1: full for a usual fee, less towards none and the highest.
    /// </summary>
    /// <param name="fee">The share of the deposit that did not come out, from 0 to <see cref="MostFee"/>.</param>
    /// <returns>The score.</returns>
    public static double FeeScore(double fee)
    {
        if (fee < 0 || fee > MostFee)
        {
            return 0;
        }

        if (fee < UsualFeeLow)
        {
            return 0.5 + (0.5 * fee / UsualFeeLow);
        }

        return fee <= UsualFeeHigh ? 1 : 1 - ((fee - UsualFeeHigh) / (MostFee - UsualFeeHigh));
    }

    /// <summary>
    /// Gives the score of the time after the deposit, from 1 at once to a fifth at the end of the window.
    /// </summary>
    /// <param name="after">The time after the deposit; positive.</param>
    /// <returns>The score, or 0 outside the window.</returns>
    public static double TimeScore(TimeSpan after) => after <= TimeSpan.Zero || after > Window
        ? 0
        : 1 - ((1 - LatestTimeScore) * (after / Window));

    /// <summary>Describes one deposit's candidates, or nothing when it has none.</summary>
    /// <param name="input">The input.</param>
    /// <param name="deposit">The transaction that paid into the mixer.</param>
    /// <param name="output">The output that paid into it.</param>
    /// <param name="depositors">The depositor's addresses.</param>
    /// <param name="mixer">The mixer's known addresses.</param>
    /// <returns>The suggestion, or <see langword="null"/>.</returns>
    private CheckFinding? Describe(
        CheckInput input,
        BitcoinTransaction deposit,
        BitcoinOutput output,
        IReadOnlySet<BitcoinAddress> depositors,
        IReadOnlySet<BitcoinAddress> mixer)
    {
        var type = deposit.Inputs.Where(i => i.Address is not null && depositors.Contains(i.Address))
            .GroupBy(i => i.Address!.Type).OrderByDescending(group => group.Count()).First().Key;
        var candidates = new List<(CheckCandidate Candidate, BitcoinTransaction Transaction)>();
        foreach (var later in input.Transactions.Where(t => t.Block?.Height > deposit.Block!.Height))
        {
            if (later.Inputs.Any(i => i.Address is not null && depositors.Contains(i.Address)))
            {
                continue;
            }

            foreach (var paid in later.Outputs)
            {
                if (Candidate(deposit, output, type, later, paid, depositors, mixer) is { } candidate)
                {
                    candidates.Add((candidate, later));
                }
            }
        }

        var best = candidates
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal)
            .ToList();
        if (best.Count == 0)
        {
            return null;
        }

        var amount = CheckText.Btc(output.Amount);
        var why = $"{Id} letar i grafen efter utbetalningar som kan vara insättningens {amount} BTC efter mixerns "
            + $"avgift (0–{CheckText.Percent(MostFee)}), inom {CheckText.Span(Window)} efter insättningen i block "
            + $"{deposit.Block!.Height}, och väger in om adresstypen är insättarens ({type.ShortName()}). Poängen väger "
            + "beloppet till hälften, tiden till tre tiondelar och adresstypen till en femtedel. En kandidat är en "
            + "bedömning, aldrig ett faktum: mixern betalar ut från andra mynt, så ingen tx på kedjan knyter dem ihop. "
            + "Bara det som finns i grafen prövas.";
        return new CheckFinding(
            $"{deposit.Txid.Value}:{output.Index}",
            CheckKind.Suggestion,
            AreaSort.Obfuscation,
            "Troliga utgångar ur mixern",
            $"{amount} in · belopp minus avgift, tid och adresstyp",
            why,
            [output.Address!, .. best.Select(item => item.Candidate.Address).Distinct()],
            [],
            [CheckBasis.Of(deposit), .. best.Select(item => item.Transaction).Distinct().Select(CheckBasis.Of)],
            [.. best.Select(item => item.Candidate)]);
    }

    /// <summary>Weighs one output as a candidate for a deposit.</summary>
    /// <param name="deposit">The deposit's transaction.</param>
    /// <param name="output">The deposit's output.</param>
    /// <param name="type">The depositor's address type.</param>
    /// <param name="later">A later transaction.</param>
    /// <param name="paid">One of its outputs.</param>
    /// <param name="depositors">The depositor's addresses.</param>
    /// <param name="mixer">The mixer's known addresses.</param>
    /// <returns>The candidate, or <see langword="null"/> when it is not one.</returns>
    private static CheckCandidate? Candidate(
        BitcoinTransaction deposit,
        BitcoinOutput output,
        BitcoinScriptType type,
        BitcoinTransaction later,
        BitcoinOutput paid,
        IReadOnlySet<BitcoinAddress> depositors,
        IReadOnlySet<BitcoinAddress> mixer)
    {
        if (paid.Address is not { } address)
        {
            return null;
        }

        if (mixer.Contains(address) || depositors.Contains(address) || output.Amount.Value <= 0
            || paid.Amount.Value > output.Amount.Value)
        {
            return null;
        }

        var fee = (double)(output.Amount.Value - paid.Amount.Value) / output.Amount.Value;
        var after = later.Block!.Time - deposit.Block!.Time;
        var feeScore = FeeScore(fee);
        var timeScore = TimeScore(after);
        if (feeScore <= 0 || timeScore <= 0)
        {
            return null;
        }

        var sameType = address.Type == type;
        var score = Math.Round(
            (AmountWeight * feeScore) + (TimeWeight * timeScore) + (TypeWeight * (sameType ? 1 : OtherTypeScore)),
            2,
            MidpointRounding.AwayFromZero);
        if (score < LowestScore)
        {
            return null;
        }

        var typeText = sameType
            ? $"samma adresstyp ({type.ShortName()})"
            : $"annan adresstyp ({address.Type.ShortName()} mot {type.ShortName()})";
        var reason = $"{CheckText.Btc(paid.Amount)} ut mot {CheckText.Btc(output.Amount)} in, avgift "
            + $"{CheckText.Percent(fee)}; {CheckText.Span(after)} senare; {typeText}";
        return new CheckCandidate(
            $"{later.Txid.Value}:{paid.Index}", address, later.Txid, paid.Index, paid.Amount, score, reason);
    }
}
