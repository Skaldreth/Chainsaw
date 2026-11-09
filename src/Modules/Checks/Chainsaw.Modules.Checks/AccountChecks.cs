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
        var transfers = (input.Graph?.Account.Transfers ?? [])
            .Where(transfer => transfer.Kind != EthereumTransferKind.Internal)
            .OrderBy(transfer => transfer.Block?.Height ?? long.MaxValue)
            .ThenBy(transfer => transfer.Transaction.Value, StringComparer.Ordinal)
            .ThenBy(transfer => transfer.Position)
            .ToList();
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
    /// <remarks>The coin's dust is 0.00001 of it, counted in its own decimals (18 for ether, 6 for TRX).</remarks>
    internal static bool IsDust(AssetAmount amount) => amount.Units.IsZero || (amount.Asset.Kind == AssetKind.Native
        ? amount.Units * 100_000 <= BigInteger.Pow(10, amount.Asset.Decimals)
        : amount.Units * 1_000 <= BigInteger.Pow(10, amount.Asset.Decimals));

    /// <summary>Tells whether two accounts look alike to the eye: the same first and last characters.</summary>
    /// <param name="one">One account's text.</param>
    /// <param name="other">The other's.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    /// <remarks>
    /// Hex (<c>0x</c>) is compared without case, since case is only its checksum; Base58 (TRON's <c>T</c>, Solana)
    /// with case, since there a capital is another character. TRON's and hex addresses have one length; the prefix is
    /// skipped.
    /// </remarks>
    internal static bool LookAlike(string one, string other)
    {
        var hex = one.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var start = hex ? 2 : one.StartsWith('T') && other.StartsWith('T') ? 1 : 0;
        var comparison = hex ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var shortest = start + (LookAlikeCharacters * 2) + 1;
        return one.Length >= shortest && other.Length >= shortest && (start == 0 || one.Length == other.Length)
            && string.Equals(one.Substring(start, LookAlikeCharacters), other.Substring(start, LookAlikeCharacters),
                comparison)
            && string.Equals(one[^LookAlikeCharacters..], other[^LookAlikeCharacters..], comparison)
            && !string.Equals(one, other, comparison);
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
            // The look-alike, the genuine and the victim, in that order, as the graph's mask (DM1a) reads them.
            Accounts = [mimic.ToChainAddress(), genuine.ToChainAddress(), victim.ToChainAddress()],
            Departure = AccountMoves.Of(dust),
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

    /// <summary>
    /// The largest relayer's fee and gas that is taken from a payout, in thousandths of the pool's amount: on the
    /// smallest pool (0.1 ETH) the two are often several percent.
    /// </summary>
    private const int MostFeePerMille = 100;

    /// <summary>How many candidates a suggestion lists at most, as many as the panel keeps waiting.</summary>
    private const int MostCandidates = 5;

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
        var transfers = input.Graph?.Account.Transfers.Where(transfer => transfer.Block is not null)
            .OrderBy(transfer => transfer.Block!.Height)
            .ThenBy(transfer => transfer.Transaction.Value, StringComparer.Ordinal)
            .ThenBy(transfer => transfer.Position)
            .ToList() ?? [];
        var findings = new List<CheckFinding>();
        foreach (var deposit in transfers.Select(transfer => DepositOf(transfer, transfers)).OfType<EthereumTransfer>())
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

    /// <summary>Tells whether an account is a Tornado Cash pool Chainsaw knows, not its router or proxy.</summary>
    /// <param name="account">The account.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsPool(EthereumAddress account) =>
        KnownContracts.Find(account) is { Category: KnownContractCategory.Mixer } contract && !IsGate(contract);

    /// <summary>Tells whether a mixer's contract is the way in (its router or proxy) rather than a pool.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsGate(KnownContract contract) =>
        contract.Name.EndsWith(" router", StringComparison.Ordinal)
        || contract.Name.EndsWith(" proxy", StringComparison.Ordinal);

    /// <summary>
    /// Gives a deposit into a pool as the depositor paid it: straight into the pool, or into the router or proxy,
    /// which passes it on to the pool in the same transaction; the depositor is the one who paid the way in.
    /// </summary>
    /// <param name="transfer">A transfer.</param>
    /// <param name="all">All the transfers read.</param>
    /// <returns>The deposit, with the pool as its receiver, or <see langword="null"/>.</returns>
    private static EthereumTransfer? DepositOf(EthereumTransfer transfer, IReadOnlyList<EthereumTransfer> all)
    {
        if (IsPool(transfer.To) && KnownContracts.Find(transfer.From) is not { Category: KnownContractCategory.Mixer })
        {
            return transfer;
        }

        if (KnownContracts.Find(transfer.To) is not { } gate || !IsGate(gate))
        {
            return null;
        }

        var passed = all.FirstOrDefault(next => next.Transaction == transfer.Transaction && next.From == transfer.To
            && IsPool(next.To) && next.Amount.Asset == transfer.Amount.Asset);
        return passed is null ? null : transfer with { To = passed.To };
    }

    /// <summary>Makes the suggestion of one deposit with its candidate payouts.</summary>
    /// <param name="deposit">The money into the pool.</param>
    /// <param name="payouts">The candidates, best first.</param>
    /// <returns>The suggestion.</returns>
    private static CheckFinding Finding(EthereumTransfer deposit, IReadOnlyList<EthereumTransfer> payouts)
    {
        var pool = KnownContracts.Find(deposit.To)!;
        var candidates = payouts.Select(payout => new CheckCandidate(
            payout.Transaction.Value + ":" + payout.To.Value,
            null,
            null,
            0,
            null,
            Score(deposit, payout),
            Reason(deposit, payout))
        {
            Arrival = AccountMoves.Of(payout),
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
            Departure = AccountMoves.Of(deposit),
        };
    }

    /// <summary>
    /// Scores a candidate: the depositor's own account high, every other alike, since nothing but the order in time
    /// tells them apart and a score must not seem to know more.
    /// </summary>
    /// <param name="deposit">The deposit.</param>
    /// <param name="payout">The payout.</param>
    /// <returns>A score between 0 and 1.</returns>
    private static double Score(EthereumTransfer deposit, EthereumTransfer payout) =>
        payout.To == deposit.From ? 0.9 : 0.3;

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

/// <summary>Turns account transfers into the movements a finding is anchored by, with their blocks.</summary>
internal static class AccountMoves
{
    /// <summary>Gives a transfer as a movement on its chain, with its block's height, hash and time.</summary>
    /// <param name="transfer">The transfer.</param>
    /// <returns>The movement.</returns>
    public static ChainMovement Of(EthereumTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return new ChainMovement(
            new ChainTransactionRef(transfer.Transaction.Chain, transfer.Transaction.Value),
            transfer.From.ToChainAddress(), transfer.To.ToChainAddress(), transfer.Amount, transfer.Block?.Height,
            transfer.Block?.Hash, transfer.Block?.Time);
    }
}
