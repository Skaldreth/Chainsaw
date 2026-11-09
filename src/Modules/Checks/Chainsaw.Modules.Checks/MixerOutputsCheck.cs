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

    /// <summary>The analysis's name and version, which the user's decisions about its suggestions are kept by.</summary>
    public static readonly HeuristicId Identity = new(Name, 1);

    /// <summary>The highest fee weighed: an output smaller than the deposit by more is not a candidate.</summary>
    public const double MostFee = 0.10;

    /// <summary>The lowest fee that counts in full; mixers usually take at least half a per cent.</summary>
    public const double UsualFeeLow = 0.005;

    /// <summary>The highest fee that counts in full.</summary>
    public const double UsualFeeHigh = 0.05;

    /// <summary>The lowest score a candidate is kept at.</summary>
    public const double LowestScore = 0.4;

    /// <summary>What a different address type still counts.</summary>
    public const double OtherTypeScore = 0.4;

    /// <summary>The weight of the amount in the score.</summary>
    private const double AmountWeight = 0.5;

    /// <summary>The weight of the time in the score.</summary>
    private const double TimeWeight = 0.3;

    /// <summary>The weight of the address type in the score.</summary>
    private const double TypeWeight = 0.2;

    /// <summary>What the time counts at the end of the window.</summary>
    private const double LatestTimeScore = 0.2;

    /// <summary>How long after a deposit an output is weighed.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public string DisplayName => "Mixerutgångar";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

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

    /// <summary>Gives the score of an address type: full for the depositor's, else
    /// <see cref="OtherTypeScore"/>.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="address">The payout's address.</param>
    /// <returns>The score.</returns>
    public static double TypeScore(MixerDeposit deposit, BitcoinAddress address) =>
        address.Type == deposit.Type ? 1 : OtherTypeScore;

    /// <summary>Writes whether a payout's address type is the depositor's, for a candidate's reason.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="address">The payout's address.</param>
    /// <returns>For example <c>samma adresstyp (P2WPKH)</c>.</returns>
    public static string TypeText(MixerDeposit deposit, BitcoinAddress address) => address.Type == deposit.Type
        ? $"samma adresstyp ({deposit.Type.ShortName()})"
        : $"annan adresstyp ({address.Type.ShortName()} mot {deposit.Type.ShortName()})";

    /// <summary>
    /// Weighs every payout after a deposit as a candidate, best first: the same weighing the suggestion shows, so
    /// that <c>mixer-chain</c> follows the same likely outputs.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="deposit">The deposit.</param>
    /// <param name="mixer">The mixers' known addresses.</param>
    /// <returns>The candidates with their transactions, best first; empty when none.</returns>
    internal static IReadOnlyList<(CheckCandidate Candidate, BitcoinTransaction Transaction)> Weigh(
        CheckInput input, MixerDeposit deposit, IReadOnlySet<BitcoinAddress> mixer)
    {
        var weighed = new List<(CheckCandidate Candidate, BitcoinTransaction Transaction)>();
        foreach (var payout in MixerDeposits.PayoutsAfter(input, deposit, mixer))
        {
            if (Candidate(deposit, payout) is { } candidate)
            {
                weighed.Add((candidate, payout.Transaction));
            }
        }

        return
        [
            .. weighed
                .OrderByDescending(item => item.Candidate.Score)
                .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>Describes one deposit's candidates, or nothing when it has none.</summary>
    /// <param name="input">The input.</param>
    /// <param name="deposit">The deposit into the mixer.</param>
    /// <param name="mixer">The mixers' known addresses.</param>
    /// <returns>The suggestion, or <see langword="null"/>.</returns>
    private CheckFinding? Describe(CheckInput input, MixerDeposit deposit, IReadOnlySet<BitcoinAddress> mixer)
    {
        var best = Weigh(input, deposit, mixer);
        if (best.Count == 0)
        {
            return null;
        }

        var amount = CheckText.Btc(deposit.Output.Amount);
        var why = $"{Id} letar i grafen efter utbetalningar som kan vara insättningens {amount} BTC efter mixerns "
            + $"avgift (0–{CheckText.Percent(MostFee)}), inom {CheckText.Span(Window)} efter insättningen i block "
            + $"{deposit.Block.Height}, och väger in om adresstypen är insättarens ({deposit.Type.ShortName()}). "
            + "Poängen väger beloppet till hälften, tiden till tre tiondelar och adresstypen till en femtedel. En "
            + "kandidat är en bedömning, aldrig ett faktum: mixern betalar ut från andra mynt, så ingen tx på kedjan "
            + "knyter dem ihop. Bara det som finns i grafen prövas.";
        return new CheckFinding(
            deposit.Key,
            CheckKind.Suggestion,
            AreaSort.Obfuscation,
            "Troliga utgångar ur mixern",
            $"{amount} in · belopp minus avgift, tid och adresstyp",
            why,
            [deposit.Output.Address!, .. best.Select(item => item.Candidate.Address).OfType<BitcoinAddress>().Distinct()],
            [],
            [
                CheckBasis.Of(deposit.Transaction),
                .. best.Select(item => item.Transaction).Distinct().Select(CheckBasis.Of),
            ],
            [.. best.Select(item => item.Candidate)]);
    }

    /// <summary>Weighs one payout as a candidate for a deposit.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="payout">A payout after it.</param>
    /// <returns>The candidate, or <see langword="null"/> when it is not one.</returns>
    private static CheckCandidate? Candidate(MixerDeposit deposit, MixerPayout payout)
    {
        var deposited = deposit.Output.Amount;
        var paid = payout.Output.Amount;
        var fee = (double)(deposited.Value - paid.Value) / deposited.Value;
        var feeScore = FeeScore(fee);
        var timeScore = TimeScore(payout.After);
        if (feeScore <= 0 || timeScore <= 0)
        {
            return null;
        }

        var score = Math.Round(
            (AmountWeight * feeScore) + (TimeWeight * timeScore)
                + (TypeWeight * TypeScore(deposit, payout.Address)),
            2,
            MidpointRounding.AwayFromZero);
        if (score < LowestScore)
        {
            return null;
        }

        var reason = $"{CheckText.Btc(paid)} ut mot {CheckText.Btc(deposited)} in, avgift "
            + $"{CheckText.Percent(fee)}; {CheckText.Span(payout.After)} senare; "
            + TypeText(deposit, payout.Address);
        return new CheckCandidate(
            payout.Key, payout.Address, payout.Transaction.Txid, payout.Output.Index, paid, score, reason);
    }
}
