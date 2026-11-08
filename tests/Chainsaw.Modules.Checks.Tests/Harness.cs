using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>A real, encrypted case in a temporary folder with the Clustering and Checks modules' tables.</summary>
internal sealed class Harness : IDisposable
{
    public const string CaseNumber = "2026-0222";

    private readonly ServiceProvider _provider;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-checks-" + Guid.NewGuid().ToString("N"));

    public Harness(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IVault, FakeVault>();
        services.AddSingleton<IPurposeKeys, FakePurposeKeys>();
        services.AddSingleton<IOperatorProfileStore>(Profiles);
        services.AddSingleton<IClock>(Clock);
        services.AddCasesModule(options =>
        {
            options.Directory = _directory;
            options.AnchorDirectory = Path.Combine(_directory, "anchors");
        });
        services.AddClusteringModule();
        services.AddChecksModule();
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
        Store = _provider.GetRequiredService<ICaseStore>();
        Case = Store.Create(CaseNumber);
        Service = _provider.GetRequiredService<ICheckService>();
        Clustering = _provider.GetRequiredService<IClusteringService>();
    }

    public FakeProfiles Profiles { get; } = new();

    public FakeClock Clock { get; } = new();

    public ICaseStore Store { get; }

    public ICase Case { get; private set; }

    public ICheckService Service { get; }

    public IClusteringService Clustering { get; }

    /// <summary>Lets the clustering analyse the transactions, then runs the checks over a graph of them.</summary>
    public CheckRun Run(IReadOnlyList<BitcoinTransaction> transactions, params BitcoinAddress[] mixer)
    {
        Clustering.Analyse(Case, transactions);
        return Service.Run(Case, GraphOf(transactions, mixer));
    }

    /// <summary>
    /// Lets the clustering analyse the transactions, then runs the checks over a graph where the given addresses are
    /// one actor that a label says is an exchange.
    /// </summary>
    public CheckRun RunWithExchange(
        IReadOnlyList<BitcoinTransaction> transactions, string exchangeName, params BitcoinAddress[] exchange)
    {
        Clustering.Analyse(Case, transactions);
        var graph = GraphOf(transactions);
        var nodes = graph.Nodes.Where(node => !node.GraphAddresses.Any(exchange.Contains)).ToList();
        nodes.Add(Node(
            "cluster:" + exchange[0].Value,
            [.. exchange],
            [new TrailEndHint(TrailEndReason.Exchange, "egen v1", exchangeName)]));
        return Service.Run(Case, graph with { Nodes = nodes });
    }

    public CaseVerification Verify() => _provider.GetRequiredService<ICaseVerifier>().Verify(Case);

    public void Reopen()
    {
        Case.Close();
        Case = Store.Open(CaseNumber);
    }

    public long Count(string table) => Case.Read(reader =>
    {
        using var command = reader.CreateCommand($"SELECT COUNT(*) FROM {table}");
        return (long)command.ExecuteScalar()!;
    });

    public IReadOnlyList<JournalEntry> Journal(string kind) =>
        [.. Case.ReadJournal().Where(entry => entry.Kind == kind)];

    /// <summary>
    /// A graph of the transactions: one actor per address, the mixer's addresses one actor where the trail ends at a
    /// mixer, as a label would say.
    /// </summary>
    public static TraceGraph GraphOf(IReadOnlyList<BitcoinTransaction> transactions, params BitcoinAddress[] mixer)
    {
        var addresses = transactions
            .SelectMany(tx => tx.Inputs.Select(i => i.Address).Concat(tx.Outputs.Select(o => o.Address)))
            .OfType<BitcoinAddress>()
            .Distinct()
            .ToList();
        var nodes = addresses.Where(address => !mixer.Contains(address))
            .Select(address => Node("address:" + address.Value, [address], []))
            .ToList();
        if (mixer.Length > 0)
        {
            nodes.Add(Node(
                "cluster:" + mixer[0].Value,
                [.. mixer],
                [new TrailEndHint(TrailEndReason.Mixer, "egen v1", "En egen etikett säger att det är en mixer.")]));
        }

        return new TraceGraph(
            [],
            nodes,
            [],
            new UtxoGraph(
                transactions.ToDictionary(tx => tx.Txid, tx => new GraphTransaction(tx, LookupStatus.FromCase, null)),
                new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [],
            [],
            []);
    }

    private static GraphNode Node(
        string key, IReadOnlyList<BitcoinAddress> addresses, IReadOnlyList<TrailEndHint> ends) =>
        new(
            key,
            addresses.Count > 1 ? GraphNodeKind.Cluster : GraphNodeKind.Address,
            addresses,
            addresses.Count,
            null,
            null,
            null,
            false,
            null,
            null,
            NodeExpansion.Expanded,
            ends,
            new Dictionary<GraphLayout, NodePosition>());

    public void Dispose()
    {
        Case.Dispose();
        _provider.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
