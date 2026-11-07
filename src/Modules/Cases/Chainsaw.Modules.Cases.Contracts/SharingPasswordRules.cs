namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>The rules for a case's sharing password, in one place for the module and the user interface.</summary>
public static class SharingPasswordRules
{
    /// <summary>The shortest sharing password that is accepted; the same as for the master key.</summary>
    public const int MinimumLength = 12;
}
