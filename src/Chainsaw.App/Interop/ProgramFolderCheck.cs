using System.Security.AccessControl;
using System.Security.Principal;
using Chainsaw.App.Links;

namespace Chainsaw.App.Interop;

/// <summary>
/// Reads the owner and access list of the folder Chainsaw runs from and of each parent up to the volume's root, and
/// lets <see cref="ProgramFolder.Judge"/> say whether others can change it. Only on Windows; elsewhere the folder
/// counts as safe, since there is neither a <c>Run</c> key nor a link to write.
/// </summary>
public static class ProgramFolderCheck
{
    /// <summary>Judges the folder; a folder whose rights cannot be read is not known to be safe.</summary>
    /// <param name="folder">The program folder, normally <see cref="AppContext.BaseDirectory"/>.</param>
    /// <returns>The verdict.</returns>
    public static ProgramFolderVerdict Check(string folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ProgramFolderVerdict.Safe;
        }

        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User?.Value is not { } user)
            {
                return new ProgramFolderVerdict(ProgramFolderProblem.Unreadable, path);
            }

            var folders = new List<FolderAcl>();
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            {
                folders.Add(Read(directory));
            }

            return ProgramFolder.Judge(folders, user);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
            or IdentityNotMappedException or InvalidOperationException or PlatformNotSupportedException)
        {
            return new ProgramFolderVerdict(ProgramFolderProblem.Unreadable, path);
        }
    }

    /// <summary>Reads one folder's owner and access list, inherited entries included, in their order.</summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The folder as plain values.</returns>
    private static FolderAcl Read(DirectoryInfo directory)
    {
        var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var owner = security.GetOwner(typeof(SecurityIdentifier))?.Value;
        var aces = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => new FolderAce(
                rule.IdentityReference.Value,
                (int)rule.FileSystemRights,
                rule.AccessControlType == AccessControlType.Deny,
                rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit),
                rule.InheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit),
                rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)))
            .ToList();
        return new FolderAcl(directory.FullName, owner, aces);
    }
}
