using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>Replacing the case's file key (sketch VT1a).</summary>
/// <remarks>
/// <para>
/// The case file must always be either the old file or the new one, whole, and a working copy must never be left that
/// belongs to a file it cannot be opened with. So the replacement is done on copies, in this order:
/// </para>
/// <list type="number">
/// <item>The case is saved, so that the case file and the working copy hold the same.</item>
/// <item>The working copy is copied to <see cref="CasePaths.RekeyCopy"/>; the journal line is sealed there, and the copy
/// is encrypted under the new key. The working copy itself is not touched, so a crash here leaves the case as it
/// was, without the line.</item>
/// <item>The new case file (new header, the copy, the seal under the new key) is written aside.</item>
/// <item>The working copy is moved aside to <see cref="CasePaths.RekeyBackup"/>, and the new file replaces the case file
/// in one move. A crash before the move leaves the old file, which opens as before (the working copy is extracted
/// anew); a crash after leaves the new file, which opens with the new envelopes.</item>
/// <item>The copy becomes the working copy, with the new file as its base, and the case goes on under the new key.</item>
/// </list>
/// What an interrupted replacement leaves beside the file is removed the next time
/// (<see cref="CasePaths.DeleteRekeyLeftovers"/>).
/// </remarks>
internal sealed partial class Case
{
    /// <summary>The step after the journal line is sealed in the copy and the copy is encrypted under the new key.</summary>
    internal const string RekeyPrepared = "prepared";

    /// <summary>The step after the new case file is written aside.</summary>
    internal const string RekeyWritten = "written";

    /// <summary>The step after the working copy is set aside, right before the new file is moved into place.</summary>
    internal const string RekeySetAside = "set-aside";

    /// <summary>The step after the new file has replaced the case file.</summary>
    internal const string RekeyReplaced = "replaced";

    /// <summary>
    /// Gets or sets what runs at each step of a replacement of the file key, by the step's name; only the tests set
    /// it, to take a picture of the folder as a crash would leave it, or to make a step fail.
    /// </summary>
    internal Action<string>? FileKeyStep { get; set; }

    /// <summary>Gets whether an interrupted replacement had left copies beside the file when the case was opened.</summary>
    internal bool RekeyLeftovers { get; init; }

    /// <inheritdoc />
    public bool FileKeyReplacementInterrupted { get; private set; }

    /// <summary>
    /// Says in the journal, when the case is opened, that a replacement of the file key was interrupted before the new
    /// file was in place: copies were left, and the journal's last line is not the replacement. Called before any line
    /// of the opening is written. The caller saves.
    /// </summary>
    /// <returns><see langword="true"/> when the line was written.</returns>
    internal bool NoteInterruptedReplacement()
    {
        if (!RekeyLeftovers || LastJournalKind() == CaseJournalKinds.FileKeyReplaced)
        {
            return false;
        }

        AppendOwnJournal(new JournalDraft(
            CaseJournalKinds.FileKeyReplacementInterrupted,
            "Ett byte av filnyckeln avbröts innan den nya filen var på plats. Filnyckeln och kuverten är de gamla, "
            + "så bytet behöver göras om."));
        FileKeyReplacementInterrupted = true;
        return true;
    }

    /// <summary>Reads the kind of the journal's last line.</summary>
    /// <returns>The kind, or <see langword="null"/> for an empty journal.</returns>
    private string? LastJournalKind()
    {
        using var scope = Database.As(GuardMode.Internal);
        using var select = Database.Command(
            $"SELECT kind FROM {CasesSchema.Journal.Table} ORDER BY sequence DESC LIMIT 1");
        return select.ExecuteScalar() as string;
    }

    /// <inheritdoc />
    public FileKeyReplacement ReplaceFileKey(ReadOnlySpan<char> masterKey, ReadOnlySpan<char> sharingPassword)
    {
        if (sharingPassword.Length is > 0 and < SharingPasswordRules.MinimumLength)
        {
            throw new CaseException(CaseError.PasswordTooShort);
        }

        lock (_gate)
        {
            using var operation = BeginOperation();
            if (_fileReplaced)
            {
                throw new CaseException(CaseError.Conflict);
            }

            VerifyMasterKey(masterKey);
            if (!VaultHasEnvelope())
            {
                throw new CaseException(CaseError.WouldLoseAccess);
            }

            var described = DescribeUnlocked();
            SaveUnlocked();
            _paths.DeleteRekeyLeftovers();
            var newKey = CaseCrypto.NewFileKey();
            try
            {
                var header = NewHeader(newKey, sharingPassword);
                var draft = ReplacementDraft(described, !sharingPassword.IsEmpty);
                var entry = PrepareRekeyedCopy(newKey, draft);
                FileKeyStep?.Invoke(RekeyPrepared);
                var hash = WriteReplacementFile(header, newKey);
                FileKeyStep?.Invoke(RekeyWritten);
                MoveReplacementIntoPlace(header, newKey, hash);
                return FileKeyReplacement.FromJournal(entry)
                    ?? throw new InvalidOperationException("The journal line of the replacement does not read back.");
            }
            catch
            {
                // Only the copies are removed; the case file and, when it is still there, the working copy are whole.
                File.Delete(_paths.SaveFile);
                _paths.DeleteRekeyLeftovers();
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(newKey);
            }
        }
    }

    /// <summary>Checks the master key with the vault, giving its failures the case's own errors.</summary>
    /// <param name="masterKey">The master key as typed.</param>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.WrongMasterKey"/>, <see cref="CaseError.VaultLocked"/>, or
    /// <see cref="CaseError.VaultFailed"/> when the vault cannot be read (changed, missing or replaced).
    /// </exception>
    private void VerifyMasterKey(ReadOnlySpan<char> masterKey)
    {
        try
        {
            _services.Vault.VerifyMasterKey(masterKey);
        }
        catch (VaultException exception) when (exception.Error == VaultError.WrongMasterKey)
        {
            throw new CaseException(CaseError.WrongMasterKey, exception);
        }
        catch (VaultException exception) when (exception.Error == VaultError.Locked)
        {
            throw new CaseException(CaseError.VaultLocked, exception);
        }
        catch (VaultException exception)
        {
            throw new CaseException(CaseError.VaultFailed, exception);
        }
    }

    /// <summary>Describes the envelopes while the gate is held.</summary>
    /// <returns>The envelopes, in file order.</returns>
    private IReadOnlyList<CaseEnvelope> DescribeUnlocked()
    {
        var wrappingKey = _services.DeriveVaultKey(CaseCrypto.EnvelopePurpose);
        try
        {
            return EnvelopeDescriptions.Describe(_header, wrappingKey, _fileKey, _services.Options);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>
    /// Builds the header under the new key: this vault's envelope, the built-in recovery key's when one is configured,
    /// and the new case password's when one is given. Done before anything is written, so that a locked vault or a
    /// missing profile stops the replacement at once.
    /// </summary>
    /// <param name="newKey">The new file key.</param>
    /// <param name="sharingPassword">The new case password, or empty.</param>
    /// <returns>The header.</returns>
    private CaseHeader NewHeader(byte[] newKey, ReadOnlySpan<char> sharingPassword)
    {
        var envelopes = new List<JsonObject>();
        var wrappingKey = _services.DeriveVaultKey(CaseCrypto.EnvelopePurpose);
        try
        {
            envelopes.Add(KeyEnvelopes.CreateOperator(Info.Id, newKey, wrappingKey, _services.CurrentOperator().Name));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }

        if (_services.Options.RecoveryKey is { } recoveryKey)
        {
            envelopes.Add(RecoveryEnvelopes.Create(Info.Id, newKey, recoveryKey));
        }

        if (!sharingPassword.IsEmpty)
        {
            envelopes.Add(KeyEnvelopes.CreatePassword(
                Info.Id, newKey, sharingPassword, _services.Options.SharingIterations));
        }

        return _header.WithEnvelopes(envelopes);
    }

    /// <summary>The journal line of the replacement: which envelopes are wrapped anew and which fall away.</summary>
    /// <param name="described">The envelopes the file has now.</param>
    /// <param name="sharingPasswordCreated">Whether a new case password is made in the same step.</param>
    /// <returns>The line.</returns>
    private JournalDraft ReplacementDraft(IReadOnlyList<CaseEnvelope> described, bool sharingPasswordCreated)
    {
        var dropped = described.Where(envelope => !FileKeyReplacement.Keeps(envelope)).Select(Words).ToList();
        List<string> kept = ["ditt valv"];
        if (_services.Options.RecoveryKey is not null)
        {
            kept.Add("organisationens återställningsnyckel");
        }

        if (sharingPasswordCreated)
        {
            kept.Add("ett nytt ärendelösenord");
        }

        var summary = dropped.Count == 0
            ? "Filnyckeln byttes; inga kuvert föll bort."
            : $"Filnyckeln byttes; kuvert som föll bort: {string.Join(", ", dropped)}.";
        summary += " Kopior av filen som redan lämnats ut påverkas inte.";
        return new JournalDraft(
            CaseJournalKinds.FileKeyReplaced,
            summary,
            JsonSerializer.Serialize(new FileKeyReplacementDetails(dropped, kept, sharingPasswordCreated)));
    }

    /// <summary>Says which envelope falls away, in words for the journal.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The words.</returns>
    private static string Words(CaseEnvelope envelope) => envelope.Kind switch
    {
        CaseEnvelopeKind.OtherVault => envelope.Name ?? "ett annat valv",
        CaseEnvelopeKind.SharingPassword => "ärendelösenordet",
        CaseEnvelopeKind.Recovery => string.Create(
            CultureInfo.InvariantCulture, $"återställningsnyckeln {Shorten(envelope.Fingerprint)}"),
        _ => "ett okänt kuvert",
    };

    /// <summary>Shortens a fingerprint to its first 16 characters for the journal.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>The short form.</returns>
    private static string Shorten(string? fingerprint)
    {
        const int Shown = 16;
        var text = fingerprint ?? string.Empty;
        return text.Length > Shown ? text[..Shown] + "…" : text;
    }

    /// <summary>
    /// Copies the working copy, seals the journal line in the copy, still under the old key, and encrypts the copy under
    /// the new key. The working copy is not touched, so that it stays the case file's until the new file is in place.
    /// </summary>
    /// <param name="newKey">The new file key.</param>
    /// <param name="draft">The journal line.</param>
    /// <returns>The sealed line.</returns>
    private JournalEntry PrepareRekeyedCopy(byte[] newKey, JournalDraft draft)
    {
        File.Copy(_paths.WorkingCopy, _paths.RekeyCopy, overwrite: true);
        var working = Database;
        using var copy = OpenDatabaseAt(_paths.RekeyCopy, _fileKey);
        JournalEntry entry;
        _database = copy;
        try
        {
            entry = AppendOwnJournal(draft);
        }
        catch (SqliteException exception)
        {
            // The copy did not take the line; the case and its working copy are as they were.
            throw new CaseException(CaseError.Corrupt, exception);
        }
        finally
        {
            _database = working;
        }

        var databaseKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            CaseCrypto.DeriveDatabaseKey(newKey, Info.Id, databaseKey);
            copy.ChangeKey(databaseKey);
        }
        catch (SqliteException exception)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(databaseKey);
        }

        return entry;
    }

    /// <summary>Opens a database file of this case under a file key, with the guard every connection has.</summary>
    /// <param name="path">The database file.</param>
    /// <param name="fileKey">The file key it is encrypted under.</param>
    /// <returns>The open database.</returns>
    private CaseDatabase OpenDatabaseAt(string path, ReadOnlySpan<byte> fileKey)
    {
        var databaseKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            CaseCrypto.DeriveDatabaseKey(fileKey, Info.Id, databaseKey);
            var database = CaseDatabase.Open(path, databaseKey, _services.Catalog.CreateGuard());
            database.AddSealed(_registered.Values.Select(chain => chain.Table));
            return database;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(databaseKey);
        }
    }

    /// <summary>Writes the new case file aside: the new header, the encrypted copy and the seal under the new key.</summary>
    /// <param name="header">The new header.</param>
    /// <param name="newKey">The new file key.</param>
    /// <returns>The new file's SHA-256.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Conflict"/> when the case file was changed outside Chainsaw meanwhile; it is left as it is.
    /// </exception>
    private byte[] WriteReplacementFile(CaseHeader header, byte[] newKey)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var output = new FileStream(
            _paths.SaveFile, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize))
        {
            using (var input = new FileStream(
                _paths.RekeyCopy, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize))
            {
                CaseFiles.Write(output, header, input, newKey, hash);
            }

            output.Flush(flushToDisk: true);
        }

        File.SetLastWriteTimeUtc(_paths.SaveFile, DateTime.UtcNow);
        var stamp = CaseFileStamp;
        if (CaseFileHash is { } before && !CaseFiles.IsUnchanged(_paths, before, ref stamp))
        {
            _paths.DeleteRekeyLeftovers();
            KeepBoth();
        }

        CaseFileStamp = stamp;
        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Goes back to the working copy under the old key when the new file could not be moved into place. When even that
    /// fails, the case is closed: the old file is whole and the same as the working copy set aside, which the next
    /// opening removes before it extracts the working copy anew.
    /// </summary>
    private void GoBackToOldWorkingCopy()
    {
        try
        {
            File.Move(_paths.RekeyBackup, _paths.WorkingCopy);
            _database = OpenDatabaseAt(_paths.WorkingCopy, _fileKey);
        }
        catch
        {
            ReleaseResources();
            throw;
        }
    }

    /// <summary>
    /// Puts the new case file in place and goes on under the new key. Until the move, a failure goes back to the old
    /// working copy; after it, the new file is whole, and a failure closes the case so that it is opened anew from it.
    /// </summary>
    /// <param name="header">The new header.</param>
    /// <param name="newKey">The new file key.</param>
    /// <param name="hash">The new file's SHA-256.</param>
    private void MoveReplacementIntoPlace(CaseHeader header, byte[] newKey, byte[] hash)
    {
        // The working copy is closed and set aside, so that a crash in the move leaves no working copy that belongs to
        // the old file beside the new one: the next opening extracts it from whichever file is there.
        ReleaseDatabase();
        File.Delete(_paths.WorkingJournal);
        File.Move(_paths.WorkingCopy, _paths.RekeyBackup);
        try
        {
            FileKeyStep?.Invoke(RekeySetAside);
            File.Move(_paths.SaveFile, _paths.CaseFile, overwrite: true);
        }
        catch
        {
            GoBackToOldWorkingCopy();
            throw;
        }

        try
        {
            FileKeyStep?.Invoke(RekeyReplaced);
            CaseFiles.WriteBase(_paths, [hash]);
            File.Move(_paths.RekeyCopy, _paths.WorkingCopy);
            File.Delete(_paths.RekeyBackup);
            newKey.CopyTo(_fileKey, 0);
            _header = header;
            CaseFileHash = hash;
            CaseFileStamp = CaseFiles.StampOf(_paths.CaseFile);
            _database = OpenDatabaseAt(_paths.WorkingCopy, _fileKey);
            WriteAnchors();
            if (_autosave.Saved())
            {
                SayHowSavesGo(new CaseSaveState(0));
            }
        }
        catch
        {
            // The new file is whole; the case is closed here and opens from it, with its own working copy if one made it.
            ReleaseResources();
            throw;
        }
    }
}
