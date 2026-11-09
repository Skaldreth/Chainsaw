namespace Chainsaw.Modules.Cases;

/// <summary>Where the case files live and how hard a sharing password is to guess.</summary>
public sealed class CasesOptions
{
    /// <summary>The PBKDF2-SHA512 iterations for a new sharing password (the same as for the master key).</summary>
    public const int DefaultSharingIterations = 600_000;

    /// <summary>The application's folder under the local application data folder.</summary>
    private const string ApplicationFolder = "Chainsaw";

    /// <summary>The name of the cases folder.</summary>
    private const string CasesFolder = "Ärenden";

    /// <summary>The name of the anchors folder under the application's folder (and the vault's).</summary>
    public const string AnchorsFolder = "Ankare";

    /// <summary>
    /// Gets or sets the folder that holds the case files. The application uses
    /// <c>%LOCALAPPDATA%\Chainsaw\Ärenden</c>, which is not synchronised to the cloud the way Documents often is;
    /// tests use a temporary folder. The folder is created when the first case is created.
    /// </summary>
    public string Directory { get; set; } = DefaultDirectory(CasesFolder);

    /// <summary>
    /// Gets or sets the folder for this computer's anchors of the cases' chains (see <c>AnchorStore</c>). The
    /// application uses <c>%LOCALAPPDATA%\Chainsaw\Ankare</c>.
    /// </summary>
    public string AnchorDirectory { get; set; } = DefaultDirectory(AnchorsFolder);

    /// <summary>
    /// Gets the organisation's public recovery key that every case gets an envelope for, or
    /// <see langword="null"/> when none is configured. The application uses the key built into the module (see
    /// <see cref="EmbeddedRecoveryKey"/>); only tests set their own, through the internal setter.
    /// </summary>
    public Contracts.RecoveryPublicKey? RecoveryKey { get; internal set; } = BuiltIn.Key;

    /// <summary>
    /// Gets the fingerprints of earlier recovery keys that are revoked: their envelopes are removed from every case
    /// that is opened. Envelopes for keys that are merely unknown are never removed. Only tests set their own.
    /// </summary>
    public IReadOnlySet<string> RevokedRecoveryKeys { get; internal set; } = BuiltIn.Revoked;

    /// <summary>The configuration built into the module, read once.</summary>
    private static RecoveryKeyConfiguration BuiltIn => BuiltInConfiguration.Value;

    /// <summary>Reads the built-in configuration the first time it is needed.</summary>
    private static readonly Lazy<RecoveryKeyConfiguration> BuiltInConfiguration = new(EmbeddedRecoveryKey.Load);

    /// <summary>
    /// Gets or sets the iterations used when a sharing password is set. Only tests lower it, to stay fast; the count
    /// is stored in the envelope, so a file always opens with the count it was written with.
    /// </summary>
    internal int SharingIterations { get; set; } = DefaultSharingIterations;

    /// <summary>
    /// Gets or sets the fewest iterations a sharing envelope may have to be used. One with fewer (which Chainsaw never
    /// writes) is refused when the password is tried and removed from the file when the case is opened. Only tests
    /// lower it, with <see cref="SharingIterations"/>.
    /// </summary>
    internal int MinimumSharingIterations { get; set; } = 100_000;

    /// <summary>The quiet after a write before an open case saves itself (<see cref="AutosaveQuietPeriod"/>).</summary>
    internal static readonly TimeSpan DefaultAutosaveQuietPeriod = TimeSpan.FromSeconds(5);

    /// <summary>The longest a write waits to be saved (see <see cref="AutosaveMaximumDelay"/>).</summary>
    internal static readonly TimeSpan DefaultAutosaveMaximumDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how long an open case must be quiet after a write before it writes its working copy back into the
    /// case file by itself, so that a file that is handed on is fresh. Only tests change it.
    /// </summary>
    internal TimeSpan AutosaveQuietPeriod { get; set; } = DefaultAutosaveQuietPeriod;

    /// <summary>
    /// Gets or sets the longest a write waits to be saved while writes keep coming, and the wait after a save that
    /// failed. Only tests change it.
    /// </summary>
    internal TimeSpan AutosaveMaximumDelay { get; set; } = DefaultAutosaveMaximumDelay;

    /// <summary>Gets or sets the time the saving follows; tests move it on themselves.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Gets or sets the time zone for the journal's local time; tests fix it.</summary>
    internal TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    /// <summary>Gives a folder under the application's local data folder, <c>%LOCALAPPDATA%\Chainsaw\</c>.</summary>
    /// <param name="name">The folder's name.</param>
    /// <returns>The full path of the folder.</returns>
    private static string DefaultDirectory(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationFolder, name);
}
