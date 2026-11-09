using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Suggests where money that reached a bridge or a swap service continues on another chain (CLAUDE.md,
/// <em>Cross-chain</em>): for each payment into one, the payments on the other chains that may be the same money after
/// the service's fee. The swap's own event comes first: a recipient that the payment's memo names on the chain is a
/// candidate wherever it is paid in the window. Otherwise a payment is weighed by its value against the payment in
/// (by the case's rates, since the assets differ), the time after it, and its address: the same key on another account
/// chain, or a payout from a bridge or swap there. Nothing on either chain ties the two together, so every candidate
/// is a judgement, always dashed (principle 2); approving one links the money to it as the user's judgement.
/// </summary>
/// <remarks>
/// It reads the graph's payments on every chain and those the cross-chain search read into the case
/// (<see cref="ICrossChainLookup"/>). Its scope is Bitcoin's, since the checks run there; it weighs payments into a
/// bridge on every chain the graph holds.
/// </remarks>
internal sealed class CrossChainCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = CheckAnalysisNames.CrossChain;

    /// <summary>The analysis's name and version, which the decisions about its suggestions are kept by.</summary>
    public static readonly HeuristicId Identity = new(Name, 1);

    /// <summary>The highest fee weighed: a payment worth less than the payment in by more is not a candidate.</summary>
    public const decimal MostFee = 0.20m;

    /// <summary>
    /// The most a payment out may be worth above the payment in and still be weighed: the two rates are read at two
    /// times, so a small gain is the rates moving, not the service paying more.
    /// </summary>
    public const decimal MostGain = 0.03m;

    /// <summary>The lowest fee that counts in full.</summary>
    public const decimal UsualFeeLow = 0.002m;

    /// <summary>The highest fee that counts in full; swap services and bridges usually take less.</summary>
    public const decimal UsualFeeHigh = 0.06m;

    /// <summary>What a fee below the usual counts (no fee, or the rates moving).</summary>
    public const double LowFeeScore = 0.7;

    /// <summary>What a fee counts at <see cref="MostFee"/>.</summary>
    public const double HighestFeeScore = 0.2;

    /// <summary>The lowest score a candidate is kept at.</summary>
    public const double LowestScore = 0.45;

    /// <summary>What an address with nothing known about it counts.</summary>
    public const double PlainAddressScore = 0.5;

    /// <summary>What a payout from a bridge or a swap on the other chain counts.</summary>
    public const double FromBridgeScore = 0.8;

    /// <summary>What the same key on another account chain counts (the same 20 bytes on Ethereum and TRON).</summary>
    public const double SameKeyScore = 0.9;

    /// <summary>What the value counts in the score of a candidate the memo does not name.</summary>
    private const double ValueWeight = 0.5;

    /// <summary>What the time counts in the score of a candidate the memo does not name.</summary>
    private const double TimeWeight = 0.25;

    /// <summary>What the address counts in the score of a candidate the memo does not name.</summary>
    private const double AddressWeight = 0.25;

    /// <summary>The score a candidate the memo names starts from: the chain itself names its recipient.</summary>
    private const double MemoBase = 0.7;

    /// <summary>What the value and the time each add to a candidate the memo names.</summary>
    private const double MemoPartWeight = 0.15;

    /// <summary>What the time counts at the end of the window.</summary>
    private const double LatestTimeScore = 0.2;

    /// <summary>How long after the payment in a payment out is weighed.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(72);

    /// <summary>How soon a payment out counts in full: services usually pay within the hour.</summary>
    public static readonly TimeSpan Quick = TimeSpan.FromHours(2);

    /// <summary>How much earlier a payment out may be stamped: two chains' block clocks differ by minutes.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(10);

    /// <summary>The Swedish culture the amounts are written in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public string DisplayName => "Annan kedja";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Graph is not { } graph)
        {
            return [];
        }

        var payments = CrossChainPayments.All(graph);
        var pool = payments.Concat(input.CrossChain.Arrivals).DistinctBy(payment => payment.Key).ToList();
        return
        [
            .. CrossChainPayments.DeparturesOf(graph, payments, Identity.ToString())
                .Select(departure => Describe(input, graph, departure, pool))
                .OfType<CheckFinding>(),
        ];
    }

    /// <summary>
    /// Gives the score of a fee, from 0 to 1: full for a usual fee, less for none or a small gain, falling towards
    /// the highest; 0 outside.
    /// </summary>
    /// <param name="fee">The share of the value in that did not come out; negative when more came out.</param>
    /// <returns>The score.</returns>
    public static double FeeScore(decimal fee)
    {
        if (fee < -MostGain || fee > MostFee)
        {
            return 0;
        }

        if (fee < UsualFeeLow)
        {
            return LowFeeScore;
        }

        return fee <= UsualFeeHigh
            ? 1
            : 1 - ((1 - HighestFeeScore) * (double)((fee - UsualFeeHigh) / (MostFee - UsualFeeHigh)));
    }

    /// <summary>Gives the score of the time after the payment in: full when quick, a fifth at the end.</summary>
    /// <param name="after">The time after the payment in.</param>
    /// <returns>The score, or 0 outside the window.</returns>
    public static double TimeScore(TimeSpan after)
    {
        if (after < -Skew || after > Window)
        {
            return 0;
        }

        return after <= Quick ? 1 : 1 - ((1 - LatestTimeScore) * ((after - Quick) / (Window - Quick)));
    }

    /// <summary>
    /// Says whether two addresses on different account chains are the same key: Ethereum's and TRON's addresses are
    /// the same 20 bytes behind different writing, so one private key holds both.
    /// </summary>
    /// <param name="first">The first address.</param>
    /// <param name="second">The second address.</param>
    /// <returns><see langword="true"/> when they are on different chains and hold the same 20 bytes.</returns>
    public static bool SameKey(ChainAddress? first, ChainAddress? second) =>
        first is not null && second is not null && first.Chain != second.Chain
        && EthereumAddress.TryParse(first.Chain, first.Value, out var one)
        && EthereumAddress.TryParse(second.Chain, second.Value, out var other)
        && string.Equals(one.Hex, other.Hex, StringComparison.Ordinal);

    /// <summary>
    /// Weighs every payment on another chain as a continuation of one departure, best first: those the memo names
    /// first, then by score, then the soonest.
    /// </summary>
    /// <param name="input">The input, with the values.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="departure">The payment into the bridge or swap.</param>
    /// <param name="pool">Every payment known, on every chain.</param>
    /// <returns>The candidates kept.</returns>
    public static IReadOnlyList<CheckCandidate> Weigh(
        CheckInput input, TraceGraph graph, CrossChainDeparture departure, IReadOnlyList<ChainMovement> pool)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(departure);
        ArgumentNullException.ThrowIfNull(pool);
        if (departure.Payment.Time is not { } start)
        {
            return [];
        }

        var valueIn = input.CrossChain.ValueOf(departure.Payment);
        var weighed = new List<(CheckCandidate Candidate, bool Named, DateTimeOffset Time)>();
        foreach (var arrival in pool)
        {
            if (arrival.Transaction.Chain == departure.Payment.Transaction.Chain || arrival.Time is not { } time)
            {
                continue;
            }

            var timeScore = TimeScore(time - start);
            if (timeScore == 0)
            {
                continue;
            }

            var named = departure.Memo?.Address is { } recipient && recipient == arrival.To;
            var feeScore = FeeOf(valueIn, input.CrossChain.ValueOf(arrival)) is { } fee ? FeeScore(fee) : (double?)null;
            if (feeScore == 0 || (feeScore is null && !named))
            {
                // The amount does not fit, or cannot be compared without the memo naming the recipient.
                continue;
            }

            var addressScore = named ? 1 : AddressScore(graph, departure, arrival);
            var score = named
                ? MemoBase + (MemoPartWeight * (feeScore ?? PlainAddressScore)) + (MemoPartWeight * timeScore)
                : (ValueWeight * feeScore!.Value) + (TimeWeight * timeScore) + (AddressWeight * addressScore);
            if (score < LowestScore)
            {
                continue;
            }

            var reason = Reason(input, departure, arrival, named, addressScore, time - start);
            weighed.Add((Candidate(arrival, Math.Round(score, 2), reason), named, time));
        }

        return
        [
            .. weighed.OrderByDescending(item => item.Named)
                .ThenByDescending(item => item.Candidate.Score)
                .ThenBy(item => item.Time)
                .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal)
                .Select(item => item.Candidate),
        ];
    }

    /// <summary>Gives the fee between two values, when both are known.</summary>
    /// <param name="valueIn">What went in was worth, or <see langword="null"/>.</param>
    /// <param name="valueOut">What came out was worth, or <see langword="null"/>.</param>
    /// <returns>The share that did not come out, or <see langword="null"/>.</returns>
    private static decimal? FeeOf(decimal? valueIn, decimal? valueOut) =>
        valueIn is > 0 && valueOut is { } paid ? 1 - (paid / valueIn.Value) : null;

    /// <summary>Gives the score of a payment's recipient and payer.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="departure">The departure.</param>
    /// <param name="arrival">The payment out.</param>
    /// <returns>The score.</returns>
    private static double AddressScore(TraceGraph graph, CrossChainDeparture departure, ChainMovement arrival)
    {
        if (SameKey(departure.Payment.From, arrival.To))
        {
            return SameKeyScore;
        }

        return FromBridge(graph, arrival) ? FromBridgeScore : PlainAddressScore;
    }

    /// <summary>Says whether a payment was paid by an actor the trail ends at as a bridge or a swap.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="arrival">The payment.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    private static bool FromBridge(TraceGraph graph, ChainMovement arrival) =>
        arrival.From is { } payer
        && graph.NodeOf(payer)?.Ends.Any(end => end.Reason == TrailEndReason.OtherChain) == true;

    /// <summary>Gives a candidate of a payment: on Bitcoin with its coin, otherwise only as the payment.</summary>
    /// <param name="arrival">The payment.</param>
    /// <param name="score">The score.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The candidate.</returns>
    private static CheckCandidate Candidate(ChainMovement arrival, double score, string reason)
    {
        var coin = arrival.Coin;
        var address = coin is null ? null : BitcoinAddress.Parse(arrival.To.Value);
        var amount = coin is null ? (Satoshi?)null : new Satoshi((long)arrival.Amount.Units);
        return new CheckCandidate(arrival.Key, address, coin?.Txid, coin?.Index ?? 0, amount, score, reason)
        {
            Arrival = arrival,
        };
    }

    /// <summary>Writes why a payment is a candidate: its amount, time, value and what its address says.</summary>
    /// <param name="input">The input, with the values.</param>
    /// <param name="departure">The departure.</param>
    /// <param name="arrival">The payment out.</param>
    /// <param name="named">Whether the memo names its recipient.</param>
    /// <param name="addressScore">What its address counted.</param>
    /// <param name="after">The time after the payment in.</param>
    /// <returns>The reason, in Swedish.</returns>
    private static string Reason(
        CheckInput input,
        CrossChainDeparture departure,
        ChainMovement arrival,
        bool named,
        double addressScore,
        TimeSpan after)
    {
        var parts = new List<string>
        {
            $"{Amount(arrival.Amount)} på {arrival.Transaction.Chain} {Span(after)} efter",
            ValueText(input, departure.Payment, arrival),
        };
        if (named)
        {
            parts.Add($"mottagaren står i {departure.Memo}");
        }
        else if (addressScore == SameKeyScore)
        {
            parts.Add("samma nyckel som betalaren (samma 20 byte på båda kedjorna)");
        }
        else if (addressScore == FromBridgeScore)
        {
            parts.Add("betald av en brygga eller swap på den andra kedjan");
        }

        return string.Join("; ", parts) + ".";
    }

    /// <summary>Writes how the values compare, or that they cannot be compared.</summary>
    /// <param name="input">The input, with the values.</param>
    /// <param name="payment">The payment in.</param>
    /// <param name="arrival">The payment out.</param>
    /// <returns>For example <c>värd 10 140 SEK mot 10 300 SEK in (1,6 % trolig avgift)</c>.</returns>
    private static string ValueText(CheckInput input, ChainMovement payment, ChainMovement arrival)
    {
        var valueIn = input.CrossChain.ValueOf(payment);
        var valueOut = input.CrossChain.ValueOf(arrival);
        if (FeeOf(valueIn, valueOut) is not { } fee)
        {
            return "kursen saknas i ärendet, så beloppet är inte prövat";
        }

        var currency = input.CrossChain.Currency ?? string.Empty;
        var feeText = fee >= 0
            ? (fee * 100).ToString("0.#", Swedish) + " % trolig avgift"
            : (-fee * 100).ToString("0.#", Swedish) + " % mer, inom kursernas skillnad";
        return $"värd {Money(valueOut!.Value)} {currency} mot {Money(valueIn!.Value)} {currency} in ({feeText})";
    }

    /// <summary>
    /// Describes one departure as a suggestion with its candidates, or nothing when no payment on another chain fits.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="departure">The payment into the bridge or swap.</param>
    /// <param name="pool">Every payment known.</param>
    /// <returns>The suggestion, or <see langword="null"/>.</returns>
    private static CheckFinding? Describe(
        CheckInput input, TraceGraph graph, CrossChainDeparture departure, IReadOnlyList<ChainMovement> pool)
    {
        var candidates = Weigh(input, graph, departure, pool);
        if (candidates.Count == 0)
        {
            return null;
        }

        var payment = departure.Payment;
        var anchor = payment.Coin is not null ? BitcoinAddress.Parse(payment.To.Value) : null;
        var block = payment.Coin is { } spent && graph.Transactions.TryGetValue(spent.Txid, out var known)
            ? known.Transaction.Block
            : null;
        var onBitcoin = candidates.Where(candidate => candidate.Address is not null).ToList();
        var chains = string.Join(
            ", ", candidates.Select(candidate => candidate.Arrival!.Transaction.Chain.ToString()).Distinct());
        return new CheckFinding(
            payment.Key,
            CheckKind.Suggestion,
            AreaSort.OtherChain,
            "Fortsättning på annan kedja",
            $"{Amount(payment.Amount)} till {departure.Service} · {Count(candidates.Count)} på {chains}",
            Why(input, departure),
            anchor is null ? [] : [anchor, .. onBitcoin.Select(candidate => candidate.Address!).Distinct()],
            [],
            anchor is null
                ? []
                : [new CheckBasis(payment.Coin!.Txid, block), .. onBitcoin.Select(BasisOf).DistinctBy(b => b.Txid)],
            candidates)
        {
            Departure = payment,
        };
    }

    /// <summary>Gives a Bitcoin candidate's basis: its transaction and block, from the payment.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns>The basis.</returns>
    private static CheckBasis BasisOf(CheckCandidate candidate)
    {
        var arrival = candidate.Arrival!;
        var block = arrival is { Height: { } height, BlockHash: { } hash, Time: { } time }
            ? new BlockReference(height, BlockHash.Parse(hash), time)
            : null;
        return new CheckBasis(candidate.Txid!, block);
    }

    /// <summary>Writes why the suggestion is made and what it rests on, saying that it is a judgement.</summary>
    /// <param name="input">The input.</param>
    /// <param name="departure">The departure.</param>
    /// <returns>The explanation, in Swedish.</returns>
    private static string Why(CheckInput input, CrossChainDeparture departure)
    {
        var payment = departure.Payment;
        var lines = new List<string>
        {
            $"{Amount(payment.Amount)} gick till {departure.Service} i tx {payment.Transaction.Id}"
                + BlockText(payment) + ". Spåret tar slut där, eftersom pengarna fortsätter på en annan kedja.",
        };
        if (departure.Memo is { } memo)
        {
            lines.Add(memo.Address is null
                ? $"Betalningen har {memo}, men mottagaren är på {memo.ChainCode}, som Chainsaw inte har, eller är "
                    + "ingen giltig adress där."
                : $"Betalningen har {memo}: tjänstens egen uppgift om mottagaren på {memo.Chain}, läst ur kedjan. "
                    + "Betalningar till den kommer först.");
        }

        lines.Add(
            $"Kandidaterna är betalningar på andra kedjor inom {Window.TotalHours:0} timmar, där värdet efter en "
            + $"trolig avgift (högst {MostFee * 100:0} %) stämmer med det som gick in, räknat med ärendets kurser"
            + (input.CrossChain.Currency is { } currency ? $" i {currency}" : string.Empty)
            + ", eftersom tillgångarna skiljer sig. Samma nyckel på Ethereum och TRON, och en utbetalning från en "
            + "brygga eller swap, väger mer.");
        lines.AddRange(input.CrossChain.Targets
            .Where(target => target.State != CrossChainTargetState.Read)
            .Select(target => $"Mottagaren {target.Address} är inte uppslagen: "
                + (target.Problem ?? "den hämtas nästa gång kontrollerna körs med utgången ansluten.")));
        lines.Add(
            "Inget på kedjorna knyter betalningarna till varandra; det är en bedömning, aldrig ett faktum. Godkänner "
            + $"du en kandidat knyts pengarna till dess tx som ditt beslut, med {Identity} som underlag.");
        return string.Join(" ", lines);
    }

    /// <summary>Writes a payment's block, so that it can be checked against the chain.</summary>
    /// <param name="payment">The payment.</param>
    /// <returns>For example <c> (block 820 000, 0000…)</c>, or that it is unconfirmed.</returns>
    private static string BlockText(ChainMovement payment) => payment.Height is { } height
        ? $" (block {height.ToString(CultureInfo.InvariantCulture)}"
            + (payment.BlockHash is { } hash ? $", {hash})" : ")")
        : " (obekräftad)";

    /// <summary>Writes an amount with its symbol, in Swedish.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>For example <c>0,51200000 BTC</c>.</returns>
    private static string Amount(AssetAmount amount) =>
        amount.ToDecimalString().Replace('.', ',') + " " + amount.Asset.Symbol;

    /// <summary>Writes a value in the currency, rounded to whole units.</summary>
    /// <param name="value">The value.</param>
    /// <returns>For example <c>10 140</c>.</returns>
    private static string Money(decimal value) => Math.Round(value).ToString("#,##0", Swedish);

    /// <summary>Writes how many candidates there are.</summary>
    /// <param name="count">How many.</param>
    /// <returns>For example <c>1 kandidat</c> or <c>3 kandidater</c>.</returns>
    private static string Count(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " kandidat" : " kandidater");

    /// <summary>Writes a time span as short as a reason needs.</summary>
    /// <param name="span">The span.</param>
    /// <returns>For example <c>45 min</c>, <c>9 h</c> or <c>3 d</c>.</returns>
    private static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromHours(1))
        {
            return Math.Max(0, Math.Round(span.TotalMinutes)).ToString("0", CultureInfo.InvariantCulture) + " min";
        }

        return span < TimeSpan.FromHours(48)
            ? Math.Round(span.TotalHours).ToString("0", CultureInfo.InvariantCulture) + " h"
            : Math.Round(span.TotalDays).ToString("0", CultureInfo.InvariantCulture) + " d";
    }
}
