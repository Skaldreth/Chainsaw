using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Suggests a nested service (CLAUDE.md, <em>Förvirring analyseras</em>): an owner without a label, as the clustering
/// counts it, that takes payments from many senders and pays many recipients of its own, and that again and again
/// pays into an exchange that a label names and gets withdrawals back from it, as a small exchange, an OTC desk or a
/// payment service does through its account at a large exchange. A request to the large exchange then gives the
/// service, not the customer. A suggestion, drawn dashed, with an area of the sort Service and no candidates: the user
/// approves or dismisses it, and it stays a judgement either way (principle 2).
/// </summary>
/// <remarks>
/// An ordinary customer of an exchange pays in and takes out, but has few senders and recipients; a scammer collects
/// from many but does not take money back from the exchange to pay many; a start point's or a victim's payments are
/// never counted as customers'.
/// </remarks>
internal sealed class NestedServiceCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "nested-service";

    /// <summary>How many independent senders (owners other than exchanges) must have paid the service.</summary>
    public const int MinimumCustomers = 8;

    /// <summary>How many different recipients (owners other than exchanges) the service must have paid.</summary>
    public const int MinimumPayees = 8;

    /// <summary>How many times the service must have paid into the exchange, and taken money back from it.</summary>
    public const int MinimumTrips = 2;

    /// <summary>
    /// The least share of what the customers paid in that must go on into the exchange, and of what the service paid
    /// out that must have come back from it: a nested service runs its business through the account, while an active
    /// wallet that also uses an exchange sends it only a part (random graphs gave many before this rule).
    /// </summary>
    public const double MinimumExchangeShare = 0.5;

    /// <summary>How long the service's business must be spread over.</summary>
    public static readonly TimeSpan MinimumSpread = TimeSpan.FromDays(1);

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Nästlad tjänst";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var exchanges = new Dictionary<BitcoinAddress, KnownService>();
        foreach (var exchange in input.Services.Where(service => service.IsExchange))
        {
            foreach (var address in exchange.Addresses)
            {
                exchanges.TryAdd(input.OwnerOf(address), exchange);
            }
        }

        if (exchanges.Count == 0)
        {
            return [];
        }

        var excluded = input.Mixers.SelectMany(mixer => mixer.Addresses)
            .Concat(input.Services.SelectMany(service => service.Addresses))
            .Concat(input.Protected)
            .Select(input.OwnerOf)
            .ToHashSet();
        var protectedOwners = input.Protected.Select(input.OwnerOf).ToHashSet();
        var business = new Dictionary<BitcoinAddress, Business>();
        var coinJoins = input.CoinJoins();
        var confirmed = input.Transactions
            .Where(t => t is { Block: not null, IsCoinbase: false } && !coinJoins.ContainsKey(t.Txid))
            .OrderBy(t => t.Block!.Height)
            .ThenBy(t => t.Txid.Value, StringComparer.Ordinal);
        foreach (var transaction in confirmed)
        {
            Read(transaction, input, exchanges, excluded, protectedOwners, business);
        }

        return
        [
            .. business.Where(entry => entry.Value.IsNested())
                .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
                .Select(entry => Describe(entry.Key, entry.Value)),
        ];
    }

    /// <summary>Reads one transaction into the business of the owners it touches.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="input">The input, for the owners.</param>
    /// <param name="exchanges">The labelled exchanges, by owner.</param>
    /// <param name="excluded">The owners never read as a service: labelled ones, start points and victims.</param>
    /// <param name="protectedOwners">The start points' and victims' owners, never counted as customers.</param>
    /// <param name="business">The business read so far, by owner.</param>
    private static void Read(
        BitcoinTransaction transaction,
        CheckInput input,
        IReadOnlyDictionary<BitcoinAddress, KnownService> exchanges,
        IReadOnlySet<BitcoinAddress> excluded,
        IReadOnlySet<BitcoinAddress> protectedOwners,
        Dictionary<BitcoinAddress, Business> business)
    {
        var payers = transaction.InputAddresses().Select(input.OwnerOf).Distinct().ToList();
        if (payers.Count == 0)
        {
            return;
        }

        var paid = transaction.PayingOutputs()
            .Select(output => (Output: output, Owner: input.OwnerOf(output.Address!)))
            .Where(entry => !payers.Contains(entry.Owner))
            .ToList();
        if (payers.Count == 1 && !excluded.Contains(payers[0]))
        {
            var service = Of(business, payers[0]);
            foreach (var (output, owner) in paid)
            {
                if (exchanges.TryGetValue(owner, out var exchange))
                {
                    service.ToExchange.Add((transaction, output, exchange));
                }
                else
                {
                    service.Payees.Add(owner);
                    service.PaidOut += output.Amount.Value;
                    service.Dealings.Add(transaction);
                }
            }
        }

        var fromExchange = payers.Count == 1 && exchanges.TryGetValue(payers[0], out var payingExchange)
            ? payingExchange
            : null;
        var customer = payers.All(payer => !exchanges.ContainsKey(payer) && !protectedOwners.Contains(payer));
        foreach (var (output, owner) in paid.Where(entry => !excluded.Contains(entry.Owner)))
        {
            var service = Of(business, owner);
            if (fromExchange is not null)
            {
                service.FromExchange.Add((transaction, output, fromExchange));
            }
            else if (customer)
            {
                service.Customers.Add(payers[0]);
                service.PaidIn += output.Amount.Value;
                service.Dealings.Add(transaction);
            }
        }
    }

    /// <summary>Gives an owner's business, making it when new.</summary>
    /// <param name="business">The business by owner.</param>
    /// <param name="owner">The owner.</param>
    /// <returns>Its business.</returns>
    private static Business Of(Dictionary<BitcoinAddress, Business> business, BitcoinAddress owner)
    {
        if (!business.TryGetValue(owner, out var found))
        {
            business[owner] = found = new Business();
        }

        return found;
    }

    /// <summary>Describes a likely nested service.</summary>
    /// <param name="owner">The service's owner key.</param>
    /// <param name="business">Its business.</param>
    /// <returns>The suggestion.</returns>
    private CheckFinding Describe(BitcoinAddress owner, Business business)
    {
        var exchange = business.Exchange!;
        var name = exchange.Name ?? "börsen";
        var deposits = business.ToExchange.Where(trip => trip.Exchange == exchange).ToList();
        var withdrawals = business.FromExchange.Where(trip => trip.Exchange == exchange).ToList();
        var accounts = deposits.Select(trip => trip.Output.Address!).Distinct().ToList();
        var (customers, payees) = (CheckText.Count(business.Customers.Count), CheckText.Count(business.Payees.Count));
        var (into, back) = (CheckText.Count(deposits.Count), CheckText.Count(withdrawals.Count));
        var why = $"Analysen {this.Named()} ser en ägare utan etikett, som klustringen räknar den, som tar emot "
            + $"betalningar från {customers} olika avsändare och betalar {payees} olika mottagare, och som {into} "
            + $"gånger betalar in till {name} (till {CheckText.Count(accounts.Count)} insättningsadress(er)) och "
            + $"{back} gånger får uttag tillbaka därifrån; minst hälften av det kunderna betalade går vidare dit, och "
            + "minst hälften av det som betalas ut kom därifrån. Så arbetar en nästlad tjänst: en mindre växlare, en "
            + "OTC-handlare eller en betaltjänst som sköter sina kunders pengar genom ett konto hos en större börs. "
            + $"En begäran till {name} ger då tjänsten, inte kunden; be också om tjänstens egna uppgifter om "
            + "insättningen. Godkänner du förslaget står det som din bedömning; sätt gärna etiketten Nästlad tjänst "
            + "med L. Det är en bedömning, inte ett faktum: en handlare med många kunder som växlar själv på börsen "
            + "ser likadan ut, och klustret kan ha fler ägare.";
        var addresses = business.Addresses(owner).Concat(accounts).Distinct();
        var basis = deposits.Select(trip => trip.Transaction)
            .Concat(withdrawals.Select(trip => trip.Transaction))
            .Concat(business.Dealings)
            .DistinctBy(transaction => transaction.Txid)
            .Select(CheckBasis.Of);
        return new CheckFinding(
            "nested:" + owner.Value,
            CheckKind.Suggestion,
            AreaSort.Service,
            $"Trolig nästlad tjänst hos {name}",
            $"{customers} avsändare, {payees} mottagare · {into} insättningar, {back} uttag",
            why,
            [.. addresses],
            [],
            [.. basis],
            []);
    }

    /// <summary>What one owner did: its customers and payees, and its trips to and from the exchanges.</summary>
    private sealed class Business
    {
        /// <summary>Gets the senders (owners) that paid it.</summary>
        public HashSet<BitcoinAddress> Customers { get; } = [];

        /// <summary>Gets the recipients (owners) it paid, other than exchanges.</summary>
        public HashSet<BitcoinAddress> Payees { get; } = [];

        /// <summary>Gets or sets what its customers paid it in all, in satoshi.</summary>
        public long PaidIn { get; set; }

        /// <summary>Gets or sets what it paid its payees in all, in satoshi.</summary>
        public long PaidOut { get; set; }

        /// <summary>Gets the transactions with its customers and payees.</summary>
        public List<BitcoinTransaction> Dealings { get; } = [];

        /// <summary>Gets its payments into an exchange.</summary>
        public List<(BitcoinTransaction Transaction, BitcoinOutput Output, KnownService Exchange)> ToExchange { get; } =
            [];

        /// <summary>Gets the withdrawals an exchange paid it.</summary>
        public List<(BitcoinTransaction Transaction, BitcoinOutput Output, KnownService Exchange)> FromExchange
        {
            get;
        } = [];

        /// <summary>
        /// Gets the exchange it both paid into and took money back from at least <see cref="MinimumTrips"/> times
        /// each, with at least <see cref="MinimumExchangeShare"/> of its business each way, the one with the most
        /// trips; <see langword="null"/> when none.
        /// </summary>
        public KnownService? Exchange =>
            ToExchange.Select(trip => trip.Exchange).Distinct()
                .Select(exchange => (Exchange: exchange,
                    Into: ToExchange.Where(trip => trip.Exchange == exchange).ToList(),
                    Back: FromExchange.Where(trip => trip.Exchange == exchange).ToList()))
                .Where(trips => trips.Into.Count >= MinimumTrips && trips.Back.Count >= MinimumTrips
                    && Sum(trips.Into) >= PaidIn * MinimumExchangeShare
                    && Sum(trips.Back) >= PaidOut * MinimumExchangeShare)
                .OrderByDescending(trips => trips.Into.Count + trips.Back.Count)
                .Select(trips => trips.Exchange)
                .FirstOrDefault();

        /// <summary>
        /// Says whether the business looks like a nested service: many customers and payees, trips both ways to one
        /// exchange, over at least <see cref="MinimumSpread"/>.
        /// </summary>
        /// <returns><see langword="true"/> when it does.</returns>
        public bool IsNested()
        {
            if (Customers.Count < MinimumCustomers || Payees.Count < MinimumPayees || Exchange is null)
            {
                return false;
            }

            var times = Dealings.Select(transaction => transaction.Block!.Time).ToList();
            return times.Max() - times.Min() >= MinimumSpread;
        }

        /// <summary>Sums the outputs of trips to or from an exchange.</summary>
        /// <param name="trips">The trips.</param>
        /// <returns>The sum, in satoshi.</returns>
        private static long Sum(
            IEnumerable<(BitcoinTransaction Transaction, BitcoinOutput Output, KnownService Exchange)> trips) =>
            trips.Sum(trip => trip.Output.Amount.Value);

        /// <summary>Gives the service's own addresses in the transactions read.</summary>
        /// <param name="owner">The service's owner key.</param>
        /// <returns>The addresses, in the order met.</returns>
        public IEnumerable<BitcoinAddress> Addresses(BitcoinAddress owner) =>
            ToExchange.SelectMany(trip => trip.Transaction.InputAddresses())
                .Concat(FromExchange.Select(trip => trip.Output.Address!))
                .Prepend(owner)
                .Distinct();
    }
}
