using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Shows mixer after mixer (chaining, the user's question 2026-11-08): when an output of a mixer that the user approved,
/// or that the mixer analyses find likely, goes into a mixer again (the same, another, or a CoinJoin), the steps are
/// one check with the amount and the fees summed along the chain. Each new deposit into a known mixer gets the mixer's
/// own suggestions (<c>mixer-outputs</c>, <c>mixer-parts</c>) by itself, so the chain can be followed step by step.
/// A step through a likely output is only as good as that output: the check is a judgement, never a fact
/// (principle 2).
/// </summary>
/// <remarks>
/// At each step an approved candidate is followed before a likely one, then the higher score. Only what the graph
/// holds is followed, and at most <see cref="MostSteps"/> steps.
/// </remarks>
internal sealed class MixerChainCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "mixer-chain";

    /// <summary>The most steps a chain is followed.</summary>
    public const int MostSteps = 10;

    /// <summary>What a whole output is called in the text.</summary>
    private const string OutputWord = "utgång";

    /// <summary>What a part of the best combination is called in the text.</summary>
    private const string PartWord = "del";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Mixer efter mixer";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var context = new ChainContext(input);
        var onward = context.Deposits.ToDictionary(deposit => deposit.Key, context.Onward, StringComparer.Ordinal);
        var reached = onward.Values.Select(link => link?.Next?.Key).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var findings = new List<CheckFinding>();
        foreach (var root in context.Deposits.Where(d => onward[d.Key] is not null && !reached.Contains(d.Key)))
        {
            var steps = new List<ChainStep>();
            MixerDeposit? current = root;
            while (current is not null && steps.Count < MostSteps)
            {
                var link = onward[current.Key];
                steps.Add(new ChainStep(current, context.MixerOf(current), link ?? context.BestOut(current)));
                current = link?.Next;
            }

            findings.Add(Describe(steps));
        }

        return findings;
    }

    /// <summary>Describes a chain of steps as a check.</summary>
    /// <param name="steps">The steps, first deposit first; every step but the last has an output
    /// followed onward.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(IReadOnlyList<ChainStep> steps)
    {
        var first = steps[0].Deposit;
        var outs = steps.Select(step => step.Out).OfType<ChainLink>().ToList();
        var last = steps[^1].Out;
        var fees = new Satoshi(outs.Sum(link => link.Fee.Value));
        var endsInCoinJoin = last?.CoinJoin is not null;
        var mixings = steps.Count + (endsInCoinJoin ? 1 : 0);
        var why = new StringBuilder();
        why.Append($"{Id} följer pengarna från insättningen i block {first.Block.Height} genom de utgångar ur "
            + "mixern som du har godkänt, eller annars de som mixerns förslag finner troligast och som du inte har "
            + "avfärdat, och ser om de går in i en mixer eller en CoinJoin igen. ");
        for (var index = 0; index < steps.Count; index++)
        {
            why.Append(StepText(steps, index)).Append(' ');
        }

        var lastOut = last is null ? "ingen trolig utgång ur den sista mixern" : $"{CheckText.Btc(last.Amount)} BTC i "
            + "den sista utgången";
        why.Append($"Längs kedjan: {CheckText.Btc(first.Output.Amount)} BTC in, {lastOut} och "
            + $"{CheckText.Btc(fees)} BTC i troliga avgifter i mixrarna "
            + $"({CheckText.Percent((double)fees.Value / first.Output.Amount.Value)} av den första insättningen). ");
        why.Append(endsInCoinJoin
            ? "Spåret tar slut i CoinJoin-en, enligt klustringens bedömning; följ den med CoinJoin-kontrollen. "
            : "Varje ny insättning får mixerns egna förslag, som står för sig och avgörs där. ");
        if (steps.Count == MostSteps && last?.Next is not null)
        {
            why.Append($"Kedjan följdes högst {CheckText.Count(MostSteps)} steg; den går vidare efter det. ");
        }

        why.Append("Ett steg genom en trolig utgång är bara så säkert som utgången: godkänn eller avfärda kandidaterna "
            + "i mixerns förslag. Det är en bedömning, aldrig ett faktum.");
        return new CheckFinding(
            first.Key,
            CheckKind.Check,
            AreaSort.Obfuscation,
            $"Mixer efter mixer, {CheckText.Count(mixings)} steg",
            SummaryOf(first, last, fees),
            why.ToString(),
            [.. AddressesOf(steps).Distinct()],
            [.. outs.Select(link => link.CoinJoin).OfType<BitcoinTransaction>().Select(t => t.Txid)],
            [.. TransactionsOf(steps).DistinctBy(t => t.Txid).Select(CheckBasis.Of)],
            []);
    }

    /// <summary>Writes the line under the title: what went in, what came out last and the fees.</summary>
    /// <param name="first">The first deposit.</param>
    /// <param name="last">The last step's output, or <see langword="null"/>.</param>
    /// <param name="fees">The likely fees along the chain.</param>
    /// <returns>For example <c>0,1500 in · 0,1430 ut · avgifter 0,00675</c>.</returns>
    private static string SummaryOf(MixerDeposit first, ChainLink? last, Satoshi fees)
    {
        var parts = new List<string> { $"{CheckText.Btc(first.Output.Amount)} in" };
        if (last is not null)
        {
            parts.Add($"{CheckText.Btc(last.Amount)} ut");
        }

        parts.Add($"avgifter {CheckText.Btc(fees)}");
        if (last?.CoinJoin is not null)
        {
            parts.Add("slutar i CoinJoin");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Writes one step: what went into which mixer, the output and where it went.</summary>
    /// <param name="steps">The chain's steps.</param>
    /// <param name="index">The step's index.</param>
    /// <returns>The step's sentence.</returns>
    private static string StepText(IReadOnlyList<ChainStep> steps, int index)
    {
        var step = steps[index];
        var which = index == 0 ? "mixern" : MixerWords(steps, index);
        var text = $"Steg {CheckText.Count(index + 1)}: {CheckText.Btc(step.Deposit.Output.Amount)} BTC in i {which} "
            + $"(block {step.Deposit.Block.Height})";
        if (step.Out is not { } link)
        {
            return text + "; ingen trolig utgång ur den i grafen än.";
        }

        var state = link.IsApproved ? "godkänd" : "trolig";
        text += $"; {state} {link.What} {CheckText.Btc(link.Amount)} BTC i tx {CheckText.Short(link.Payout.Txid)} "
            + $"(block {link.Payout.Block!.Height}), trolig avgift {CheckText.Btc(link.Fee)} BTC";
        if (link.CoinJoin is not null)
        {
            return text + $"; den går vidare i tx {CheckText.Short(link.Spender!.Txid)}, som klustringen känner igen "
                + "som en CoinJoin (en bedömning).";
        }

        if (link.Next is not { } next)
        {
            return text + "; den går inte vidare in i någon mixer i grafen.";
        }

        text += $"; den går vidare i tx {CheckText.Short(link.Spender!.Txid)} till en insättning på "
            + $"{CheckText.Btc(next.Output.Amount)} BTC";
        return next.Output.Amount == link.Amount
            ? text + "."
            : text + ", inte samma belopp: något kom till eller gick bort mellan stegen.";
    }

    /// <summary>Says which mixer a later step goes into: the same as an earlier step, or another.</summary>
    /// <param name="steps">The chain's steps.</param>
    /// <param name="index">The step's index, from 1.</param>
    /// <returns><c>samma mixer igen</c> or <c>en annan mixer</c>.</returns>
    private static string MixerWords(IReadOnlyList<ChainStep> steps, int index) =>
        steps.Take(index).Any(step => step.Mixer == steps[index].Mixer) ? "samma mixer igen" : "en annan mixer";

    /// <summary>Gives the addresses the area is drawn around: each deposit's and each output's.</summary>
    /// <param name="steps">The chain's steps.</param>
    /// <returns>The addresses, first deposit first.</returns>
    private static IEnumerable<BitcoinAddress> AddressesOf(IReadOnlyList<ChainStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step.Deposit.Output.Address!;
            if (step.Out is { } link)
            {
                yield return link.Address;
            }
        }
    }

    /// <summary>Gives the transactions the chain rests on, in order: deposit, output, the spend onward.</summary>
    /// <param name="steps">The chain's steps.</param>
    /// <returns>The transactions, with repeats.</returns>
    private static IEnumerable<BitcoinTransaction> TransactionsOf(IReadOnlyList<ChainStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step.Deposit.Transaction;
            if (step.Out is { } link)
            {
                yield return link.Payout;
                if (link.Spender is { } spender)
                {
                    yield return spender;
                }
            }
        }
    }

    /// <summary>What the chain is followed through: the deposits, the mixers, the CoinJoins and the approvals.</summary>
    private sealed class ChainContext
    {
        /// <summary>The input.</summary>
        private readonly CheckInput _input;

        /// <summary>The mixers' known addresses.</summary>
        private readonly IReadOnlySet<BitcoinAddress> _mixer;

        /// <summary>Which known mixer each address belongs to, by its index in the input.</summary>
        private readonly Dictionary<BitcoinAddress, int> _mixerOf = [];

        /// <summary>The first deposit of each transaction that pays into a mixer.</summary>
        private readonly Dictionary<Txid, MixerDeposit> _depositIn = [];

        /// <summary>
        /// The transactions the clustering recognises as CoinJoin, where it ends the trail; a possible CoinJoin is not
        /// an end, since the trail goes on there.
        /// </summary>
        private readonly HashSet<Txid> _coinJoins;

        /// <summary>Creates the context.</summary>
        /// <param name="input">The input.</param>
        public ChainContext(CheckInput input)
        {
            _input = input;
            _mixer = MixerDeposits.AddressesOf(input);
            for (var index = 0; index < input.Mixers.Count; index++)
            {
                var service = input.Mixers[index].Service;
                var same = service is null
                    ? index
                    : input.Mixers.ToList().FindIndex(known => known.Service == service);
                foreach (var address in input.Mixers[index].Addresses)
                {
                    _mixerOf.TryAdd(address, same);
                }
            }

            Deposits = [.. MixerDeposits.Of(input, _mixer)];
            foreach (var deposit in Deposits)
            {
                _depositIn.TryAdd(deposit.Transaction.Txid, deposit);
            }

            _coinJoins =
            [
                .. LatestFindings.Of<CoinJoinFinding>(input)
                    .Where(found => found.Finding.EndsTrail)
                    .Select(found => found.Transaction.Txid),
            ];
        }

        /// <summary>Gets the deposits into the mixers, in block order.</summary>
        public IReadOnlyList<MixerDeposit> Deposits { get; }

        /// <summary>
        /// Gives which known mixer a deposit went into: actors whose labels name the same service are the same mixer.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The index in the input of the first actor of the same service.</returns>
        public int MixerOf(MixerDeposit deposit) => _mixerOf[deposit.Output.Address!];

        /// <summary>
        /// Gives the output of a deposit that goes into a mixer or a CoinJoin again, approved before likely, then the
        /// higher score and the key; <see langword="null"/> when none does.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The link onward, or <see langword="null"/>.</returns>
        public ChainLink? Onward(MixerDeposit deposit) =>
            FirstOf(LinksOf(deposit).Select(link => Follow(link)).OfType<ChainLink>());

        /// <summary>
        /// Gives a deposit's best output wherever it went, approved before likely, for the chain's last step.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The output, or <see langword="null"/> when the mixer analyses find none.</returns>
        public ChainLink? BestOut(MixerDeposit deposit) => FirstOf(LinksOf(deposit));

        /// <summary>Picks the output a chain follows: approved before likely, then the score, then the key.</summary>
        /// <param name="links">The outputs.</param>
        /// <returns>The first, or <see langword="null"/>.</returns>
        private static ChainLink? FirstOf(IEnumerable<ChainLink> links) =>
            links
                .OrderByDescending(link => link.IsApproved)
                .ThenByDescending(link => link.Score)
                .ThenBy(link => link.Key, StringComparer.Ordinal)
                .FirstOrDefault();

        /// <summary>
        /// Gives the outputs a deposit's money may have left the mixer by: the likely outputs and the parts of the best
        /// combination that the user has not dismissed (none from a suggestion dismissed as a whole), and every
        /// candidate the user approved for it, in <c>mixer-outputs</c> or <c>mixer-parts</c>.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The outputs, without their spend yet; one per coin.</returns>
        private IEnumerable<ChainLink> LinksOf(MixerDeposit deposit)
        {
            var outputs = MixerOutputsCheck.Identity;
            var partsId = MixerPartsCheck.Identity;
            var approvedOutputs = MixerDeposits.ApprovedCoins(_input, outputs, deposit);
            var approvedParts = MixerDeposits.ApprovedCoins(_input, partsId, deposit);
            var approved = approvedOutputs.Union(approvedParts).ToHashSet(StringComparer.Ordinal);
            var dismissed = MixerDeposits.DismissedCoins(_input, outputs, deposit)
                .Union(MixerDeposits.DismissedCoins(_input, partsId, deposit))
                .Where(key => !approved.Contains(key))
                .ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var outputsDismissed = MixerDeposits.DismissedWhole(_input, outputs, deposit);
            foreach (var (candidate, payout) in MixerOutputsCheck.Weigh(_input, deposit, _mixer))
            {
                var mine = approved.Contains(candidate.Key);
                if (!mine && (outputsDismissed || dismissed.Contains(candidate.Key)))
                {
                    continue;
                }

                seen.Add(candidate.Key);
                var fee = new Satoshi(deposit.Output.Amount.Value - candidate.Amount.Value);
                yield return new ChainLink(
                    candidate.Key, candidate.Output, candidate.Address, candidate.Amount, fee, candidate.Score,
                    mine, OutputWord, payout);
            }

            var partsDismissed = MixerDeposits.DismissedWhole(_input, partsId, deposit);
            if (MixerPartsCheck.Best(_input, deposit, _mixer) is { } parts)
            {
                var fee = new Satoshi(deposit.Output.Amount.Value - parts.Sum.Value);
                foreach (var part in parts.Parts.Where(part => !seen.Contains(part.Key)))
                {
                    var mine = approved.Contains(part.Key);
                    if (!mine && (partsDismissed || dismissed.Contains(part.Key)))
                    {
                        continue;
                    }

                    seen.Add(part.Key);
                    yield return new ChainLink(
                        part.Key, part.Output.Index, part.Address, part.Output.Amount, fee, parts.Score, mine,
                        PartWord, part.Transaction);
                }
            }

            var approvedPartsPaid = approvedParts
                .Select(key => OutputOf(key)?.Output.Amount.Value ?? 0)
                .Sum();
            foreach (var key in approved.Where(key => !seen.Contains(key)).Order(StringComparer.Ordinal))
            {
                if (OutputOf(key) is not ({ Address: { } address } output, var payout))
                {
                    continue;
                }

                var isPart = approvedParts.Contains(key) && !approvedOutputs.Contains(key);
                var paid = isPart ? approvedPartsPaid : output.Amount.Value;
                var fee = new Satoshi(Math.Max(0, deposit.Output.Amount.Value - paid));
                yield return new ChainLink(
                    key, output.Index, address, output.Amount, fee, 0, true, isPart ? PartWord : OutputWord, payout);
            }
        }

        /// <summary>Finds where an output went: into a mixer again, into a CoinJoin, or elsewhere.</summary>
        /// <param name="link">The output.</param>
        /// <returns>The link with its spend, or <see langword="null"/> when it does not go into a mixer.</returns>
        private ChainLink? Follow(ChainLink link)
        {
            if (MixerDeposits.SpenderOf(_input, link.Payout.Txid, link.Index) is not { } spender)
            {
                return null;
            }

            if (_depositIn.TryGetValue(spender.Txid, out var next))
            {
                return link with { Spender = spender, Next = next };
            }

            return _coinJoins.Contains(spender.Txid) ? link with { Spender = spender, CoinJoin = spender } : null;
        }

        /// <summary>Gives the output an approved candidate names, when the graph holds it.</summary>
        /// <param name="key">The candidate's coin, <c>txid:index</c>.</param>
        /// <returns>The output and its transaction, or <see langword="null"/>.</returns>
        private (BitcoinOutput Output, BitcoinTransaction Transaction)? OutputOf(string key) =>
            _input.Approved.FirstOrDefault(a => MixerDeposits.CoinKey(a.Txid, a.Output) == key) is { } approved
                ? Output(approved.Txid, approved.Output)
                : null;

        /// <summary>Gives an output of a transaction in the input.</summary>
        /// <param name="txid">The transaction.</param>
        /// <param name="index">The output's index.</param>
        /// <returns>The output and its transaction, or <see langword="null"/> when the graph does not hold it.</returns>
        private (BitcoinOutput Output, BitcoinTransaction Transaction)? Output(Txid txid, int index)
        {
            var transaction = _input.Transactions.FirstOrDefault(t => t.Txid == txid && t.Block is not null);
            var output = transaction?.Outputs.FirstOrDefault(o => o.Index == index);
            return output is null ? null : (output, transaction!);
        }
    }
}

/// <summary>An output of a mixer that the chain may follow, and, once followed, where it went.</summary>
/// <param name="Key">The output's coin, <c>txid:index</c>.</param>
/// <param name="Index">The output's index in <paramref name="Payout"/>.</param>
/// <param name="Address">The output's address.</param>
/// <param name="Amount">The output's amount.</param>
/// <param name="Fee">The step's likely fee: the deposit minus the output, or minus all the parts for a part.</param>
/// <param name="Score">The mixer analysis's score; 0 for an approved candidate the analyses no longer weigh.</param>
/// <param name="IsApproved">Whether the user approved it.</param>
/// <param name="What">What it is, as the text says it: <c>utgång</c> or <c>del</c>.</param>
/// <param name="Payout">The transaction it came in.</param>
internal sealed record ChainLink(
    string Key, int Index, BitcoinAddress Address, Satoshi Amount, Satoshi Fee, double Score, bool IsApproved,
    string What, BitcoinTransaction Payout)
{
    /// <summary>Gets the transaction that spends the output into a mixer or a CoinJoin, once followed there.</summary>
    public BitcoinTransaction? Spender { get; init; }

    /// <summary>Gets the deposit into a mixer it went into, when it did.</summary>
    public MixerDeposit? Next { get; init; }

    /// <summary>Gets the CoinJoin it went into, when it did.</summary>
    public BitcoinTransaction? CoinJoin { get; init; }
}

/// <summary>One step of a chain: a deposit into a mixer and the output out of it.</summary>
/// <param name="Deposit">The deposit.</param>
/// <param name="Mixer">Which known mixer, by its index in the input.</param>
/// <param name="Out">
/// The output followed onward; for the last step the best output, which goes nowhere the chain follows, or
/// <see langword="null"/> when the mixer analyses find none.
/// </param>
internal sealed record ChainStep(MixerDeposit Deposit, int Mixer, ChainLink? Out);
