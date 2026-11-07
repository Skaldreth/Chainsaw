using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// An open case: the connection to the encrypted working copy, the file key in pinned memory, the header with its
/// envelopes and the lock that keeps the case from being opened twice.
/// </summary>
/// <remarks>
/// Every operation holds <see cref="_gate"/>, so they run one at a time. The file key is kept while the case is open
/// only to wrap new envelopes; it is cleared when the case closes.
/// </remarks>
internal sealed class Case : ICase
{
    /// <summary>Serialises every operation on the case.</summary>
    private readonly Lock _gate = new();

    /// <summary>The services the case uses.</summary>
    private readonly CaseServices _services;

    /// <summary>The case's files.</summary>
    private readonly CasePaths _paths;

    /// <summary>The file key in a pinned array; cleared on close.</summary>
    private readonly byte[] _fileKey;

    /// <summary>The lock file, held open while the case is open.</summary>
    private readonly FileStream _lock;

    /// <summary>Called once when the case has closed, so that the store forgets it.</summary>
    private readonly Action<Case> _onClosed;

    /// <summary>
    /// The declared column types of each sealed chain whose table has the right shape, read once after the schema is
    /// up to date.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _shapes = new(StringComparer.Ordinal);

    /// <summary>The header with the current envelopes.</summary>
    private CaseHeader _header;

    /// <summary>The connection; <see langword="null"/> after close.</summary>
    private CaseDatabase? _database;

    /// <summary>Whether another module's work is running, so that nested calls are refused.</summary>
    private bool _inWork;

    /// <summary>Wraps an opened database; the factory methods in <see cref="CaseStore"/> build it.</summary>
    /// <param name="services">The services.</param>
    /// <param name="paths">The case's files.</param>
    /// <param name="header">The header.</param>
    /// <param name="fileKey">The file key; the case owns and clears it.</param>
    /// <param name="lockFile">The held lock file; the case owns and closes it.</param>
    /// <param name="database">The open database with an up-to-date schema.</param>
    /// <param name="info">The case information.</param>
    /// <param name="onClosed">Called once when the case has closed.</param>
    public Case(
        CaseServices services,
        CasePaths paths,
        CaseHeader header,
        byte[] fileKey,
        FileStream lockFile,
        CaseDatabase database,
        CaseInfo info,
        Action<Case> onClosed)
    {
        _services = services;
        _paths = paths;
        _header = header;
        _fileKey = fileKey;
        _lock = lockFile;
        _database = database;
        _onClosed = onClosed;
        Info = info;
        foreach (var chain in services.Catalog.Chains)
        {
            // A table changed outside Chainsaw leaves its chain without a shape: nothing more is sealed in it, and
            // the verification reports it.
            if (HashChain.ReadShape(database, null, chain) is { } shape)
            {
                _shapes[chain.Name] = shape;
            }
        }
    }

    /// <inheritdoc />
    public CaseInfo Info { get; }

    /// <inheritdoc />
    public string Path => _paths.CaseFile;

    /// <inheritdoc />
    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _database is null;
            }
        }
    }

    /// <inheritdoc />
    public bool HasSharingPassword
    {
        get
        {
            lock (_gate)
            {
                return KeyEnvelopes.OfKind(_header, KeyEnvelopes.PasswordKind).Any();
            }
        }
    }

    /// <summary>Gets the open database, for <see cref="CaseTransaction"/> and the verifier.</summary>
    internal CaseDatabase Database => _database ?? throw new CaseException(CaseError.Closed);

    /// <inheritdoc />
    public T Read<T>(Func<ICaseReader, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            var database = EnterWork();
            using var transaction = database.Begin(deferred: true);
            var reader = new CaseTransaction(this, transaction, writable: false);
            try
            {
                database.Mode = GuardMode.Reading;
                return work(reader);
            }
            finally
            {
                ExitWork(reader, database);
            }
        }
    }

    /// <inheritdoc />
    public T Write<T>(Func<ICaseTransaction, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            var database = EnterWork();
            using var transaction = database.Begin();
            var scope = new CaseTransaction(this, transaction, writable: true);
            try
            {
                database.Mode = GuardMode.Writing;
                var result = work(scope);
                ExitWork(scope, database);
                transaction.Commit();
                return result;
            }
            finally
            {
                ExitWork(scope, database);
            }
        }
    }

    /// <inheritdoc />
    public JournalEntry AppendJournal(JournalDraft draft)
    {
        JournalRecords.Validate(draft);
        lock (_gate)
        {
            var database = EnterWork();
            try
            {
                using var transaction = database.Begin();
                var entry = AppendJournalIn(transaction, draft);
                transaction.Commit();
                return entry;
            }
            finally
            {
                _inWork = false;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<JournalEntry> ReadJournal(long fromSequence = 1, int maximumCount = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        lock (_gate)
        {
            var database = EnterWork();
            try
            {
                return JournalRecords.Read(database, null, fromSequence, maximumCount);
            }
            finally
            {
                _inWork = false;
            }
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
            EnsureIdle();
            var envelope = KeyEnvelopes.CreatePassword(
                Info.Id, _fileKey, password, _services.Options.SharingIterations);
            var replaced = HasSharingPasswordUnlocked();
            AppendOwnJournal(new JournalDraft(
                CaseJournalKinds.SharingPasswordSet,
                replaced ? "Delningslösenordet byttes" : "Ett delningslösenord lades till"));
            _header = _header.WithEnvelopes(
                _header.Envelopes.Where(item => !KeyEnvelopes.IsKind(item, KeyEnvelopes.PasswordKind)).Append(envelope));
            SaveUnlocked();
        }
    }

    /// <inheritdoc />
    public void RemoveSharingPassword()
    {
        lock (_gate)
        {
            EnsureIdle();
            if (!HasSharingPasswordUnlocked())
            {
                return;
            }

            if (!VaultHasEnvelope())
            {
                throw new CaseException(CaseError.WouldLoseAccess);
            }

            AppendOwnJournal(new JournalDraft(CaseJournalKinds.SharingPasswordRemoved, "Delningslösenordet togs bort"));
            _header = _header.WithEnvelopes(
                _header.Envelopes.Where(item => !KeyEnvelopes.IsKind(item, KeyEnvelopes.PasswordKind)));
            SaveUnlocked();
        }
    }

    /// <inheritdoc />
    public void AddOperatorAccess()
    {
        lock (_gate)
        {
            EnsureIdle();
            if (VaultHasEnvelope())
            {
                return;
            }

            var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
            try
            {
                DeriveEnvelopeKey(_services.PurposeKeys, wrappingKey);
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
    public void Save()
    {
        lock (_gate)
        {
            EnsureIdle();
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

            EnsureIdle();
            try
            {
                SaveUnlocked();
                ReleaseDatabase();
                File.Delete(_paths.WorkingCopy);
                File.Delete(_paths.WorkingJournal);
            }
            finally
            {
                // On a failed save the working copy stays, so that the next opening recovers it.
                ReleaseDatabase();
                CryptographicOperations.ZeroMemory(_fileKey);
                _lock.Dispose();
            }
        }

        _onClosed(this);
    }

    /// <summary>
    /// Drops the case without saving, as if the process had died: the working copy stays for recovery. Only tests
    /// use it, to check the recovery.
    /// </summary>
    internal void Abandon()
    {
        lock (_gate)
        {
            ReleaseDatabase();
            CryptographicOperations.ZeroMemory(_fileKey);
            _lock.Dispose();
        }

        _onClosed(this);
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

    /// <summary>Adds a journal entry in a transaction that is already open, as the Cases module.</summary>
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

    /// <summary>Adds a record to a sealed chain in a transaction that is already open.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain's name.</param>
    /// <param name="values">The values.</param>
    /// <returns>The sealed record.</returns>
    internal SealedRecord AppendSealedIn(SqliteTransaction transaction, string chain, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return SealAsCases(transaction, _services.Catalog.Chain(chain), values);
    }

    /// <summary>
    /// Checks the whole database page by page and then every sealed chain, in one read transaction. A page that
    /// does not authenticate makes the database unsound; the chains are then not walked, since SQLite cannot read
    /// them reliably.
    /// </summary>
    /// <returns>Whether the database is sound, and one result per chain, ordered by name.</returns>
    internal (bool DatabaseIntact, IReadOnlyList<ChainVerification> Chains) VerifyChains()
    {
        lock (_gate)
        {
            var database = EnterWork();
            try
            {
                using var transaction = database.Begin(deferred: true);
                if (!HashChain.QuickCheck(database, transaction))
                {
                    return (false, []);
                }

                return (true, [.. _services.Catalog.Chains.Select(chain => HashChain.Verify(database, transaction, chain))]);
            }
            catch (SqliteException)
            {
                return (false, []);
            }
            finally
            {
                _inWork = false;
            }
        }
    }

    /// <summary>Records something the Cases module did, in a transaction of its own.</summary>
    /// <param name="draft">What happened.</param>
    internal void AppendOwnJournal(JournalDraft draft)
    {
        using var transaction = Database.Begin();
        AppendJournalIn(transaction, draft);
        transaction.Commit();
    }

    /// <summary>Writes the case file: the header and then the working copy, to a new file that replaces the old.</summary>
    internal void SaveUnlocked()
    {
        _ = Database;
        using (var output = new FileStream(
            _paths.SaveFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920))
        {
            CaseContainer.WriteHeader(output, _header);
            using (var input = new FileStream(
                _paths.WorkingCopy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 81920))
            {
                input.CopyTo(output);
            }

            output.Flush(flushToDisk: true);
        }

        File.Move(_paths.SaveFile, _paths.CaseFile, overwrite: true);
    }

    /// <summary>Derives this vault's envelope key, giving a locked vault the case's own error.</summary>
    /// <param name="purposeKeys">The vault's keys per purpose.</param>
    /// <param name="destination">Receives the key.</param>
    /// <exception cref="CaseException"><see cref="CaseError.VaultLocked"/>.</exception>
    internal static void DeriveEnvelopeKey(IPurposeKeys purposeKeys, Span<byte> destination)
    {
        try
        {
            purposeKeys.DeriveKey(CaseCrypto.EnvelopePurpose, destination);
        }
        catch (VaultException exception) when (exception.Error == VaultError.Locked)
        {
            throw new CaseException(CaseError.VaultLocked, exception);
        }
    }

    /// <summary>Seals a record as the Cases module and returns to the mode the work was in.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="values">The values.</param>
    /// <returns>The sealed record.</returns>
    private SealedRecord SealAsCases(SqliteTransaction transaction, SealedChain chain, IReadOnlyList<object?> values)
    {
        var database = Database;
        var mode = database.Mode;
        database.Mode = GuardMode.Internal;
        try
        {
            var shape = _shapes.GetValueOrDefault(chain.Name) ?? throw new CaseException(CaseError.Corrupt);
            return HashChain.Append(database, transaction, chain, shape, values);
        }
        finally
        {
            database.Mode = mode;
        }
    }

    /// <summary>Gets the operator from the vault for the journal.</summary>
    /// <returns>The profile.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.VaultLocked"/> or <see cref="CaseError.NoOperatorProfile"/>.</exception>
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
            DeriveEnvelopeKey(_services.PurposeKeys, wrappingKey);
            return KeyEnvelopes.TryOpenOperator(_header, wrappingKey, opened)
                && CryptographicOperations.FixedTimeEquals(opened, _fileKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(opened);
        }
    }

    /// <summary>Checks for a sharing envelope while the gate is held.</summary>
    /// <returns><see langword="true"/> when there is one.</returns>
    private bool HasSharingPasswordUnlocked() => KeyEnvelopes.OfKind(_header, KeyEnvelopes.PasswordKind).Any();

    /// <summary>Starts another module's work: the case must be open and no other work may be running.</summary>
    /// <returns>The database.</returns>
    private CaseDatabase EnterWork()
    {
        EnsureIdle();
        _inWork = true;
        return Database;
    }

    /// <summary>Ends a module's work: the handle stops working and the guard returns to the Cases module.</summary>
    /// <param name="scope">The handle given to the work.</param>
    /// <param name="database">The database.</param>
    private void ExitWork(CaseTransaction scope, CaseDatabase database)
    {
        scope.End();
        database.Mode = GuardMode.Internal;
        _inWork = false;
    }

    /// <summary>Refuses a closed case and calls made from inside a module's work.</summary>
    /// <exception cref="CaseException"><see cref="CaseError.Closed"/>.</exception>
    /// <exception cref="InvalidOperationException">Called from inside <see cref="Read{T}"/> or <see cref="Write{T}"/>.</exception>
    private void EnsureIdle()
    {
        _ = Database;
        if (_inWork)
        {
            throw new InvalidOperationException("The case cannot be used from inside a read or a write; use the handle.");
        }
    }

    /// <summary>Closes the connection once.</summary>
    private void ReleaseDatabase()
    {
        _database?.Dispose();
        _database = null;
    }

    /// <summary>Writes the schema versions as JSON details for the journal.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The JSON.</returns>
    internal static string SchemaDetails(CaseDatabase database) =>
        JsonSerializer.Serialize(SchemaMigrator.ReadVersions(database)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToString(CultureInfo.InvariantCulture)));
}
