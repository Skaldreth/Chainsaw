using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.Modules.Monitoring;

/// <summary>The purposes the files are protected for, so that one is never read as another.</summary>
internal static class BackgroundPurposes
{
    /// <summary>The plan.</summary>
    public const string Plan = "Chainsaw.Watch.Plan.v1";

    /// <summary>A queued entry.</summary>
    public const string Queue = "Chainsaw.Watch.Queue.v1";

    /// <summary>The background's progress.</summary>
    public const string Progress = "Chainsaw.Watch.Progress.v1";
}

/// <summary>The plan as <c>plan.bin</c>, protected for the Windows user.</summary>
/// <param name="folder">The shared folder.</param>
internal sealed class BackgroundPlanFile(BackgroundFolder folder) : IBackgroundPlan
{
    /// <summary>Lets one change at a time in this process.</summary>
    private readonly Lock _writing = new();

    /// <summary>Gets the file.</summary>
    private string FilePath => folder.PathOf("plan.bin");

    /// <inheritdoc/>
    public BackgroundPlan Read()
    {
        TryRead(out var plan);
        return plan;
    }

    /// <inheritdoc/>
    public bool TryRead(out BackgroundPlan plan)
    {
        try
        {
            plan = folder.ReadProtected(FilePath, BackgroundPurposes.Plan) is { } json
                ? BackgroundCodec.DecodePlan(json)
                : BackgroundPlan.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or PlatformNotSupportedException)
        {
            // A plan that is not this user's, or broken, watches nothing; Chainsaw writes it anew at the next change.
            plan = BackgroundPlan.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            plan = BackgroundPlan.Empty;
            return false;
        }
    }

    /// <inheritdoc/>
    public DateTimeOffset? WrittenAt()
    {
        try
        {
            return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public BackgroundPlan Update(Func<BackgroundPlan, BackgroundPlan> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_writing)
        {
            var plan = change(Read());
            folder.WriteProtected(FilePath, BackgroundPurposes.Plan, BackgroundCodec.Encode(plan));
            return plan;
        }
    }
}

/// <summary>
/// The queue: one protected file per entry under <c>queue\&lt;case&gt;\</c>, named by when it was made, its id and,
/// for an entry with hits, a mark, so that the lock screen can tell that a watch has hit without reading any entry.
/// </summary>
/// <param name="folder">The shared folder.</param>
internal sealed class WatchQueue(BackgroundFolder folder) : IWatchQueue
{
    /// <summary>The ending of an entry's file.</summary>
    private const string Ending = ".bin";

    /// <summary>The mark of an entry with hits, before the ending.</summary>
    private const string HitMark = "-h";

    /// <summary>The ending of an entry set aside.</summary>
    private const string AsideEnding = ".broken";

    /// <inheritdoc/>
    public void Add(QueuedCheck entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var mark = entry.HasNewHits ? HitMark : string.Empty;
        var name = $"{entry.Utc.UtcTicks.ToString("D19", CultureInfo.InvariantCulture)}-{entry.Id:N}{mark}{Ending}";
        folder.WriteProtected(Path.Combine(CaseFolder(entry.CaseId), name), BackgroundPurposes.Queue,
            BackgroundCodec.Encode(entry));
    }

    /// <inheritdoc/>
    public IReadOnlyList<Guid> List(Guid caseId) =>
        [.. Files(caseId).Select(IdOf).OfType<Guid>()];

    /// <inheritdoc/>
    public QueuedCheck? Read(Guid caseId, Guid id)
    {
        if (Find(caseId, id) is not { } path)
        {
            return null;
        }

        try
        {
            if (folder.ReadProtected(path, BackgroundPurposes.Queue) is not { } json)
            {
                return null;
            }

            var entry = BackgroundCodec.DecodeEntry(json);
            if (entry.Id != id || entry.CaseId != caseId || !entry.Responses.All(Whole))
            {
                throw new InvalidDataException("A queued entry does not hold together.");
            }

            return entry;
        }
        catch (InvalidDataException)
        {
            SetAside(caseId, id);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Remove(Guid caseId, Guid id)
    {
        if (Find(caseId, id) is { } path)
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc/>
    public bool HasHits()
    {
        var root = folder.PathOf("queue");
        try
        {
            return Directory.Exists(root)
                && Directory.EnumerateFiles(root, "*" + HitMark + Ending, SearchOption.AllDirectories).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Sets an entry that cannot be read aside, so that it is kept but never read again.</summary>
    /// <param name="caseId">The case.</param>
    /// <param name="id">The entry.</param>
    public void SetAside(Guid caseId, Guid id)
    {
        if (Find(caseId, id) is { } path)
        {
            File.Move(path, path + AsideEnding, overwrite: true);
        }
    }

    /// <summary>Says whether a response's body still matches its SHA-256.</summary>
    /// <param name="response">The response.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Whole(Chains.Contracts.SealedResponse response) =>
        response.Body is not null && response.Sha256 is not null
        && Convert.ToHexStringLower(SHA256.HashData(response.Body)) == response.Sha256;

    /// <summary>Gives a case's folder in the queue.</summary>
    /// <param name="caseId">The case.</param>
    /// <returns>The folder.</returns>
    private string CaseFolder(Guid caseId) => folder.PathOf("queue", caseId.ToString("N"));

    /// <summary>Lists a case's entry files, oldest first.</summary>
    /// <param name="caseId">The case.</param>
    /// <returns>The files.</returns>
    private IEnumerable<string> Files(Guid caseId)
    {
        var path = CaseFolder(caseId);
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*" + Ending).Order(StringComparer.Ordinal).ToList()
            : [];
    }

    /// <summary>Finds an entry's file.</summary>
    /// <param name="caseId">The case.</param>
    /// <param name="id">The entry.</param>
    /// <returns>The file, or <see langword="null"/>.</returns>
    private string? Find(Guid caseId, Guid id)
    {
        // The id is in the name: the folder is searched for it, not listed and sorted, so a long queue stays quick.
        var path = CaseFolder(caseId);
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, $"*-{id:N}*{Ending}").FirstOrDefault(each => IdOf(each) == id)
            : null;
    }

    /// <summary>Reads an entry's id from its file's name.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The id, or <see langword="null"/> for a name that is not an entry's.</returns>
    private static Guid? IdOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('-');
        return parts.Length >= 2 && Guid.TryParseExact(parts[1], "N", out var id) ? id : null;
    }
}

/// <summary>
/// Holds a case as open in Chainsaw with a file under <c>held\</c> that Chainsaw keeps open without sharing: the
/// background sees that it cannot open it. The file is removed when it is closed, and the system lets go of it when
/// Chainsaw ends in any way.
/// </summary>
/// <param name="folder">The shared folder.</param>
internal sealed class CaseHolds(BackgroundFolder folder) : ICaseHolds
{
    /// <inheritdoc/>
    public IDisposable? Hold(Guid caseId)
    {
        var path = PathOf(caseId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public bool IsHeld(Guid caseId)
    {
        var path = PathOf(caseId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Gives a case's hold file.</summary>
    /// <param name="caseId">The case.</param>
    /// <returns>The path.</returns>
    private string PathOf(Guid caseId) => folder.PathOf("held", caseId.ToString("N") + ".lock");
}

/// <summary>The background's status as <c>status.json</c>: plain, since it says nothing about a case.</summary>
/// <param name="folder">The shared folder.</param>
internal sealed class BackgroundStatusFile(BackgroundFolder folder) : IBackgroundStatusFile
{
    /// <summary>The states as words, readable by hand.</summary>
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Gets the file.</summary>
    private string FilePath => folder.PathOf("status.json");

    /// <inheritdoc/>
    public BackgroundStatus? Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<BackgroundStatus>(File.ReadAllBytes(FilePath), Json)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void Write(BackgroundStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        BackgroundFolder.WriteWhole(FilePath, JsonSerializer.SerializeToUtf8Bytes(status, Json));
    }
}

/// <summary>The background's progress as <c>progress.bin</c>, protected for the Windows user.</summary>
/// <param name="folder">The shared folder.</param>
internal sealed class BackgroundProgressFile(BackgroundFolder folder) : IBackgroundProgressFile
{
    /// <summary>Gets the file.</summary>
    private string FilePath => folder.PathOf("progress.bin");

    /// <inheritdoc/>
    public IReadOnlyList<BackgroundProgress> Read()
    {
        try
        {
            return folder.ReadProtected(FilePath, BackgroundPurposes.Progress) is { } json
                ? BackgroundCodec.DecodeProgress(json)
                : [];
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Without it the background starts from the plan; a hit queued twice is brought in once.
            return [];
        }
    }

    /// <inheritdoc/>
    public void Write(IReadOnlyList<BackgroundProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        folder.WriteProtected(FilePath, BackgroundPurposes.Progress, BackgroundCodec.Encode(progress));
    }
}
