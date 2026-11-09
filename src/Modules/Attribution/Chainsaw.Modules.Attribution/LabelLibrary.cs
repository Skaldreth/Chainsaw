using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The label library on this computer (see <see cref="ILabelLibrary"/>): one file in the application data folder,
/// encrypted with AES-256-GCM under a key the vault derives for this purpose, so that it opens only with the vault
/// that wrote it.
/// </summary>
/// <param name="keys">The vault's purpose keys.</param>
/// <param name="options">Where the library lives.</param>
/// <param name="clustering">Clustering, to keep everything near a victim out of the library.</param>
internal sealed class LabelLibrary(IPurposeKeys keys, AttributionOptions options, IClusteringService clustering)
    : ILabelLibrary
{
    /// <summary>The purpose name of the library's key (see <see cref="IPurposeKeys"/>).</summary>
    public const string Purpose = "attribution/library";

    /// <summary>The library's file name.</summary>
    private const string FileName = "bibliotek.bin";

    /// <summary>
    /// Why a copy is taken back when its label was withdrawn where it was set; it says nothing about that case.
    /// </summary>
    private const string WithdrawnReason = "Etiketten togs tillbaka i ärendet där den sattes.";

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
        var inCase = @case.Read(LabelStore.Read);
        var labels = inCase.Active.Where(LabelRules.MaySpread).ToList();

        // A label the user set here and then took back is withdrawn everywhere: the copies other cases got from the
        // library are taken back there too, and it is never given again. Only the user's own judgement (own or from
        // an open source, or a supplier's report) is withdrawn so; a copy, a sanction hit or an analysis's label
        // taken back here only leaves the library from this case. A label near a victim only stays out (below).
        var withdrawn = inCase.All
            .Where(label => label.Origin.Source is LabelSource.Own or LabelSource.OpenSource or LabelSource.Provider
                && inCase.Retracted.Contains(label.Sequence))
            .Select(label => label.Origin.OriginId)
            .ToHashSet(StringComparer.Ordinal);
        labels.RemoveAll(label => withdrawn.Contains(label.Origin.OriginId));

        // Sprid inte where the label was set holds its origin back everywhere: a copy another case got before keeps
        // it, but the library no longer gives it on, whichever case it is absorbed from. Letting it spread again
        // where it was set releases it.
        var (heldBack, released) = SpreadMarks(inCase.Active);
        var caseId = CaseKey(@case);
        lock (_gate)
        {
            var document = Load();
            var nearVictim = NearVictim(
                @case,
                inCase.Active,
                labels.Select(label => label.Subject).Concat(document.Labels.Select(stored => stored.SubjectOf())));
            labels.RemoveAll(label => nearVictim.Contains(label.Subject));
            document.Withdrawn.UnionWith(withdrawn);
            document.HeldBack.ExceptWith(released);
            document.HeldBack.UnionWith(heldBack);
            foreach (var stored in document.Labels)
            {
                stored.Cases.Remove(caseId);

                // Near a victim in this case, or held back where it was set: out of the library, without touching
                // the copies other cases have.
                if (IsKeptOut(document, stored.OriginId) || nearVictim.Contains(stored.SubjectOf()))
                {
                    stored.Cases.Clear();
                }
            }

            labels.RemoveAll(label => IsKeptOut(document, label.Origin.OriginId));

            var byKey = document.Labels.ToDictionary(Key);
            foreach (var label in labels)
            {
                var key = LabelStore.Key(label.Origin.OriginId, label.Subject, label.Kind, label.Name);
                if (!byKey.TryGetValue(key, out var stored))
                {
                    stored = LibraryRecord.From(label, @case.Info.CaseNumber);
                    byKey.Add(key, stored);
                    document.Labels.Add(stored);
                }
                else if (!label.Origin.IsFromOtherCase)
                {
                    // The case the label was set in has the last word on why, the note and its number.
                    (stored.Reason, stored.Note) = (label.Origin.Reason, label.Origin.Note);
                    stored.OriginCase = @case.Info.CaseNumber;
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
        ArgumentNullException.ThrowIfNull(addresses);
        return ShareInto(@case, [.. addresses.Select(address => address.ToChainAddress())]);
    }

    /// <inheritdoc/>
    public IReadOnlyList<LabelEntry> ShareInto(ICase @case, IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        var wanted = addresses.ToHashSet();
        var texts = wanted.Select(address => address.Value).ToHashSet(StringComparer.Ordinal);
        var caseId = CaseKey(@case);
        List<LibraryRecord> candidates;
        HashSet<string> withdrawn;
        lock (_gate)
        {
            var document = Load();
            withdrawn = document.Withdrawn;
            candidates =
            [
                .. document.Labels.Where(stored => texts.Contains(stored.Subject)
                    && !stored.Cases.ContainsKey(caseId) && stored.Kind != LabelKind.Victim
                    && !IsKeptOut(document, stored.OriginId)
                    && wanted.Contains(stored.SubjectOf().Target)),
            ];
        }

        if (candidates.Count == 0 && !@case.Read(reader => HasWithdrawnCopies(LabelStore.Read(reader), withdrawn)))
        {
            return [];
        }

        return @case.Write(transaction =>
        {
            var inCase = LabelStore.Read(transaction);
            TakeBackWithdrawn(transaction, inCase, withdrawn);
            var incoming = candidates.Select(stored => stored.ToCase()).Where(label => !inCase.Has(
                LabelStore.Key(label.Origin.OriginId, label.Subject, label.Kind, label.Name))).ToList();
            if (incoming.Count == 0)
            {
                return (IReadOnlyList<LabelEntry>)[];
            }

            // Everything follows (CLAUDE.md, Allt följer med): who with title and unit, when, why, the note and the
            // case it was set in. The journal names the labels and the cases they came from.
            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.LabelsFromLibrary,
                string.Format(CultureInfo.InvariantCulture, "{0} etiketter från andra ärenden", incoming.Count),
                JsonSerializer.Serialize(incoming.Select(label => new
                {
                    subject = label.Subject.Stored,
                    subjectKind = label.Subject.Kind.ToString(),
                    kind = label.Kind.ToString(),
                    name = label.Name,
                    source = label.Origin.OriginalSource.ToString(),
                    setBy = label.Origin.SetBy.Name,
                    setUtc = LabelStore.Text(label.Origin.SetUtc),
                    caseNumber = label.Origin.CaseNumber,
                }))));
            return (IReadOnlyList<LabelEntry>)[.. incoming.Select(label => SealCopy(transaction, label, journal))];
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

    /// <summary>
    /// Takes back the copies this case got from the library of labels that were withdrawn where they were set, with
    /// one line in the journal that says no more than that.
    /// </summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="inCase">The case's labels.</param>
    /// <param name="withdrawn">The withdrawn origins.</param>
    private static void TakeBackWithdrawn(
        ICaseTransaction transaction, StoredLabels inCase, IReadOnlySet<string> withdrawn)
    {
        var copies = WithdrawnCopies(inCase, withdrawn).ToList();
        if (copies.Count == 0)
        {
            return;
        }

        var journal = transaction.AppendJournal(new JournalDraft(
            AttributionJournalKinds.LabelRetracted,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} etiketter från andra ärenden togs tillbaka där de sattes",
                copies.Count),
            JsonSerializer.Serialize(new { labels = copies.Select(label => label.Sequence) })));
        foreach (var copy in copies)
        {
            LabelStore.SealRetract(transaction, copy, WithdrawnReason, journal);
        }
    }

    /// <summary>Says whether the case holds copies of withdrawn labels.</summary>
    /// <param name="inCase">The case's labels.</param>
    /// <param name="withdrawn">The withdrawn origins.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool HasWithdrawnCopies(StoredLabels inCase, IReadOnlySet<string> withdrawn) =>
        WithdrawnCopies(inCase, withdrawn).Any();

    /// <summary>Gives the case's copies from other cases of labels that were withdrawn where they were set.</summary>
    /// <param name="inCase">The case's labels.</param>
    /// <param name="withdrawn">The withdrawn origins.</param>
    /// <returns>The copies that are not taken back yet.</returns>
    private static IEnumerable<LabelEntry> WithdrawnCopies(StoredLabels inCase, IReadOnlySet<string> withdrawn) =>
        inCase.Active.Where(label => label.Origin.IsFromOtherCase && withdrawn.Contains(label.Origin.OriginId));

    /// <summary>
    /// Gives the subjects that are near a victim in the case: an address or a cluster that holds, or shares a cluster
    /// with, an address labelled as a victim. A label on such a subject never spreads, since it can tell who was
    /// robbed (for example the victim's own exchange account). The clusters are read at once, not one by one.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="active">The case's labels that are not taken back.</param>
    /// <param name="subjects">The subjects to test.</param>
    /// <returns>Those of the subjects that are near a victim.</returns>
    private HashSet<LabelSubject> NearVictim(
        ICase @case, IReadOnlyList<LabelEntry> active, IEnumerable<LabelSubject> subjects)
    {
        var victimSubjects = active.Where(label => label.Kind == LabelKind.Victim)
            .Select(label => label.Subject)
            .ToList();
        if (victimSubjects.Count == 0)
        {
            return [];
        }

        var asked = subjects.ToHashSet();

        // Only Bitcoin is clustered: a victim's address and a label on a cluster are read with their clusters.
        var bitcoin = victimSubjects
            .Concat(asked.Where(subject => subject.Kind == LabelSubjectKind.Cluster))
            .Select(subject => subject.Address)
            .OfType<BitcoinAddress>()
            .ToHashSet();
        var clusters = bitcoin.Count == 0
            ? new Dictionary<BitcoinAddress, Cluster>()
            : clustering.GetClusters(@case, bitcoin);
        IEnumerable<ChainAddress> Near(LabelSubject subject) =>
            subject.Address is { } address && clusters.TryGetValue(address, out var cluster)
                ? cluster.Addresses.Select(member => member.ToChainAddress())
                : [subject.Target];

        var victims = victimSubjects.SelectMany(Near).ToHashSet();
        return
        [
            .. asked.Where(subject => victims.Contains(subject.Target)
                || (subject.Kind == LabelSubjectKind.Cluster && Near(subject).Any(victims.Contains))),
        ];
    }

    /// <summary>
    /// Gives the origins the case holds back with <em>Sprid inte</em>, and those it lets spread: only labels the user
    /// set in this case (own or from an open source), since a copy, a list's hit or an analysis's label cannot be
    /// marked. A label on a victim never spreads anyway and is left to the victim rule.
    /// </summary>
    /// <param name="active">The case's labels that are not taken back.</param>
    /// <returns>The origins held back, and the origins let spread.</returns>
    private static (HashSet<string> HeldBack, HashSet<string> Released) SpreadMarks(IReadOnlyList<LabelEntry> active)
    {
        var own = active
            .Where(label => label.Origin.Source is LabelSource.Own or LabelSource.OpenSource
                && label.Kind != LabelKind.Victim)
            .ToList();
        return (
            own.Where(label => label.DoNotSpread).Select(label => label.Origin.OriginId)
                .ToHashSet(StringComparer.Ordinal),
            own.Where(label => !label.DoNotSpread).Select(label => label.Origin.OriginId)
                .ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>Says whether an origin is kept out of the library: withdrawn, or held back with Sprid inte.</summary>
    /// <param name="document">The library.</param>
    /// <param name="originId">The origin.</param>
    /// <returns><see langword="true"/> when it is never given.</returns>
    private static bool IsKeptOut(LibraryDocument document, string originId) =>
        document.Withdrawn.Contains(originId) || document.HeldBack.Contains(originId);

    /// <summary>Seals a copy from the library, with the case it was set in when that is known.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="label">The copy.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The sealed copy.</returns>
    private static LabelEntry SealCopy(ICaseTransaction transaction, LabelEntry label, JournalEntry journal)
    {
        var sealedCopy = LabelStore.Seal(transaction, label, journal);
        if (label.Origin.CaseNumber is { } number)
        {
            LabelStore.SealCaseNumber(transaction, sealedCopy, number, journal);
        }

        return sealedCopy;
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

        /// <summary>
        /// Gets or sets the origins that were taken back where they were set; they are never given again, and
        /// copies are taken back in the cases that have them.
        /// </summary>
        public HashSet<string> Withdrawn { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets the origins marked <em>Sprid inte</em> where they were set; they are not given while they are
        /// marked, but copies other cases already have stay there.
        /// </summary>
        public HashSet<string> HeldBack { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>One label in the library, with what stays in the library.</summary>
    private sealed class LibraryRecord
    {
        /// <summary>Gets or sets the origin's identity.</summary>
        public required string OriginId { get; set; }

        /// <summary>Gets or sets whether the label is on an address or a cluster.</summary>
        public required LabelSubjectKind SubjectKind { get; set; }

        /// <summary>
        /// Gets or sets the address as <see cref="LabelSubject.Target"/> gives it; its form says its chain
        /// (<see cref="LabelStore.Subject"/>).
        /// </summary>
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

        /// <summary>Gets or sets the number of the case it was set in; <see langword="null"/> when not known.</summary>
        public string? OriginCase { get; set; }

        /// <summary>Gets or sets the cases that hold it: case id to case number; stays in the library.</summary>
        public Dictionary<string, string> Cases { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Makes a library label from a label in a case.</summary>
        /// <param name="label">The label.</param>
        /// <param name="caseNumber">The number of the case it is absorbed from.</param>
        /// <returns>The library label, without cases.</returns>
        public static LibraryRecord From(LabelEntry label, string caseNumber) => new()
        {
            OriginId = label.Origin.OriginId,
            SubjectKind = label.Subject.Kind,
            Subject = label.Subject.Stored,
            Kind = label.Kind,
            Name = label.Name,
            Source = label.Origin.OriginalSource,
            SourceName = label.Origin.SourceName,
            SourceVersion = label.Origin.SourceVersion,
            SetBy = label.Origin.SetBy,
            SetUtc = label.Origin.SetUtc,
            Reason = label.Origin.Reason,
            Note = label.Origin.Note,
            OriginCase = label.Origin.IsFromOtherCase ? label.Origin.CaseNumber : caseNumber,
        };

        /// <summary>Gives what the label is on.</summary>
        /// <returns>The subject.</returns>
        public LabelSubject SubjectOf() => LabelStore.Subject(SubjectKind, Subject);

        /// <summary>Gives the label's origin as seen from somewhere.</summary>
        /// <param name="source">How it got there.</param>
        /// <param name="reason">The reason it carries there.</param>
        /// <param name="note">The note it carries there.</param>
        /// <returns>The origin.</returns>
        public LabelOrigin OriginOf(LabelSource source, string? reason, string? note) =>
            new(OriginId, source, Source, SourceName, SourceVersion, SetBy, SetUtc, reason, note)
            {
                CaseNumber = OriginCase,
            };

        /// <summary>
        /// Gives the label as it enters another case: everything follows (CLAUDE.md, <em>Allt följer med</em>): who
        /// with title and unit, when, why, the note and the case it was set in. The other cases that hold it stay in
        /// the library.
        /// </summary>
        /// <returns>The label, not sealed yet.</returns>
        public LabelEntry ToCase() =>
            new(0, SubjectOf(), Kind, Name, OriginOf(LabelSource.OtherCase, Reason, Note), false, 0);
    }
}
