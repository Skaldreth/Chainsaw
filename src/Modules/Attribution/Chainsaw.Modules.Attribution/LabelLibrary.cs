using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The label library on this computer (see <see cref="ILabelLibrary"/>): one file in the application data folder,
/// encrypted with AES-256-GCM under a key the vault derives for this purpose, so that it opens only with the vault
/// that wrote it.
/// </summary>
/// <param name="keys">The vault's purpose keys.</param>
/// <param name="options">Where the library lives.</param>
internal sealed class LabelLibrary(IPurposeKeys keys, AttributionOptions options) : ILabelLibrary
{
    /// <summary>The purpose name of the library's key (see <see cref="IPurposeKeys"/>).</summary>
    public const string Purpose = "attribution/library";

    /// <summary>The library's file name.</summary>
    private const string FileName = "bibliotek.bin";

    /// <summary>The size of the nonce, in bytes.</summary>
    private const int NonceSize = 12;

    /// <summary>The size of the tag, in bytes.</summary>
    private const int TagSize = 16;

    /// <summary>The size of the key, in bytes (AES-256).</summary>
    private const int KeySize = 32;

    /// <summary>The file's magic and format version, also the associated data that the tag covers.</summary>
    private static readonly byte[] Magic = "CHSWLIB1"u8.ToArray();

    /// <summary>Where the nonce starts: after the magic.</summary>
    private static readonly int NonceStart = Magic.Length;

    /// <summary>Where the tag starts: after the nonce.</summary>
    private static readonly int TagStart = NonceStart + NonceSize;

    /// <summary>Where the encrypted content starts: after the tag.</summary>
    private static readonly int ContentStart = TagStart + TagSize;

    /// <summary>How the library's content is written.</summary>
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Guards the file.</summary>
    private readonly Lock _gate = new();

    /// <summary>Gets the library's file.</summary>
    private string FilePath => Path.Combine(options.LibraryDirectory, FileName);

    /// <inheritdoc/>
    public LibraryAbsorption Absorb(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var labels = @case.Read(LabelStore.Read).Active.Where(LabelRules.MaySpread).ToList();
        var caseId = CaseKey(@case);
        lock (_gate)
        {
            var document = Load();
            foreach (var stored in document.Labels)
            {
                stored.Cases.Remove(caseId);
            }

            var byKey = document.Labels.ToDictionary(Key);
            foreach (var label in labels)
            {
                var key = LabelStore.Key(label.Origin.OriginId, label.Subject, label.Kind, label.Name);
                if (!byKey.TryGetValue(key, out var stored))
                {
                    stored = LibraryRecord.From(label);
                    byKey.Add(key, stored);
                    document.Labels.Add(stored);
                }
                else if (stored.Reason is null && !label.Origin.IsFromOtherCase)
                {
                    // The case the label was set in knows why; a copy that came from another case does not.
                    (stored.Reason, stored.Note) = (label.Origin.Reason, label.Origin.Note);
                }

                stored.Cases[caseId] = @case.Info.CaseNumber;
            }

            var removed = document.Labels.RemoveAll(stored => stored.Cases.Count == 0);
            Save(document);
            return new LibraryAbsorption(labels.Count, removed);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<LabelEntry> ShareInto(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        var wanted = addresses.Select(address => address.Value).ToHashSet(StringComparer.Ordinal);
        var caseId = CaseKey(@case);
        List<LibraryRecord> candidates;
        lock (_gate)
        {
            candidates =
            [
                .. Load().Labels.Where(stored => wanted.Contains(stored.Subject)
                    && !stored.Cases.ContainsKey(caseId) && stored.Kind != LabelKind.Victim),
            ];
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        return @case.Write(transaction =>
        {
            var inCase = LabelStore.Read(transaction);
            var incoming = candidates.Select(stored => stored.ToCase()).Where(label => !inCase.Has(
                LabelStore.Key(label.Origin.OriginId, label.Subject, label.Kind, label.Name))).ToList();
            if (incoming.Count == 0)
            {
                return (IReadOnlyList<LabelEntry>)[];
            }

            // Only who, when and the label are written: the other case's number, note and reason stay behind.
            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.LabelsFromLibrary,
                string.Format(CultureInfo.InvariantCulture, "{0} etiketter från andra ärenden", incoming.Count),
                JsonSerializer.Serialize(incoming.Select(label => new
                {
                    subject = label.Subject.Address.Value,
                    subjectKind = label.Subject.Kind.ToString(),
                    kind = label.Kind.ToString(),
                    name = label.Name,
                    source = label.Origin.OriginalSource.ToString(),
                    setBy = label.Origin.SetBy.Name,
                    setUtc = LabelStore.Text(label.Origin.SetUtc),
                }))));
            return (IReadOnlyList<LabelEntry>)
                [.. incoming.Select(label => LabelStore.Seal(transaction, label, journal))];
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<LibraryLabel> List()
    {
        lock (_gate)
        {
            return
            [
                .. Load().Labels.Select(stored => new LibraryLabel(
                    stored.SubjectOf(),
                    stored.Kind,
                    stored.Name,
                    stored.OriginOf(stored.Source, stored.Reason, stored.Note),
                    [.. stored.Cases.Values.Distinct().Order(StringComparer.Ordinal)])),
            ];
        }
    }

    /// <summary>Gives the key a case is known by in the library.</summary>
    /// <param name="case">The case.</param>
    /// <returns>The case's id.</returns>
    private static string CaseKey(ICase @case) => @case.Info.Id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Gives a stored label's identity.</summary>
    /// <param name="stored">The stored label.</param>
    /// <returns>The key.</returns>
    private static LabelKey Key(LibraryRecord stored) =>
        LabelStore.Key(stored.OriginId, stored.SubjectOf(), stored.Kind, stored.Name);

    /// <summary>Reads the library.</summary>
    /// <returns>The content; empty when there is no library yet.</returns>
    /// <exception cref="VaultException">The vault is locked.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is damaged or was written with another vault; it is left as it is.
    /// </exception>
    private LibraryDocument Load()
    {
        if (!File.Exists(FilePath))
        {
            return new LibraryDocument();
        }

        var bytes = File.ReadAllBytes(FilePath);
        if (bytes.Length < ContentStart || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("The label library is not a library file.");
        }

        var plain = new byte[bytes.Length - ContentStart];
        Span<byte> key = stackalloc byte[KeySize];
        try
        {
            keys.DeriveKey(Purpose, key);
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                bytes.AsSpan(NonceStart, NonceSize),
                bytes.AsSpan(ContentStart),
                bytes.AsSpan(TagStart, TagSize),
                plain,
                Magic);
            return JsonSerializer.Deserialize<LibraryDocument>(plain, Json)
                ?? throw new InvalidDataException("The label library is empty.");
        }
        catch (AuthenticationTagMismatchException exception)
        {
            throw new InvalidDataException("The label library is damaged or belongs to another vault.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The label library cannot be read.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Writes the library through a temporary file, so that it is either whole or as it was.</summary>
    /// <param name="document">The content.</param>
    /// <exception cref="VaultException">The vault is locked.</exception>
    private void Save(LibraryDocument document)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var bytes = new byte[ContentStart + plain.Length];
        Magic.CopyTo(bytes, 0);
        RandomNumberGenerator.Fill(bytes.AsSpan(NonceStart, NonceSize));
        Span<byte> key = stackalloc byte[KeySize];
        try
        {
            keys.DeriveKey(Purpose, key);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(
                bytes.AsSpan(NonceStart, NonceSize),
                plain,
                bytes.AsSpan(ContentStart),
                bytes.AsSpan(TagStart, TagSize),
                Magic);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        Directory.CreateDirectory(options.LibraryDirectory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>The library's content.</summary>
    private sealed class LibraryDocument
    {
        /// <summary>Gets or sets the labels.</summary>
        public List<LibraryRecord> Labels { get; set; } = [];
    }

    /// <summary>One label in the library, with what stays in the library.</summary>
    private sealed class LibraryRecord
    {
        /// <summary>Gets or sets the origin's identity.</summary>
        public required string OriginId { get; set; }

        /// <summary>Gets or sets whether the label is on an address or a cluster.</summary>
        public required LabelSubjectKind SubjectKind { get; set; }

        /// <summary>Gets or sets the address.</summary>
        public required string Subject { get; set; }

        /// <summary>Gets or sets what the label says.</summary>
        public required LabelKind Kind { get; set; }

        /// <summary>Gets or sets the name.</summary>
        public string? Name { get; set; }

        /// <summary>Gets or sets where the label first came from.</summary>
        public required LabelSource Source { get; set; }

        /// <summary>Gets or sets the source's name.</summary>
        public required string SourceName { get; set; }

        /// <summary>Gets or sets the source's version.</summary>
        public required string SourceVersion { get; set; }

        /// <summary>Gets or sets who set it.</summary>
        public required OperatorProfile SetBy { get; set; }

        /// <summary>Gets or sets when.</summary>
        public required DateTimeOffset SetUtc { get; set; }

        /// <summary>Gets or sets why; stays in the library.</summary>
        public string? Reason { get; set; }

        /// <summary>Gets or sets the note; stays in the library.</summary>
        public string? Note { get; set; }

        /// <summary>Gets or sets the cases that hold it: case id to case number; stays in the library.</summary>
        public Dictionary<string, string> Cases { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Makes a library label from a label in a case.</summary>
        /// <param name="label">The label.</param>
        /// <returns>The library label, without cases.</returns>
        public static LibraryRecord From(LabelEntry label) => new()
        {
            OriginId = label.Origin.OriginId,
            SubjectKind = label.Subject.Kind,
            Subject = label.Subject.Address.Value,
            Kind = label.Kind,
            Name = label.Name,
            Source = label.Origin.OriginalSource,
            SourceName = label.Origin.SourceName,
            SourceVersion = label.Origin.SourceVersion,
            SetBy = label.Origin.SetBy,
            SetUtc = label.Origin.SetUtc,
            Reason = label.Origin.IsFromOtherCase ? null : label.Origin.Reason,
            Note = label.Origin.IsFromOtherCase ? null : label.Origin.Note,
        };

        /// <summary>Gives what the label is on.</summary>
        /// <returns>The subject.</returns>
        public LabelSubject SubjectOf() => new(SubjectKind, BitcoinAddress.Parse(Subject));

        /// <summary>Gives the label's origin as seen from somewhere.</summary>
        /// <param name="source">How it got there.</param>
        /// <param name="reason">The reason it carries there.</param>
        /// <param name="note">The note it carries there.</param>
        /// <returns>The origin.</returns>
        public LabelOrigin OriginOf(LabelSource source, string? reason, string? note) =>
            new(OriginId, source, Source, SourceName, SourceVersion, SetBy, SetUtc, reason, note);

        /// <summary>
        /// Gives the label as it enters another case: who, when and the label, never the reason, the note or the
        /// case numbers.
        /// </summary>
        /// <returns>The label, not sealed yet.</returns>
        public LabelEntry ToCase() =>
            new(0, SubjectOf(), Kind, Name, OriginOf(LabelSource.OtherCase, null, null), false, 0);
    }
}
