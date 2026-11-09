using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>
/// The exits allowed in the background (decided 2026-11-08), as one file protected for the Windows user (DPAPI):
/// for each allowed exit the vault's own record (name, place, sort and WireGuard file) copied verbatim, and the
/// program's selected exit. The open Chainsaw writes it; the background process only reads it, through
/// <see cref="BackgroundExitSecrets"/>, so that the same connection and helper start the tunnel there.
/// </summary>
internal sealed class BackgroundExitFile : IBackgroundExits
{
    /// <summary>What the file is protected for, so that it is never read as another of Chainsaw's files.</summary>
    internal const string Purpose = "Chainsaw.Network.BackgroundExits.v1";

    /// <summary>The format of an id in the file: 32 lower-case hexadecimal digits.</summary>
    private const string IdFormat = "N";

    /// <summary>The file, or <see langword="null"/> where copies are not kept.</summary>
    private readonly string? _path;

    /// <summary>Protects the file for the Windows user, or <see langword="null"/> where there is no protection.</summary>
    private readonly IUserDataProtector? _protector;

    /// <summary>The exits in the vault, where a copy is taken from; <see langword="null"/> in the background.</summary>
    private readonly Func<ExitRecords>? _vault;

    /// <summary>Lets one change at a time in this process.</summary>
    private readonly Lock _writing = new();

    /// <summary>Creates the file's keeper.</summary>
    /// <param name="path">The file, or <see langword="null"/> where copies are not kept.</param>
    /// <param name="protector">The protection, or <see langword="null"/> where there is none.</param>
    /// <param name="vault">
    /// Gives the exits in the vault, where a copy is taken from; <see langword="null"/> in the background, which only
    /// reads.
    /// </param>
    public BackgroundExitFile(string? path, IUserDataProtector? protector, Func<ExitRecords>? vault)
    {
        (_path, _protector, _vault) = (path, protector, vault);
    }

    /// <inheritdoc />
    public bool IsAvailable => _path is not null && _protector is not null;

    /// <inheritdoc />
    public bool IsAllowed(Guid exitId) => Read().Exits.ContainsKey(exitId.ToString(IdFormat));

    /// <inheritdoc />
    public Guid? Usable()
    {
        var file = Read();
        return Guid.TryParseExact(file.Selected, IdFormat, out var id) && file.Exits.ContainsKey(file.Selected!)
            ? id
            : null;
    }

    /// <inheritdoc />
    public void Allow(Guid exitId)
    {
        if (!IsAvailable || _vault is null)
        {
            throw new InvalidOperationException("Copies of the exits cannot be kept here.");
        }

        var records = _vault();
        var value = records.Value(exitId) ?? throw new ArgumentException("No exit has the id.", nameof(exitId));
        Change(file => file with
        {
            Selected = records.SelectedId()?.ToString(IdFormat),
            Exits = new Dictionary<string, string>(file.Exits) { [exitId.ToString(IdFormat)] = value },
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// It closes safely: a file that cannot be read (held, broken or foreign) or changed just now is removed whole, so
    /// that a copy of a WireGuard file can never outlive the permission; only when that fails too is it thrown.
    /// </remarks>
    public void Forbid(Guid exitId)
    {
        if (_path is null)
        {
            return;
        }

        var key = exitId.ToString(IdFormat);
        if (!TryRead(out var file))
        {
            Clear();
            return;
        }

        if (!file.Exits.ContainsKey(key))
        {
            return;
        }

        try
        {
            Change(written => written with { Exits = written.Exits.Where(each => each.Key != key).ToDictionary() });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            Clear();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (_path is null)
        {
            return;
        }

        lock (_writing)
        {
            try
            {
                File.Delete(_path);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException("The exits kept for the background could not be removed.", exception);
            }
        }
    }

    /// <summary>
    /// Follows the program's selected exit, when copies are kept, so that the background goes out only through it.
    /// </summary>
    /// <param name="selected">The selected exit, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// When the file cannot be changed, every copy is removed instead: the background must never go out through an
    /// exit the user has left.
    /// </remarks>
    public void Follow(Guid? selected)
    {
        if (!Exists())
        {
            return;
        }

        try
        {
            Change(file => file with { Selected = selected?.ToString(IdFormat) });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException)
        {
            Clear();
        }
    }

    /// <summary>Copies an allowed exit's record again after its name or place changed in the vault.</summary>
    /// <param name="exitId">The exit.</param>
    public void Refresh(Guid exitId)
    {
        if (_vault is not null && IsAllowed(exitId) && _vault().Value(exitId) is { } value)
        {
            Change(file => file with
            {
                Exits = new Dictionary<string, string>(file.Exits) { [exitId.ToString(IdFormat)] = value },
            });
        }
    }

    /// <summary>Finds an allowed exit's record, as the vault holds it.</summary>
    /// <param name="exitId">The exit.</param>
    /// <returns>The record, or <see langword="null"/> when it is not allowed.</returns>
    public string? Find(Guid exitId) => Read().Exits.GetValueOrDefault(exitId.ToString(IdFormat));

    /// <summary>Lists the allowed exits.</summary>
    /// <returns>Their ids.</returns>
    public IReadOnlyList<Guid> Allowed() =>
        [.. Read().Exits.Keys.Select(key => Guid.TryParseExact(key, IdFormat, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)];

    /// <summary>Gives the selected exit as written, allowed or not.</summary>
    /// <returns>Its id, or <see langword="null"/>.</returns>
    public Guid? Selected() => Guid.TryParseExact(Read().Selected, IdFormat, out var id) ? id : null;

    /// <summary>Tells whether the file is there.</summary>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool Exists() => _path is not null && File.Exists(_path);

    /// <summary>
    /// Reads the file. A file that is missing, foreign (another user) or broken holds nothing: the background then
    /// waits for an exit, and the next permission writes it anew.
    /// </summary>
    /// <returns>What it holds.</returns>
    private ExitsFile Read()
    {
        TryRead(out var file);
        return file;
    }

    /// <summary>Reads the file, telling a missing file apart from one that cannot be read.</summary>
    /// <param name="file">What it holds; empty when it is missing or cannot be read.</param>
    /// <returns><see langword="false"/> when a file is there but cannot be read (held, broken or foreign).</returns>
    private bool TryRead(out ExitsFile file)
    {
        file = ExitsFile.Empty;
        if (_path is null || _protector is null || !File.Exists(_path))
        {
            return true;
        }

        byte[]? json = null;
        try
        {
            json = _protector.Unprotect(File.ReadAllBytes(_path), Purpose);
            if (JsonSerializer.Deserialize<ExitsFile>(json) is not { Exits: not null } read)
            {
                return false;
            }

            file = read;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or CryptographicException or JsonException or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            if (json is not null)
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
    }

    /// <summary>Changes the file in one step: written whole under another name, flushed and moved into place.</summary>
    /// <param name="change">Makes the new content from the old.</param>
    private void Change(Func<ExitsFile, ExitsFile> change)
    {
        if (_path is null || _protector is null)
        {
            throw new InvalidOperationException("Copies of the exits cannot be kept here.");
        }

        lock (_writing)
        {
            var file = change(Read());
            if (file.Exits.Count == 0)
            {
                // Nothing allowed: no file at all, so that no WireGuard file lies outside the vault.
                File.Delete(_path);
                return;
            }

            var json = JsonSerializer.SerializeToUtf8Bytes(file);
            try
            {
                WriteWhole(_path, _protector.Protect(json, Purpose));
            }
            catch (CryptographicException exception)
            {
                throw new IOException("The exits for the background could not be protected.", exception);
            }
            catch (PlatformNotSupportedException exception)
            {
                throw new InvalidOperationException("Copies of the exits cannot be protected here.", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json);
            }
        }
    }

    /// <summary>Writes bytes whole, flushed through to the disk, and moves them into place.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The bytes.</param>
    private static void WriteWhole(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>What the file holds.</summary>
    /// <param name="Selected">The program's selected exit, or <see langword="null"/>.</param>
    /// <param name="Exits">The allowed exits' records from the vault, by id.</param>
    private sealed record ExitsFile(string? Selected, IReadOnlyDictionary<string, string> Exits)
    {
        /// <summary>Gets the file when none is written.</summary>
        public static ExitsFile Empty { get; } = new(null, new Dictionary<string, string>());

        /// <summary>Masks the records, which hold private keys, so that the content is never logged.</summary>
        /// <returns>A description without the records.</returns>
        public override string ToString() => $"ExitsFile {{ Selected = {Selected}, Exits = {Exits.Count} }}";
    }
}

/// <summary>
/// The vault's named secrets as the background process sees them (decided 2026-11-08): only the allowed exits and the
/// selected exit, read from <see cref="BackgroundExitFile"/>, so that <see cref="ExitRecords"/> and the connection
/// work there unchanged. An exit that is not allowed is not there, the selected exit is there only when it is allowed,
/// and nothing can be written.
/// </summary>
/// <param name="file">The file of allowed exits.</param>
internal sealed class BackgroundExitSecrets(BackgroundExitFile file) : IVaultSecrets
{
    /// <inheritdoc />
    public IReadOnlyList<string> List(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return [.. file.Allowed().Select(ExitName).Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public string? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == ExitRecords.SelectedName)
        {
            return file.Usable()?.ToString("N");
        }

        return name.StartsWith(ExitRecords.ExitPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(name[ExitRecords.ExitPrefix.Length..], "N", out var id)
                ? file.Find(id)
                : null;
    }

    /// <inheritdoc />
    public void Save(string name, string value) => throw ReadOnly();

    /// <inheritdoc />
    public bool Remove(string name) => throw ReadOnly();

    /// <summary>Gives the secret's name of an exit.</summary>
    /// <param name="id">The exit.</param>
    /// <returns>The name.</returns>
    private static string ExitName(Guid id) => ExitRecords.ExitPrefix + id.ToString("N");

    /// <summary>The error: the background never writes the exits.</summary>
    /// <returns>The error.</returns>
    private static NotSupportedException ReadOnly() =>
        new("The background process only reads the exits allowed for it.");
}
