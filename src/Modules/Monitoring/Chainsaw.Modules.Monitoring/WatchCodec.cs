using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Turns watches and hits into the text sealed in the case and back. The form is Chainsaw's own and spelled out, so
/// that a record can be read with any tool and never depends on how a type happens to serialise.
/// </summary>
internal static class WatchCodec
{
    /// <summary>Writes a node.</summary>
    /// <param name="subject">The node.</param>
    /// <returns>The JSON.</returns>
    public static string Encode(WatchSubject subject) => JsonSerializer.Serialize(new SubjectText(
        subject.Kind.ToString(), subject.Anchor.Chain.ToString(), subject.Anchor.Value, subject.Name));

    /// <summary>Reads a node.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The node.</returns>
    public static WatchSubject DecodeSubject(string json)
    {
        var text = JsonSerializer.Deserialize<SubjectText>(json) ?? throw Broken();
        return new WatchSubject(
            Enum.Parse<WatchSubjectKind>(text.Kind),
            new ChainAddress(Enum.Parse<Chain>(text.Chain), text.Anchor),
            text.Name);
    }

    /// <summary>Writes a filter.</summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The JSON.</returns>
    public static string Encode(WatchFilter filter) => JsonSerializer.Serialize(new FilterText(
        filter.Chain.ToString(),
        filter.Direction.ToString(),
        [.. filter.Receivers.Select(party => party.Value)],
        [.. filter.Senders.Select(party => party.Value)],
        filter.Asset is { } asset ? AssetOf(asset) : null,
        filter.Minimum is { } minimum ? Units(minimum) : null));

    /// <summary>Reads a filter.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The filter.</returns>
    public static WatchFilter DecodeFilter(string json)
    {
        var text = JsonSerializer.Deserialize<FilterText>(json) ?? throw Broken();
        var chain = Enum.Parse<Chain>(text.Chain);
        var asset = text.Asset is { } stored ? AssetOf(stored) : null;
        var counted = asset ?? (chain == Chain.Bitcoin ? Asset.Bitcoin : null);
        AssetAmount? minimum = text.Minimum is { } units && counted is not null
            ? new AssetAmount(counted, BigInteger.Parse(units, CultureInfo.InvariantCulture))
            : null;
        return new WatchFilter(
            chain,
            Enum.Parse<WatchDirection>(text.Direction),
            [.. text.Receivers.Select(value => new ChainAddress(chain, value))],
            [.. text.Senders.Select(value => new ChainAddress(chain, value))],
            asset,
            minimum);
    }

    /// <summary>Writes an asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The JSON.</returns>
    public static string Encode(Asset asset) => JsonSerializer.Serialize(AssetOf(asset));

    /// <summary>Reads an asset.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The asset.</returns>
    public static Asset DecodeAsset(string json) =>
        AssetOf(JsonSerializer.Deserialize<AssetText>(json) ?? throw Broken());

    /// <summary>Writes an amount's units in decimal digits.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The units.</returns>
    public static string Units(AssetAmount amount) => amount.Units.ToString(CultureInfo.InvariantCulture);

    /// <summary>Writes addresses as their texts.</summary>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The JSON.</returns>
    public static string Encode(IEnumerable<ChainAddress> addresses) =>
        JsonSerializer.Serialize(addresses.Select(address => address.Value).ToList());

    /// <summary>Reads addresses on a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="json">The JSON.</param>
    /// <returns>The addresses.</returns>
    public static IReadOnlyList<ChainAddress> DecodeAddresses(Chain chain, string json) =>
        [.. (JsonSerializer.Deserialize<List<string>>(json) ?? []).Select(value => new ChainAddress(chain, value))];

    /// <summary>Writes an operator.</summary>
    /// <param name="profile">The operator.</param>
    /// <returns>The JSON.</returns>
    public static string Encode(OperatorProfile profile) => JsonSerializer.Serialize(profile);

    /// <summary>Reads an operator.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The operator.</returns>
    public static OperatorProfile DecodeOperator(string json) =>
        JsonSerializer.Deserialize<OperatorProfile>(json) ?? throw Broken();

    /// <summary>Writes a time as sealed: round-trip UTC.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text.</returns>
    public static string Text(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Reads a sealed time.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The time.</returns>
    public static DateTimeOffset Time(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Gives the key that says two hits are the same: the watch, transaction, direction and asset.</summary>
    /// <param name="watchId">The watch.</param>
    /// <param name="match">The match.</param>
    /// <returns>The key.</returns>
    public static string Key(string watchId, WatchMatch match) =>
        string.Join('|', watchId, match.Transaction, match.Direction, match.Amount.Asset.ToString());

    /// <summary>Gives an asset's stored form.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The stored form.</returns>
    private static AssetText AssetOf(Asset asset) =>
        new(asset.Chain.ToString(), asset.Kind.ToString(), asset.Symbol, asset.Decimals, asset.Contract);

    /// <summary>Makes an asset from its stored form.</summary>
    /// <param name="text">The stored form.</param>
    /// <returns>The asset.</returns>
    private static Asset AssetOf(AssetText text)
    {
        var chain = Enum.Parse<Chain>(text.Chain);
        if (Enum.Parse<AssetKind>(text.Kind) == AssetKind.Token)
        {
            return Asset.Token(chain, text.Contract ?? throw Broken(), text.Symbol, text.Decimals);
        }

        return chain switch
        {
            Chain.Bitcoin => Asset.Bitcoin,
            Chain.Ethereum => Asset.Ether,
            _ => Asset.Tronix,
        };
    }

    /// <summary>Gives the error for a record that cannot be read.</summary>
    /// <returns>The error.</returns>
    private static InvalidDataException Broken() => new("A watch's sealed record cannot be read.");

    /// <summary>A node as stored.</summary>
    /// <param name="Kind">Address or cluster.</param>
    /// <param name="Chain">The chain.</param>
    /// <param name="Anchor">The address.</param>
    /// <param name="Name">The name the user saw.</param>
    private sealed record SubjectText(string Kind, string Chain, string Anchor, string Name);

    /// <summary>A filter as stored.</summary>
    /// <param name="Chain">The chain.</param>
    /// <param name="Direction">The direction.</param>
    /// <param name="Receivers">The receivers.</param>
    /// <param name="Senders">The senders.</param>
    /// <param name="Asset">The asset, or none for all.</param>
    /// <param name="Minimum">The minimum's units, or none.</param>
    private sealed record FilterText(
        string Chain, string Direction, List<string> Receivers, List<string> Senders, AssetText? Asset,
        string? Minimum);

    /// <summary>An asset as stored.</summary>
    /// <param name="Chain">The chain.</param>
    /// <param name="Kind">Native or token.</param>
    /// <param name="Symbol">The symbol.</param>
    /// <param name="Decimals">The decimals.</param>
    /// <param name="Contract">The token's contract.</param>
    private sealed record AssetText(string Chain, string Kind, string Symbol, int Decimals, string? Contract);
}
