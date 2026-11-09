namespace Chainsaw.App.Links;

/// <summary>Why the program folder is not safe to start from Windows (the <c>Run</c> key and the link).</summary>
public enum ProgramFolderProblem
{
    /// <summary>Only the user, the administrators and Windows itself can change the folder.</summary>
    None,

    /// <summary>Someone else may change the folder, its files or one of its parents.</summary>
    OpenToOthers,

    /// <summary>Someone else owns the folder or a parent, and an owner can always change its rights.</summary>
    OwnedByOther,

    /// <summary>The rights could not be read, so the folder is not known to be safe.</summary>
    Unreadable,
}

/// <summary>One entry (ACE) of a folder's access list, as plain values so that it is judged without Windows.</summary>
/// <param name="Sid">Whom it names, as a SID string (<c>S-1-5-11</c>).</param>
/// <param name="Rights">The access mask, generic rights included.</param>
/// <param name="IsDeny">Whether it denies the rights rather than allows them.</param>
/// <param name="ContainerInherit">Whether it is inherited by subfolders.</param>
/// <param name="ObjectInherit">Whether it is inherited by files.</param>
/// <param name="InheritOnly">Whether it applies only to what inherits it, not to the folder itself.</param>
public sealed record FolderAce(
    string Sid, int Rights, bool IsDeny = false, bool ContainerInherit = false, bool ObjectInherit = false,
    bool InheritOnly = false);

/// <summary>A folder's owner and access list, in the order Windows reads it.</summary>
/// <param name="Path">The folder.</param>
/// <param name="Owner">The owner's SID, or <see langword="null"/> when not known.</param>
/// <param name="Aces">The entries, in the list's order.</param>
public sealed record FolderAcl(string Path, string? Owner, IReadOnlyList<FolderAce> Aces);

/// <summary>Whether the program folder is safe, and if not, where, who and which rights.</summary>
/// <param name="Problem">What is wrong, or <see cref="ProgramFolderProblem.None"/>.</param>
/// <param name="Path">The folder where it was found.</param>
/// <param name="Sid">Who may change it.</param>
/// <param name="Rights">The rights that make it unsafe.</param>
public sealed record ProgramFolderVerdict(
    ProgramFolderProblem Problem, string? Path = null, string? Sid = null, int Rights = 0)
{
    /// <summary>The verdict for a folder only the user, the administrators and Windows can change.</summary>
    public static readonly ProgramFolderVerdict Safe = new(ProgramFolderProblem.None);

    /// <summary>Gets whether Windows may start Chainsaw's programs from the folder on its own.</summary>
    public bool IsSafe => Problem == ProgramFolderProblem.None;
}

/// <summary>
/// Judges whether others can change the folder Chainsaw runs from (decision 16). The <c>Run</c> key starts the
/// background process from it at every sign-in and the link <c>chainsaw:</c> starts <c>Chainsaw.exe</c> from it, so a
/// folder another local user may write to (such as <c>C:\Chainsaw</c>, which inherits <em>Authenticated Users:
/// Modify</em> from <c>C:\</c>) would run their code as this user. Pure: the access lists are read elsewhere.
/// </summary>
public static class ProgramFolder
{
    /// <summary>Everyone (<c>S-1-1-0</c>), whose deny entries apply to every user.</summary>
    public const string Everyone = "S-1-1-0";

    /// <summary>CREATOR OWNER, which in an inherit-only entry stands for whoever creates the child.</summary>
    public const string CreatorOwner = "S-1-3-0";

    /// <summary>OWNER RIGHTS, which limits the owner; the owner is judged on its own.</summary>
    public const string OwnerRights = "S-1-3-4";

    /// <summary>Local System.</summary>
    public const string LocalSystem = "S-1-5-18";

    /// <summary>The local Administrators group.</summary>
    public const string Administrators = "S-1-5-32-544";

    /// <summary>The TrustedInstaller service.</summary>
    public const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    /// <summary>Creating files (<c>FILE_WRITE_DATA</c>/<c>FILE_ADD_FILE</c>).</summary>
    public const int WriteData = 0x2;

    /// <summary>Appending, or creating subfolders (<c>FILE_APPEND_DATA</c>/<c>FILE_ADD_SUBDIRECTORY</c>).</summary>
    public const int AppendData = 0x4;

    /// <summary>Writing extended attributes (<c>FILE_WRITE_EA</c>), part of generic write only.</summary>
    private const int WriteExtendedAttributes = 0x10;

    /// <summary>Deleting what is in the folder (<c>FILE_DELETE_CHILD</c>), and so replacing it.</summary>
    public const int DeleteChild = 0x40;

    /// <summary>Writing attributes (<c>FILE_WRITE_ATTRIBUTES</c>), part of generic write only.</summary>
    private const int WriteAttributes = 0x100;

    /// <summary>Deleting or renaming the object itself (<c>DELETE</c>).</summary>
    public const int Delete = 0x10000;

    /// <summary>Changing the rights (<c>WRITE_DAC</c>).</summary>
    public const int ChangePermissions = 0x40000;

    /// <summary>Taking ownership (<c>WRITE_OWNER</c>).</summary>
    public const int TakeOwnership = 0x80000;

    /// <summary>Every right on a file or folder (<c>FILE_ALL_ACCESS</c>).</summary>
    private const int AllFileRights = 0x1F01FF;

    /// <summary><c>GENERIC_ALL</c>, as an inherit-only entry often carries it.</summary>
    private const int GenericAll = 0x10000000;

    /// <summary><c>GENERIC_WRITE</c>.</summary>
    private const int GenericWrite = 0x40000000;

    /// <summary>
    /// The rights that let someone change the program folder or what is in it: add a DLL or a program, replace or
    /// append to one, delete or rename, change the rights or take the folder.
    /// </summary>
    private const int ProgramRights =
        WriteData | AppendData | DeleteChild | Delete | ChangePermissions | TakeOwnership;

    /// <summary>
    /// The rights on a parent that reach the program folder: moving it away (deleting a child, or renaming the parent
    /// itself) and changing the parent's rights. Creating files or folders in a parent changes nothing Chainsaw runs.
    /// </summary>
    private const int ParentRights = DeleteChild | Delete | ChangePermissions | TakeOwnership;

    /// <summary>Where an entry applies in a folder.</summary>
    private enum Scope
    {
        /// <summary>The folder itself.</summary>
        Folder,

        /// <summary>The files in it.</summary>
        Files,

        /// <summary>The folders in it.</summary>
        Subfolders,
    }

    /// <summary>
    /// Judges the program folder and its parents: unsafe when anyone but the user, Administrators, SYSTEM and
    /// TrustedInstaller (and CREATOR OWNER in an inherit-only entry) owns one of them, or holds a right that changes
    /// the program folder, its files or subfolders, or moves it away from a parent. Entries are read in order, as
    /// Windows does: a right a deny entry for the same SID or for Everyone has already taken is not granted after it.
    /// </summary>
    /// <param name="folders">The program folder first, then each parent up to the volume's root.</param>
    /// <param name="user">The current user's SID.</param>
    /// <returns>The first problem found, or <see cref="ProgramFolderVerdict.Safe"/>.</returns>
    public static ProgramFolderVerdict Judge(IReadOnlyList<FolderAcl> folders, string user)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        for (var index = 0; index < folders.Count; index++)
        {
            var folder = folders[index];
            if (folder.Owner is { } owner && !IsTrusted(owner, user))
            {
                return new ProgramFolderVerdict(ProgramFolderProblem.OwnedByOther, folder.Path, owner);
            }

            Scope[] scopes = index == 0 ? [Scope.Folder, Scope.Files, Scope.Subfolders] : [Scope.Folder];
            var dangerous = index == 0 ? ProgramRights : ParentRights;
            foreach (var scope in scopes)
            {
                if (OpenRight(folder, scope, dangerous, user) is { } found)
                {
                    return found;
                }
            }
        }

        return ProgramFolderVerdict.Safe;
    }

    /// <summary>Finds an untrusted SID granted a dangerous right where it applies in a folder.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="scope">Where in it.</param>
    /// <param name="dangerous">The rights that matter there.</param>
    /// <param name="user">The current user's SID.</param>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    private static ProgramFolderVerdict? OpenRight(FolderAcl folder, Scope scope, int dangerous, string user)
    {
        var applying = folder.Aces.Where(ace => AppliesTo(ace, scope)).ToList();
        var granted = applying
            .Where(ace => !ace.IsDeny && !IsTrusted(ace, user))
            .Select(ace => ace.Sid)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in granted)
        {
            var rights = Granted(applying, sid) & dangerous;
            if (rights != 0)
            {
                return new ProgramFolderVerdict(ProgramFolderProblem.OpenToOthers, folder.Path, sid, rights);
            }
        }

        return null;
    }

    /// <summary>The rights a SID is granted by the entries in order, after the denies before each grant.</summary>
    /// <param name="aces">The entries that apply, in order.</param>
    /// <param name="sid">The SID.</param>
    /// <returns>The rights granted.</returns>
    private static int Granted(IEnumerable<FolderAce> aces, string sid)
    {
        int granted = 0, denied = 0;
        foreach (var ace in aces)
        {
            var rights = Specific(ace.Rights);
            if (ace.IsDeny && (Same(ace.Sid, sid) || Same(ace.Sid, Everyone)))
            {
                denied |= rights & ~granted;
            }
            else if (!ace.IsDeny && Same(ace.Sid, sid))
            {
                granted |= rights & ~denied;
            }
        }

        return granted;
    }

    /// <summary>Whether an entry applies to the folder itself, its files or its subfolders.</summary>
    /// <param name="ace">The entry.</param>
    /// <param name="scope">Where.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool AppliesTo(FolderAce ace, Scope scope) => scope switch
    {
        Scope.Folder => !ace.InheritOnly,
        Scope.Files => ace.ObjectInherit,
        _ => ace.ContainerInherit,
    };

    /// <summary>Maps the generic rights to the file rights they stand for, so that they compare bit by bit.</summary>
    /// <param name="rights">The access mask.</param>
    /// <returns>The mask with the generic rights spelt out.</returns>
    private static int Specific(int rights)
    {
        if ((rights & GenericAll) != 0)
        {
            rights |= AllFileRights;
        }

        if ((rights & GenericWrite) != 0)
        {
            rights |= WriteData | AppendData | WriteExtendedAttributes | WriteAttributes;
        }

        return rights;
    }

    /// <summary>Whether a SID is one that may change Chainsaw's program folder.</summary>
    /// <param name="sid">The SID.</param>
    /// <param name="user">The current user's SID.</param>
    /// <returns><see langword="true"/> for the user, Administrators, SYSTEM and TrustedInstaller.</returns>
    private static bool IsTrusted(string sid, string user) =>
        Same(sid, user) || Same(sid, Administrators) || Same(sid, LocalSystem) || Same(sid, TrustedInstaller);

    /// <summary>Whether an entry's SID is trusted, CREATOR OWNER counting only in an inherit-only entry.</summary>
    /// <param name="ace">The entry.</param>
    /// <param name="user">The current user's SID.</param>
    /// <returns><see langword="true"/> when it may be ignored.</returns>
    private static bool IsTrusted(FolderAce ace, string user) =>
        IsTrusted(ace.Sid, user) || Same(ace.Sid, OwnerRights) || (ace.InheritOnly && Same(ace.Sid, CreatorOwner));

    /// <summary>Compares two SIDs.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
