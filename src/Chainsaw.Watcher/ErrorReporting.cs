using System.Runtime.InteropServices;

namespace Chainsaw.Watcher;

/// <summary>
/// Keeps Windows Error Reporting away from this process (decided 2026-11-09): a hard crash must never leave a dump,
/// which could hold a decrypted case or a key, that Microsoft may ask for. The program is excluded from reporting
/// for the signed-in user (no administrator needed), and a dump that is made all the same leaves the heap out.
/// </summary>
/// <remarks>
/// Every failure is ignored: an older or locked-down Windows only keeps its default, and starting never fails for it.
/// </remarks>
internal static partial class ErrorReporting
{
    /// <summary>Leaves the heap out of a report's dump (WER_FAULT_REPORTING_FLAG_NOHEAP).</summary>
    private const uint NoHeap = 1;

    /// <summary>Excludes the running program from Windows Error Reporting, for the signed-in user only.</summary>
    public static void Exclude()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } path)
        {
            return;
        }

        try
        {
            // By the full path as asked, and by the file name, which is how Windows lists excluded programs; either
            // is enough where Windows matches it, and an unused entry costs nothing.
            _ = WerAddExcludedApplication(path, allUsers: false);
            _ = WerAddExcludedApplication(Path.GetFileName(path), allUsers: false);
            _ = WerSetFlags(NoHeap);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // No Windows Error Reporting here: nothing to keep away.
        }
    }

    /// <summary>Adds a program to the user's (or every user's) list of programs never reported.</summary>
    /// <param name="exeName">The program's path or file name.</param>
    /// <param name="allUsers">Whether for every user, which needs an administrator; always <see langword="false"/>.</param>
    /// <returns>An HRESULT; ignored.</returns>
    [LibraryImport("wer.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WerAddExcludedApplication(
        string exeName, [MarshalAs(UnmanagedType.Bool)] bool allUsers);

    /// <summary>Sets how a report is made for this process.</summary>
    /// <param name="flags">The flags (WER_FAULT_REPORTING_*).</param>
    /// <returns>An HRESULT; ignored.</returns>
    [LibraryImport("wer.dll")]
    private static partial int WerSetFlags(uint flags);
}
