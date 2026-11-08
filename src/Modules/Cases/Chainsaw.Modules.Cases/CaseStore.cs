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
            var header = NewHeader(Guid.NewGuid(), fileKey);
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
    public ICase ImportWithPassword(string path, ReadOnlySpan<char> password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = new CasePaths(System.IO.Path.GetFullPath(path));
        RequireExists(source);
        var target = PathsFor(System.IO.Path.GetFileNameWithoutExtension(source.CaseFile));
        if (string.Equals(source.CaseFile, target.CaseFile, StringComparison.OrdinalIgnoreCase))
        {
            return OpenWithPassword(target.CaseFile, password);
        }

        var locks = LockCount();
        RequireUnlocked();
        if (!WorkingCopyBranches.IsFree(target))
        {
            // Before the lock: a case with the number that is open here holds it, and is no other Chainsaw.
            throw new CaseException(CaseError.AlreadyExists);
        }

        System.IO.Directory.CreateDirectory(Directory);
        var lockFile = AcquireLock(target);
        var fileKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        var copied = false;
        try
        {
            // The password is tried where the file lies, so that a wrong one copies nothing.
            var sourceHeader = ReadHeader(source);
            if (!KeyEnvelopes.TryOpenPassword(sourceHeader, password, fileKey))
            {
                throw new CaseException(CaseError.NoAccess);
            }

            CryptographicOperations.ZeroMemory(fileKey);
            if (!WorkingCopyBranches.IsFree(target))
            {
                throw new CaseException(CaseError.AlreadyExists);
            }

            RequireIdNotInFolder(sourceHeader.CaseId);
            var sha256 = CopyInto(source, target);
            copied = true;

            // The key is opened again from the copy, the file that is opened, and the copy must be the file read.
            var header = ReadHeader(target);
            if (!SameHeader(header, sourceHeader) || !KeyEnvelopes.TryOpenPassword(header, password, fileKey))
            {
                throw new CaseException(CaseError.Corrupt);
            }

            var opening = new Opening(OpeningKind.Password, locks)
            {
                Import = new ImportedFile(System.IO.Path.GetFileName(source.CaseFile), sha256),
            };
            return OpenLocked(target, lockFile, header, fileKey, opening);
        }
        catch
        {
            Clear(fileKey);
            if (copied && !File.Exists(target.WorkingCopy))
            {
                // Only the copy made here is removed; the colleague's file stays where it is.
                File.Delete(target.CaseFile);
            }

            lockFile.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CaseEnvelope> DescribeEnvelopes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var paths = new CasePaths(System.IO.Path.GetFullPath(path));
        RequireExists(paths);
        RequireUnlocked();
        var header = ReadHeader(paths);
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            Case.DeriveVaultKey(_services.PurposeKeys, CaseCrypto.EnvelopePurpose, wrappingKey);
            return EnvelopeDescriptions.Describe(header, wrappingKey, [], _services.Options);
        }
        finally
        {
            Clear(wrappingKey);
        }
    }

    /// <inheritdoc />
    public WorkingCopyConflict? FindConflict(string caseNumber)
    {
        var paths = PathsFor(caseNumber);
        RequireExists(paths);
        if (!File.Exists(paths.WorkingCopy) || CaseFiles.MatchingBase(paths) is not null)
        {
            return null;
        }

        return new WorkingCopyConflict(
            caseNumber,
            new DateTimeOffset(File.GetLastWriteTimeUtc(paths.CaseFile), TimeSpan.Zero),
            new DateTimeOffset(File.GetLastWriteTimeUtc(paths.WorkingCopy), TimeSpan.Zero),
            WorkingCopyBranches.SuggestBranchNumber(System.IO.Path.GetFullPath(Directory), caseNumber));
    }

    /// <inheritdoc />
    public ICase OpenSettingAsideWorkingCopy(string caseNumber)
    {
        var locks = LockCount();
        var paths = PathsFor(caseNumber);
        RequireExists(paths);
        RequireUnlocked();
        var lockFile = AcquireLock(paths);
        var fileKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        WorkingCopyBranches.SetAside? setAside = null;
        try
        {
            var header = ReadHeader(paths);
            OpenWithVault(header, fileKey);

            // A working copy that belongs to the file after all is recovered as an ordinary opening does.
            if (File.Exists(paths.WorkingCopy) && CaseFiles.MatchingBase(paths) is null)
            {
                setAside = WorkingCopyBranches.MoveAside(
                    paths, System.IO.Path.GetFullPath(Directory), caseNumber, _services.Clock.UtcNow);
            }

            var opened = OpenLocked(paths, lockFile, header, fileKey, new Opening(OpeningKind.Vault, locks));
            if (setAside is not null)
            {
                // Journalled only once the case is open and known, so that a failed opening leaves no line behind.
                RecordOrUndo(opened, SetAsideDraft(setAside), () => WorkingCopyBranches.MoveBack(setAside));
            }

            return opened;
        }
        catch
        {
            Clear(fileKey);
            if (setAside is not null && !File.Exists(paths.WorkingCopy))
            {
                // The file did not open: the working copy goes back, while the lock is still held, and the choice is
                // offered again.
                WorkingCopyBranches.MoveBack(setAside);
            }

            lockFile.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public ICase SaveWorkingCopyAsCase(string caseNumber, string branchNumber)
    {
        var locks = LockCount();
        var paths = PathsFor(caseNumber);
        var branch = PathsFor(branchNumber);
        if (string.Equals(paths.CaseFile, branch.CaseFile, StringComparison.OrdinalIgnoreCase))
        {
            throw new CaseException(CaseError.AlreadyExists);
        }

        RequireExists(paths);
        RequireUnlocked();
        var lockFile = AcquireLock(paths);
        FileStream? branchLock = null;
        var fileKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        byte[]? newKey = null;
        var made = false;
        try
        {
            branchLock = AcquireLock(branch);
            if (!WorkingCopyBranches.IsFree(branch))
            {
                throw new CaseException(CaseError.AlreadyExists);
            }

            if (!File.Exists(paths.WorkingCopy) || CaseFiles.MatchingBase(paths) is not null)
            {
                throw new CaseException(CaseError.NotFound);
            }

            var header = ReadHeader(paths);
            OpenWithVault(header, fileKey);
            var origin = new BranchOrigin(caseNumber, header.CaseId, NotCarried(header));

            // The working copy is copied, made a case of its own with a new id and file key, and written as a new
            // file; the original stays in place until the new case is open, so that nothing is lost on the way.
            made = true;
            newKey = CaseCrypto.NewFileKey();
            var newHeader = NewHeader(Guid.NewGuid(), newKey);
            WorkingCopyBranches.CopyToBranch(paths, branch);
            RebindBranch(branch, (fileKey, header.CaseId), (newKey, newHeader.CaseId), branchNumber);
            WorkingCopyBranches.WriteCaseFile(branch, newHeader);
            WorkingCopyBranches.RemoveBranch(branch, caseFileToo: false);
            var opening = new Opening(OpeningKind.Branch, locks) { Origin = origin };
            var opened = OpenLocked(branch, branchLock, newHeader, newKey, opening);
            (branchLock, newKey) = (null, null);
            SetAsideAfterBranch(paths, caseNumber, opened);
            return opened;
        }
        catch
        {
            Clear(newKey);
            if (made)
            {
                // The new case was not made: what was written for it goes, and the original is untouched.
                WorkingCopyBranches.RemoveBranch(branch, caseFileToo: true);
            }

            throw;
        }
        finally
        {
            Clear(fileKey);
            branchLock?.Dispose();
            lockFile.Dispose();
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
            // working copy was taken from it, neither is overwritten. A new one is extracted without a leftover
            // rollback journal.
            var (caseFileHash, caseFileStamp) = recovering
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
                    CaseFileStamp = caseFileStamp,
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
        // A case of its own made from a working copy has a new id, and so no anchors yet, like a new case.
        var anchorsJournalled = opening.Kind != OpeningKind.Branch && opened.ReconcileAnchors();
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
            opened.AppendOwnJournal(PasswordDraft(opening.Import));
        }

        if (opening.Origin is { } origin)
        {
            // In the opening's own save, so that the new case is never on disk without the line that names it.
            opened.AppendOwnJournal(BranchDraft(origin, opened));
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
    /// The journal line for an opening with the sharing password; for a file taken in from elsewhere, also its name
    /// and the SHA-256 of the file as it was read, so that the copy can be traced to the file the colleague gave.
    /// </summary>
    /// <param name="import">The file taken in, or <see langword="null"/>.</param>
    /// <returns>The line.</returns>
    private static JournalDraft PasswordDraft(ImportedFile? import) => import is null
        ? new JournalDraft(CaseJournalKinds.OpenedWithPassword, "Ärendet öppnades med delningslösenordet")
        : new JournalDraft(
            CaseJournalKinds.OpenedWithPassword,
            $"Ärendet togs in från filen {import.FileName} och öppnades med delningslösenordet",
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [BranchDetails.ImportedFrom] = import.FileName,
                [BranchDetails.ImportedSha256] = import.Sha256,
            }));

    /// <summary>
    /// Writes a line in a case that has just opened and is known to the store, and saves it; when that fails, the case
    /// is closed and the step that led here is undone, so that the user is offered the same choice again.
    /// </summary>
    /// <param name="opened">The open case.</param>
    /// <param name="draft">The line.</param>
    /// <param name="undo">What undoes the step that led here.</param>
    private static void RecordOrUndo(Case opened, JournalDraft draft, Action undo)
    {
        try
        {
            opened.JournalAndSave(draft);
        }
        catch
        {
            opened.Dispose();
            undo();
            throw;
        }
    }

    /// <summary>The journal line for a working copy moved aside before the file was opened.</summary>
    /// <param name="setAside">What was set aside.</param>
    /// <returns>The line.</returns>
    private static JournalDraft SetAsideDraft(WorkingCopyBranches.SetAside setAside) => new(
        CaseJournalKinds.WorkingCopySetAside,
        $"En arbetskopia som inte hörde till filen flyttades till {setAside.Folder}",
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [BranchDetails.Folder] = setAside.Folder,
            [BranchDetails.WorkingCopySha256] = setAside.Sha256,
        }));

    /// <summary>
    /// The journal line that makes a working copy a case of its own: the case it came from (number and id), its new
    /// id and number, and the envelopes that could not follow. <see cref="CaseChecks"/> follows it from the first line.
    /// </summary>
    /// <param name="origin">The case it came from.</param>
    /// <param name="opened">The new case.</param>
    /// <returns>The line.</returns>
    private static JournalDraft BranchDraft(BranchOrigin origin, Case opened)
    {
        var notCarried = origin.NotCarried.Count == 0
            ? string.Empty
            : $" Kuvert som inte kunde följa med: {string.Join(", ", origin.NotCarried)}.";
        return new JournalDraft(
            CaseJournalKinds.BranchSaved,
            $"Arbetskopian blev ett eget ärende, {opened.Info.CaseNumber}, ur ärendet {origin.CaseNumber} "
            + $"({origin.CaseId:D}), eftersom filen hade sparats sedan dess.{notCarried}",
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [BranchDetails.From] = origin.CaseNumber,
                [BranchDetails.FromCaseId] = origin.CaseId.ToString("D"),
                [BranchDetails.CaseNumber] = opened.Info.CaseNumber,
                [BranchDetails.CaseId] = opened.Info.Id.ToString("D"),
                [BranchDetails.NotCarried] = origin.NotCarried,
            }));
    }

    /// <summary>
    /// Moves the original's working copy aside once it lives on as a case of its own, and says where in the new
    /// case's journal. A move that fails leaves the conflict as it was; the choice is then offered again.
    /// </summary>
    /// <param name="paths">The original's files.</param>
    /// <param name="caseNumber">The original's case number.</param>
    /// <param name="opened">The new case.</param>
    private void SetAsideAfterBranch(CasePaths paths, string caseNumber, Case opened)
    {
        WorkingCopyBranches.SetAside setAside;
        try
        {
            setAside = WorkingCopyBranches.MoveAside(
                paths, System.IO.Path.GetFullPath(Directory), caseNumber, _services.Clock.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        try
        {
            opened.JournalAndSave(SetAsideDraft(setAside));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The new case is open and whole; only the line about the original's working copy is missing.
        }
    }

    /// <summary>Says which of a file's envelopes cannot follow its working copy to a case of its own.</summary>
    /// <param name="header">The file's header.</param>
    /// <returns>The envelopes, in words, for the journal.</returns>
    private List<string> NotCarried(CaseHeader header)
    {
        var builtIn = _services.Options.RecoveryKey?.Fingerprint;
        var words = new List<string>();
        var others = KeyEnvelopes.OfKind(header, KeyEnvelopes.OperatorKind).Count() - 1;
        if (others > 0)
        {
            words.Add(string.Create(CultureInfo.InvariantCulture, $"{others} andra valv"));
        }

        if (KeyEnvelopes.OfKind(header, KeyEnvelopes.PasswordKind).Any())
        {
            words.Add("ärendelösenordet");
        }

        var otherKeys = RecoveryEnvelopes.Fingerprints(header).Where(fingerprint => fingerprint != builtIn).ToList();
        words.AddRange(otherKeys.Select(fingerprint => $"återställningsnyckeln {fingerprint}"));
        var unknown = header.Envelopes.Count(envelope => !KeyEnvelopes.IsKind(envelope, KeyEnvelopes.OperatorKind)
            && !KeyEnvelopes.IsKind(envelope, KeyEnvelopes.PasswordKind)
            && RecoveryEnvelopes.FingerprintOf(envelope) is null);
        if (unknown > 0)
        {
            words.Add(string.Create(CultureInfo.InvariantCulture, $"{unknown} okända"));
        }

        return words;
    }

    /// <summary>Gives a branch's database a new id, number and key; the database keys are cleared.</summary>
    /// <param name="branch">The branch's files; its working copy is the copy to change.</param>
    /// <param name="from">The file key and id it has now.</param>
    /// <param name="to">The file key and id it gets.</param>
    /// <param name="branchNumber">The new case number.</param>
    private static void RebindBranch(
        CasePaths branch, (byte[] Key, Guid Id) from, (byte[] Key, Guid Id) to, string branchNumber)
    {
        var oldKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        var newKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            CaseCrypto.DeriveDatabaseKey(from.Key, from.Id, oldKey);
            CaseCrypto.DeriveDatabaseKey(to.Key, to.Id, newKey);
            CaseDatabase.Rebind(branch.WorkingCopy, oldKey, newKey, to.Id, branchNumber);
        }
        finally
        {
            Clear(oldKey, newKey);
        }
    }

    /// <summary>Refuses a case whose id is already in the folder under another file name.</summary>
    /// <param name="caseId">The id.</param>
    /// <exception cref="CaseException"><see cref="CaseError.AlreadyExists"/>.</exception>
    private void RequireIdNotInFolder(Guid caseId)
    {
        foreach (var file in List())
        {
            try
            {
                if (ReadHeader(new CasePaths(file.Path)).CaseId == caseId)
                {
                    throw new CaseException(CaseError.AlreadyExists);
                }
            }
            catch (CaseException exception) when (exception.Error != CaseError.AlreadyExists)
            {
                // A file whose header cannot be read is not this case.
            }
        }
    }

    /// <summary>Tells whether two headers are the same, as they are written.</summary>
    /// <param name="first">One header.</param>
    /// <param name="second">The other.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    private static bool SameHeader(CaseHeader first, CaseHeader second)
    {
        using var one = new MemoryStream();
        using var other = new MemoryStream();
        CaseContainer.WriteHeader(one, first);
        CaseContainer.WriteHeader(other, second);
        return one.ToArray().AsSpan().SequenceEqual(other.ToArray());
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

    /// <summary>Opens the file key with this vault's envelope.</summary>
    /// <param name="header">The header.</param>
    /// <param name="fileKey">Receives the file key.</param>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoAccess"/>, <see cref="CaseError.Corrupt"/> or <see cref="CaseError.VaultLocked"/>.
    /// </exception>
    private void OpenWithVault(CaseHeader header, byte[] fileKey)
    {
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            Case.DeriveVaultKey(_services.PurposeKeys, CaseCrypto.EnvelopePurpose, wrappingKey);
            if (!KeyEnvelopes.TryOpenOperator(header, wrappingKey, fileKey))
            {
                throw new CaseException(CaseError.NoAccess);
            }
        }
        finally
        {
            Clear(wrappingKey);
        }
    }

    /// <summary>
    /// Copies a case file into the folder, written aside and then given its name, never over a file that exists;
    /// what is written aside is removed whatever happens.
    /// </summary>
    /// <param name="source">The file to copy.</param>
    /// <param name="target">Where it goes.</param>
    /// <returns>The SHA-256 of what was copied, in lower-case hexadecimal.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.AlreadyExists"/>.</exception>
    private static string CopyInto(CasePaths source, CasePaths target)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var input = new FileStream(source.CaseFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target.SaveFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                CaseFiles.CopyHashing(input, output, hash);
                output.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(target.SaveFile, target.CaseFile, overwrite: false);
            }
            catch (IOException exception) when (File.Exists(target.CaseFile))
            {
                throw new CaseException(CaseError.AlreadyExists, exception);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally
        {
            File.Delete(target.SaveFile);
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
    /// <param name="caseId">The new case's id.</param>
    /// <param name="fileKey">The new file key.</param>
    /// <returns>The header.</returns>
    private CaseHeader NewHeader(Guid caseId, byte[] fileKey)
    {
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

        /// <summary>A working copy just made a case of its own, with a new file key in this vault's envelope.</summary>
        Branch,
    }

    /// <summary>How a case is being opened, for the journal, and the count of vault locks when it began.</summary>
    /// <param name="Kind">How the file key was opened.</param>
    /// <param name="Locks">The count of vault locks when the opening began.</param>
    /// <param name="Administrator">The administrator, for an opening with the recovery key.</param>
    /// <param name="Fingerprint">The recovery key's fingerprint, for an opening with it.</param>
    private sealed record Opening(
        OpeningKind Kind, long Locks, string? Administrator = null, string? Fingerprint = null)
    {
        /// <summary>Gets the file taken in from elsewhere, with the password, or <see langword="null"/>.</summary>
        public ImportedFile? Import { get; init; }

        /// <summary>Gets the case a working copy came from, for a case of its own, or <see langword="null"/>.</summary>
        public BranchOrigin? Origin { get; init; }
    }

    /// <summary>A colleague's file taken into the folder: its name and the SHA-256 of what was copied.</summary>
    /// <param name="FileName">The file's name.</param>
    /// <param name="Sha256">The SHA-256 in lower-case hexadecimal.</param>
    private sealed record ImportedFile(string FileName, string Sha256);

    /// <summary>The case a working copy came from when it was made a case of its own.</summary>
    /// <param name="CaseNumber">Its case number.</param>
    /// <param name="CaseId">Its id.</param>
    /// <param name="NotCarried">The envelopes that could not follow, in words.</param>
    private sealed record BranchOrigin(string CaseNumber, Guid CaseId, IReadOnlyList<string> NotCarried);
}
