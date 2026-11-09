using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit's patterns for the fifth round: a central mixer without a label, an exchange that is not one, a nested
/// service, and ordinary random graphs to measure the false alarms on. Every address is synthetic.
/// </summary>
internal static class Behaviour
{
    /// <summary>The first block of the patterns; a block is ten minutes.</summary>
    public const long Height = 900_000;

    /// <summary>
    /// A central mixer: each user pays a deposit address of its own, and a little later the pool pays a new address
    /// the deposit minus three percent, from other coins. The deposit addresses and the pool are one owner.
    /// </summary>
    /// <param name="label">The mixer's label, for its addresses.</param>
    /// <param name="users">How many users.</param>
    /// <param name="start">The first deposit's block.</param>
    /// <returns>The transactions and each address's owner.</returns>
    public static (List<BitcoinTransaction> Txs, Dictionary<BitcoinAddress, BitcoinAddress> Owners) CentralMixer(
        string label, int users, long start = Height)
    {
        var pool = Pool(label);
        var txs = new List<BitcoinTransaction>();
        var owners = new Dictionary<BitcoinAddress, BitcoinAddress> { [pool] = pool };
        for (var i = 0; i < users; i++)
        {
            var amount = 2_000_000 + (i * 1_100_000);
            var deposit = Wpkh($"{label}-deposit-{i}");
            owners[deposit] = pool;
            txs.Add(Tx($"{label}-in-{i}", start + (i * 6))
                .In(Wpkh($"{label}-user-{i}"), amount + 30_000)
                .Out(deposit, amount)
                .Build());
            txs.Add(Tx($"{label}-out-{i}", start + (i * 6) + 3)
                .In(pool, 90_000_000, $"{label}-pool-coin-{i}")
                .Out(Wpkh($"{label}-paid-{i}"), amount * 97 / 100)
                .Out(pool, 90_000_000 - (amount * 97 / 100) - 10_000)
                .Build());
        }

        return (txs, owners);
    }

    /// <summary>A mixer's pool address.</summary>
    /// <param name="label">The mixer's label.</param>
    /// <returns>The address.</returns>
    public static BitcoinAddress Pool(string label) => Pkh(label + "-pool");

    /// <summary>
    /// An exchange without a label: customers pay deposit addresses of their own, and the hot wallet pays other
    /// customers withdrawals in amounts that have nothing to do with the deposits.
    /// </summary>
    /// <param name="label">The exchange's label, for its addresses.</param>
    /// <param name="customers">How many customers.</param>
    /// <returns>The transactions and each address's owner.</returns>
    public static (List<BitcoinTransaction> Txs, Dictionary<BitcoinAddress, BitcoinAddress> Owners) Exchange(
        string label, int customers)
    {
        var hot = Pkh(label + "-hot");
        var txs = new List<BitcoinTransaction>();
        var owners = new Dictionary<BitcoinAddress, BitcoinAddress> { [hot] = hot };
        for (var i = 0; i < customers; i++)
        {
            var amount = 3_000_000 + (i * 700_000);
            var deposit = Wpkh($"{label}-deposit-{i}");
            owners[deposit] = hot;
            txs.Add(Tx($"{label}-in-{i}", Height + (i * 6))
                .In(Wpkh($"{label}-customer-{i}"), amount + 30_000)
                .Out(deposit, amount)
                .Build());
            var withdrawal = 50_000_000 / (i + 2);
            txs.Add(Tx($"{label}-out-{i}", Height + (i * 6) + 3)
                .In(hot, 90_000_000, $"{label}-hot-coin-{i}")
                .Out(Wpkh($"{label}-withdrawal-{i}"), withdrawal)
                .Out(hot, 90_000_000 - withdrawal - 10_000)
                .Build());
        }

        return (txs, owners);
    }

    /// <summary>
    /// A nested service: customers pay it over two days, it pays recipients of its own, and twice it pays into its
    /// account at the exchange and twice takes a withdrawal back.
    /// </summary>
    /// <param name="customers">How many customers, and as many recipients.</param>
    /// <param name="trips">How many payments into the exchange, and as many withdrawals back.</param>
    /// <returns>The transactions.</returns>
    public static List<BitcoinTransaction> NestedService(int customers = 9, int trips = 2)
    {
        var service = Service;
        var txs = new List<BitcoinTransaction>();
        for (var i = 0; i < customers; i++)
        {
            txs.Add(Tx($"ns-customer-{i}", Height + (i * 36))
                .In(Wpkh($"ns-customer-{i}"), 5_000_000 + (i * 10_000))
                .Out(service, 4_990_000 + (i * 10_000))
                .Build());
            txs.Add(Tx($"ns-payee-{i}", Height + (i * 36) + 10)
                .In(service, 4_000_000, $"ns-service-coin-{i}")
                .Out(Wpkh($"ns-payee-{i}"), 1_500_000 + (i * 77_000))
                .Out(service, 2_400_000 - (i * 77_000))
                .Build());
        }

        for (var i = 0; i < trips; i++)
        {
            txs.Add(Tx($"ns-to-exchange-{i}", Height + 20 + (i * 100))
                .In(service, 20_000_000, $"ns-trip-coin-{i}")
                .Out(Account, 19_990_000)
                .Build());
            txs.Add(Tx($"ns-from-exchange-{i}", Height + 60 + (i * 100))
                .In(Hot, 80_000_000, $"ex-hot-coin-{i}")
                .Out(service, 15_000_000)
                .Out(Hot, 64_990_000)
                .Build());
        }

        return txs;
    }

    /// <summary>The nested service's address.</summary>
    public static BitcoinAddress Service { get; } = Wpkh("ns-service");

    /// <summary>The labelled exchange's hot wallet.</summary>
    public static BitcoinAddress Hot { get; } = Pkh("ex-hot");

    /// <summary>The nested service's deposit address at the exchange, which the exchange sweeps.</summary>
    public static BitcoinAddress Account { get; } = Wpkh("ex-account");

    /// <summary>The input for a nested service: the exchange labelled, its account address its own.</summary>
    /// <param name="txs">The transactions.</param>
    /// <returns>The input.</returns>
    public static CheckInput NestedInput(IReadOnlyList<BitcoinTransaction> txs) =>
        new(txs, [], new Dictionary<Txid, TransactionConclusions>())
        {
            Services = [new KnownService([Hot], true, "Börsen X")],
            Owners = new Dictionary<BitcoinAddress, BitcoinAddress> { [Account] = Hot, [Hot] = Hot },
        };

    /// <summary>An input without labels or decisions, with the given owners.</summary>
    /// <param name="txs">The transactions.</param>
    /// <param name="owners">Each address's owner.</param>
    /// <param name="mixers">The labelled mixers.</param>
    /// <returns>The input.</returns>
    public static CheckInput Input(
        IReadOnlyList<BitcoinTransaction> txs,
        IReadOnlyDictionary<BitcoinAddress, BitcoinAddress>? owners = null,
        IReadOnlyList<KnownMixer>? mixers = null) =>
        new(txs, mixers ?? [], new Dictionary<Txid, TransactionConclusions>())
        {
            Owners = owners ?? new Dictionary<BitcoinAddress, BitcoinAddress>(),
        };

    /// <summary>
    /// An ordinary, random graph whose coins are linked: each transaction spends a coin its payer got earlier in the
    /// graph (or one from outside the first time), pays another address a random share of it and takes the rest back
    /// as change. Few addresses make a dense graph, where each has many senders and recipients.
    /// </summary>
    /// <param name="seed">The seed.</param>
    /// <param name="addresses">How many addresses.</param>
    /// <param name="transactions">How many transactions.</param>
    /// <returns>The transactions, and three addresses to label.</returns>
    public static (List<BitcoinTransaction> Txs, BitcoinAddress[] Labelled) RandomGraph(
        int seed, int addresses, int transactions)
    {
        var random = new Random(seed);
        var height = Height;
        var txs = new List<BitcoinTransaction>(transactions);
        var coins = Enumerable.Range(0, addresses).Select(_ => new List<(BitcoinTransaction Tx, int Index)>())
            .ToArray();
        for (var i = 0; i < transactions; i++)
        {
            height += random.Next(0, 4);
            var from = random.Next(addresses);
            var to = (from + 1 + random.Next(addresses - 1)) % addresses;
            var builder = Tx($"r{seed}-{i}", height);
            long available;
            if (coins[from].Count > 0)
            {
                var pick = random.Next(coins[from].Count);
                var (previous, index) = coins[from][pick];
                coins[from].RemoveAt(pick);
                builder = builder.Spend(previous, index);
                available = previous.Outputs[index].Amount.Value;
            }
            else
            {
                available = (long)Math.Pow(10, 5 + (random.NextDouble() * 4));
                builder = builder.In(Wpkh($"r{seed}-a{from}"), available);
            }

            var paid = Math.Max(1_000, (long)(available * (0.05 + (random.NextDouble() * 0.9))));
            var change = available - paid - 500;
            builder = builder.Out(Wpkh($"r{seed}-a{to}"), paid);
            if (change > 1_000)
            {
                builder = builder.Out(Wpkh($"r{seed}-a{from}"), change);
            }

            var tx = builder.Build();
            txs.Add(tx);
            coins[to].Add((tx, 0));
            if (change > 1_000)
            {
                coins[from].Add((tx, 1));
            }
        }

        return (txs, [Wpkh($"r{seed}-a0"), Wpkh($"r{seed}-a1"), Wpkh($"r{seed}-a2")]);
    }

    /// <summary>The input of a random graph, with one address labelled a mixer and two exchanges.</summary>
    /// <param name="txs">The transactions.</param>
    /// <param name="labelled">Three addresses to label.</param>
    /// <returns>The input.</returns>
    public static CheckInput RandomInput(IReadOnlyList<BitcoinTransaction> txs, BitcoinAddress[] labelled) =>
        new(txs, [new KnownMixer([labelled[0]])], new Dictionary<Txid, TransactionConclusions>())
        {
            Services = [new KnownService([labelled[1]], true, "Börs 1"), new KnownService([labelled[2]], true)],
        };
}
