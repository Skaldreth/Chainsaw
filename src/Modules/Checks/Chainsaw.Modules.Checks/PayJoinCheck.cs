using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Shows the clustering's PayJoin findings (<c>payjoin</c>) as checks with an area of the sort Wallet: a transaction
/// where an input was not needed for either payment. On the strong signs of a PayJoin the clustering has held the
/// transaction's merges back as suggestions (<em>Möjlig PayJoin</em>); otherwise it merged as usual and the check only
/// notes the unneeded input. It finds nothing of its own, and is a judgement, never a fact (principle 2).
/// </summary>
internal sealed class PayJoinCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "payjoin-hint";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Möjlig PayJoin";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return
        [
            .. LatestFindings.Of<GenericFinding>(input)
                .Where(found => found.Finding.Sort == HeuristicNames.PayJoinSort)
                .Select(found => Describe(found.Transaction, found.Stored, found.Finding)),
        ];
    }

    /// <summary>Reads from the finding's data whether the clustering saw the strong signs of a PayJoin.</summary>
    /// <param name="data">The finding's JSON, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it did.</returns>
    private static bool IsStrong(string? data)
    {
        if (data is null)
        {
            return false;
        }

        using var json = JsonDocument.Parse(data);
        return json.RootElement.TryGetProperty("strong", out var strong) && strong.ValueKind == JsonValueKind.True;
    }

    /// <summary>Says what the user can do about it.</summary>
    /// <param name="strong">Whether the clustering saw the strong signs.</param>
    /// <returns>The advice, in Swedish.</returns>
    private static string Advice(bool strong) => strong
        ? "Godkänn sammanslagningen bara om du vet att inputs har samma ägare."
        : "Visar det sig vara en PayJoin, bryt upp sammanslagningen.";

    /// <summary>Describes a possible PayJoin.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="stored">The clustering's stored finding, for its version.</param>
    /// <param name="finding">The finding.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(BitcoinTransaction transaction, StoredFinding stored, GenericFinding finding)
    {
        var strong = IsStrong(finding.Data);
        var why = $"Analysen {this.Named()} visar klustringens {stored.Heuristic} för tx "
            + $"{CheckText.Short(transaction.Txid)}: {finding.Reason} {Advice(strong)} Det är en bedömning, inte ett "
            + "faktum.";
        return new CheckFinding(
            transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Wallet,
            strong ? "Möjlig PayJoin" : "Onödig input",
            strong ? "Mottagarens input, inget slås ihop automatiskt" : "Klustringen slår ihop som vanligt",
            why,
            [.. transaction.InputAddresses()],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            []);
    }
}
