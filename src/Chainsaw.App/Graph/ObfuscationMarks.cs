using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// A row of many hops folded into one line (sketch HP1a): the first and the last hop's nodes stand, the nodes between
/// are folded away, and each hop is a bead on the line placed after its time. Only how the graph shows it; the
/// tracing is not changed.
/// </summary>
/// <param name="Key">The finding's key, which the row is folded and unfolded by.</param>
/// <param name="First">The node the row starts at.</param>
/// <param name="Last">The node the row ends at.</param>
/// <param name="Between">The nodes folded away between them.</param>
/// <param name="Beads">Each hop's place on the line after its time, from 0 to 1.</param>
/// <param name="Text">The pill's words, for example <c>7 hopp · 3 d 4 h</c>.</param>
/// <param name="Lines">The hops as hovering lists them: each address, how long it waited and when it went on.</param>
public sealed record HopRow(
    string Key,
    string First,
    string Last,
    IReadOnlyList<string> Between,
    IReadOnlyList<double> Beads,
    string Text,
    IReadOnlyList<string> Lines);

/// <summary>
/// A likely address poisoning in the graph (sketch DM1a): the address that looks like a genuine one, and the genuine
/// one, so that copying the look-alike first compares the two character by character.
/// </summary>
/// <param name="Suspect">The look-alike.</param>
/// <param name="Genuine">The address it imitates.</param>
/// <param name="GenuineName">The genuine address's name in the graph, or the address shortened.</param>
public sealed record Poisoning(BitcoinAddress Suspect, BitcoinAddress Genuine, string GenuineName);

/// <summary>
/// Works out the obfuscation the graph tones down or folds (sketches DM1a and HP1a) from the stored checks
/// <c>dust v1</c> and <c>pass-through v1</c>. Pure, so that it is tested without WPF.
/// </summary>
public static class ObfuscationMarks
{
    /// <summary>The analysis that finds dust and address poisoning.</summary>
    public const string DustAnalysis = "dust";

    /// <summary>The analysis that finds many hops.</summary>
    public const string PassThroughAnalysis = "pass-through";

    /// <summary>The prefix of a dust finding's key.</summary>
    public const string DustPrefix = "dust:";

    /// <summary>The prefix of a poisoning finding's key.</summary>
    public const string PoisonPrefix = "poison:";

    /// <summary>
    /// Gives the nodes the graph tones down: the dust's senders and the look-alikes of a poisoning (sketch DM1a).
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="checks">The stored checks.</param>
    /// <returns>The nodes' keys.</returns>
    public static IReadOnlySet<string> Dusted(GraphScene scene, IReadOnlyList<StoredCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(checks);
        var addresses = checks.Where(check => check.Analysis.Name == DustAnalysis)
            .SelectMany(check => check.Finding.Key.StartsWith(PoisonPrefix, StringComparison.Ordinal)
                ? check.Finding.Addresses.Take(1)
                : check.Finding.Addresses)
            .ToHashSet();
        return scene.Nodes.Where(node => node.Node.GraphAddresses.Any(addresses.Contains))
            .Select(node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Gives the poisonings in the graph, by the look-alike's node.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="checks">The stored checks.</param>
    /// <returns>The poisonings by node key.</returns>
    public static IReadOnlyDictionary<string, Poisoning> Poisonings(
        GraphScene scene, IReadOnlyList<StoredCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(checks);
        var found = new Dictionary<string, Poisoning>(StringComparer.Ordinal);
        foreach (var check in checks.Where(check => check.Analysis.Name == DustAnalysis
            && check.Finding.Key.StartsWith(PoisonPrefix, StringComparison.Ordinal)))
        {
            if (check.Finding.Addresses is not [var suspect, var genuine, ..]
                || scene.Nodes.FirstOrDefault(node => node.Node.GraphAddresses.Contains(suspect)) is not { } node)
            {
                continue;
            }

            var named = scene.Nodes.FirstOrDefault(other => other.Node.GraphAddresses.Contains(genuine));
            var name = named is { IsNamed: true } ? named.Name : GraphScene.Shorten(genuine.Value);
            found.TryAdd(node.Key, new Poisoning(suspect, genuine, name));
        }

        return found;
    }

    /// <summary>
    /// Compares a look-alike with the genuine address character by character, for the box that opens when the
    /// look-alike is copied (sketch DM1a).
    /// </summary>
    /// <param name="genuine">The genuine address.</param>
    /// <param name="suspect">The look-alike.</param>
    /// <returns>Each position: the genuine character, the look-alike's and whether they differ.</returns>
    public static IReadOnlyList<(char Genuine, char Suspect, bool Differs)> Compare(string genuine, string suspect)
    {
        ArgumentNullException.ThrowIfNull(genuine);
        ArgumentNullException.ThrowIfNull(suspect);
        var length = Math.Max(genuine.Length, suspect.Length);
        return
        [
            .. Enumerable.Range(0, length).Select(index =>
            {
                var left = index < genuine.Length ? genuine[index] : ' ';
                var right = index < suspect.Length ? suspect[index] : ' ';
                return (left, right, left != right);
            }),
        ];
    }

    /// <summary>Gives the rows of many hops in the graph, each with at least one node to fold away.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="checks">The stored checks.</param>
    /// <returns>The rows.</returns>
    public static IReadOnlyList<HopRow> HopRows(GraphScene scene, IReadOnlyList<StoredCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(checks);
        var rows = new List<HopRow>();
        foreach (var check in checks.Where(check => check.Analysis.Name == PassThroughAnalysis))
        {
            var nodes = check.Finding.Addresses
                .Select(address => scene.Nodes.FirstOrDefault(node => node.Node.GraphAddresses.Contains(address)))
                .OfType<NodeView>()
                .Select(node => node.Key)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (nodes.Count < 3)
            {
                continue;
            }

            // Only a node whose every line belongs to the row folds away; one with other transactions stays, so that
            // nothing but the row's own lines is hidden.
            var inRow = nodes.ToHashSet(StringComparer.Ordinal);
            var between = nodes[1..^1].Where(key => scene.Edges
                    .Where(edge => edge.Edge.From == key || edge.Edge.To == key)
                    .All(edge => inRow.Contains(edge.Edge.From) && inRow.Contains(edge.Edge.To)))
                .ToList();
            if (between.Count == 0)
            {
                continue;
            }

            var hops = check.Finding.Basis;
            rows.Add(new HopRow(
                check.Finding.Key, nodes[0], nodes[^1], between, Beads(hops), TextOf(hops),
                LinesOf(check.Finding.Addresses, hops)));
        }

        return rows;
    }

    /// <summary>Writes a span of time short, as the pill does: <c>3 d 4 h</c>, <c>5 h 12 min</c>.</summary>
    /// <param name="span">The span.</param>
    /// <returns>The words.</returns>
    public static string Span(TimeSpan span) => span switch
    {
        { TotalDays: >= 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays} d {span.Hours} h"),
        { TotalHours: >= 1 } =>
            string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h {span.Minutes} min"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, (int)span.TotalMinutes)} min"),
    };

    /// <summary>Places each hop on the line after its time; evenly when the times are not known.</summary>
    /// <param name="hops">The hops' transactions with their blocks.</param>
    /// <returns>The places, from 0 to 1.</returns>
    private static IReadOnlyList<double> Beads(IReadOnlyList<CheckBasis> hops)
    {
        var times = hops.Select(hop => hop.Block?.Time).ToList();
        if (times.Any(time => time is null) || hops.Count < 2 || times[^1] == times[0])
        {
            return [.. Enumerable.Range(0, hops.Count).Select(index => (index + 1.0) / (hops.Count + 1))];
        }

        var (first, whole) = (times[0]!.Value, (times[^1]!.Value - times[0]!.Value).TotalSeconds);
        return [.. times.Select(time => 0.08 + (0.84 * (time!.Value - first).TotalSeconds / whole))];
    }

    /// <summary>Writes the pill's words: how many hops and how long the row took.</summary>
    /// <param name="hops">The hops.</param>
    /// <returns>The words.</returns>
    private static string TextOf(IReadOnlyList<CheckBasis> hops)
    {
        var count = hops.Count.ToString(CultureInfo.InvariantCulture) + " hopp";
        return hops is [{ Block: { } first }, .., { Block: { } last }]
            ? count + " · " + Span(last.Time - first.Time)
            : count;
    }

    /// <summary>Writes a line per hop: the address it left, how long the money waited there, and the block.</summary>
    /// <param name="addresses">The row's addresses in order.</param>
    /// <param name="hops">The hops.</param>
    /// <returns>The lines.</returns>
    private static IReadOnlyList<string> LinesOf(
        IReadOnlyList<BitcoinAddress> addresses, IReadOnlyList<CheckBasis> hops)
    {
        var lines = new List<string>();
        for (var index = 0; index < hops.Count && index < addresses.Count; index++)
        {
            var wait = index > 0 && hops[index].Block is { } now && hops[index - 1].Block is { } before
                ? " · väntade " + Span(now.Time - before.Time)
                : string.Empty;
            var block = hops[index].Block is { } at
                ? string.Create(CultureInfo.InvariantCulture, $" · block {at.Height}")
                : string.Empty;
            lines.Add(GraphScene.Shorten(addresses[index].Value) + wait + block);
        }

        return lines;
    }
}
