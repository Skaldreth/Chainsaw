namespace Chainsaw.Modules.Cases;

/// <summary>The field names in the details of the journal entry that created a case.</summary>
internal static class CreatedDetails
{
    /// <summary>The case id.</summary>
    public const string CaseId = "caseId";

    /// <summary>The case number.</summary>
    public const string CaseNumber = "caseNumber";

    /// <summary>When the case was created, in UTC, as written in the case information.</summary>
    public const string CreatedUtc = "createdUtc";

    /// <summary>The schema versions the case was created with.</summary>
    public const string Schema = "schema";

    /// <summary>
    /// The fingerprint of the recovery key the case got an envelope for, or null when no key was configured.
    /// </summary>
    public const string RecoveryKey = "recoveryKey";
}
