using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// An open case: the connection to the encrypted working copy, the file key and the anchor key in pinned memory, the
/// header with its envelopes and the lock that keeps the case from being opened twice.
/// </summary>
/// <remarks>
/// Every operation holds <see cref="_gate"/>, so they run one at a time, and runs its SQL in a guard scope; between
/// operations the guard refuses everything. The file key is kept while the case is open only to wrap new envelopes,
/// and the anchor key to write the anchors when saving after the vault has been locked; both are cleared on close.
/// </remarks>
internal sealed class Case : ICase
{
    /// <summary>The size of the buffer when the working copy is copied into the case file.</summary>
    private const int CopyBufferSize = 81920;

    /// <summary>Serialises every operation on the case.</summary>
    private readonly Lock _gate = new();

    /// <summary>The services the case uses.</summary>
    private readonly CaseServices _services;

    /// <summary>The case's files.</summary>
    private readonly CasePaths _paths;

    /// <summary>The file key in a pinned array; cleared on close.</summary>
    private readonly byte[] _fileKey;

    /// <summary>The key that authenticates this computer's anchors, in a pinned array; cleared on close.</summary>
    private readonly byte[] _anchorKey;

    /// <summary>The lock file, held open while the case is open.</summary>
    private readonly FileStream _lock;

    /// <summary>Called once when the case has closed, so that the store forgets it.</summary>
    private readonly Action<Case> _onClosed;

    /// <summary>
    /// The chains in the case: this build's and those registered by modules this build does not know.
    /// </summary>
    private readonly IReadOnlyList<SealedChain> _chains;

    /// <summary>The registered definitions by chain name.</summary>
    private readonly IReadOnlyDictionary<string, SealedChain> _registered;

    /// <summary>The names of registered definitions that do not hold.</summary>
    private readonly IReadOnlyList<string> _invalidRegistrations;

    /// <summary>
    /// The declared column types of each chain whose table has the right shape, read once after the schema is up to
    /// date.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _shapes = new(StringComparer.Ordinal);

    /// <summary>
    /// The header with the current envelopes; replaced as a whole, so it can be read without the gate.
    /// </summary>
    private volatile CaseHeader _header;

    /// <summary>The connection; <see langword="null"/> after close.</summary>
    private volatile CaseDatabase? _database;

    /// <summary>
    /// Whether another module's work or one of the case's own operations is running; nested calls are refused.
    /// </summary>
    private bool _inWork;

    /// <summary>
    /// Whether the anchor file could not be read when the case was opened; the anchors are then neither compared nor
    /// moved in this session.
    /// </summary>
    private bool _anchorsUnavailable;

    /// <summary>Whether the journal has recorded why the anchors are replaced at the next save.</summary>
    private bool _replaceAnchors;

    /// <summary>Set, without waiting for the gate, when the vault is locked and the case is about to close.</summary>
    private volatile bool _closing;

    /// <summary>The title in force, read when the case is opened and replaced when it is set.</summary>
    private volatile string _title;

    /// <summary>When the case saves itself after writes; started once the case has opened.</summary>
    private readonly AutosaveSchedule _autosave;

    /// <summary>Wraps an opened database; the store's create and open build it.</summary>
    /// <param name="services">The services.</param>
    /// <param name="paths">The case's files.</param>
    /// <param name="header">The header.</param>
    /// <param name="keys">The file key and the anchor key; the case owns and clears them.</param>
    /// <param name="lockFile">The held lock file; the case owns and closes it.</param>
    /// <param name="database">The open database with an up-to-date schema.</param>
    /// <param name="info">The case information.</param>
    /// <param name="onClosed">Called once when the case has closed.</param>
    public Case(
        CaseServices services,
        CasePaths paths,
        CaseHeader header,
        (byte[] File, byte[] Anchor) keys,
        FileStream lockFile,
        CaseDatabase database,
        CaseInfo info,
        Action<Case> onClosed)
    {
        _services = services;
        _paths = paths;
        _header = header;
        (_fileKey, _anchorKey) = keys;
        _lock = lockFile;
        _database = database;
        _onClosed = onClosed;
        Info = info;

        using var scope = database.As(GuardMode.Internal);
        var (registered, invalid) = HashChain.ReadRegistry(database, null);
        _registered = registered.ToDictionary(chain => chain.Name, StringComparer.Ordinal);
        _invalidRegistrations = invalid;
        _chains =
        [
            .. services.Catalog.Chains,
            .. registered.Where(chain => !services.Catalog.Chains.Any(known => known.Name == chain.Name)),
        ];
        database.AddSealed(registered.Select(chain => chain.Table));
        foreach (var chain in _chains)
        {
            // A table changed outside Chainsaw leaves its chain without a shape: nothing more is sealed in it, and
            // the verification reports it.
            if (HashChain.ReadShape(database, null, chain) is { } shape)
            {
                _shapes[chain.Name] = shape;
            }
        }

        _title = ReadTitle(database);

        // Made last, so that a constructor that fails leaves no timer behind.
        _autosave = new AutosaveSchedule(
            services.Options.Time,
            services.Options.AutosaveQuietPeriod,
            services.Options.AutosaveMaximumDelay,
            OnAutosaveDue);
    }

    /// <inheritdoc />
    public CaseInfo Info { get; }

    /// <summary>
    /// Gets or sets the SHA-256 of the case file as it is on disk now (the file the working copy belongs to), or
    /// <see langword="null"/> before a new case is first saved. A save records it next to the new file's hash until
    /// the new file is in place, so that any number of failed saves still recover.
    /// </summary>
    internal byte[]? CaseFileHash { get; set; }

    /// <summary>Gets the store's count of vault locks when the opening began.</summary>
    internal long LocksAtOpening { get; init; }

    /// <inheritdoc />
    public string Title => _title;

    /// <inheritdoc />
    public string Path => _paths.CaseFile;

    /// <inheritdoc />
    /// <remarks>Never waits for an operation that runs, so that the user interface can always ask.</remarks>
    public bool IsClosed => _closing || _database is null;

    /// <inheritdoc />
    /// <remarks>Reads the header the last operation left, without waiting for one that runs.</remarks>
    public bool HasSharingPassword => HasSharingPasswordUnlocked();

    /// <inheritdoc />
    /// <remarks>Reads the header the last operation left, without waiting for one that runs.</remarks>
    public IReadOnlyList<string> RecoveryFingerprints => RecoveryEnvelopes.Fingerprints(_header);

    /// <summary>Gets the open database, for <see cref="CaseTransaction"/> and the tests.</summary>
    internal CaseDatabase Database => _database ?? throw new CaseException(CaseError.Closed);

    /// <inheritdoc />
    public T Read<T>(Func<ICaseReader, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            using var operation = BeginOperation();
            var database = Database;
            using var scope = database.As(GuardMode.Internal);
            using var transaction = database.Begin(deferred: true);
            var reader = new CaseTransaction(this, transaction, writable: false);

            // The handle ends inside the work's scope: the rest of a reader the work left open runs with no more
            // rights than the work had.
            using var workScope = database.As(GuardMode.Reading);
            return RunWork(work, reader);
        }
    }

    /// <inheritdoc />
    public T Write<T>(Func<ICaseTransaction, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            using var operation = BeginOperation();
            var database = Database;
            using var scope = database.As(GuardMode.Internal);
            using var transaction = database.Begin();
            var writer = new CaseTransaction(this, transaction, writable: true);
            T result;
            using (database.As(GuardMode.Writing))
            {
                // As in Read: the rest of a reader left open runs with the work's rights, before the commit.
                result = RunWork(work, writer);
            }

            transaction.Commit();
            _autosave.Written();
            return result;
        }
    }

    /// <inheritdoc />
    public JournalEntry AppendJournal(JournalDraft draft)
    {
        JournalRecords.ValidateFromModule(draft);
        lock (_gate)
        {
            using var operation = BeginOperation();
            return AppendOwnJournal(draft);
        }
    }

    /// <inheritdoc />
    public bool SetTitle(string title)
    {
        if (!CaseTitleRules.IsValid(title))
        {
            throw new ArgumentException(
                $"A title holds at most {CaseTitleRules.MaximumLength} characters and no control characters.",
                nameof(title));
        }

        var normalized = CaseTitleRules.Normalize(title);
        lock (_gate)
        {
            using var operation = BeginOperation();
            if (string.Equals(normalized, _title, StringComparison.Ordinal))
            {
                return false;
            }

            var database = Database;
            using var scope = database.As(GuardMode.Internal);
            using var transaction = database.Begin();
            var utc = _services.Clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            SealAsCases(transaction, CasesSchema.Title, [utc, normalized]);
            AppendJournalIn(transaction, new JournalDraft(
                CaseJournalKinds.TitleSet,
                TitleSummary(normalized, _title.Length == 0),
                JsonSerializer.Serialize(new Dictionary<string, string> { ["title"] = normalized })));
            transaction.Commit();
            _autosave.Written();
            _title = normalized;
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<JournalEntry> ReadJournal(long fromSequence = 1, int maximumCount = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        lock (_gate)
        {
            using var operation = BeginOperation();
            using var scope = Database.As(GuardMode.Internal);
            return JournalRecords.Read(Database, null, fromSequence, maximumCount);
        }
    }

    /// <inheritdoc />
    public void SetSharingPassword(ReadOnlySpan<char> password)
    {
        if (password.Length < SharingPasswordRules.MinimumLength)
        {
            throw new CaseException(CaseError.PasswordTooShort);
        }

        lock (_gate)
        {
            using var operation = BeginOperation();
            var others = WithoutSharingEnvelope().ToList();
            RequireRoomFor(others);
            var envelope = KeyEnvelopes.CreatePassword(
                Info.Id, _fileKey, password, _services.Options.SharingIterations);
            AppendOwnJournal(new JournalDraft(
                CaseJournalKinds.SharingPasswordSet,
                HasSharingPasswordUnlocked() ? "Delningslösenordet byttes" : "Ett delningslösenord lades till"));
            _header = _header.WithEnvelopes(others.Append(envelope));
            SaveUnlocked();
        }
    }

    /// <inheritdoc />
    public void RemoveSharingPassword()
    {
        lock (_gate)
        {
            using var operation = BeginOperation();
            if (!HasSharingPasswordUnlocked())
            {
                return;
            }

            if (!VaultHasEnvelope())
            {
                throw new CaseException(CaseError.WouldLoseAccess);
            }

            AppendOwnJournal(new JournalDraft(
                CaseJournalKinds.SharingPasswordRemoved, "Delningslösenordet togs bort"));
            _header = _header.WithEnvelopes(WithoutSharingEnvelope());
            SaveUnlocked();
        }
    }

    /// <inheritdoc />
    public void AddOperatorAccess()
    {
        lock (_gate)
        {
            using var operation = BeginOperation();
            if (VaultHasEnvelope())
            {
                return;
            }

            RequireRoomFor(_header.Envelopes);
            var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
            try
            {
                DeriveVaultKey(_services.PurposeKeys, CaseCrypto.EnvelopePurpose, wrappingKey);
                var envelope = KeyEnvelopes.CreateOperator(Info.Id, _fileKey, wrappingKey);
                AppendOwnJournal(new JournalDraft(
                    CaseJournalKinds.OperatorAccessAdded, "Operatörens valv fick ett eget kuvert"));
                _header = _header.WithEnvelopes(_header.Envelopes.Append(envelope));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrappingKey);
            }

            SaveUnlocked();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CaseEnvelope> DescribeEnvelopes()
    {
        lock (_gate)
        {
            using var operation = BeginOperation();
            var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
            try
            {
                DeriveVaultKey(_services.PurposeKeys, CaseCrypto.EnvelopePurpose, wrappingKey);
                return EnvelopeDescriptions.Describe(_header, wrappingKey, _fileKey, _services.Options);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(wrappingKey);
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<CaseSaveState>? SaveStateChanged;

    /// <inheritdoc />
    public void Save()
    {
        lock (_gate)
        {
            using var operation = BeginOperation();
            SaveUnlocked();
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        lock (_gate)
        {
            if (_database is null)
            {
                return;
            }

            if (_inWork)
            {
                throw new InvalidOperationException("The case cannot be closed from inside a read or a write.");
            }

            // Closing is an operation too, so that nothing can run a read or write in the middle of it.
            _inWork = true;
            try
            {
                SaveUnlocked();
                ReleaseDatabase();
                // The base goes last: a working copy that is left behind still belongs to the file it was just
                // saved to, and is recovered rather than taken for a conflict.
                File.Delete(_paths.WorkingCopy);
                File.Delete(_paths.WorkingJournal);
                File.Delete(_paths.WorkingBase);
            }
            finally
            {
                // On a failed save the working copy stays, so that the next opening recovers it.
                ReleaseResources();
            }
        }
    }

    /// <summary>Closes the case; a failed save keeps the working copy for recovery instead of throwing.</summary>
    public void Dispose()
    {
        try
        {
            Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The working copy is kept and recovered, with a journal entry, the next time the case is opened.
        }
    }

    /// <summary>
    /// Refuses every new operation at once, without waiting for one that runs, so that the case can be closed in the
    /// background when the vault is locked.
    /// </summary>
    internal void BeginClosing() => _closing = true;

    /// <summary>
    /// Lets the case save itself after writes, once it has opened: until then the opening does its own saving, and a
    /// case that fails to open must never be saved by a timer.
    /// </summary>
    internal void StartAutosave() => _autosave.Start();

    /// <summary>Gets whether the working copy holds writes that the case file does not yet; for the tests.</summary>
    internal bool HasUnsavedWrites => _autosave.IsDirty;

    /// <summary>
    /// Gets or sets what runs when a save by itself has passed its checks and is about to begin; only the tests set
    /// it, to lock the vault in between.
    /// </summary>
    internal Action? AutosaveChecked { get; set; }

    /// <summary>
    /// Drops the case without saving, as if the process had died: the working copy stays for recovery. Only tests
    /// use it, to check the recovery.
    /// </summary>
    internal void Abandon()
    {
        lock (_gate)
        {
            ReleaseDatabase();
            ReleaseResources();
        }
    }

    /// <summary>Adds a journal entry in a transaction that is already open.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="draft">What happened.</param>
    /// <returns>The sealed entry.</returns>
    internal JournalEntry AppendJournalIn(SqliteTransaction transaction, JournalDraft draft)
    {
        JournalRecords.Validate(draft);
        var profile = CurrentOperator();
        var values = JournalRecords.Values(draft, profile, _services.Clock.UtcNow, _services.Options.TimeZone);
        var record = SealAsCases(transaction, CasesSchema.Journal, values);
        return JournalRecords.ToEntry(record, values);
    }

    /// <summary>
    /// Adds a record, for another module, to a sealed chain of another module of this build, in a transaction that is
    /// already open.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain's name.</param>
    /// <param name="values">The values.</param>
    /// <returns>The sealed record.</returns>
    internal SealedRecord AppendSealedIn(SqliteTransaction transaction, string chain, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return SealAsCases(transaction, _services.Catalog.ModuleChain(chain), values);
    }

    /// <summary>
    /// Checks the whole database page by page, the case information, every chain and this computer's anchors, in
    /// one read transaction. When a page does not authenticate the chains are not walked, since SQLite cannot read
    /// them reliably.
    /// </summary>
    /// <returns>The problems with the case as a whole, and one result per chain, ordered by name.</returns>
    internal (IReadOnlyList<CaseProblem> Problems, IReadOnlyList<ChainVerification> Chains) Verify()
    {
        lock (_gate)
        {
            using var operation = BeginOperation();
            var database = Database;
            using var scope = database.As(GuardMode.Internal);
            try
            {
                using var transaction = database.Begin(deferred: true);
                if (!HashChain.QuickCheck(database, transaction))
                {
                    return ([CaseProblem.DatabaseUnsound], []);
                }

                TryReadAnchors(out var anchors);
                return CaseChecks.Run(
                    database, transaction, Info, _chains, _registered, _invalidRegistrations, anchors);
            }
            catch (SqliteException)
            {
                return ([CaseProblem.DatabaseUnsound], []);
            }
        }
    }

    /// <summary>
    /// Runs a module's work and then ends its handle. When the work fails, its own failure is what the caller sees,
    /// with a failure to end the handle attached; when only ending fails, that failure is thrown.
    /// </summary>
    /// <typeparam name="THandle">The handle's type.</typeparam>
    /// <typeparam name="T">What the work returns.</typeparam>
    /// <param name="work">The work.</param>
    /// <param name="handle">The handle given to the work.</param>
    /// <returns>What the work returned.</returns>
    private static T RunWork<THandle, T>(Func<THandle, T> work, CaseTransaction handle)
        where THandle : ICaseReader
    {
        T result;
        try
        {
            result = work((THandle)(ICaseReader)handle);
        }
        catch (Exception workFailure)
        {
            try
            {
                handle.End();
            }
            catch (Exception endFailure)
            {
                throw new AggregateException(workFailure, endFailure);
            }

            throw;
        }

        handle.End();
        return result;
    }

    /// <summary>Gives the journal's line for a new title.</summary>
    /// <param name="title">The new title; empty when the title is taken away.</param>
    /// <param name="first">Whether the case had no title before.</param>
    /// <returns>The line.</returns>
    private static string TitleSummary(string title, bool first)
    {
        if (title.Length == 0)
        {
            return "Titeln togs bort";
        }

        return first ? $"Titeln sattes till {title}" : $"Titeln ändrades till {title}";
    }

    /// <summary>Reads the title in force: the last record of the chain <c>cases.title</c>.</summary>
    /// <param name="database">The open database with an up-to-date schema.</param>
    /// <returns>The title, or an empty text when none has been set.</returns>
    private static string ReadTitle(CaseDatabase database)
    {
        using var scope = database.As(GuardMode.Internal);
        using var select = database.Command(
            $"SELECT title FROM {CasesSchema.Title.Table} ORDER BY sequence DESC LIMIT 1");
        return select.ExecuteScalar() as string ?? string.Empty;
    }

    /// <summary>
    /// Records something the Cases module did in a case that is open and known to the store, and saves; it takes the
    /// gate like any other operation, since the case may be saving itself.
    /// </summary>
    /// <param name="draft">What happened.</param>
    internal void JournalAndSave(JournalDraft draft)
    {
        lock (_gate)
        {
            using var operation = BeginOperation();
            AppendOwnJournal(draft);
            SaveUnlocked();
        }
    }

    /// <summary>Records something the Cases module did, in a transaction of its own.</summary>
    /// <param name="draft">What happened.</param>
    /// <returns>The sealed entry.</returns>
    internal JournalEntry AppendOwnJournal(JournalDraft draft)
    {
        using var scope = Database.As(GuardMode.Internal);
        using var transaction = Database.Begin();
        var entry = AppendJournalIn(transaction, draft);
        transaction.Commit();
        _autosave.Written();
        return entry;
    }

    /// <summary>
    /// Writes the case file (the header and then the working copy) to a new file that replaces the old, records the
    /// new file's hash for recovery and moves this computer's anchors forward.
    /// </summary>
    internal void SaveUnlocked()
    {
        _ = Database;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var output = new FileStream(
            _paths.SaveFile, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize))
        {
            using (var header = new MemoryStream())
            {
                CaseContainer.WriteHeader(header, _header);
                hash.AppendData(header.GetBuffer(), 0, (int)header.Length);
                header.WriteTo(output);
            }

            using (var input = new FileStream(
                _paths.WorkingCopy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CopyBufferSize))
            {
                CaseFiles.CopyHashing(input, output, hash);
            }

            output.Flush(flushToDisk: true);
        }

        // Until the new file has replaced the old, the working copy belongs to both, so that a failed or interrupted
        // replace still recovers instead of becoming a conflict.
        var saved = hash.GetHashAndReset();
        CaseFiles.WriteBase(_paths, CaseFileHash is null ? [saved] : [CaseFileHash, saved]);
        File.Move(_paths.SaveFile, _paths.CaseFile, overwrite: true);
        CaseFileHash = saved;
        CaseFiles.WriteBase(_paths, [saved]);
        WriteAnchors();
        if (_autosave.Saved())
        {
            SayHowSavesGo(new CaseSaveState(0));
        }
    }

    /// <summary>
    /// Raises <see cref="SaveStateChanged"/> for every handler on its own, so that a handler that throws is never taken
    /// for a save that failed and never keeps the others from hearing it.
    /// </summary>
    /// <param name="state">How the saves go.</param>
    private void SayHowSavesGo(CaseSaveState state)
    {
        foreach (var handler in SaveStateChanged?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<CaseSaveState>)handler)(this, state);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The save itself went as the state says; a listener's own failure is its own.
            }
        }
    }

    /// <summary>
    /// Brings the recovery envelopes up to date when the case is opened, with one entry in the journal: removes the
    /// envelopes for keys this build revokes (so that a compromised key does not open later copies; copies made
    /// before still hold them), and adds one for the configured key when the file has none (an older case, or a key
    /// that has been replaced). Envelopes for keys that are merely unknown here (a newer key, or another
    /// organisation's) stay. The caller saves.
    /// </summary>
    /// <returns><see langword="true"/> when the envelopes changed.</returns>
    internal bool UpdateRecoveryEnvelopes()
    {
        var recoveryKey = _services.Options.RecoveryKey;
        var (kept, removed) = RecoveryEnvelopes.Without(_header, _services.Options.RevokedRecoveryKeys);
        var add = recoveryKey is not null
            && !RecoveryEnvelopes.Fingerprints(_header).Contains(recoveryKey.Fingerprint, StringComparer.Ordinal)
            && kept.Count < CaseContainer.MaximumEnvelopes;
        if (!add && removed.Count == 0)
        {
            // At the limit a header with one more envelope would not be read again; the file keeps its ways in.
            return false;
        }

        var envelopes = add ? kept.Append(RecoveryEnvelopes.Create(Info.Id, _fileKey, recoveryKey!)) : kept;
        AppendOwnJournal(new JournalDraft(
            CaseJournalKinds.RecoveryAccessChanged,
            RecoverySummary(add, removed.Count > 0),
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                [RecoveryDetails.Fingerprint] = add ? recoveryKey!.Fingerprint : null,
                [RecoveryDetails.Removed] = removed,
            })));
        _header = _header.WithEnvelopes(envelopes);
        return true;
    }

    /// <summary>
    /// Refuses to add an envelope to a header that is full, before anything is journalled, since a header with one
    /// more could not be written (and every later save, and the close, would fail).
    /// </summary>
    /// <param name="kept">The envelopes the header keeps beside the one to add.</param>
    /// <exception cref="CaseException"><see cref="CaseError.TooManyEnvelopes"/>.</exception>
    private static void RequireRoomFor(IReadOnlyCollection<System.Text.Json.Nodes.JsonObject> kept)
    {
        if (kept.Count >= CaseContainer.MaximumEnvelopes)
        {
            throw new CaseException(CaseError.TooManyEnvelopes);
        }
    }

    /// <summary>The journal's line for a change of the recovery envelopes.</summary>
    /// <param name="added">Whether an envelope for the configured key was added.</param>
    /// <param name="removed">Whether envelopes for revoked keys were removed.</param>
    /// <returns>The line.</returns>
    private static string RecoverySummary(bool added, bool removed) => (added, removed) switch
    {
        (true, false) => "Ärendet fick ett kuvert för organisationens återställningsnyckel",
        (true, true) => "Ärendet fick ett kuvert för organisationens nya återställningsnyckel; återkallade togs bort",
        _ => "Kuverten för återkallade återställningsnycklar togs bort ur ärendet",
    };

    /// <summary>Derives a key from the vault, giving a locked vault the case's own error.</summary>
    /// <param name="purposeKeys">The vault's keys per purpose.</param>
    /// <param name="purpose">The purpose.</param>
    /// <param name="destination">Receives the key.</param>
    /// <exception cref="CaseException"><see cref="CaseError.VaultLocked"/>.</exception>
    internal static void DeriveVaultKey(IPurposeKeys purposeKeys, string purpose, Span<byte> destination)
    {
        try
        {
            purposeKeys.DeriveKey(purpose, destination);
        }
        catch (VaultException exception) when (exception.Error == VaultError.Locked)
        {
            throw new CaseException(CaseError.VaultLocked, exception);
        }
    }

    /// <summary>
    /// Compares the case with this computer's anchors when it is opened, and seals what does not match in the
    /// journal: chains that no longer reach their anchors (the case was rolled back, rewritten, or replaced by a copy
    /// that went another way), anchors that cannot be read, and anchors that are missing although this vault has
    /// saved the case before. The anchors are then replaced at the next save, since the journal now holds the
    /// evidence; without the entry the anchors would stay behind for ever and hide a later rollback.
    /// </summary>
    /// <returns><see langword="true"/> when something was journalled.</returns>
    internal bool ReconcileAnchors()
    {
        if (!TryReadAnchors(out var anchors))
        {
            // The file is held by another program just now: nothing is concluded, and the anchors are left alone.
            _anchorsUnavailable = true;
            return false;
        }

        if (anchors is null)
        {
            JournalAnchors("Ankarna på den här datorn gick inte att läsa och ersätts", AnchorProblem.Unreadable, []);
            return true;
        }

        if (anchors.Count == 0)
        {
            if (!VaultHasEnvelope())
            {
                return false;
            }

            JournalAnchors("Ankarna på den här datorn saknas och ersätts", AnchorProblem.Missing, []);
            return true;
        }

        var database = Database;
        List<ChainAnchor> missed;
        using (database.As(GuardMode.Internal))
        using (var transaction = database.Begin(deferred: true))
        {
            missed =
            [
                .. anchors.Where(anchor =>
                    _chains.FirstOrDefault(chain => chain.Name == anchor.Chain) is not { } chain
                    || !CaseChecks.Reaches(database, transaction, chain, anchor)),
            ];
        }

        if (missed.Count == 0)
        {
            return false;
        }

        JournalAnchors(
            "Ärendet når inte längre det den här datorn sparade senast (äldre kopia eller ändrat)",
            AnchorProblem.Diverged,
            missed);
        return true;
    }

    /// <summary>Seals an anchor problem in the journal and lets the next save replace the anchors.</summary>
    /// <param name="summary">The summary.</param>
    /// <param name="problem">What was wrong.</param>
    /// <param name="missed">The anchors that the case no longer reaches.</param>
    private void JournalAnchors(string summary, AnchorProblem problem, IReadOnlyList<ChainAnchor> missed)
    {
        var details = JsonSerializer.Serialize(new AnchorDetails(
            problem.ToString(),
            [.. missed.Select(anchor => new AnchorDetail(
                anchor.Chain, anchor.Sequence, Convert.ToHexStringLower(anchor.Hash)))]));
        AppendOwnJournal(new JournalDraft(CaseJournalKinds.AnchorsReplaced, summary, details));
        _replaceAnchors = true;
    }

    /// <summary>
    /// Moves each chain's anchor to its head, but only where the chain still reaches its old anchor, unless the
    /// journal has just recorded why the anchors are replaced. A case that was changed while it was open elsewhere in
    /// this process keeps its old anchors, so that the verification goes on showing it.
    /// </summary>
    private void WriteAnchors()
    {
        IReadOnlyList<ChainAnchor>? anchors = [];
        if (_anchorsUnavailable || (!_replaceAnchors && !TryReadAnchors(out anchors)) || anchors is null)
        {
            return;
        }

        var database = Database;
        var updated = anchors.ToDictionary(anchor => anchor.Chain, StringComparer.Ordinal);
        try
        {
            using var scope = database.As(GuardMode.Internal);
            using var transaction = database.Begin(deferred: true);
            foreach (var chain in _chains.Where(chain => _shapes.ContainsKey(chain.Name)))
            {
                var kept = updated.GetValueOrDefault(chain.Name);
                var (sequence, hash) = HashChain.ReadHead(database, transaction, chain.Name);
                if (hash is not null && (kept is null || CaseChecks.Reaches(database, transaction, chain, kept)))
                {
                    updated[chain.Name] = new ChainAnchor(chain.Name, sequence, hash);
                }
            }
        }
        catch (SqliteException)
        {
            // A database that cannot be read is saved as it is, and its anchors stay where they were.
            return;
        }

        AnchorStore.Write(_services.Options.AnchorDirectory, Info.Id, _anchorKey, [.. updated.Values]);
        _replaceAnchors = false;
    }

    /// <summary>Reads this computer's anchors of the case.</summary>
    /// <param name="anchors">The anchors, or <see langword="null"/> when they are malformed or changed.</param>
    /// <returns><see langword="false"/> when the file could not be read just now.</returns>
    private bool TryReadAnchors(out IReadOnlyList<ChainAnchor>? anchors)
    {
        try
        {
            anchors = AnchorStore.Read(_services.Options.AnchorDirectory, Info.Id, _anchorKey);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            anchors = null;
            return false;
        }
    }

    /// <summary>Seals a record as the Cases module, in the narrow sealing mode.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="values">The values.</param>
    /// <returns>The sealed record.</returns>
    private SealedRecord SealAsCases(SqliteTransaction transaction, SealedChain chain, IReadOnlyList<object?> values)
    {
        var database = Database;
        var shape = _shapes.GetValueOrDefault(chain.Name) ?? throw new CaseException(CaseError.Corrupt);

        // Everything that can fail on the values is done before any rights are given; the sealing itself may only
        // insert into this table and move the head.
        var normalized = HashChain.Normalize(chain, shape, values);
        using var scope = database.AsSealing(chain.Table);
        return HashChain.Append(database, transaction, chain, normalized);
    }

    /// <summary>Gets the operator from the vault for the journal.</summary>
    /// <returns>The profile.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.VaultLocked"/> or <see cref="CaseError.NoOperatorProfile"/>.
    /// </exception>
    private OperatorProfile CurrentOperator()
    {
        try
        {
            return _services.Profiles.Get() ?? throw new CaseException(CaseError.NoOperatorProfile);
        }
        catch (VaultException exception) when (exception.Error == VaultError.Locked)
        {
            throw new CaseException(CaseError.VaultLocked, exception);
        }
    }

    /// <summary>Checks whether this vault has an envelope in the file that opens to this case's file key.</summary>
    /// <returns><see langword="true"/> when it has.</returns>
    private bool VaultHasEnvelope()
    {
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        var opened = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            DeriveVaultKey(_services.PurposeKeys, CaseCrypto.EnvelopePurpose, wrappingKey);
            return KeyEnvelopes.TryOpenOperator(_header, wrappingKey, opened)
                && CryptographicOperations.FixedTimeEquals(opened, _fileKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(opened);
        }
    }

    /// <summary>Gives the envelopes without the sharing envelope.</summary>
    /// <returns>The envelopes.</returns>
    private IEnumerable<System.Text.Json.Nodes.JsonObject> WithoutSharingEnvelope() =>
        _header.Envelopes.Where(item => !KeyEnvelopes.IsKind(item, KeyEnvelopes.PasswordKind));

    /// <summary>Checks for a sharing envelope while the gate is held.</summary>
    /// <returns><see langword="true"/> when there is one.</returns>
    private bool HasSharingPasswordUnlocked() => KeyEnvelopes.OfKind(_header, KeyEnvelopes.PasswordKind).Any();

    /// <summary>
    /// Starts one of the case's own operations: the case must be open and nothing may be running, also not on this
    /// thread (the gate lets the same thread in again, for example from a handler of an exception during an
    /// operation).
    /// </summary>
    /// <returns>The operation, which ends when disposed.</returns>
    private Operation BeginOperation()
    {
        EnsureIdle();
        _inWork = true;
        return new Operation(this);
    }

    /// <summary>Refuses a closed or closing case and calls made from inside a module's work.</summary>
    /// <exception cref="CaseException"><see cref="CaseError.Closed"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Called from inside <see cref="Read{T}"/> or <see cref="Write{T}"/>.
    /// </exception>
    private void EnsureIdle()
    {
        if (_closing)
        {
            throw new CaseException(CaseError.Closed);
        }

        _ = Database;
        if (_inWork)
        {
            throw new InvalidOperationException(
                "The case cannot be used from inside a read or a write; use the handle.");
        }
    }

    /// <summary>Closes the connection once.</summary>
    private void ReleaseDatabase()
    {
        _database?.Dispose();
        _database = null;
    }

    /// <summary>Clears the keys, releases the lock and tells the store, whatever else failed.</summary>
    private void ReleaseResources()
    {
        try
        {
            ReleaseDatabase();
        }
        finally
        {
            _database = null;
            _autosave.Dispose();
            CryptographicOperations.ZeroMemory(_fileKey);
            CryptographicOperations.ZeroMemory(_anchorKey);
            try
            {
                _lock.Dispose();
            }
            finally
            {
                _onClosed(this);
            }
        }
    }

    /// <summary>
    /// Called on the timer's thread when a save may be due. Never waits for the gate: when another operation holds
    /// it, the save is tried again a little later, so that the timer's thread is never held up and a closing never
    /// waits behind it.
    /// </summary>
    private void OnAutosaveDue()
    {
        try
        {
            // A closing saves by itself; a lock of the vault must find nothing left to wait for.
            if (_closing)
            {
                return;
            }

            if (!_gate.TryEnter())
            {
                _autosave.Busy();
                return;
            }

            try
            {
                AutosaveUnlocked();
            }
            finally
            {
                _gate.Exit();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Nothing may leave the timer's thread, which would end the process; the working copy keeps the writes,
            // and a closing (or the next write) saves them.
        }
    }

    /// <summary>
    /// Saves, with the gate held, when the case is open and idle and the time has come. A save that fails leaves the
    /// writes unsaved (the working copy still holds them and is recovered after a crash) and is tried again later.
    /// </summary>
    private void AutosaveUnlocked()
    {
        if (_closing || _database is null)
        {
            return;
        }

        if (_inWork)
        {
            // The same thread is inside an operation (the timer called back from within it); try after it has ended.
            _autosave.Busy();
            return;
        }

        if (!_autosave.IsDue())
        {
            return;
        }

        AutosaveChecked?.Invoke();
        try
        {
            // Inside the try: a lock of the vault may set the closing flag from another thread at any moment.
            using var operation = BeginOperation();
            SaveUnlocked();
        }
        catch (CaseException exception) when (exception.Error == CaseError.Closed)
        {
            // The case began closing after the checks above; the closing saves it.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An exception must not leave the timer's thread; the case file is untouched until the atomic replace.
            // The listeners are told after, each on its own, so that none of them is taken for the save.
            SayHowSavesGo(new CaseSaveState(_autosave.Failed()));
        }
    }

    /// <summary>Gives the schema versions as a JSON object for the journal.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The JSON element.</returns>
    internal static JsonElement SchemaDetails(CaseDatabase database)
    {
        using var scope = database.As(GuardMode.Internal);
        return JsonSerializer.SerializeToElement(SchemaMigrator.ReadVersions(database)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    /// <summary>Ends one of the case's own operations when disposed.</summary>
    /// <param name="owner">The case.</param>
    private readonly struct Operation(Case owner) : IDisposable
    {
        /// <summary>Ends the operation.</summary>
        public void Dispose() => owner._inWork = false;
    }
}
