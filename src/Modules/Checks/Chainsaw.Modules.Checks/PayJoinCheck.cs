using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Shows the clustering's guesses of a PayJoin (<c>payjoin v1</c>) as checks with an area of the sort Wallet: a
/// transaction where an input was not needed for either payment, so its inputs may have two owners. The clustering has
/// already held the transaction's merges back as suggestions; this check says so where the user works. It finds
/// nothing of its own, and is a judgement, never a fact (principle 2).
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

    /// <summary>Reads from the finding's data whether the inputs were of different address types.</summary>
    /// <param name="data">The finding's JSON, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when they were.</returns>
    private static bool MixedTypes(string? data)
    {
        if (data is null)
        {
            return false;
        }

        using var json = JsonDocument.Parse(data);
        return json.RootElement.TryGetProperty("mixedTypes", out var mixed) && mixed.ValueKind == JsonValueKind.True;
    }

    /// <summary>Describes a possible PayJoin.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="stored">The clustering's stored finding, for its version.</param>
    /// <param name="finding">The finding.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(BitcoinTransaction transaction, StoredFinding stored, GenericFinding finding)
    {
        var mixedTypes = MixedTypes(finding.Data);
        var why = $"Analysen {this.Named()} visar klustringens {stored.Heuristic} för tx "
            + $"{CheckText.Short(transaction.Txid)}: {finding.Reason} Godkänn sammanslagningen bara om du vet att "
            + "inputs har samma ägare. Det är en bedömning, inte ett faktum.";
        return new CheckFinding(
            transaction.Txid.Value,
            CheckKind.Check,
            AreaSort.Wallet,
            "Möjlig PayJoin",
            mixedTypes ? "En onödig input, olika adresstyper" : "En onödig input",
            why,
            [.. transaction.InputAddresses()],
            [transaction.Txid],
            [CheckBasis.Of(transaction)],
            []);
    }
}
