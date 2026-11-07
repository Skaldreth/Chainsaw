namespace Chainsaw.Modules.Cases;

/// <summary>The field names in the details of the journal entries about the recovery key.</summary>
internal static class RecoveryDetails
{
    /// <summary>The administrator who opened the case with the private recovery key.</summary>
    public const string Administrator = "administrator";

    /// <summary>The recovery key's fingerprint.</summary>
    public const string Fingerprint = "fingerprint";
}
