namespace Chainsaw.Modules.Cases;

/// <summary>The field names in the details of the journal entries about the recovery key.</summary>
internal static class RecoveryDetails
{
    /// <summary>The administrator who opened the case with the private recovery key.</summary>
    public const string Administrator = "administrator";

    /// <summary>The recovery key's fingerprint.</summary>
    public const string Fingerprint = "fingerprint";

    /// <summary>The fingerprints of the revoked keys whose envelopes were removed.</summary>
    public const string Removed = "removed";

    /// <summary>How many damaged envelopes for the configured key were replaced by a sound one.</summary>
    public const string Replaced = "replaced";
}
