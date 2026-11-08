using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Reads a time pattern (CLAUDE.md, <em>Förvirringstekniker</em>): the hours of the day when an owner, as the
/// clustering counts it, spends, and the longest run of hours without, which is likely when the owner sleeps and so
/// hints at a time zone. A check with an area of the sort Time; a judgement, never a fact (principle 2), and a weak
/// one, since the time is the block's.
/// </summary>
internal sealed class ActiveHoursCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "active-hours";

    /// <summary>How many confirmed transactions an owner must have spent in, for the hours to say anything.</summary>
    public const int MinimumTransactions = 8;

    /// <summary>How many hours in a row without a transaction a pattern needs.</summary>
    public const int MinimumQuietHours = 6;

    /// <summary>The local hour, with a half, that a night's sleep is taken to be centred on.</summary>
    public const double SleepCentre = 3.5;

    /// <summary>The hours of a day.</summary>
    private const int HoursPerDay = 24;

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Tidsmönster";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var byOwner = input.Transactions
            .Where(transaction => transaction is { Block: not null, IsCoinbase: false })
            .SelectMany(transaction => transaction.InputAddresses()
                .Select(input.OwnerOf)
                .Distinct()
                .Select(owner => (Owner: owner, Transaction: transaction)))
            .GroupBy(entry => entry.Owner, entry => entry.Transaction);
        var findings = new List<CheckFinding>();
        foreach (var owner in byOwner)
        {
            var spent = owner.DistinctBy(transaction => transaction.Txid).ToList();
            if (spent.Count >= MinimumTransactions && QuietRun(spent) is { } quiet)
            {
                findings.Add(Describe(owner.Key, spent, quiet, input));
            }
        }

        return [.. findings.OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>Finds the longest run of hours (UTC, around midnight too) in which no transaction was made.</summary>
    /// <param name="spent">The owner's transactions.</param>
    /// <returns>
    /// The first quiet hour and how many, or <see langword="null"/> when no run is at least
    /// <see cref="MinimumQuietHours"/>.
    /// </returns>
    private static (int Start, int Length)? QuietRun(IReadOnlyList<BitcoinTransaction> spent)
    {
        var busy = new bool[HoursPerDay];
        foreach (var transaction in spent)
        {
            busy[transaction.Block!.Time.UtcDateTime.Hour] = true;
        }

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

        return best.Length is >= MinimumQuietHours and < HoursPerDay ? best : null;
    }

    /// <summary>Describes an owner's hours.</summary>
    /// <param name="owner">The owner key.</param>
    /// <param name="spent">The owner's transactions.</param>
    /// <param name="quiet">The longest quiet run.</param>
    /// <param name="input">The input, for the owner's addresses.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(
        BitcoinAddress owner, IReadOnlyList<BitcoinTransaction> spent, (int Start, int Length) quiet, CheckInput input)
    {
        var activeFrom = (quiet.Start + quiet.Length) % HoursPerDay;
        var activeTo = (quiet.Start + HoursPerDay - 1) % HoursPerDay;
        var centre = quiet.Start + (quiet.Length / 2.0);
        var offset = (int)Math.Round(SleepCentre - centre);
        offset = ((offset + 11) % HoursPerDay + HoursPerDay) % HoursPerDay - 11;
        var zone = "UTC" + (offset >= 0 ? "+" : "−") + Math.Abs(offset).ToString(CultureInfo.InvariantCulture);
        var hours = $"{Hour(activeFrom)}–{Hour(activeTo + 1)}";
        var count = CheckText.Count(spent.Count);
        var why = $"Analysen {this.Named()} ser att ägaren, som klustringen räknar den, spenderade i {count} tx bara "
            + $"mellan kl. {hours} UTC, och aldrig under {CheckText.Count(quiet.Length)} timmar i rad. Är det när "
            + $"ägaren sover pekar det på en tidszon runt {zone}. Det är en svag bedömning, inte ett faktum: tiden är "
            + "blockets, som kommer minuter till timmar efter att tx:en skickades, en tjänst eller ett skript kan "
            + "skicka åt ägaren, och klustret kan ha fler ägare.";
        var addresses = spent.SelectMany(transaction => transaction.InputAddresses())
            .Where(address => input.OwnerOf(address) == owner)
            .Distinct();
        return new CheckFinding(
            "hours:" + owner.Value,
            CheckKind.Check,
            AreaSort.Time,
            $"Aktiv kl. {hours} UTC",
            $"{count} tx, tyst {CheckText.Count(quiet.Length)} h i rad, trolig tidszon {zone}",
            why,
            [.. addresses],
            [],
            [.. spent.Select(CheckBasis.Of)],
            []);
    }

    /// <summary>Writes an hour of the day as two digits.</summary>
    /// <param name="hour">The hour, from 0; 24 is written as 24.</param>
    /// <returns>For example <c>07</c>.</returns>
    private static string Hour(int hour) => hour.ToString("00", CultureInfo.InvariantCulture);
}
