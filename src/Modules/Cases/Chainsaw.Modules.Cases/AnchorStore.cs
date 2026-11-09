using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The anchors of a case on this computer: a small file per case outside the case file that records how far each
/// chain had come at the last save, authenticated with HMAC-SHA256 under a key from the vault. A case that is
/// replaced by an older copy, or whose chains are cut or rewritten by someone with the file key, no longer reaches its
/// anchors, and the verification says so.
/// </summary>
/// <remarks>
/// <para>
/// The file holds the case id, a generation, chain names, sequence numbers and hashes, nothing about the case's
/// content. Each vault has its own file per case.
/// </para>
/// <para>
/// The generation goes up by one at every write, and the vault keeps the last generation it wrote as a secret of its
/// own (<see cref="CounterName"/>). A file that is removed, or replaced by an older one together with an older case
/// file, is then seen: the file is missing or behind the vault. Someone who can also put back an older vault file
/// (and knows its master key's old wrapping) is not stopped by this; the top hash in the reports is the anchor that
/// lies beyond this computer.
/// </para>
/// </remarks>
internal static class AnchorStore
{
    /// <summary>The purpose name of the key that authenticates the anchors (see <c>IPurposeKeys</c>).</summary>
    public const string Purpose = "cases/anchor";

    /// <summary>The extension of an anchor file.</summary>
    private const string Extension = ".anchor";

    /// <summary>The domain of the authenticated bytes, so that the tag cannot be mistaken for another.</summary>
    private const string Domain = "chainsaw/case/anchor/v2";

    /// <summary>The domain of the anchor file's name.</summary>
    private const string NameDomain = "chainsaw/case/anchor-name/v1";

    /// <summary>The number of bytes of the name's HMAC that make up the file name.</summary>
    private const int NameLength = 16;

    /// <summary>The field with the case id.</summary>
    private const string CaseIdField = "caseId";

    /// <summary>The field with the generation, which the tag covers.</summary>
    private const string GenerationField = "generation";

    /// <summary>The prefix of the vault secret that holds the last generation written.</summary>
    private const string CounterPrefix = "cases/anchors/";

    /// <summary>The field with the chains, as a JSON text that the tag covers exactly.</summary>
    private const string ChainsField = "chains";

    /// <summary>The field with the tag.</summary>
    private const string TagField = "tag";

    /// <summary>The largest anchor file that is read.</summary>
    private const int MaximumLength = 1024 * 1024;

    /// <summary>Reads the anchors of a case.</summary>
    /// <param name="directory">The anchors folder.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="key">The anchor key.</param>
    /// <returns>
    /// The anchors and their generation (no anchors and generation 0 when there is no file), or
    /// <see langword="null"/> when the file is malformed or its tag does not hold.
    /// </returns>
    /// <exception cref="IOException">
    /// The file could not be read just now (for example held by another program).
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be read just now.</exception>
    public static AnchorFile? Read(string directory, Guid caseId, ReadOnlySpan<byte> key)
    {
        var path = PathFor(directory, caseId, key);
        if (!File.Exists(path))
        {
            return AnchorFile.None;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > MaximumLength || JsonNode.Parse(bytes) is not JsonObject document)
            {
                return null;
            }

            var chains = document[ChainsField]?.GetValue<string>() ?? string.Empty;
            var generation = document[GenerationField]?.GetValue<long>() ?? -1;
            var tag = Convert.FromBase64String(document[TagField]?.GetValue<string>() ?? string.Empty);
            var storedId = document[CaseIdField]?.GetValue<string>();
            var idHolds = string.Equals(storedId, caseId.ToString("D"), StringComparison.Ordinal);
            if (!idHolds || generation < 1
                || !CryptographicOperations.FixedTimeEquals(tag, Tag(key, caseId, generation, chains)))
            {
                return null;
            }

            var anchors = JsonSerializer.Deserialize<List<StoredAnchor>>(chains)?
                .Select(anchor => new ChainAnchor(anchor.Chain, anchor.Sequence, Convert.FromHexString(anchor.Hash)))
                .ToList();
            return anchors is null ? null : new AnchorFile(anchors, generation);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Writes the anchors of a case, atomically.</summary>
    /// <param name="directory">The anchors folder; created when missing.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="key">The anchor key.</param>
    /// <param name="file">The anchors and their generation, at least 1.</param>
    public static void Write(string directory, Guid caseId, ReadOnlySpan<byte> key, AnchorFile file)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(file.Generation, 1);
        var anchors = file.Anchors;
        Directory.CreateDirectory(directory);
        var chains = JsonSerializer.Serialize(anchors
            .OrderBy(anchor => anchor.Chain, StringComparer.Ordinal)
            .Select(anchor => new StoredAnchor(anchor.Chain, anchor.Sequence, Convert.ToHexStringLower(anchor.Hash))));
        var document = new JsonObject
        {
            [CaseIdField] = caseId.ToString("D"),
            [GenerationField] = file.Generation,
            [ChainsField] = chains,
            [TagField] = Convert.ToBase64String(Tag(key, caseId, file.Generation, chains)),
        };
        var path = PathFor(directory, caseId, key);
        var temporary = path + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, document);
            output.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Gives the path of a case's anchor file for one vault: its name is derived from the anchor key and the case id,
    /// so that two vaults on the same computer keep their own anchors and the name does not show the case id.
    /// </summary>
    /// <param name="directory">The anchors folder.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="key">The anchor key.</param>
    /// <returns>The path.</returns>
    private static string PathFor(string directory, Guid caseId, ReadOnlySpan<byte> key) =>
        Path.Combine(directory, NameFor(caseId, key) + Extension);

    /// <summary>
    /// Gives the name of the vault secret that holds the last generation this vault wrote for a case; like the file's
    /// name it is derived from the anchor key, so that it does not show the case id.
    /// </summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="key">The anchor key.</param>
    /// <returns>The secret's name.</returns>
    public static string CounterName(Guid caseId, ReadOnlySpan<byte> key) => CounterPrefix + NameFor(caseId, key);

    /// <summary>Gives the name derived from the anchor key and the case id.</summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="key">The anchor key.</param>
    /// <returns>32 lower-case hexadecimal digits.</returns>
    private static string NameFor(Guid caseId, ReadOnlySpan<byte> key)
    {
        var name = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{NameDomain}\n{caseId:D}"));
        return Convert.ToHexStringLower(name.AsSpan(0, NameLength));
    }

    /// <summary>Computes the tag over the case id, the generation and the chains text.</summary>
    /// <param name="key">The anchor key.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="generation">The generation.</param>
    /// <param name="chains">The chains as JSON text.</param>
    /// <returns>The 32-byte tag.</returns>
    private static byte[] Tag(ReadOnlySpan<byte> key, Guid caseId, long generation, string chains) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{Domain}\n{caseId:D}\n{generation}\n{chains}")));

    /// <summary>One anchor as it is stored.</summary>
    /// <param name="Chain">The chain's name.</param>
    /// <param name="Sequence">The sequence number.</param>
    /// <param name="Hash">The hash in lower-case hex.</param>
    private sealed record StoredAnchor(string Chain, long Sequence, string Hash);
}
