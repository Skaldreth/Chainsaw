using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Writes a finding as the JSON that is sealed, and reads it back; the fingerprint is the SHA-256 of that JSON, so that
/// a finding that has not changed is not sealed again. The parts only a cross-chain suggestion has are left out when
/// empty, so that every other finding is written exactly as before.
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
            [.. finding.Transactions.Select(txid => txid.Value)],
            [
                .. finding.Basis.Select(basis => new StoredBasis(
                    basis.Txid.Value, basis.Block?.Height, basis.Block?.Hash.Value, basis.Block?.Time)),
            ],
            [
                .. finding.Candidates.Select(candidate => new StoredCandidate(
                    candidate.Key,
                    candidate.Address?.Value,
                    candidate.Txid?.Value,
                    candidate.Output,
                    candidate.Amount?.Value,
                    candidate.Score,
                    candidate.Reason,
                    StoredMovement.Of(candidate.Arrival))),
            ],
            StoredMovement.Of(finding.Departure));
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
            [.. stored.Addresses.Select(BitcoinAddress.Parse)],
            [.. stored.Transactions.Select(Txid.Parse)],
            [
                .. stored.Basis.Select(basis => new CheckBasis(
                    Txid.Parse(basis.Txid),
                    basis is { Height: { } height, Hash: { } hash, Time: { } time }
                        ? new BlockReference(height, BlockHash.Parse(hash), time)
                        : null)),
            ],
            [
                .. stored.Candidates.Select(candidate => new CheckCandidate(
                    candidate.Key,
                    candidate.Address is { } address ? BitcoinAddress.Parse(address) : null,
                    candidate.Txid is { } txid ? Txid.Parse(txid) : null,
                    candidate.Output,
                    candidate.Amount is { } amount ? new Satoshi(amount) : null,
                    candidate.Score,
                    candidate.Reason)
                {
                    Arrival = candidate.Arrival?.ToMovement(),
                }),
            ])
        {
            Departure = stored.Departure?.ToMovement(),
        };
    }

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
    /// <param name="Departure">The payment a cross-chain suggestion starts from; left out when none.</param>
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
        IReadOnlyList<StoredCandidate> Candidates,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StoredMovement? Departure = null);

    /// <summary>A transaction a finding rests on, as sealed.</summary>
    /// <param name="Txid">The txid.</param>
    /// <param name="Height">The block's height.</param>
    /// <param name="Hash">The block's hash.</param>
    /// <param name="Time">The block's time.</param>
    private sealed record StoredBasis(string Txid, long? Height, string? Hash, DateTimeOffset? Time);

    /// <summary>A candidate as sealed.</summary>
    /// <param name="Key">The key.</param>
    /// <param name="Address">The address on Bitcoin, or null.</param>
    /// <param name="Txid">The txid on Bitcoin, or null.</param>
    /// <param name="Output">The output.</param>
    /// <param name="Amount">The amount in satoshi, or null.</param>
    /// <param name="Score">The score.</param>
    /// <param name="Reason">Why.</param>
    /// <param name="Arrival">The payment on any chain; left out when none.</param>
    private sealed record StoredCandidate(
        string Key,
        string? Address,
        string? Txid,
        int Output,
        long? Amount,
        double Score,
        string Reason,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StoredMovement? Arrival = null);

    /// <summary>A payment on any chain as sealed: every part written as the chain writes it.</summary>
    /// <param name="Chain">The transaction's chain.</param>
    /// <param name="Transaction">The transaction's id.</param>
    /// <param name="From">The payer, or null.</param>
    /// <param name="To">The recipient.</param>
    /// <param name="Asset">The asset.</param>
    /// <param name="Units">The amount in the asset's smallest unit, as invariant digits.</param>
    /// <param name="Height">The block's height, or null.</param>
    /// <param name="BlockHash">The block's hash, or null.</param>
    /// <param name="Time">The block's time, or null.</param>
    /// <param name="Output">The coin's output on a UTXO chain, or null.</param>
    /// <param name="Movement">The movement's kind on an account chain, or null.</param>
    /// <param name="Position">The movement's position on an account chain.</param>
    private sealed record StoredMovement(
        string Chain,
        string Transaction,
        string? From,
        string To,
        StoredAsset Asset,
        string Units,
        long? Height,
        string? BlockHash,
        DateTimeOffset? Time,
        int? Output,
        string? Movement,
        long Position)
    {
        /// <summary>Gives a payment as sealed.</summary>
        /// <param name="movement">The payment, or null.</param>
        /// <returns>What is sealed, or null.</returns>
        public static StoredMovement? Of(ChainMovement? movement) => movement is null
            ? null
            : new StoredMovement(
                movement.Transaction.Chain.ToString(),
                movement.Transaction.Id,
                movement.From?.Value,
                movement.To.Value,
                StoredAsset.Of(movement.Amount.Asset),
                movement.Amount.Units.ToString(CultureInfo.InvariantCulture),
                movement.Height,
                movement.BlockHash,
                movement.Time,
                movement.Coin?.Index,
                movement.Movement?.Kind.ToString(),
                movement.Movement?.Position ?? 0);

        /// <summary>Reads the payment back.</summary>
        /// <returns>The payment.</returns>
        public ChainMovement ToMovement()
        {
            var chain = Enum.Parse<Chain>(Chain);
            var reference = new ChainTransactionRef(chain, Transaction);
            return new ChainMovement(
                reference,
                From is null ? null : new ChainAddress(chain, From),
                new ChainAddress(chain, To),
                new AssetAmount(Asset.ToAsset(), BigInteger.Parse(Units, CultureInfo.InvariantCulture)),
                Height,
                BlockHash,
                Time)
            {
                Coin = Output is { } index ? new Outpoint(Txid.Parse(Transaction), index) : null,
                Movement = Movement is null
                    ? null
                    : new AccountSeed(
                        EthereumTxHash.Parse(chain, Transaction), Enum.Parse<EthereumTransferKind>(Movement), Position),
            };
        }
    }

    /// <summary>An asset as sealed: its chain, kind, symbol, decimals and contract.</summary>
    /// <param name="Chain">The chain.</param>
    /// <param name="Kind">The kind.</param>
    /// <param name="Symbol">The symbol.</param>
    /// <param name="Decimals">The decimals.</param>
    /// <param name="Contract">The token's contract, or null.</param>
    private sealed record StoredAsset(string Chain, string Kind, string Symbol, int Decimals, string? Contract)
    {
        /// <summary>Gives an asset as sealed.</summary>
        /// <param name="asset">The asset.</param>
        /// <returns>What is sealed.</returns>
        public static StoredAsset Of(Asset asset) =>
            new(asset.Chain.ToString(), asset.Kind.ToString(), asset.Symbol, asset.Decimals, asset.Contract);

        /// <summary>Reads the asset back: a chain's own coin, or a token named by its contract.</summary>
        /// <returns>The asset.</returns>
        public Asset ToAsset()
        {
            var chain = Enum.Parse<Chain>(Chain);
            return Enum.Parse<AssetKind>(Kind) == AssetKind.Native
                ? chain.NativeAsset()
                : Asset.Token(chain, Contract!, Symbol, Decimals);
        }
    }
}
