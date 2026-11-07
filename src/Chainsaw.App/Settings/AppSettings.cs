namespace Chainsaw.App.Settings;

/// <summary>
/// The program's own settings, which hold nothing secret and nothing about a case, so they live in a plain file next
/// to the vault (<c>settings.json</c>).
/// </summary>
public sealed record AppSettings
{
    /// <summary>The automatic lock's time unless the user chooses another (decided 2026-11-07).</summary>
    public const int DefaultAutoLockMinutes = 15;

    /// <summary>The shortest time that can be chosen.</summary>
    public const int MinimumAutoLockMinutes = 1;

    /// <summary>The longest time that can be chosen: a working day.</summary>
    public const int MaximumAutoLockMinutes = 480;

    /// <summary>Gets how many minutes without activity before Chainsaw locks itself.</summary>
    public int AutoLockMinutes { get; init; } = DefaultAutoLockMinutes;

    /// <summary>Gets the automatic lock's time, kept within the allowed range.</summary>
    public TimeSpan AutoLockTimeout =>
        TimeSpan.FromMinutes(Math.Clamp(AutoLockMinutes, MinimumAutoLockMinutes, MaximumAutoLockMinutes));
}
