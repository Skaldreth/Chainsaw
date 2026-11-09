using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Shows mixer after mixer (chaining, the user's question 2026-11-08): when an output of a mixer that the user approved,
/// or that the mixer analyses find likely, goes into a mixer again (the same, another, a likely mixer without a label,
/// or a CoinJoin), the steps are one check with the amount and the fees summed along the chain. Each new deposit into
/// a known mixer gets the mixer's own suggestions (<c>mixer-outputs</c>, <c>mixer-parts</c>) by itself, so the chain can
/// be followed step by step. A step through a likely output is only as good as that output: the check is a judgement,
/// never a fact (principle 2).
/// </summary>
/// <remarks>
/// At each step the approved outputs that go on are followed, all of them; without any, the likeliest output that goes
/// on, or, when that is a part of the best combination, every part of it that goes on. Parts spent together in one
/// transaction are one way onward; outputs that go into different mixers branch the chain. A likely mixer without a
/// label (<see cref="LikelyMixers"/>) is passed through its likeliest payout. Only what the graph holds is followed,
/// and at most <see cref="MostSteps"/> steps in all.
/// </remarks>
internal sealed class MixerChainCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "mixer-chain";

    /// <summary>The most steps a chain is followed, over all its branches.</summary>
    public const int MostSteps = 10;

    /// <summary>What a whole output is called in the text.</summary>
    private const string OutputWord = "utgång";

    /// <summary>What a part of the best combination is called in the text.</summary>
    private const string PartWord = "del";

    /// <summary>
    /// How much lower a likely output's score may be than the likeliest output's for the chain to follow it because it
    /// goes into a mixer; a clearly worse one is not followed.
    /// </summary>
    public const double FollowMargin = 0.1;

    /// <summary>The letters that name a step's ways onward when the chain branches.</summary>
    private const string Letters = "abcdefghijklmnopqrstuvwxyz";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 2);

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
        var reached = onward.Values.SelectMany(branches => branches)
            .Select(branch => branch.Next?.Key)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var findings = new List<CheckFinding>();
        foreach (var root in context.Roots.Where(d => onward[d.Key].Count > 0 && !reached.Contains(d.Key)))
        {
            var walk = new ChainWalk(context, onward);
            walk.Visit(root, -1);
            findings.Add(Describe(walk.Steps, walk.IsCut));
        }

        return findings;
    }

    /// <summary>Describes a chain of steps as a check.</summary>
    /// <param name="steps">The steps in the order walked, first deposit first.</param>
    /// <param name="isCut">Whether the chain goes on beyond <see cref="MostSteps"/>.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(IReadOnlyList<ChainStep> steps, bool isCut)
    {
        var first = steps[0].Deposit;
        var fees = new Satoshi(steps.Sum(step => FeeOf(step).Value));
        var coinJoins = steps.SelectMany(step => step.Branches).Where(branch => branch.CoinJoin is not null).ToList();
        var outs = Ends(steps).ToList();
        var paidOut = outs.Count == 0 ? (Satoshi?)null : new Satoshi(outs.Sum(amount => amount.Value));
        var mixings = steps.Count + coinJoins.Count;
        var ways = 1 + steps.Sum(step => Math.Max(0, step.Branches.Count - 1));
        var why = new StringBuilder();
        why.Append($"{Id} följer pengarna från insättningen i block {first.Block.Height} genom de utgångar ur "
            + "mixern som du har godkänt, eller annars de som mixerns förslag finner troligast och som du inte har "
            + "avfärdat, och ser om de går in i en mixer eller en CoinJoin igen. ");
        for (var index = 0; index < steps.Count; index++)
        {
            why.Append(StepText(steps, index)).Append(' ');
        }

        var lastOut = paidOut is { } sum ? $"{CheckText.Btc(sum)} BTC i de sista utgångarna" : "ingen trolig utgång "
            + "ur den sista mixern";
        why.Append($"Längs kedjan: {CheckText.Btc(first.Output.Amount)} BTC in, {lastOut} och "
            + $"{CheckText.Btc(fees)} BTC i troliga avgifter i mixrarna "
            + $"({CheckText.Percent((double)fees.Value / first.Output.Amount.Value)} av den första insättningen). ");
        why.Append(coinJoins.Count > 0
            ? "Spåret tar slut i CoinJoin-en, enligt klustringens bedömning; följ den med CoinJoin-kontrollen. "
            : "Varje ny insättning i en mixer med etikett får mixerns egna förslag, som står för sig och avgörs där. ");
        if (steps.Any(step => step.IsLikelyMixer))
        {
            why.Append($"En trolig mixer utan etikett är {MixerBehaviourCheck.Identity}:s bedömning, och dess "
                + "utgång är den utbetalning som analysen knöt till insättningen; sätt etiketten Mixer med L om du "
                + "håller med, så får den egna förslag. ");
        }

        if (isCut)
        {
            why.Append($"Kedjan följdes högst {CheckText.Count(MostSteps)} steg; den går vidare efter det. ");
        }

        why.Append("Ett steg genom en trolig utgång är bara så säkert som utgången: godkänn eller avfärda kandidaterna "
            + "i mixerns förslag. Det är en bedömning, aldrig ett faktum.");
        var title = $"Mixer efter mixer, {CheckText.Count(mixings)} steg";
        return new CheckFinding(
            first.Key,
            CheckKind.Check,
            AreaSort.Obfuscation,
            ways > 1 ? $"{title}, {CheckText.Count(ways)} grenar" : title,
            SummaryOf(first, paidOut, fees, coinJoins.Count > 0),
            why.ToString(),
            [.. AddressesOf(steps).Distinct()],
            [.. coinJoins.Select(branch => branch.CoinJoin!.Txid).Distinct()],
            [.. TransactionsOf(steps).DistinctBy(t => t.Txid).Select(CheckBasis.Of)],
            []);
    }

    /// <summary>
    /// Gives a step's likely fee: one output's or a combination's own, or, for several whole outputs, what the deposit
    /// lost besides them.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <returns>The fee; zero when nothing comes out.</returns>
    private static Satoshi FeeOf(ChainStep step)
    {
        var links = step.Links;
        if (links.Count == 0)
        {
            return Satoshi.Zero;
        }

        if (links.Count == 1 || links.All(link => link.What == PartWord))
        {
            return links[0].Fee;
        }

        return new Satoshi(Math.Max(0, step.Deposit.Output.Amount.Value - links.Sum(link => link.Amount.Value)));
    }

    /// <summary>Gives what came out where the chain ends: each last step's output and each way into a CoinJoin.</summary>
    /// <param name="steps">The steps.</param>
    /// <returns>The amounts.</returns>
    private static IEnumerable<Satoshi> Ends(IReadOnlyList<ChainStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Best is { } best)
            {
                yield return best.Amount;
            }

            foreach (var branch in step.Branches.Where(branch => branch.CoinJoin is not null))
            {
                yield return branch.Amount;
            }
        }
    }

    /// <summary>Writes the line under the title: what went in, what came out last and the fees.</summary>
    /// <param name="first">The first deposit.</param>
    /// <param name="paidOut">What came out where the chain ends, or <see langword="null"/>.</param>
    /// <param name="fees">The likely fees along the chain.</param>
    /// <param name="endsInCoinJoin">Whether a way ends in a CoinJoin.</param>
    /// <returns>For example <c>0,1500 in · 0,1430 ut · avgifter 0,00675</c>.</returns>
    private static string SummaryOf(MixerDeposit first, Satoshi? paidOut, Satoshi fees, bool endsInCoinJoin)
    {
        var parts = new List<string> { $"{CheckText.Btc(first.Output.Amount)} in" };
        if (paidOut is { } sum)
        {
            parts.Add($"{CheckText.Btc(sum)} ut");
        }

        parts.Add($"avgifter {CheckText.Btc(fees)}");
        if (endsInCoinJoin)
        {
            parts.Add("slutar i CoinJoin");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Writes one step: what went into which mixer, the outputs and where they went.</summary>
    /// <param name="steps">The chain's steps.</param>
    /// <param name="index">The step's index.</param>
    /// <returns>The step's sentence.</returns>
    private static string StepText(IReadOnlyList<ChainStep> steps, int index)
    {
        var step = steps[index];
        var which = index == 0 ? "mixern" : MixerWords(steps, index);
        var after = index > 0 && step.Parent != index - 1 ? $" (efter steg {CheckText.Count(step.Parent + 1)})" : "";
        var text = $"Steg {CheckText.Count(index + 1)}{after}: {CheckText.Btc(step.Deposit.Output.Amount)} BTC in i "
            + $"{which} (block {step.Deposit.Block.Height})";
        if (step.Branches.Count == 0)
        {
            return step.Best is { } best
                ? $"{text}; {LinkText(best)}; den går inte vidare in i någon mixer i grafen."
                : text + "; ingen trolig utgång ur den i grafen än.";
        }

        if (step.Branches.Count == 1)
        {
            return $"{text}; {BranchText(step.Branches[0])}.";
        }

        var ways = step.Branches.Select((branch, i) => $"({Letters[i % Letters.Length]}) {BranchText(branch)}");
        return $"{text}; kedjan delar sig i {CheckText.Count(step.Branches.Count)} vägar: {string.Join("; ", ways)}.";
    }

    /// <summary>Writes one way onward: the outputs and where they went.</summary>
    /// <param name="branch">The way onward.</param>
    /// <returns>The clause, without a full stop.</returns>
    private static string BranchText(ChainBranch branch)
    {
        var links = branch.Links;
        var text = links.Count == 1 ? LinkText(links[0]) : TogetherText(links);
        var they = links.Count == 1 ? "den" : "de";
        if (branch.CoinJoin is not null)
        {
            return text + $"; {they} går vidare i tx {CheckText.Short(branch.Spender.Txid)}, som klustringen känner "
                + "igen som en CoinJoin (en bedömning)";
        }

        var next = branch.Next!;
        text += $"; {they} går vidare i tx {CheckText.Short(branch.Spender.Txid)} till en insättning på "
            + $"{CheckText.Btc(next.Output.Amount)} BTC";
        return next.Output.Amount == branch.Amount
            ? text
            : text + ", inte samma belopp: något kom till eller gick bort mellan stegen";
    }

    /// <summary>Writes one output: approved or likely, what, the amount, the transaction and the fee.</summary>
    /// <param name="link">The output.</param>
    /// <returns>For example <c>trolig utgång 0,14625 BTC i tx … (block 900004), trolig avgift 0,00375 BTC</c>.</returns>
    private static string LinkText(ChainLink link)
    {
        var state = link.IsApproved ? "godkänd" : "trolig";
        var rank = link is { IsApproved: false, Rank: > 1 } && link.Score < link.BestScore
            ? $" (inte den troligaste: nummer {CheckText.Count(link.Rank)} av {CheckText.Count(link.Of)}, poäng "
                + $"{Score(link.Score)} mot {Score(link.BestScore)}, men den som går vidare in i en mixer)"
            : string.Empty;
        return $"{state} {link.What} {CheckText.Btc(link.Amount)} BTC i tx {CheckText.Short(link.Payout.Txid)} "
            + $"(block {link.Payout.Block!.Height}){rank}, trolig avgift {CheckText.Btc(link.Fee)} BTC";
    }

    /// <summary>Writes a score with two decimals, the Swedish way.</summary>
    /// <param name="score">The score.</param>
    /// <returns>For example <c>0,92</c>.</returns>
    private static string Score(double score) =>
        score.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("sv-SE"));

    /// <summary>Writes several outputs that go on together in one transaction.</summary>
    /// <param name="links">The outputs.</param>
    /// <returns>For example <c>2 troliga delar på sammanlagt 0,1100 BTC (…), trolig avgift …</c>.</returns>
    private static string TogetherText(IReadOnlyList<ChainLink> links)
    {
        var state = links.All(link => link.IsApproved) ? "godkända" : "troliga";
        var what = links.All(link => link.What == PartWord) ? "delar" : "utgångar";
        var sum = new Satoshi(links.Sum(link => link.Amount.Value));
        var each = string.Join(", ", links.Select(link => $"{CheckText.Btc(link.Amount)} i tx "
            + $"{CheckText.Short(link.Payout.Txid)}"));
        return $"{CheckText.Count(links.Count)} {state} {what} på sammanlagt {CheckText.Btc(sum)} BTC ({each}), "
            + $"trolig avgift {CheckText.Btc(links[0].Fee)} BTC, som följs tillsammans";
    }

    /// <summary>
    /// Says which mixer a later step goes into: the same as an earlier step, a likely mixer without a label, or another.
    /// </summary>
    /// <param name="steps">The chain's steps.</param>
    /// <param name="index">The step's index, from 1.</param>
    /// <returns>For example <c>samma mixer igen</c>.</returns>
    private static string MixerWords(IReadOnlyList<ChainStep> steps, int index)
    {
        if (steps.Take(index).Any(step => step.Mixer == steps[index].Mixer))
        {
            return "samma mixer igen";
        }

        return steps[index].IsLikelyMixer ? "en trolig mixer utan etikett" : "en annan mixer";
    }

    /// <summary>Gives the addresses the area is drawn around: each deposit's and each output's.</summary>
    /// <param name="steps">The chain's steps.</param>
    /// <returns>The addresses, first deposit first.</returns>
    private static IEnumerable<BitcoinAddress> AddressesOf(IReadOnlyList<ChainStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step.Deposit.Output.Address!;
            foreach (var link in step.Links)
            {
                yield return link.Address;
            }
        }
    }

    /// <summary>Gives the transactions the chain rests on, in order: deposit, outputs, the spends onward.</summary>
    /// <param name="steps">The chain's steps.</param>
    /// <returns>The transactions, with repeats.</returns>
    private static IEnumerable<BitcoinTransaction> TransactionsOf(IReadOnlyList<ChainStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step.Deposit.Transaction;
            foreach (var link in step.Links)
            {
                yield return link.Payout;
                if (link.Spender is { } spender)
                {
                    yield return spender;
                }
            }
        }
    }

    /// <summary>Walks a chain from its first deposit, depth first, at most <see cref="MostSteps"/> steps.</summary>
    /// <param name="context">What the chain is followed through.</param>
    /// <param name="onward">The ways onward of every deposit.</param>
    private sealed class ChainWalk(ChainContext context, Dictionary<string, IReadOnlyList<ChainBranch>> onward)
    {
        /// <summary>The deposits already walked, so that nothing is walked twice.</summary>
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

        /// <summary>Gets the steps in the order walked.</summary>
        public List<ChainStep> Steps { get; } = [];

        /// <summary>Gets whether a way onward was left because the chain had <see cref="MostSteps"/> steps.</summary>
        public bool IsCut { get; private set; }

        /// <summary>Walks a deposit and, after it, every deposit its ways onward reach.</summary>
        /// <param name="deposit">The deposit.</param>
        /// <param name="parent">The step it was reached from, or -1 for the first.</param>
        public void Visit(MixerDeposit deposit, int parent)
        {
            if (Steps.Count == MostSteps)
            {
                IsCut = true;
                return;
            }

            if (!_visited.Add(deposit.Key))
            {
                return;
            }

            var branches = onward[deposit.Key];
            var index = Steps.Count;
            var best = branches.Count == 0 ? context.BestOut(deposit) : null;
            Steps.Add(new ChainStep(
                deposit, context.MixerOf(deposit), context.IsLikely(deposit), parent, branches, best));
            foreach (var branch in branches)
            {
                if (branch.Next is { } next)
                {
                    Visit(next, index);
                }
            }
        }
    }

    /// <summary>
    /// What the chain is followed through: the deposits into the known and the likely mixers, the CoinJoins and the
    /// approvals.
    /// </summary>
    private sealed class ChainContext
    {
        /// <summary>The input.</summary>
        private readonly CheckInput _input;

        /// <summary>The mixers' known addresses.</summary>
        private readonly IReadOnlySet<BitcoinAddress> _mixer;

        /// <summary>Which mixer each deposit went into, by the deposit's key.</summary>
        private readonly Dictionary<string, string> _mixerOf = new(StringComparer.Ordinal);

        /// <summary>The deposits into a likely mixer without a label, with their likely payouts, by key.</summary>
        private readonly Dictionary<string, LikelyDeposit> _likely = new(StringComparer.Ordinal);

        /// <summary>Which transaction in the input spends each coin.</summary>
        private readonly Dictionary<(Txid Txid, int Index), BitcoinTransaction> _spenders;

        /// <summary>The first deposit of each transaction that pays into a mixer, known before likely.</summary>
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
            _spenders = input.Spenders();
            var labelOf = new Dictionary<BitcoinAddress, string>();
            for (var index = 0; index < input.Mixers.Count; index++)
            {
                var service = input.Mixers[index].Service;
                var same = service is null
                    ? index
                    : input.Mixers.ToList().FindIndex(known => known.Service == service);
                foreach (var address in input.Mixers[index].Addresses)
                {
                    labelOf.TryAdd(address, "label:" + same.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            Roots = [.. MixerDeposits.Of(input, _mixer)];
            var deposits = new List<MixerDeposit>(Roots);
            foreach (var deposit in Roots)
            {
                _mixerOf[deposit.Key] = labelOf[deposit.Output.Address!];
                _depositIn.TryAdd(deposit.Transaction.Txid, deposit);
            }

            foreach (var mixer in LikelyMixers.Of(input))
            {
                foreach (var likely in mixer.Deposits.Where(likely => _mixerOf.TryAdd(likely.Deposit.Key, mixer.Key)))
                {
                    _likely[likely.Deposit.Key] = likely;
                    deposits.Add(likely.Deposit);
                    _depositIn.TryAdd(likely.Deposit.Transaction.Txid, likely.Deposit);
                }
            }

            Deposits = deposits;
            _coinJoins =
            [
                .. LatestFindings.Of<CoinJoinFinding>(input)
                    .Where(found => found.Finding.EndsTrail)
                    .Select(found => found.Transaction.Txid),
            ];
        }

        /// <summary>Gets the deposits into the known mixers, in block order: where a chain may start.</summary>
        public IReadOnlyList<MixerDeposit> Roots { get; }

        /// <summary>Gets every deposit a chain may pass: into the known mixers, then into the likely ones.</summary>
        public IReadOnlyList<MixerDeposit> Deposits { get; }

        /// <summary>
        /// Gives which mixer a deposit went into: actors whose labels name the same service are the same mixer, and a
        /// likely mixer is its owner.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The mixer's key.</returns>
        public string MixerOf(MixerDeposit deposit) => _mixerOf[deposit.Key];

        /// <summary>Says whether a deposit went into a likely mixer without a label.</summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns><see langword="true"/> when it did.</returns>
        public bool IsLikely(MixerDeposit deposit) => _likely.ContainsKey(deposit.Key);

        /// <summary>
        /// Gives a deposit's ways onward into a mixer or a CoinJoin: every approved output that goes on, or else the
        /// likeliest that goes on (with every part of the best combination that goes on, when it is a part); outputs
        /// spent in the same transaction are one way.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The ways onward, the likeliest first; empty when none goes on.</returns>
        public IReadOnlyList<ChainBranch> Onward(MixerDeposit deposit)
        {
            var ranked = Ranked(LinksOf(deposit)).ToList();
            var followed = ranked
                .Select((link, index) => Follow(link) is { } onward
                    ? onward with { Rank = index + 1, Of = ranked.Count, BestScore = ranked[0].Score }
                    : null)
                .OfType<ChainLink>()
                .ToList();
            if (followed.Count == 0)
            {
                return [];
            }

            IEnumerable<ChainLink> chosen;
            if (ranked.Any(link => link.IsApproved))
            {
                // The user decided where the money went: only what was approved is followed.
                chosen = followed.Where(link => link.IsApproved);
            }
            else if (ranked[0].Score - followed[0].Score > FollowMargin)
            {
                // A clearly worse output is never followed only because it goes into a mixer.
                return [];
            }
            else
            {
                chosen = followed[0].What == PartWord ? followed.Where(link => link.What == PartWord) : [followed[0]];
            }

            return [.. chosen.GroupBy(link => link.Spender!.Txid).Select(group => new ChainBranch([.. group]))];
        }

        /// <summary>
        /// Gives a deposit's best output wherever it went, approved before likely, for a step where the chain ends.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The output, or <see langword="null"/> when the mixer analyses find none.</returns>
        public ChainLink? BestOut(MixerDeposit deposit) => Ranked(LinksOf(deposit)).FirstOrDefault();

        /// <summary>Orders outputs as a chain follows them: approved before likely, then the score, the key.</summary>
        /// <param name="links">The outputs.</param>
        /// <returns>The outputs in order.</returns>
        private static IEnumerable<ChainLink> Ranked(IEnumerable<ChainLink> links) =>
            links
                .OrderByDescending(link => link.IsApproved)
                .ThenByDescending(link => link.Score)
                .ThenBy(link => link.Key, StringComparer.Ordinal);

        /// <summary>
        /// Gives the outputs a deposit's money may have left the mixer by: for a likely mixer its likely payouts, else
        /// the likely outputs and the parts of the best combination that the user has not dismissed (none from a
        /// suggestion dismissed as a whole), and every candidate the user approved for it, in <c>mixer-outputs</c> or
        /// <c>mixer-parts</c>.
        /// </summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The outputs, without their spend yet; one per coin.</returns>
        private IEnumerable<ChainLink> LinksOf(MixerDeposit deposit)
        {
            if (_likely.TryGetValue(deposit.Key, out var likely))
            {
                if (likely.Match is not { } match)
                {
                    return [];
                }

                var what = match.IsParts ? PartWord : OutputWord;
                return match.Outputs.Select(payout => new ChainLink(
                    payout.Key, payout.Output.Index, payout.Address, payout.Output.Amount,
                    deposit.Output.Amount - match.Amount, match.Score, false, what, payout.Transaction));
            }

            return KnownLinksOf(deposit);
        }

        /// <summary>Gives the outputs of a deposit into a known mixer (see <see cref="LinksOf"/>).</summary>
        /// <param name="deposit">The deposit.</param>
        /// <returns>The outputs; one per coin.</returns>
        private IEnumerable<ChainLink> KnownLinksOf(MixerDeposit deposit)
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
                if ((!mine && (outputsDismissed || dismissed.Contains(candidate.Key)))
                    || candidate is not { Address: { } address, Amount: { } paid })
                {
                    continue;
                }

                seen.Add(candidate.Key);
                var fee = new Satoshi(deposit.Output.Amount.Value - paid.Value);
                yield return new ChainLink(
                    candidate.Key, candidate.Output, address, paid, fee, candidate.Score, mine, OutputWord, payout);
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

        /// <summary>
        /// Finds where an output went: into a known mixer, a CoinJoin or a likely mixer again, or elsewhere.
        /// </summary>
        /// <param name="link">The output.</param>
        /// <returns>The link with its spend, or <see langword="null"/> when it does not go into a mixer.</returns>
        private ChainLink? Follow(ChainLink link)
        {
            if (!_spenders.TryGetValue((link.Payout.Txid, link.Index), out var spender))
            {
                return null;
            }

            if (_depositIn.TryGetValue(spender.Txid, out var next) && !_likely.ContainsKey(next.Key))
            {
                return link with { Spender = spender, Next = next };
            }

            if (_coinJoins.Contains(spender.Txid))
            {
                return link with { Spender = spender, CoinJoin = spender };
            }

            return next is null ? null : link with { Spender = spender, Next = next };
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

    /// <summary>Gets its place among the deposit's outputs, likeliest first, once followed; 0 before.</summary>
    public int Rank { get; init; }

    /// <summary>Gets how many outputs the deposit's were ranked among, once followed.</summary>
    public int Of { get; init; }

    /// <summary>Gets the likeliest output's score, once followed.</summary>
    public double BestScore { get; init; }
}

/// <summary>One way onward from a step: one output, or several spent together in one transaction.</summary>
/// <param name="Links">The outputs, the likeliest first; all spent in the same transaction.</param>
internal sealed record ChainBranch(IReadOnlyList<ChainLink> Links)
{
    /// <summary>Gets the transaction that spends them.</summary>
    public BitcoinTransaction Spender => Links[0].Spender!;

    /// <summary>Gets the deposit they went into, when they went into a mixer.</summary>
    public MixerDeposit? Next => Links[0].Next;

    /// <summary>Gets the CoinJoin they went into, when they did.</summary>
    public BitcoinTransaction? CoinJoin => Links[0].CoinJoin;

    /// <summary>Gets what the outputs hold together.</summary>
    public Satoshi Amount => new(Links.Sum(link => link.Amount.Value));
}

/// <summary>One step of a chain: a deposit into a mixer and the outputs out of it.</summary>
/// <param name="Deposit">The deposit.</param>
/// <param name="Mixer">Which mixer: a label's key, or a likely mixer's owner.</param>
/// <param name="IsLikelyMixer">Whether the mixer is a likely one without a label.</param>
/// <param name="Parent">The step it was reached from, or -1 for the first.</param>
/// <param name="Branches">The ways onward; empty where the chain ends.</param>
/// <param name="Best">
/// Where the chain ends, the best output, which goes nowhere the chain follows, or <see langword="null"/> when the
/// mixer analyses find none; <see langword="null"/> when the chain goes on.
/// </param>
internal sealed record ChainStep(
    MixerDeposit Deposit,
    string Mixer,
    bool IsLikelyMixer,
    int Parent,
    IReadOnlyList<ChainBranch> Branches,
    ChainLink? Best)
{
    /// <summary>Gets every output of the step the text names: the ways onward's, or the best one.</summary>
    public IReadOnlyList<ChainLink> Links =>
        Best is { } best ? [best] : [.. Branches.SelectMany(branch => branch.Links)];
}
