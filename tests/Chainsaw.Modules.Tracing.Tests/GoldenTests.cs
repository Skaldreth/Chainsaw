using System.Data.Common;
using System.Globalization;
using System.Text;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The sketches' made-up case written out in full and compared with a facit recorded before the graph and the
/// tracing were made independent of the chain (stage 20): Bitcoin's graph, both methods' results, the journal and
/// every row the case holds must stay exactly as they were. The lines are written in the invariant culture.
/// </summary>
public sealed class GoldenTests : IDisposable
{
    private const string Facit = "bitcoin-flow.txt";

    private readonly CaseHarness _harness = new();
    private readonly Flows _flows = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Bitcoins_graph_results_and_sealed_data_are_unchanged()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        await _flows.ExpandInto(_harness);
        var graph = await _harness.Load();
        var text = new StringBuilder();
        WriteGraph(text, graph);
        foreach (var method in new[] { TracingMethodKind.Fifo, TracingMethodKind.Haircut })
        {
            WriteResult(text, _harness.Service.Trace(graph, new TraceRequest(
                method, [new TraceSeed(new Outpoint(_flows.Pay.Txid, 0))])));
        }

        WriteCase(text);
        var caseId = _harness.Case.Read(reader => Rows(reader, "SELECT * FROM case_info"))[0][1];
        var actual = text.ToString()
            .Replace(caseId, "<case>", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", Facit);
        if (Environment.GetEnvironmentVariable("CHAINSAW_RECORD_GOLDEN") is { Length: > 0 } target)
        {
            await File.WriteAllTextAsync(Path.Combine(target, Facit), actual);
        }

        var expected = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    private static void WriteGraph(StringBuilder text, TraceGraph graph)
    {
        text.AppendLine("# graph");
        foreach (var node in graph.Nodes)
        {
            text.AppendLine(Invariant($"node {node.Key} {node.Kind} hop={node.Hop} size={node.ClusterSize} " +
                $"start={node.IsStartPoint} first={node.FirstReceived:O} {node.Expansion} " +
                $"ends=[{string.Join("; ", node.Ends.Select(end => end.Reason + " " + end.Source))}] " +
                $"addresses=[{string.Join(", ", node.GraphAddresses.Select(address => address.Value))}]"));
        }

        foreach (var edge in graph.Edges)
        {
            text.AppendLine(Invariant($"edge {edge.Key} {edge.Kind} {edge.Direction} {Units(edge.Amount)} " +
                $"first={edge.FirstTime:O}"));
            foreach (var transfer in edge.Transfers)
            {
                text.AppendLine(Invariant($"  transfer {TransferText(transfer)}"));
            }
        }

        foreach (var pair in graph.Transactions.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            text.AppendLine(Invariant($"tx {pair.Key.Value} {pair.Value.Status} {pair.Value.Problem}"));
        }

        foreach (var pair in graph.Spends.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            text.AppendLine(Invariant($"spend {pair.Key} {pair.Value.State} {pair.Value.SpentBy?.Txid.Value} " +
                $"{pair.Value.SpentBy?.InputIndex} {pair.Value.MayBeOutdated}"));
        }

        foreach (var pair in graph.TransactionEnds.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal))
        {
            text.AppendLine(Invariant($"tx-end {pair.Key.Value} " +
                $"{string.Join("; ", pair.Value.Select(end => end.Reason + " " + end.Source))}"));
        }
    }

    private static void WriteResult(StringBuilder text, TraceResult result)
    {
        text.AppendLine(Invariant($"# trace {result.Method} traced={Units(result.Traced)}"));
        foreach (var coin in result.Coins)
        {
            text.AppendLine(Invariant($"coin {coin.Coin} {coin.Address?.Value} {coin.NodeKey} {coin.Taint} " +
                $"{AmountText(coin.Amount)} {coin.MayBeOutdated}"));
        }

        foreach (var transaction in result.Transactions)
        {
            text.AppendLine(Invariant($"tx {transaction.Txid.Value} in={Units(transaction.In)} " +
                $"out=[{string.Join(", ", transaction.Outputs.Select(Units))}] fee={Units(transaction.Fee)} " +
                $"{transaction.MayBeOutdated}"));
        }

        foreach (var edge in result.Edges)
        {
            text.AppendLine(Invariant($"edge {edge.EdgeKey} {AmountText(edge.Amount)} {edge.MayBeOutdated}"));
        }

        foreach (var end in result.Ends)
        {
            text.AppendLine(Invariant($"end {end.Reason} {AmountText(end.Amount)} coin={end.Coin} " +
                $"tx={end.Transaction?.Value} node={end.NodeKey} {end.MayBeOutdated} " +
                $"basis=[{string.Join("; ", end.Basis.Select(hint => hint.Reason + " " + hint.Source))}] " +
                $"links={end.Continuations.Count} {end.Explanation}"));
            text.AppendLine("  why " + end.Amount.Explain());
        }
    }

    private void WriteCase(StringBuilder text)
    {
        text.AppendLine("# journal");
        foreach (var entry in _harness.Case.ReadJournal())
        {
            text.AppendLine(Invariant($"{entry.Sequence} {entry.Kind} {entry.Summary} {entry.Details}"));
        }

        text.AppendLine("# rows");
        var tables = _harness.Case.Read(reader => Rows(reader,
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").Select(row => row[0]).ToList());
        foreach (var table in tables)
        {
            var rows = _harness.Case.Read(reader => Rows(reader, $"SELECT * FROM \"{table}\""));
            text.AppendLine(Invariant($"table {table} {rows.Count}"));
            foreach (var row in rows.Select(row => string.Join(" | ", row)).Order(StringComparer.Ordinal))
            {
                text.AppendLine("  " + row);
            }
        }
    }

    private static List<string[]> Rows(Cases.Contracts.ICaseReader reader, string sql)
    {
        using DbCommand command = reader.CreateCommand(sql);
        using var data = command.ExecuteReader();
        var rows = new List<string[]>();
        while (data.Read())
        {
            rows.Add([.. Enumerable.Range(0, data.FieldCount).Select(i => Cell(data.GetValue(i)))]);
        }

        return rows;
    }

    private static string Cell(object value) => value switch
    {
        // The hash chain starts from the case's random id, so a hash differs between runs; its length is kept.
        byte[] { Length: 32 } => "<hash>",
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string TransferText(EdgeTransfer transfer) =>
        Invariant($"{transfer.Txid.Value} {transfer.Coin} {Units(transfer.Amount)} {transfer.Block?.Height}");

    private static string AmountText(TracedAmount amount) =>
        Invariant($"{Units(amount.Amount)} {amount.Method} [{string.Join(", ", amount.Through.Select(t => t.Value))}]");

    private static string Units(SharedKernel.Satoshi amount) => amount.Value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(string text) => text;
}
