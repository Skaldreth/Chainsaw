namespace Chainsaw.Modules.Cases;

/// <summary>
/// The field names in the details of the journal entries about a working copy that did not belong to its file
/// (sketch ÄR1b).
/// </summary>
internal static class BranchDetails
{
    /// <summary>The folder under the cases folder that a working copy was moved to.</summary>
    public const string Folder = "folder";

    /// <summary>The SHA-256 of the working copy that was moved, in lower-case hexadecimal.</summary>
    public const string WorkingCopySha256 = "workingCopySha256";

    /// <summary>The case number of the file the working copy went apart from.</summary>
    public const string From = "from";

    /// <summary>The id of the case the working copy went apart from.</summary>
    public const string FromCaseId = "fromCaseId";

    /// <summary>The case number the working copy was saved under.</summary>
    public const string CaseNumber = "caseNumber";

    /// <summary>The new id the working copy was saved under.</summary>
    public const string CaseId = "caseId";

    /// <summary>The name of a colleague's file that was taken into the folder.</summary>
    public const string ImportedFrom = "importedFrom";

    /// <summary>The SHA-256 of a colleague's file as it was copied, in lower-case hexadecimal.</summary>
    public const string ImportedSha256 = "importedSha256";

    /// <summary>The envelopes of the file that could not follow to the new case, as words.</summary>
    public const string NotCarried = "notCarried";
}
