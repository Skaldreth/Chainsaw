namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>The journal codes the Cases module writes itself.</summary>
public static class CaseJournalKinds
{
    /// <summary>The case was created.</summary>
    public const string Created = "case.created";

    /// <summary>The working copy of an interrupted session was taken back into the case.</summary>
    public const string Recovered = "case.recovered";

    /// <summary>The case's schema was brought up to date by this build.</summary>
    public const string SchemaUpgraded = "case.schema-upgraded";

    /// <summary>The case was opened with its sharing password.</summary>
    public const string OpenedWithPassword = "case.opened-with-password";

    /// <summary>A sharing envelope was added or replaced.</summary>
    public const string SharingPasswordSet = "case.sharing-password-set";

    /// <summary>The sharing envelope was removed.</summary>
    public const string SharingPasswordRemoved = "case.sharing-password-removed";

    /// <summary>An envelope for the operator's vault was added.</summary>
    public const string OperatorAccessAdded = "case.operator-access-added";

    /// <summary>
    /// The case did not match what this computer recorded when it last saved it (an older copy, a change made outside
    /// Chainsaw, or anchors that were missing or unreadable); the details say what, and the anchors were replaced.
    /// </summary>
    public const string AnchorsReplaced = "case.anchors-replaced";

    /// <summary>
    /// An envelope for the organisation's recovery key was added: to an older case, or because the key built into
    /// Chainsaw was replaced. The details give the key's fingerprint.
    /// </summary>
    public const string RecoveryAccessAdded = "case.recovery-access-added";

    /// <summary>
    /// The case was opened with the organisation's private recovery key; the details name the administrator and the
    /// key's fingerprint.
    /// </summary>
    public const string OpenedWithRecoveryKey = "case.opened-with-recovery-key";
}
