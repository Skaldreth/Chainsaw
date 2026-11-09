using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Reads a time pattern (CLAUDE.md, <em>Förvirringstekniker</em>): the hours of the day when an owner, as the
/// clustering counts it, spends, and, as a series of its own, when it receives, and the longest run of hours without,
/// which for a spender is likely when the owner sleeps and so hints at a time zone. The hours are read in Swedish time
/// (Europe/Stockholm, with summer time), so that a habit that follows the clock is not smeared by an hour when the
/// clocks change. A check with an area of the sort Time; a judgement, never a fact (principle 2), and a weak one,
/// since the time is the block's.
/// </summary>
/// <remarks>
/// A run of quiet hours is only a pattern when it is unlikely to be chance: the exact chance that as many transactions
/// at random hours leave as long a run is counted for each series, and since many series are tested at once, the
/// series kept are those the Benjamini–Hochberg procedure keeps at a false discovery rate of <see cref="MostChance"/>.
/// Chainsaw has no setting for the user's time zone; Swedish time is the one the investigation works in, and fixed, so
/// that the same case gives the same findings on every computer.
/// </remarks>
internal sealed class ActiveHoursCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "active-hours";

    /// <summary>How many confirmed transactions a series must have, for the hours to say anything.</summary>
    public const int MinimumTransactions = 20;

    /// <summary>The false discovery rate the series are kept at: one in a hundred.</summary>
    public const double MostChance = 0.01;

    /// <summary>How many hours in a row without a transaction a pattern needs.</summary>
    public const int MinimumQuietHours = 6;

    /// <summary>The local hour, with a half, that a night's sleep is taken to be centred on.</summary>
    public const double SleepCentre = 3.5;

    /// <summary>The hours of a day.</summary>
    private const int HoursPerDay = 24;

    /// <summary>The time zone the hours are read in.</summary>
    private readonly TimeZoneInfo _zone;

    /// <summary>What the time zone is called in the texts.</summary>
    private readonly string _zoneName;

    /// <summary>Creates the analysis, reading the hours in Swedish time.</summary>
    public ActiveHoursCheck()
        : this(Stockholm(), "svensk tid")
    {
    }

    /// <summary>Creates the analysis, reading the hours in a time zone.</summary>
    /// <param name="zone">The time zone.</param>
    /// <param name="zoneName">What the texts call it, for example <c>svensk tid</c>.</param>
    internal ActiveHoursCheck(TimeZoneInfo zone, string zoneName)
    {
        _zone = zone;
        _zoneName = zoneName;
    }

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 2);

    /// <inheritdoc/>
    public string DisplayName => "Tidsmönster";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var confirmed = input.Transactions.Where(transaction => transaction is { Block: not null, IsCoinbase: false })
            .ToList();
        var series = SeriesOf(confirmed, input, spends: true)
            .Concat(SeriesOf(confirmed, input, spends: false))
            .Where(each => each.Transactions.Count >= MinimumTransactions)
            .Select(each => each with { Quiet = Longest(Busy(each.Transactions)) })
            .Select(each => each with { Chance = Chance(each.Transactions.Count, each.Quiet.Length) })
            .ToList();
        var kept = Kept(series);
        return
        [
            .. kept.Select(each => Describe(each, series.Count))
                .OrderBy(finding => finding.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Gives the exact chance that a number of transactions at random hours (each hour as likely) leave a run of at
    /// least a number of quiet hours, around midnight too: the occupied hours' count is counted first, and given the
    /// count every set of hours is as likely.
    /// </summary>
    /// <param name="transactions">How many transactions.</param>
    /// <param name="quiet">The quiet run's length in hours.</param>
    /// <returns>The chance, from 0 to 1.</returns>
    internal static double Chance(int transactions, int quiet)
    {
        if (quiet <= 0)
        {
            return 1;
        }

        if (quiet >= HoursPerDay)
        {
            return transactions == 0 ? 1 : 0;
        }

        // The chance that k different hours are busy, after each transaction in turn.
        var busy = new double[HoursPerDay + 1];
        busy[0] = 1;
        for (var i = 0; i < transactions; i++)
        {
            for (var k = HoursPerDay; k >= 1; k--)
            {
                busy[k] = (busy[k] * k / HoursPerDay) + (busy[k - 1] * (HoursPerDay - k + 1) / HoursPerDay);
            }

            busy[0] = 0;
        }

        var chance = 0.0;
        for (var k = 1; k <= HoursPerDay; k++)
        {
            chance += busy[k] * (1 - (WithoutLongRun(k, quiet) / Choose(HoursPerDay, k)));
        }

        return Math.Clamp(chance, 0, 1);
    }

    /// <summary>
    /// Counts the sets of k busy hours on the day's circle whose every quiet run is shorter than a number of hours:
    /// each set with one busy hour picked is a place and the k gaps after each busy hour, so the count is the places
    /// times the gaps' compositions, over k.
    /// </summary>
    /// <param name="k">How many hours are busy, from 1.</param>
    /// <param name="quiet">The length no quiet run may reach.</param>
    /// <returns>The count.</returns>
    private static double WithoutLongRun(int k, int quiet) =>
        HoursPerDay * Compositions(HoursPerDay - k, k, quiet - 1) / k;

    /// <summary>Counts the ways to write a total as an ordered sum of parts, each from 0 to a most.</summary>
    /// <param name="total">The total.</param>
    /// <param name="parts">How many parts.</param>
    /// <param name="most">The largest part.</param>
    /// <returns>The count.</returns>
    private static double Compositions(int total, int parts, int most)
    {
        var ways = new double[total + 1];
        ways[0] = 1;
        for (var part = 0; part < parts; part++)
        {
            var next = new double[total + 1];
            for (var sum = 0; sum <= total; sum++)
            {
                for (var size = 0; size <= most && size <= sum; size++)
                {
                    next[sum] += ways[sum - size];
                }
            }

            ways = next;
        }

        return ways[total];
    }

    /// <summary>Counts the ways to choose k of n.</summary>
    /// <param name="n">How many to choose from.</param>
    /// <param name="k">How many to choose.</param>
    /// <returns>The count.</returns>
    private static double Choose(int n, int k)
    {
        var result = 1.0;
        for (var i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }

    /// <summary>
    /// Keeps the series with a quiet run of at least <see cref="MinimumQuietHours"/> that the Benjamini–Hochberg
    /// procedure keeps at a false discovery rate of <see cref="MostChance"/>, over every series tested.
    /// </summary>
    /// <param name="series">Every series tested.</param>
    /// <returns>The series kept.</returns>
    private static IReadOnlyList<HourSeries> Kept(IReadOnlyList<HourSeries> series)
    {
        var ranked = series.OrderBy(each => each.Chance).ThenBy(each => each.Key, StringComparer.Ordinal).ToList();
        var last = -1;
        for (var rank = 0; rank < ranked.Count; rank++)
        {
            if (ranked[rank].Chance <= (rank + 1) * MostChance / ranked.Count)
            {
                last = rank;
            }
        }

        return
        [
            .. ranked.Take(last + 1)
                .Where(each => each.Quiet.Length is >= MinimumQuietHours and < HoursPerDay),
        ];
    }

    /// <summary>Gives each owner's spends, or each owner's receipts, as a series.</summary>
    /// <param name="confirmed">The confirmed transactions.</param>
    /// <param name="input">The input, for the owners.</param>
    /// <param name="spends">Whether the series are spends; else receipts from others.</param>
    /// <returns>The series, without their runs yet.</returns>
    private static IEnumerable<HourSeries> SeriesOf(
        IReadOnlyList<BitcoinTransaction> confirmed, CheckInput input, bool spends)
    {
        var entries = new List<(BitcoinAddress Owner, BitcoinAddress Address, BitcoinTransaction Transaction)>();
        foreach (var transaction in confirmed)
        {
            var payers = transaction.InputAddresses().Select(input.OwnerOf).ToHashSet();
            IReadOnlyList<BitcoinAddress> addresses = spends
                ? transaction.InputAddresses()
                : [.. transaction.PayingOutputs().Select(output => output.Address!)
                    .Where(address => !payers.Contains(input.OwnerOf(address)))];
            entries.AddRange(addresses.Select(address => (input.OwnerOf(address), address, transaction)));
        }

        foreach (var owner in entries.GroupBy(entry => entry.Owner))
        {
            var list = owner.DistinctBy(entry => entry.Transaction.Txid)
                .OrderBy(entry => entry.Transaction.Block!.Height)
                .ThenBy(entry => entry.Transaction.Txid.Value, StringComparer.Ordinal)
                .ToList();
            var addresses = owner.Select(entry => entry.Address).Distinct().ToList();
            var key = (spends ? "hours:" : "hours-in:") + list[0].Address.Value;
            yield return new HourSeries(key, spends, [.. list.Select(entry => entry.Transaction)], addresses);
        }
    }

    /// <summary>Marks the hours of the day, in the analysis's time zone, that had a transaction.</summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>Whether each hour was busy.</returns>
    private bool[] Busy(IReadOnlyList<BitcoinTransaction> transactions)
    {
        var busy = new bool[HoursPerDay];
        foreach (var transaction in transactions)
        {
            busy[LocalHour(transaction)] = true;
        }

        return busy;
    }

    /// <summary>Gives a transaction's block's hour in the analysis's time zone.</summary>
    /// <param name="transaction">The confirmed transaction.</param>
    /// <returns>The hour, from 0.</returns>
    private int LocalHour(BitcoinTransaction transaction) =>
        TimeZoneInfo.ConvertTime(transaction.Block!.Time, _zone).Hour;

    /// <summary>Finds the longest run of hours that are not busy, around midnight too.</summary>
    /// <param name="busy">Whether each hour of the day had a transaction.</param>
    /// <returns>The run's first hour and length.</returns>
    private static (int Start, int Length) Longest(bool[] busy)
    {
        var best = (Start: 0, Length: 0);
        for (var start = 0; start < HoursPerDay; start++)
        {
            var length = 0;
            while (length < HoursPerDay && !busy[(start + length) % HoursPerDay])
            {
                length++;
            }

            if (length > best.Length)
            {
                best = (start, length);
            }
        }

        return best;
    }

    /// <summary>Describes a series's hours.</summary>
    /// <param name="series">The series.</param>
    /// <param name="tested">How many series were tested at once.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(HourSeries series, int tested)
    {
        var quiet = series.Quiet;
        var activeFrom = (quiet.Start + quiet.Length) % HoursPerDay;
        var activeTo = (quiet.Start + HoursPerDay - 1) % HoursPerDay;
        var hours = $"{Hour(activeFrom)}–{Hour(activeTo + 1)}";
        var count = CheckText.Count(series.Transactions.Count);
        var quietHours = CheckText.Count(quiet.Length);
        var chance = $"Att slumpen ger så lång tystnad med lika många tx händer i {Share(series.Chance)} av fallen, "
            + $"och mönstret står kvar också när hänsyn tas till att {CheckText.Count(tested)} serier prövades "
            + $"samtidigt (Benjamini–Hochberg, högst {CheckText.Percent(MostChance)} falska fynd).";
        string why;
        string title;
        string summary;
        if (series.IsSpending)
        {
            var zone = ZoneHint(quiet);
            why = $"Analysen {this.Named()} ser att ägaren, som klustringen räknar den, spenderade i {count} tx bara "
                + $"mellan kl. {hours} {_zoneName}, och aldrig under {quietHours} timmar i rad. Är det när ägaren "
                + $"sover pekar det på en tidszon runt {zone}. {chance} Det är en svag bedömning, inte ett faktum: "
                + "tiden är blockets, som kommer minuter till timmar efter att tx:en skickades, en tjänst eller ett "
                + "skript kan skicka åt ägaren, och klustret kan ha fler ägare.";
            title = $"Aktiv kl. {hours} {_zoneName}";
            summary = $"{count} tx, tyst {quietHours} h i rad, trolig tidszon {zone}";
        }
        else
        {
            why = $"Analysen {this.Named()} ser att ägaren, som klustringen räknar den, tog emot betalningar från "
                + $"andra i {count} tx bara mellan kl. {hours} {_zoneName}, och aldrig under {quietHours} timmar i "
                + "rad. Det säger när de som betalar ägaren är aktiva, till exempel offer på samma ort, eller när "
                + $"ägaren tar ut pengar till sig själv från en tjänst. {chance} Det är en svag bedömning, inte ett "
                + "faktum: tiden är blockets, och avsändarna kan vara många och olika.";
            title = $"Tar emot kl. {hours} {_zoneName}";
            summary = $"{count} tx in, tyst {quietHours} h i rad";
        }

        return new CheckFinding(
            series.Key,
            CheckKind.Check,
            AreaSort.Time,
            title,
            summary,
            why,
            [.. series.Addresses],
            [],
            [.. series.Transactions.Select(CheckBasis.Of)],
            []);
    }

    /// <summary>
    /// Gives the time zone a quiet run hints at, if it is when the owner sleeps: the analysis's zone's offset without
    /// summer time, moved by how far the run's centre lies from <see cref="SleepCentre"/>.
    /// </summary>
    /// <param name="quiet">The quiet run, in the analysis's time zone.</param>
    /// <returns>For example <c>UTC+1</c>.</returns>
    private string ZoneHint((int Start, int Length) quiet)
    {
        var centre = quiet.Start + (quiet.Length / 2.0);
        var offset = (int)Math.Round(SleepCentre - centre) + (int)Math.Round(_zone.BaseUtcOffset.TotalHours);
        offset = ((offset + 11) % HoursPerDay + HoursPerDay) % HoursPerDay - 11;
        return "UTC" + (offset >= 0 ? "+" : "−") + Math.Abs(offset).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Writes a small chance as a percentage, or <c>under 0,1 %</c>.</summary>
    /// <param name="chance">The chance.</param>
    /// <returns>The text.</returns>
    private static string Share(double chance) => chance < 0.001 ? "under 0,1 %" : CheckText.Percent(chance);

    /// <summary>Writes an hour of the day as two digits.</summary>
    /// <param name="hour">The hour, from 0; 24 is written as 24.</param>
    /// <returns>For example <c>07</c>.</returns>
    private static string Hour(int hour) => hour.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>Gives Swedish time, with summer time; the name differs between Windows and other systems.</summary>
    /// <returns>The time zone.</returns>
    private static TimeZoneInfo Stockholm()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }

    /// <summary>One owner's spends or receipts, with their quiet run and its chance.</summary>
    /// <param name="Key">The finding's key: <c>hours:</c> or <c>hours-in:</c> and the series's first address.</param>
    /// <param name="IsSpending">Whether the series is spends; else receipts from others.</param>
    /// <param name="Transactions">The transactions, in block order.</param>
    /// <param name="Addresses">The owner's addresses that spent or received in them.</param>
    private sealed record HourSeries(
        string Key, bool IsSpending, IReadOnlyList<BitcoinTransaction> Transactions,
        IReadOnlyList<BitcoinAddress> Addresses)
    {
        /// <summary>Gets the longest quiet run, in the analysis's time zone.</summary>
        public (int Start, int Length) Quiet { get; init; }

        /// <summary>Gets the chance that random hours leave as long a run.</summary>
        public double Chance { get; init; } = 1;
    }
}
