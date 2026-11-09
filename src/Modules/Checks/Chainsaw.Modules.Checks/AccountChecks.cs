using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises address poisoning on the account chains (Ethereum, BSC, TRON): a transfer of nothing or of dust between
/// an account and a look-alike of an account it really pays or is paid by, the same in its first and last characters,
/// so that the victim copies the wrong address from its history. It reads the graph's account transfers, as
/// <c>cross-chain</c> does, and so runs with the checks of the UTXO chains. A check with an area of the sort
/// Obfuscation; a judgement, never a fact (principle 2).
/// </summary>
internal sealed class AccountPoisoningCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "account-poisoning";

    /// <summary>
    /// How many characters at the start (after <c>0x</c> or <c>T</c>) and at the end two look-alikes share.
    /// </summary>
    public const int LookAlikeCharacters = 4;

    /// <summary>The most of the chain's own coin that is dust, in its smallest units (0.00001 of a coin).</summary>
    private static readonly BigInteger NativeDust = BigInteger.Pow(10, 13);

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Adressförgiftning på konton";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var transfers = input.Graph?.Account.Transfers ?? [];
        var real = transfers.Where(transfer => !IsDust(transfer.Amount)).ToList();
        var findings = new Dictionary<string, CheckFinding>(StringComparer.Ordinal);
        foreach (var dust in transfers.Where(transfer => IsDust(transfer.Amount)))
        {
            foreach (var (victim, mimic) in new[] { (dust.To, dust.From), (dust.From, dust.To) })
            {
                var genuine = real
                    .Where(transfer => transfer.From == victim || transfer.To == victim)
                    .Select(transfer => transfer.From == victim ? transfer.To : transfer.From)
                    .FirstOrDefault(other => other != mimic && LookAlike(other.Value, mimic.Value));
                if (genuine is null)
                {
                    continue;
                }

                var key = "poison:" + mimic.ToChainAddress();
                findings.TryAdd(key, Finding(key, dust, victim, mimic, genuine));
            }
        }

        return [.. findings.Values];
    }

    /// <summary>
    /// Tells whether a transfer moved nothing, or dust: a little of the coin, or a thousandth of a token.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsDust(AssetAmount amount) => amount.Units.IsZero || (amount.Asset.Kind == AssetKind.Native
        ? amount.Units <= NativeDust
        : amount.Units * 1_000 <= BigInteger.Pow(10, amount.Asset.Decimals));

    /// <summary>Tells whether two accounts look alike to the eye: the same first and last characters.</summary>
    /// <param name="one">One account's text.</param>
    /// <param name="other">The other's.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    internal static bool LookAlike(string one, string other)
    {
        var start = one.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return one.Length == other.Length && one.Length > start + (LookAlikeCharacters * 2)
            && string.Equals(one.Substring(start, LookAlikeCharacters), other.Substring(start, LookAlikeCharacters),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(one[^LookAlikeCharacters..], other[^LookAlikeCharacters..],
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Makes the finding of one look-alike.</summary>
    /// <param name="key">The finding's key.</param>
    /// <param name="dust">The transfer of nothing or of dust.</param>
    /// <param name="victim">The account it was aimed at.</param>
    /// <param name="mimic">The look-alike.</param>
    /// <param name="genuine">The account it looks like, which the victim really deals with.</param>
    /// <returns>The finding.</returns>
    private static CheckFinding Finding(
        string key, EthereumTransfer dust, EthereumAddress victim, EthereumAddress mimic, EthereumAddress genuine)
    {
        var why = $"{mimic.Value} liknar {genuine.Value}, som {victim.Value} verkligen handlar med, i de "
            + $"{LookAlikeCharacters} första och sista tecknen, och skickade eller tog emot "
            + (dust.Amount.Units.IsZero ? "ett nollbelopp" : "ett dammbelopp")
            + $" i tx {dust.Transaction.Value}. Det är adressförgiftning: kopiera aldrig adressen ur historiken. "
            + "En bedömning, inte ett faktum.";
        return new CheckFinding(
            key, CheckKind.Check, AreaSort.Obfuscation, "Adressförgiftning troligt", "account-poisoning v1", why,
            [], [], [], [])
        {
            Accounts = [victim.ToChainAddress(), mimic.ToChainAddress()],
        };
    }
}

/// <summary>
/// Suggests where money paid into a Tornado Cash pool may have come out (the user's question 2026-11-07): payouts from
/// the same pool, of the pool's fixed amount less at most a relayer's fee, after the deposit, among the transfers the
/// graph has read; ranked first when it goes back to the depositor's own account, then by how soon. The pool's own
/// history is not read by itself (it is very long), so a payout the graph has not read is not suggested. Candidates
/// are always dashed, never facts (principle 2).
/// </summary>
internal sealed class TornadoOutputsCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "tornado-outputs";

    /// <summary>The largest relayer's fee that is taken from a payout, in thousandths of the pool's amount.</summary>
    private const int MostFeePerMille = 10;

    /// <summary>How many candidates a suggestion lists at most.</summary>
    private const int MostCandidates = 10;

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Tornado Cash, troliga uttag";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var transfers = input.Graph?.Account.Transfers.Where(transfer => transfer.Block is not null).ToList() ?? [];
        var findings = new List<CheckFinding>();
        foreach (var deposit in transfers.Where(transfer => IsPool(transfer.To)))
        {
            var payouts = transfers
                .Where(payout => payout.From == deposit.To && payout.To != deposit.To
                    && payout.Block!.Time > deposit.Block!.Time
                    && payout.Amount.Asset == deposit.Amount.Asset
                    && payout.Amount.Units <= deposit.Amount.Units
                    && payout.Amount.Units * 1_000 >= deposit.Amount.Units * (1_000 - MostFeePerMille))
                .OrderByDescending(payout => payout.To == deposit.From)
                .ThenBy(payout => payout.Block!.Time)
                .Take(MostCandidates)
                .ToList();
            if (payouts.Count > 0)
            {
                findings.Add(Finding(deposit, payouts));
            }
        }

        return findings;
    }

    /// <summary>Tells whether an account is a Tornado Cash pool Chainsaw knows.</summary>
    /// <param name="account">The account.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsPool(EthereumAddress account) =>
        KnownContracts.Find(account) is { Category: KnownContractCategory.Mixer };

    /// <summary>Makes the suggestion of one deposit with its candidate payouts.</summary>
    /// <param name="deposit">The money into the pool.</param>
    /// <param name="payouts">The candidates, best first.</param>
    /// <returns>The suggestion.</returns>
    private static CheckFinding Finding(EthereumTransfer deposit, IReadOnlyList<EthereumTransfer> payouts)
    {
        var pool = KnownContracts.Find(deposit.To)!;
        var candidates = payouts.Select((payout, index) => new CheckCandidate(
            payout.Transaction.Value + ":" + payout.To.Value,
            null,
            null,
            index,
            null,
            Score(deposit, payout, index),
            Reason(deposit, payout))
        {
            Arrival = new ChainMovement(
                new ChainTransactionRef(payout.Transaction.Chain, payout.Transaction.Value),
                payout.From.ToChainAddress(), payout.To.ToChainAddress(), payout.Amount, payout.Block?.Height,
                payout.Block?.Hash, payout.Block?.Time),
        }).ToList();
        var paid = deposit.Amount.ToDecimalString() + " " + deposit.Amount.Asset.Symbol;
        var why = $"{deposit.From.Value} betalade in {paid} i {pool.Name} ({pool.Provenance()}) i tx "
            + $"{deposit.Transaction.Value}. Poolen betalar alltid ut samma belopp, så beloppet säger inget; "
            + "kandidaterna är poolens utbetalningar efter insättningen som grafen har läst, med den egna adressen "
            + "först. Uttag som grafen inte läst föreslås inte. Kandidater är bedömningar, aldrig fakta.";
        return new CheckFinding(
            "tornado:" + deposit.Transaction.Value + ":" + deposit.Position.ToString(CultureInfo.InvariantCulture),
            CheckKind.Suggestion, AreaSort.Obfuscation, "Tornado Cash, troliga uttag", "tornado-outputs v1", why,
            [], [], [], candidates)
        {
            Accounts =
            [
                deposit.From.ToChainAddress(), deposit.To.ToChainAddress(),
                .. payouts.Select(payout => payout.To.ToChainAddress()).Distinct(),
            ],
        };
    }

    /// <summary>Scores a candidate: the depositor's own account highest, then the sooner the better.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="payout">The payout.</param>
    /// <param name="index">Its place among the candidates.</param>
    /// <returns>A score between 0 and 1.</returns>
    private static double Score(EthereumTransfer deposit, EthereumTransfer payout, int index) =>
        payout.To == deposit.From ? 0.9 : Math.Max(0.1, 0.6 - (0.05 * index));

    /// <summary>Says why a payout is a candidate.</summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="payout">The payout.</param>
    /// <returns>The reason.</returns>
    private static string Reason(EthereumTransfer deposit, EthereumTransfer payout)
    {
        var hours = (payout.Block!.Time - deposit.Block!.Time).TotalHours;
        var own = payout.To == deposit.From ? " till insättarens egen adress" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"Utbetalning{own} till {payout.To.Value} i tx ")
            + string.Create(CultureInfo.InvariantCulture,
                $"{payout.Transaction.Value}, {hours:0} h efter insättningen.");
    }
}
