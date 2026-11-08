using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Reads Ransomwhere's open data set of ransomware payment addresses (<c>ransomwhere.json</c>, Jack Cable,
/// https://ransomwhe.re): a JSON list of records with <c>address</c>, <c>blockchain</c>, <c>family</c>,
/// <c>transactions</c> and the times the record was created and updated.
/// </summary>
/// <remarks>
/// <para>
/// Licence and quality, checked 2026-11-08 (CLAUDE.md, <em>Attributionskällor</em>): the data set is published on
/// Zenodo (record 13999026, version 1.1.0 of 2024-10-27) under <b>CC BY 4.0</b>, which allows any use with
/// attribution, so it is built in, with the attribution in every label's reason. The records are reported by the
/// public and by researchers, so each label is a judgement (<see cref="LabelKind.Reported"/>) and never a fact; every
/// record in that file was on Bitcoin.
/// </para>
/// <para>
/// OpenSanctions republishes the same data under its own terms (free only for non-commercial use), so it is never the
/// file read here; WalletExplorer has no stated licence for its data and is not built in (BACKLOG).
/// </para>
/// </remarks>
internal static class RansomwhereParser
{
    /// <summary>The source's name as an origin states it.</summary>
    public const string ListName = "Ransomwhere";

    /// <summary>The licence and the attribution it asks for.</summary>
    public const string License = "CC BY 4.0, Jack Cable, Ransomwhere (ransomwhe.re)";

    /// <summary>The longest family name kept, so that a label's name stays within its limit.</summary>
    private const int MaximumFamilyLength = 80;

    /// <summary>The chain a record must name to be read; the only one in the data set.</summary>
    private const string BitcoinChain = "bitcoin";

    /// <summary>Says whether a file is Ransomwhere's data set: a JSON list whose first record has its fields.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool Recognises(byte[] content)
    {
        try
        {
            var reader = new Utf8JsonReader(content);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray
                || !reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            using var first = JsonDocument.ParseValue(ref reader);
            return first.RootElement.TryGetProperty("address", out _)
                && first.RootElement.TryGetProperty("blockchain", out _)
                && first.RootElement.TryGetProperty("family", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads the data set.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The date of the newest record, the addresses and how many records were skipped.</returns>
    /// <exception cref="OpenSourceListException">
    /// <see cref="OpenSourceListError.NotKnown"/> when the file is not the data set, or
    /// <see cref="OpenSourceListError.NoDate"/> when no record says when it was updated.
    /// </exception>
    public static ParsedOpenSource Parse(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new OpenSourceListException(OpenSourceListError.NotKnown);
            }

            DateOnly? newest = null;
            var entries = new List<OpenSourceEntry>();
            var skipped = 0;
            foreach (var record in document.RootElement.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object)
                {
                    throw new OpenSourceListException(OpenSourceListError.NotKnown);
                }

                newest = Later(newest, Date(record, "updatedAt") ?? Date(record, "createdAt"));
                if (Entry(record) is { } entry)
                {
                    entries.Add(entry);
                }
                else
                {
                    skipped++;
                }
            }

            return new ParsedOpenSource(
                ListName,
                License,
                newest ?? throw new OpenSourceListException(OpenSourceListError.NoDate),
                entries,
                skipped);
        }
        catch (JsonException exception)
        {
            throw new OpenSourceListException(OpenSourceListError.NotKnown, exception);
        }
    }

    /// <summary>Reads one record as a label, if it names a Bitcoin address.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The entry, or <see langword="null"/> when the record cannot be read.</returns>
    private static OpenSourceEntry? Entry(JsonElement record)
    {
        if (Text(record, "blockchain") is not BitcoinChain
            || !BitcoinAddress.TryParse(Text(record, "address"), out var address))
        {
            return null;
        }

        var family = Text(record, "family") is { Length: > 0 } named
            ? named[..Math.Min(named.Length, MaximumFamilyLength)]
            : "okänd familj";
        var payments = record.TryGetProperty("transactions", out var transactions)
            && transactions.ValueKind == JsonValueKind.Array
                ? transactions.GetArrayLength()
                : 0;
        return new OpenSourceEntry(
            address.ToChainAddress(),
            LabelKind.Reported,
            $"Ransomware: {family}",
            string.Format(
                CultureInfo.InvariantCulture,
                "betalningsadress för ransomware ({0}), med {1} betalningar i datamängden",
                family,
                payments));
    }

    /// <summary>Gives a property's text.</summary>
    /// <param name="record">The record.</param>
    /// <param name="name">The property.</param>
    /// <returns>The trimmed text, or <see langword="null"/> when it is missing or not text.</returns>
    private static string? Text(JsonElement record, string name) =>
        record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    /// <summary>Reads a time property's date in UTC.</summary>
    /// <param name="record">The record.</param>
    /// <param name="name">The property, for example <c>updatedAt</c>.</param>
    /// <returns>The date, or <see langword="null"/>.</returns>
    private static DateOnly? Date(JsonElement record, string name) =>
        DateTimeOffset.TryParse(
            Text(record, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? DateOnly.FromDateTime(time.UtcDateTime)
            : null;

    /// <summary>Gives the later of two dates.</summary>
    /// <param name="first">One date, or none.</param>
    /// <param name="second">Another, or none.</param>
    /// <returns>The later, or the one there is.</returns>
    private static DateOnly? Later(DateOnly? first, DateOnly? second)
    {
        if (first is not { } one)
        {
            return second;
        }

        if (second is not { } two)
        {
            return first;
        }

        return one > two ? one : two;
    }
}

/// <summary>What an open source holds.</summary>
/// <param name="Name">The source's name.</param>
/// <param name="License">Its licence and attribution.</param>
/// <param name="DataDate">The date of its newest record.</param>
/// <param name="Entries">What it says, one entry per address and record.</param>
/// <param name="Skipped">Records that name no address Chainsaw can read.</param>
internal sealed record ParsedOpenSource(
    string Name, string License, DateOnly DataDate, IReadOnlyList<OpenSourceEntry> Entries, int Skipped);
