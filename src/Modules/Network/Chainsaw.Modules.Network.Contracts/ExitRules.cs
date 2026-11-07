namespace Chainsaw.Modules.Network.Contracts;

/// <summary>The limits on what an exit is described with.</summary>
public static class ExitRules
{
    /// <summary>The longest name of an exit.</summary>
    public const int MaximumNameLength = 100;

    /// <summary>The longest place of an exit.</summary>
    public const int MaximumPlaceLength = 100;

    /// <summary>The longest configuration of an exit, in characters (a WireGuard file is far shorter).</summary>
    public const int MaximumConfigurationLength = 64 * 1024;
}
