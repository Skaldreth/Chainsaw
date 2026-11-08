using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads an address's history for what its expansions held back (sketch EX1): newest first up to
/// <see cref="HeldBackList.Most"/>, and, when that does not reach the traced arrival, back again from a transaction of
/// the graph's just after it, so that the time round it is covered. Every page goes through the source as an
/// expansion's does, so it is sealed in the case (principle 1) and read from there offline (principle 9); only the
/// address and a transaction's id are ever sent (principle 4).
/// </summary>
/// <param name="source">Bitcoin's facts.</param>
internal sealed class HeldBackHistory(IBitcoinSource source)
{
    /// <summary>How a run of pages ended.</summary>
    private enum RunEnd
    {
        /// <summary>The history has no more pages.</summary>
        Whole,

        /// <summary>The run read as many transactions as it may, and there are more.</summary>
        Full,

        /// <summary>The run reached back past the time it was to cover.</summary>
        Reached,

        /// <summary>A page could not be had, or the source gave the same page again.</summary>
        Broken,
    }

    /// <summary>Reads what was held back at an address.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="around">A time the read must also cover, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What was held back, newest first.</returns>
    public async Task<HeldBackList> ReadAsync(
        ICase @case, BitcoinAddress address, DateTimeOffset? around, CancellationToken cancellationToken)
    {
        var state = @case.Read(GraphStateStore.Read);
        var reading = new Reading(address, state.Transactions.ToHashSet());
        var end = await RunAsync(@case, reading, null, HeldBackList.Most, null, cancellationToken)
            .ConfigureAwait(false);
        if (end != RunEnd.Full)
        {
            return reading.List(end == RunEnd.Whole);
        }

        reading.Say(string.Format(
            CultureInfo.InvariantCulture,
            "Adressens historik är längre än {0} tx; de nyaste {0} lästes.",
            HeldBackList.Most));
        if (around is { } at && reading.Oldest > at - HeldBackList.AroundArrival)
        {
            await ReadAroundAsync(@case, reading, state, at, cancellationToken).ConfigureAwait(false);
        }

        return reading.List(complete: false);
    }

    /// <summary>
    /// Reads the history back from a transaction of the graph's at the address near the time round the arrival, down
    /// past its start, and says that it did; nothing when the graph has no transaction there to start from.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="reading">What has been read.</param>
    /// <param name="state">The graph's state.</param>
    /// <param name="around">The time to cover.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when it is read.</returns>
    private async Task ReadAroundAsync(
        ICase @case, Reading reading, GraphState state, DateTimeOffset around, CancellationToken cancellationToken)
    {
        var seed = await SeedAsync(@case, reading.Address, state, around, reading.Oldest, cancellationToken)
            .ConfigureAwait(false);
        if (seed is null)
        {
            reading.Say("Tiden runt den spårade ankomsten nåddes inte.");
            return;
        }

        var end = await RunAsync(
                @case,
                reading,
                seed,
                HeldBackList.MostAroundArrival,
                around - HeldBackList.AroundArrival,
                cancellationToken)
            .ConfigureAwait(false);
        if (end != RunEnd.Broken)
        {
            reading.Say("Tiden runt den spårade ankomsten lästes också.");
        }
    }

    /// <summary>
    /// Gives where to read back from round a time: among the graph's confirmed transactions at the address that are
    /// not newer than what was read already (reading back from a newer one would only read it again), the first
    /// after the time's window, else the last inside it; read from the case, sending nothing.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="state">The graph's state.</param>
    /// <param name="around">The time.</param>
    /// <param name="covered">The oldest time already read.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The transaction, or <see langword="null"/> when the graph has none there.</returns>
    private async Task<Txid?> SeedAsync(
        ICase @case,
        BitcoinAddress address,
        GraphState state,
        DateTimeOffset around,
        DateTimeOffset covered,
        CancellationToken cancellationToken)
    {
        var from = around - HeldBackList.AroundArrival;
        var to = around + HeldBackList.AroundArrival;
        var times = new List<(Txid Txid, DateTimeOffset Time)>();
        foreach (var txid in state.Transactions)
        {
            var lookup = await source.GetTransactionAsync(@case, txid, LookupMode.CaseOnly, cancellationToken)
                .ConfigureAwait(false);
            if (lookup.Value is { Block.Time: var time } transaction
                && time >= from
                && time <= covered
                && HeldBackReader.Read(transaction, address) is not null)
            {
                times.Add((txid, time));
            }
        }

        // The first after the window covers it whole; without one, the last inside it covers the most of it.
        var past = times.Where(entry => entry.Time >= to).ToList();
        if (past.Count > 0)
        {
            return past.MinBy(entry => entry.Time).Txid;
        }

        return times.Count == 0 ? null : times.MaxBy(entry => entry.Time).Txid;
    }

    /// <summary>Reads pages of the history from a point, keeping what the graph does not hold.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="reading">What has been read, added to.</param>
    /// <param name="after">The transaction to read back from, or <see langword="null"/> for the newest.</param>
    /// <param name="most">The most new transactions the run may read.</param>
    /// <param name="stopBefore">A time the run may stop once it has read past, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>How the run ended.</returns>
    private async Task<RunEnd> RunAsync(
        ICase @case,
        Reading reading,
        Txid? after,
        int most,
        DateTimeOffset? stopBefore,
        CancellationToken cancellationToken)
    {
        var count = 0;
        var inRun = new HashSet<Txid>();
        while (true)
        {
            var page = await source.GetAddressTransactionsAsync(@case, reading.Address, after, cancellationToken)
                .ConfigureAwait(false);
            if (page.Value is null)
            {
                reading.Say(Lookups.Problem(page.Status, page.Problem));
                return RunEnd.Broken;
            }

            reading.Say(Lookups.Outdated(page));
            var moved = false;
            foreach (var transaction in page.Value.Transactions)
            {
                // A run from near the arrival may cross pages read before; it moves on as long as its own pages do.
                moved |= inRun.Add(transaction.Txid);
                if (reading.Has(transaction.Txid))
                {
                    continue;
                }

                if (count == most)
                {
                    return RunEnd.Full;
                }

                reading.Add(transaction);
                count++;
            }

            if (page.Value.NextAfter is null)
            {
                return RunEnd.Whole;
            }

            if (stopBefore is { } stop && page.Value.Transactions.Any(tx => tx.Block?.Time < stop))
            {
                return RunEnd.Reached;
            }

            if (page.Value.NextAfter == after || !moved)
            {
                reading.Say("Källan gav samma sida igen; historiken lästes så långt den gick.");
                return RunEnd.Broken;
            }

            after = page.Value.NextAfter;
        }
    }

    /// <summary>What has been read of an address's history so far, and what the user should know about it.</summary>
    /// <param name="address">The address.</param>
    /// <param name="inGraph">The transactions the graph holds, which were not held back.</param>
    private sealed class Reading(BitcoinAddress address, HashSet<Txid> inGraph)
    {
        /// <summary>The transactions read.</summary>
        private readonly HashSet<Txid> _seen = [];

        /// <summary>What was held back.</summary>
        private readonly List<HeldBackTransaction> _held = [];

        /// <summary>What the user should know, joined.</summary>
        private string? _problem;

        /// <summary>Gets the address.</summary>
        public BitcoinAddress Address => address;

        /// <summary>Gets the oldest confirmation time read, or the latest possible before any.</summary>
        public DateTimeOffset Oldest { get; private set; } = DateTimeOffset.MaxValue;

        /// <summary>Says whether a transaction has been read.</summary>
        /// <param name="txid">The transaction.</param>
        /// <returns><see langword="true"/> when it has.</returns>
        public bool Has(Txid txid) => _seen.Contains(txid);

        /// <summary>Adds a transaction read: kept when the graph does not hold it and it touches the address.</summary>
        /// <param name="transaction">The transaction.</param>
        public void Add(BitcoinTransaction transaction)
        {
            _seen.Add(transaction.Txid);
            if (transaction.Block?.Time is { } time && time < Oldest)
            {
                Oldest = time;
            }

            if (!inGraph.Contains(transaction.Txid) && HeldBackReader.Read(transaction, address) is { } held)
            {
                _held.Add(held);
            }
        }

        /// <summary>Adds what the user should know, once.</summary>
        /// <param name="text">The text, or <see langword="null"/>.</param>
        public void Say(string? text) => _problem = Lookups.Join(_problem, text);

        /// <summary>Gives the list, newest first and the unconfirmed first of all.</summary>
        /// <param name="complete">Whether the whole history was read.</param>
        /// <returns>The list.</returns>
        public HeldBackList List(bool complete) => new(
            address,
            [.. _held.OrderByDescending(held => held.Time ?? DateTimeOffset.MaxValue)],
            complete,
            _problem);
    }
}
