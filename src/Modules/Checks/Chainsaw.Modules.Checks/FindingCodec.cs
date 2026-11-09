using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Writes a finding as the JSON that is sealed, and reads it back; the fingerprint is the SHA-256 of that JSON, so that
/// a finding that has not changed is not sealed again.
/// </summary>
internal static class FindingCodec
{
    /// <summary>Writes a finding as JSON.</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The JSON.</returns>
    public static string Encode(CheckFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var stored = new StoredFinding(
            finding.Key,
            finding.Kind.ToString(),
            finding.Area.ToString(),
            finding.Title,
            finding.Summary,
            finding.Why,
            [.. finding.Addresses.Select(address => address.Value)],
            [.. finding.Transactions.Select(txid => txid.Stored)],
            [
                .. finding.Basis.Select(basis => new StoredBasis(
                    basis.Txid.Stored, basis.Block?.Height, basis.Block?.Hash.Value, basis.Block?.Time)),
            ],
            [
                .. finding.Candidates.Select(candidate => new StoredCandidate(
                    candidate.Key,
                    candidate.Address.Value,
                    candidate.Txid.Stored,
                    candidate.Output,
                    candidate.Amount.Value,
                    candidate.Score,
                    candidate.Reason)),
            ]);
        return JsonSerializer.Serialize(stored);
    }

    /// <summary>Reads a sealed finding.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The finding.</returns>
    /// <exception cref="InvalidDataException">The JSON is not a finding.</exception>
    public static CheckFinding Decode(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredFinding>(json)
            ?? throw new InvalidDataException("A sealed finding is empty.");
        return new CheckFinding(
            stored.Key,
            Enum.Parse<CheckKind>(stored.Kind),
            Enum.Parse<AreaSort>(stored.Area),
            stored.Title,
            stored.Summary,
            stored.Why,
            [.. stored.Addresses.Select(AddressOf)],
            [.. stored.Transactions.Select(TxidOf)],
            [
                .. stored.Basis.Select(basis => new CheckBasis(
                    TxidOf(basis.Txid),
                    basis is { Height: { } height, Hash: { } hash, Time: { } time }
                        ? new BlockReference(height, BlockHash.Parse(hash), time)
                        : null)),
            ],
            [
                .. stored.Candidates.Select(candidate => new CheckCandidate(
                    candidate.Key,
                    AddressOf(candidate.Address),
                    TxidOf(candidate.Txid),
                    candidate.Output,
                    new Satoshi(candidate.Amount),
                    candidate.Score,
                    candidate.Reason)),
            ]);
    }

    /// <summary>
    /// Reads a stored address on whichever UTXO chain writes it so (Bitcoin, Litecoin, Zcash); no two write one alike.
    /// </summary>
    /// <param name="text">The stored address.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is no address.</exception>
    private static BitcoinAddress AddressOf(string text) =>
        BitcoinAddress.TryParseOnAnyChain(text, out var address) && address.Value == text
            ? address
            : throw new FormatException("A sealed finding names something that is not an address.");

    /// <summary>Reads a stored txid on its chain (<see cref="Txid.Stored"/>: bare on Bitcoin).</summary>
    /// <param name="text">The stored txid.</param>
    /// <returns>The txid.</returns>
    /// <exception cref="FormatException">The text is no txid.</exception>
    private static Txid TxidOf(string text) =>
        Txid.TryParseStored(text, out var txid)
            ? txid
            : throw new FormatException("A sealed finding names something that is not a txid.");

    /// <summary>Gives the fingerprint of a finding's JSON.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The SHA-256 in lower-case hex.</returns>
    public static string Fingerprint(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    /// <summary>Writes a time as stored.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text, in UTC.</returns>
    public static string Text(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Reads a stored time.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The time.</returns>
    public static DateTimeOffset Time(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>A finding as sealed.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Kind">Check or suggestion.</param>
    /// <param name="Area">The area's sort.</param>
    /// <param name="Title">The title.</param>
    /// <param name="Summary">The summary.</param>
    /// <param name="Why">Why.</param>
    /// <param name="Addresses">The addresses.</param>
    /// <param name="Transactions">The transactions.</param>
    /// <param name="Basis">The basis.</param>
    /// <param name="Candidates">The candidates.</param>
    private sealed record StoredFinding(
        string Key,
        string Kind,
        string Area,
        string Title,
        string Summary,
        string Why,
        IReadOnlyList<string> Addresses,
        IReadOnlyList<string> Transactions,
        IReadOnlyList<StoredBasis> Basis,
        IReadOnlyList<StoredCandidate> Candidates);

    /// <summary>A transaction a finding rests on, as sealed.</summary>
    /// <param name="Txid">The txid.</param>
    /// <param name="Height">The block's height.</param>
    /// <param name="Hash">The block's hash.</param>
    /// <param name="Time">The block's time.</param>
    private sealed record StoredBasis(string Txid, long? Height, string? Hash, DateTimeOffset? Time);

    /// <summary>A candidate as sealed.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Address">The address.</param>
    /// <param name="Txid">The txid.</param>
    /// <param name="Output">The output.</param>
    /// <param name="Amount">The amount in satoshi.</param>
    /// <param name="Score">The score.</param>
    /// <param name="Reason">Why.</param>
    private sealed record StoredCandidate(
        string Key, string Address, string Txid, int Output, long Amount, double Score, string Reason);
}
