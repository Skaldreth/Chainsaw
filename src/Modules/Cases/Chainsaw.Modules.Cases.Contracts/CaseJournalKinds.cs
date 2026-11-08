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
    /// The recovery envelopes changed when the case was opened: an envelope for the organisation's key was added (to
    /// an older case, or because the key built into Chainsaw was replaced), and envelopes for revoked keys were
    /// removed. The details give the added key's fingerprint and the removed ones.
    /// </summary>
    public const string RecoveryAccessChanged = "case.recovery-access-changed";

    /// <summary>
    /// The case was opened with the organisation's private recovery key; the details name the administrator and the
    /// key's fingerprint.
    /// </summary>
    public const string OpenedWithRecoveryKey = "case.opened-with-recovery-key";

    /// <summary>
    /// The case's title was set or changed; the details give the new title. Earlier titles stay in the sealed chain
    /// <c>cases.title</c>.
    /// </summary>
    public const string TitleSet = "case.title-set";

    /// <summary>
    /// The case was opened after a working copy that did not belong to the file had been moved to the folder
    /// <c>Återställda</c>, where it is kept; the details give the folder and the working copy's SHA-256.
    /// </summary>
    public const string WorkingCopySetAside = "case.working-copy-set-aside";

    /// <summary>
    /// The case was made from a working copy that did not belong to its file, as a case of its own with a new id and
    /// case number; the details give the case it went apart from (number and id), the new id and number, and the
    /// envelopes that could not follow.
    /// </summary>
    public const string BranchSaved = "case.branch-saved";
}
