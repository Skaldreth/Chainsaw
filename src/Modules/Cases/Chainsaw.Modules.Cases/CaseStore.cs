using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The cases folder: creates, opens and lists case files, keeps track of the open cases and closes them all when the
/// vault is locked.
/// </summary>
internal sealed class CaseStore : ICaseStore, IDisposable
{
    /// <summary>The services every case uses.</summary>
    private readonly CaseServices _services;

    /// <summary>Guards <see cref="_open"/>.</summary>
    private readonly Lock _openGate = new();

    /// <summary>The cases that are open now.</summary>
    private readonly HashSet<Case> _open = [];

    /// <summary>
    /// Counts the locks of the vault, so that an opening that a lock and an unlock overtook is closed.
    /// </summary>
    private long _locks;

    /// <summary>The background closing started by the last lock of the vault.</summary>
    private Task _closing = Task.CompletedTask;

    /// <summary>Creates the store and starts following the vault's state.</summary>
    /// <param name="vault">The vault.</param>
    /// <param name="purposeKeys">The vault's keys per purpose.</param>
    /// <param name="profiles">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The options.</param>
    /// <param name="contributors">Every module's schema, the Cases module's own included.</param>
    public CaseStore(
        IVault vault,
        IPurposeKeys purposeKeys,
        IOperatorProfileStore profiles,
        IClock clock,
        CasesOptions options,
        IEnumerable<ICaseSchemaContributor> contributors)
    {
        _services = new CaseServices(vault, purposeKeys, profiles, clock, options, new SchemaCatalog(contributors));
        vault.StateChanged += OnVaultStateChanged;
    }

    /// <inheritdoc />
    public string Directory => _services.Options.Directory;

    /// <inheritdoc />
    public RecoveryPublicKey? RecoveryKey => _services.Options.RecoveryKey;

    /// <inheritdoc/>
    public IReadOnlyList<string> RevokedRecoveryKeys =>
        [.. _services.Options.RevokedRecoveryKeys.Order(StringComparer.Ordinal)];

    /// <summary>Gets the schema catalog, for the verifier.</summary>
    internal SchemaCatalog Catalog => _services.Catalog;

    /// <inheritdoc />
    public IReadOnlyList<CaseFileInfo> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return
        [
            .. new DirectoryInfo(Directory).EnumerateFiles()
                .Where(file => string.Equals(file.Extension, CasePaths.Extension, StringComparison.OrdinalIgnoreCase))
                .Select(file => new CaseFileInfo(
                    System.IO.Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)))
                .Where(file => CaseNumberRules.IsValid(file.CaseNumber))
                .OrderBy(file => file.CaseNumber, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <inheritdoc />
    public ICase Create(string caseNumber)
    {
        var locks = LockCount();
        var paths = PathsFor(caseNumber);
        RequireUnlocked();
        var profile = CurrentProfile();
        System.IO.Directory.CreateDirectory(Directory);
        var lockFile = AcquireLock(paths);
        byte[]? fileKey = null;
        byte[]? anchorKey = null;
        CaseDatabase? database = null;
        var createdHere = false;
        try
        {
            if (File.Exists(paths.CaseFile))
            {
                throw new CaseException(CaseError.AlreadyExists);
            }

            // A working copy without a case file is left by a creation that never saved; it holds nothing yet.
            createdHere = true;
            DeleteWorkingFiles(paths);
            fileKey = CaseCrypto.NewFileKey();
            anchorKey = DeriveAnchorKey();
            var header = NewHeader(fileKey);
            database = OpenDatabase(paths.WorkingCopy, fileKey, header.CaseId);
            SchemaMigrator.Run(database, _services.Catalog);
            var info = new CaseInfo(header.CaseId, caseNumber, _services.Clock.UtcNow.ToUniversalTime());
            WriteInfo(database, info);
            var opened = new Case(
                _services, paths, header, (fileKey, anchorKey), lockFile, database, info, Forget)
            {
                LocksAtOpening = locks,
            };
            (database, fileKey, anchorKey) = (null, null, null);
            FinishOpening(opened, () =>
            {
                opened.AppendOwnJournal(new JournalDraft(
                    CaseJournalKinds.Created,
                    $"Ärendet {caseNumber} skapades av {profile.Name}",
                    CreationDetails(info, opened)));
                opened.SaveUnlocked();
            });
            return opened;
        }
        catch
        {
            database?.Dispose();
            Clear(fileKey, anchorKey);
            if (createdHere)
            {
                DeleteWorkingFiles(paths);
                File.Delete(paths.CaseFile);
            }

            lockFile.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public ICase Open(string caseNumber)
    {
        var locks = LockCount();
        var paths = PathsFor(caseNumber);
        RequireExists(paths);
        RequireUnlocked();
        var lockFile = AcquireLock(paths);
        var fileKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            var header = ReadHeader(paths);
            Case.DeriveVaultKey(_services.PurposeKeys, CaseCrypto.EnvelopePurpose, wrappingKey);
            if (!KeyEnvelopes.TryOpenOperator(header, wrappingKey, fileKey))
            {
                throw new CaseException(CaseError.NoAccess);
            }

            return OpenLocked(paths, lockFile, header, fileKey, new Opening(OpeningKind.Vault, locks));
        }
        catch
        {
            Clear(fileKey);
            lockFile.Dispose();
            throw;
        }
        finally
        {
            Clear(wrappingKey);
        }
    }

    /// <inheritdoc />
    public ICase OpenWithPassword(string path, ReadOnlySpan<char> password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var locks = LockCount();
        var paths = new CasePaths(System.IO.Path.GetFullPath(path));
        RequireExists(paths);
        RequireUnlocked();
        var lockFile = AcquireLock(paths);
        var fileKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            var header = ReadHeader(paths);
            if (!KeyEnvelopes.TryOpenPassword(header, password, fileKey))
            {
                throw new CaseException(CaseError.NoAccess);
            }

            return OpenLocked(paths, lockFile, header, fileKey, new Opening(OpeningKind.Password, locks));
        }
        catch
        {
            Clear(fileKey);
            lockFile.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public ICase OpenWithRecoveryKey(string path, ECDiffieHellman privateKey, string administrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(administrator);
        var fingerprint = RecoveryPublicKey.FromPrivateKey(privateKey).Fingerprint;
        var locks = LockCount();
        var paths = new CasePaths(System.IO.Path.GetFullPath(path));
        RequireExists(paths);
        RequireUnlocked();
        var lockFile = AcquireLock(paths);
        var fileKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            var header = ReadHeader(paths);
            if (!RecoveryEnvelopes.TryOpen(header, privateKey, fileKey))
            {
                throw new CaseException(CaseError.NoAccess);
            }

            var opening = new Opening(OpeningKind.Recovery, locks, administrator.Trim(), fingerprint);
            return OpenLocked(paths, lockFile, header, fileKey, opening);
        }
        catch
        {
            Clear(fileKey);
            lockFile.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ReadRecoveryFingerprints(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var paths = new CasePaths(System.IO.Path.GetFullPath(path));
        RequireExists(paths);
        return RecoveryEnvelopes.Fingerprints(ReadHeader(paths));
    }

    /// <summary>Stops following the vault and closes every open case.</summary>
    public void Dispose()
    {
        _services.Vault.StateChanged -= OnVaultStateChanged;
        CloseAll();
    }

    /// <summary>Waits until the cases that a lock of the vault started closing have closed; for the tests.</summary>
    internal void WaitForClosing()
    {
        Task closing;
        lock (_openGate)
        {
            closing = _closing;
        }

        closing.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Opens a locked case file whose file key is known: copies the database to a working copy (or recovers the one
    /// left by an interrupted session), brings the schema up to date and journals what happened.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="lockFile">The held lock; the case takes it over, a failure leaves it to the caller.</param>
    /// <param name="header">The header the key was opened from.</param>
    /// <param name="fileKey">The file key; the case takes it over, a failure leaves it to the caller.</param>
    /// <param name="opening">How the file key was opened (for the journal) and the count of vault locks then.</param>
    /// <returns>The open case.</returns>
    private Case OpenLocked(
        CasePaths paths,
        FileStream lockFile,
        CaseHeader header,
        byte[] fileKey,
        Opening opening)
    {
        var recovering = File.Exists(paths.WorkingCopy);
        CaseDatabase? database = null;
        byte[]? anchorKey = null;
        try
        {
            // A recovered working copy must belong to the case file as it is now; if the file was replaced after the
            // working copy was taken from it, neither is overwritten.
            var caseFileHash = recovering
                ? CaseFiles.MatchingBase(paths) ?? throw new CaseException(CaseError.Conflict)
                : CaseFiles.Extract(paths, header.CaseId);

            anchorKey = DeriveAnchorKey();
            database = OpenWorkingCopy(paths, fileKey, header.CaseId, recovering);
            var (applied, info) = PrepareSchema(database, recovering);
            if (info.Id != header.CaseId)
            {
                throw new CaseException(recovering ? CaseError.Conflict : CaseError.Corrupt);
            }

            var opened = Construct(
                () => new Case(
                    _services, paths, header, (fileKey, anchorKey), lockFile, database, info, Forget)
                {
                    CaseFileHash = caseFileHash,
                    LocksAtOpening = opening.Locks,
                },
                recovering);
            (database, anchorKey) = (null, null);
            FinishOpening(opened, () => JournalOpening(opened, recovering, applied, opening));
            return opened;
        }
        catch
        {
            database?.Dispose();
            Clear(anchorKey);
            if (!recovering)
            {
                // Only a working copy made here is removed; a recovered one may hold the only copy of saved work.
                DeleteWorkingFiles(paths);
            }

            throw;
        }
    }

    /// <summary>
    /// Brings the schema up to date and reads the case information; a page that does not read or authenticate makes
    /// the file corrupt (or, for a recovered working copy, a conflict).
    /// </summary>
    /// <param name="database">The open database.</param>
    /// <param name="recovering">Whether the working copy was left by an interrupted session.</param>
    /// <returns>The schema steps that ran and the case information.</returns>
    private (IReadOnlyList<string> Applied, CaseInfo Info) PrepareSchema(CaseDatabase database, bool recovering)
    {
        try
        {
            var applied = SchemaMigrator.Run(database, _services.Catalog);
            return (applied, ReadInfo(database));
        }
        catch (SqliteException exception)
        {
            throw new CaseException(recovering ? CaseError.Conflict : CaseError.Corrupt, exception);
        }
    }

    /// <summary>
    /// Builds the case, which reads its chains; a page that does not authenticate makes the file corrupt.
    /// </summary>
    /// <param name="construct">Builds the case.</param>
    /// <param name="recovering">Whether the working copy was left by an interrupted session.</param>
    /// <returns>The case.</returns>
    private static Case Construct(Func<Case> construct, bool recovering)
    {
        try
        {
            return construct();
        }
        catch (SqliteException exception)
        {
            throw new CaseException(recovering ? CaseError.Conflict : CaseError.Corrupt, exception);
        }
    }

    /// <summary>
    /// Journals what opening the case did (and what does not match this computer's anchors), adds an envelope for the
    /// recovery key when the file lacks one, and saves when anything was journalled.
    /// </summary>
    /// <param name="opened">The open case.</param>
    /// <param name="recovered">Whether a working copy was recovered.</param>
    /// <param name="applied">The schema steps that ran.</param>
    /// <param name="opening">How the file key was opened.</param>
    private static void JournalOpening(Case opened, bool recovered, IReadOnlyList<string> applied, Opening opening)
    {
        var anchorsJournalled = opened.ReconcileAnchors();
        if (recovered)
        {
            opened.AppendOwnJournal(new JournalDraft(
                CaseJournalKinds.Recovered, "Arbetskopian från ett avbrutet pass togs tillbaka in i ärendet"));
        }

        if (applied.Count > 0)
        {
            opened.AppendOwnJournal(new JournalDraft(
                CaseJournalKinds.SchemaUpgraded,
                "Ärendets tabeller uppdaterades till den här versionen av Chainsaw",
                JsonSerializer.Serialize(applied)));
        }

        if (opening.Kind == OpeningKind.Password)
        {
            opened.AppendOwnJournal(new JournalDraft(
                CaseJournalKinds.OpenedWithPassword, "Ärendet öppnades med delningslösenordet"));
        }

        if (opening.Kind == OpeningKind.Recovery)
        {
            opened.AppendOwnJournal(new JournalDraft(
                CaseJournalKinds.OpenedWithRecoveryKey,
                $"Ärendet öppnades med organisationens återställningsnyckel av administratören {opening.Administrator}",
                JsonSerializer.Serialize(new Dictionary<string, string?>
                {
                    [RecoveryDetails.Administrator] = opening.Administrator,
                    [RecoveryDetails.Fingerprint] = opening.Fingerprint,
                })));
        }

        var recoveryAdded = opened.UpdateRecoveryEnvelopes();
        if (recovered || applied.Count > 0 || opening.Kind != OpeningKind.Vault || anchorsJournalled || recoveryAdded)
        {
            opened.SaveUnlocked();
        }
    }

    /// <summary>
    /// Runs the last step of opening while the case is not yet known to the store, so that nothing else can touch it,
    /// and then hands it to the store; a vault locked in between closes it. A failure closes the case.
    /// </summary>
    /// <param name="opened">The case.</param>
    /// <param name="finish">The step.</param>
    private void FinishOpening(Case opened, Action finish)
    {
        try
        {
            finish();
            Track(opened);

            // A lock after this check finds the case among the open ones and closes it; a lock before it, even one
            // followed by an unlock, shows in the count.
            if (_services.Vault.State != VaultState.Unlocked || LockCount() != opened.LocksAtOpening)
            {
                throw new CaseException(CaseError.VaultLocked);
            }

            // Only a case that has opened saves itself; a lock from here on stops it through BeginClosing.
            opened.StartAutosave();
        }
        catch (SqliteException exception)
        {
            // A page that does not authenticate shows up when the opening reads the chains or the anchors.
            opened.Dispose();
            throw new CaseException(CaseError.Corrupt, exception);
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    /// <summary>Clears keys that were not handed over.</summary>
    /// <param name="keys">The keys; <see langword="null"/> entries are skipped.</param>
    private static void Clear(params byte[]?[] keys)
    {
        foreach (var key in keys)
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>Removes a working copy and the files that belong to it.</summary>
    /// <param name="paths">The case's files.</param>
    private static void DeleteWorkingFiles(CasePaths paths)
    {
        File.Delete(paths.WorkingCopy);
        File.Delete(paths.WorkingJournal);
        File.Delete(paths.WorkingCopyTemporary);
        File.Delete(paths.WorkingBase);
    }

    /// <summary>Derives the key that authenticates this computer's anchors.</summary>
    /// <returns>The key in a pinned buffer that the caller hands over or clears.</returns>
    private byte[] DeriveAnchorKey()
    {
        var key = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            Case.DeriveVaultKey(_services.PurposeKeys, AnchorStore.Purpose, key);
            return key;
        }
        catch
        {
            Clear(key);
            throw;
        }
    }

    /// <summary>Gives the files for a case number in the folder.</summary>
    /// <param name="caseNumber">The case number.</param>
    /// <returns>The paths.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.InvalidCaseNumber"/>.</exception>
    private CasePaths PathsFor(string caseNumber) =>
        CaseNumberRules.IsValid(caseNumber)
            ? CasePaths.For(System.IO.Path.GetFullPath(Directory), caseNumber)
            : throw new CaseException(CaseError.InvalidCaseNumber);

    /// <summary>Refuses a case file that does not exist.</summary>
    /// <param name="paths">The case's files.</param>
    /// <exception cref="CaseException"><see cref="CaseError.NotFound"/>.</exception>
    private static void RequireExists(CasePaths paths)
    {
        if (!File.Exists(paths.CaseFile))
        {
            throw new CaseException(CaseError.NotFound);
        }
    }

    /// <summary>Refuses to work while the vault is locked.</summary>
    /// <exception cref="CaseException"><see cref="CaseError.VaultLocked"/>.</exception>
    private void RequireUnlocked()
    {
        if (_services.Vault.State != VaultState.Unlocked)
        {
            throw new CaseException(CaseError.VaultLocked);
        }
    }

    /// <summary>Gets the operator profile that the journal needs.</summary>
    /// <returns>The profile.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.VaultLocked"/> or <see cref="CaseError.NoOperatorProfile"/>.
    /// </exception>
    private OperatorProfile CurrentProfile()
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

    /// <summary>Holds the case's lock file open without sharing, so that the case is only open once.</summary>
    /// <param name="paths">The case's files.</param>
    /// <returns>The held lock file; it is removed when it is closed.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.InUse"/>.</exception>
    private static FileStream AcquireLock(CasePaths paths)
    {
        try
        {
            return new FileStream(
                paths.LockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException exception) when (exception is not (FileNotFoundException or DirectoryNotFoundException))
        {
            throw new CaseException(CaseError.InUse, exception);
        }
        catch (UnauthorizedAccessException exception) when (File.Exists(paths.LockFile))
        {
            // On Windows a lock file that is being removed by the session that just closed cannot be opened.
            throw new CaseException(CaseError.InUse, exception);
        }
    }

    /// <summary>Reads a case file's header.</summary>
    /// <param name="paths">The case's files.</param>
    /// <returns>The header.</returns>
    private static CaseHeader ReadHeader(CasePaths paths)
    {
        using var stream = new FileStream(paths.CaseFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        return CaseContainer.ReadHeader(stream);
    }

    /// <summary>Opens the working copy; one left by an interrupted session that does not open is a conflict.</summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="recovering">Whether the working copy was left by an interrupted session.</param>
    /// <returns>The open database.</returns>
    private CaseDatabase OpenWorkingCopy(CasePaths paths, byte[] fileKey, Guid caseId, bool recovering)
    {
        var database = default(CaseDatabase);
        try
        {
            database = OpenDatabase(paths.WorkingCopy, fileKey, caseId);

            // The registered sealed tables are guarded before any schema step runs, also those of modules this
            // build does not know.
            using var scope = database.As(GuardMode.Internal);
            if (HashChain.TableExists(database, null, HashChain.RegistryTable))
            {
                database.AddSealed(HashChain.ReadRegistry(database, null).Chains.Select(chain => chain.Table));
            }

            return database;
        }
        catch (CaseException exception) when (recovering && exception.Error == CaseError.Corrupt)
        {
            database?.Dispose();
            throw new CaseException(CaseError.Conflict, exception);
        }
        catch (SqliteException exception)
        {
            database?.Dispose();
            throw new CaseException(recovering ? CaseError.Conflict : CaseError.Corrupt, exception);
        }
    }

    /// <summary>Opens a database file with the key derived from the file key.</summary>
    /// <param name="path">The file.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="caseId">The case id.</param>
    /// <returns>The open database.</returns>
    private CaseDatabase OpenDatabase(string path, byte[] fileKey, Guid caseId)
    {
        var databaseKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            CaseCrypto.DeriveDatabaseKey(fileKey, caseId, databaseKey);
            return CaseDatabase.Open(path, databaseKey, _services.Catalog.CreateGuard());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(databaseKey);
        }
    }

    /// <summary>
    /// Makes the header of a new case with an envelope for this vault and, when one is configured, one for the
    /// organisation's recovery key.
    /// </summary>
    /// <param name="fileKey">The new file key.</param>
    /// <returns>The header.</returns>
    private CaseHeader NewHeader(byte[] fileKey)
    {
        var caseId = Guid.NewGuid();
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            Case.DeriveVaultKey(_services.PurposeKeys, CaseCrypto.EnvelopePurpose, wrappingKey);
            List<JsonObject> envelopes = [KeyEnvelopes.CreateOperator(caseId, fileKey, wrappingKey)];
            if (_services.Options.RecoveryKey is { } recoveryKey)
            {
                envelopes.Add(RecoveryEnvelopes.Create(caseId, fileKey, recoveryKey));
            }

            return new CaseHeader(caseId, envelopes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Writes the case information once, as the Cases module.</summary>
    /// <param name="database">The new database.</param>
    /// <param name="info">The information.</param>
    private static void WriteInfo(CaseDatabase database, CaseInfo info)
    {
        using var scope = database.As(GuardMode.Internal);
        using var transaction = database.Begin();
        using (var insert = database.Command(
            "INSERT INTO case_info (id, case_id, case_number, created_utc) VALUES (1, $id, $number, $created)",
            transaction))
        {
            insert.Parameters.AddWithValue("$id", info.Id.ToString("D"));
            insert.Parameters.AddWithValue("$number", info.CaseNumber);
            insert.Parameters.AddWithValue("$created", info.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Reads the case information.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The information.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when it is missing or malformed.</exception>
    private static CaseInfo ReadInfo(CaseDatabase database)
    {
        using var scope = database.As(GuardMode.Internal);
        using var select = database.Command("SELECT case_id, case_number, created_utc FROM case_info WHERE id = 1");
        using var reader = select.ExecuteReader();
        if (!reader.Read()
            || !Guid.TryParseExact(reader.GetString(0), "D", out var id)
            || !DateTimeOffset.TryParseExact(
                reader.GetString(2), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
        {
            throw new CaseException(CaseError.Corrupt);
        }

        return new CaseInfo(id, reader.GetString(1), created);
    }

    /// <summary>
    /// The details of the first journal entry: the case, the schema it was created with and the recovery key it got
    /// an envelope for (<see langword="null"/> when none was configured, so that the journal says so).
    /// </summary>
    /// <param name="info">The case information.</param>
    /// <param name="opened">The new case.</param>
    /// <returns>The JSON.</returns>
    private string CreationDetails(CaseInfo info, Case opened) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [CreatedDetails.CaseId] = info.Id.ToString("D"),
            [CreatedDetails.CaseNumber] = info.CaseNumber,
            [CreatedDetails.CreatedUtc] = info.CreatedUtc.ToString("O", CultureInfo.InvariantCulture),
            [CreatedDetails.Schema] = Case.SchemaDetails(opened.Database),
            [CreatedDetails.RecoveryKey] = _services.Options.RecoveryKey?.Fingerprint,
        });

    /// <summary>Gives the number of locks of the vault so far.</summary>
    /// <returns>The count.</returns>
    private long LockCount()
    {
        lock (_openGate)
        {
            return _locks;
        }
    }

    /// <summary>Remembers an open case.</summary>
    /// <param name="opened">The case.</param>
    /// <returns>The same case.</returns>
    private Case Track(Case opened)
    {
        lock (_openGate)
        {
            _open.Add(opened);
        }

        return opened;
    }

    /// <summary>Forgets a case that has closed.</summary>
    /// <param name="closed">The case.</param>
    private void Forget(Case closed)
    {
        lock (_openGate)
        {
            _open.Remove(closed);
        }
    }

    /// <summary>
    /// When the vault leaves the unlocked state, refuses every new operation on the open cases at once and saves and
    /// closes them in the background, so that the thread that locked the vault (often the UI thread) does not wait.
    /// </summary>
    /// <param name="sender">The vault.</param>
    /// <param name="state">The new state.</param>
    private void OnVaultStateChanged(object? sender, VaultState state)
    {
        if (state == VaultState.Unlocked)
        {
            return;
        }

        lock (_openGate)
        {
            // The cases are taken now, so that a case opened after the vault is unlocked again is not closed by a
            // closing that waited behind a slow save.
            _locks++;
            List<Case> open = [.. _open];
            foreach (var item in open)
            {
                item.BeginClosing();
            }

            _closing = _closing.ContinueWith(_ => Close(open), TaskScheduler.Default);
        }
    }

    /// <summary>Closes every open case; a case whose save fails keeps its working copy for recovery.</summary>
    private void CloseAll()
    {
        List<Case> open;
        lock (_openGate)
        {
            open = [.. _open];
        }

        Close(open);
    }

    /// <summary>Closes cases one by one; a failure in one never keeps the others open.</summary>
    /// <param name="cases">The cases.</param>
    private static void Close(IEnumerable<Case> cases)
    {
        foreach (var item in cases)
        {
            try
            {
                item.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The case has released its keys and its lock (Close does so whatever fails); go on with the others.
            }
        }
    }

    /// <summary>How a case's file key was opened.</summary>
    private enum OpeningKind
    {
        /// <summary>With this vault's envelope.</summary>
        Vault,

        /// <summary>With the sharing password.</summary>
        Password,

        /// <summary>With the organisation's private recovery key.</summary>
        Recovery,
    }

    /// <summary>How a case is being opened, for the journal, and the count of vault locks when it began.</summary>
    /// <param name="Kind">How the file key was opened.</param>
    /// <param name="Locks">The count of vault locks when the opening began.</param>
    /// <param name="Administrator">The administrator, for an opening with the recovery key.</param>
    /// <param name="Fingerprint">The recovery key's fingerprint, for an opening with it.</param>
    private sealed record Opening(
        OpeningKind Kind, long Locks, string? Administrator = null, string? Fingerprint = null);
}
