using Chainsaw.App.Guide;
using Chainsaw.Modules.Cases.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>One of the four lines of a fingerprint: four groups of four digits.</summary>
/// <param name="Groups">The groups.</param>
public sealed record FingerprintLine(IReadOnlyList<string> Groups);

/// <summary>A recovery key this Chainsaw revokes, as <em>Om Chainsaw</em> lists it, struck through.</summary>
/// <param name="ShortText">The first two groups and the last, such as <c>1A2B 3C4D … 9F0E</c>.</param>
/// <param name="FullText">The whole fingerprint in groups, on hovering.</param>
public sealed record RevokedKey(string ShortText, string FullText);

/// <summary>
/// <em>Om Chainsaw</em> (sketch ST2a), the same in the settings and from the lock screen, where it shows before the
/// unlock so that the recovery key can be checked: the version, the organisation's recovery key built in, with its
/// fingerprint in four lines of groups and <em>Kopiera</em>, the keys it revokes struck through, an honest warning
/// when no key is built in, and the licences. It reads what is built into Cases and knows nothing of WPF.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>The line under the title when a key is built in.</summary>
    public const string KeyHintText = "Organisationens. Varje ärende får ett kuvert som administratören kan öppna.";

    /// <summary>The line under the title when no key is built in.</summary>
    public const string NoKeyHintText = "Organisationens. Ingen är inbyggd i det här bygget.";

    /// <summary>The digits in a group.</summary>
    private const int GroupLength = 4;

    /// <summary>The groups on a line.</summary>
    private const int GroupsPerLine = 4;

    /// <summary>Creates the page from what is built in.</summary>
    /// <param name="cases">The cases, for the recovery key built in and the keys revoked.</param>
    /// <param name="build">The build, for the version.</param>
    public AboutViewModel(ICaseStore cases, BuildInfo build)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(build);
        VersionText = $"{build.Version} · {build.ShortCommit}";
        var key = cases.RecoveryKey;
        HasKey = key is not null;
        Fingerprint = key?.DisplayFingerprint ?? string.Empty;
        Lines = key is null ? [] : [.. LinesOf(key.Fingerprint)];
        Revoked = [.. cases.RevokedRecoveryKeys.Select(RevokedOf)];
    }

    /// <summary>Gets the version and the commit.</summary>
    public string VersionText { get; }

    /// <summary>Gets whether a recovery key is built in (<em>Inbyggd</em>), or not (<em>Ingen nyckel</em>).</summary>
    public bool HasKey { get; }

    /// <summary>Gets whether no recovery key is built in, so that the warning is shown.</summary>
    public bool HasNoKey => !HasKey;

    /// <summary>Gets the line under the title.</summary>
    public string KeyHint => HasKey ? KeyHintText : NoKeyHintText;

    /// <summary>Gets the fingerprint as copied: sixteen groups of four, upper case; empty without a key.</summary>
    public string Fingerprint { get; }

    /// <summary>Gets the fingerprint in four lines of four groups.</summary>
    public IReadOnlyList<FingerprintLine> Lines { get; }

    /// <summary>Gets the keys this Chainsaw revokes; their envelopes are taken out of cases that are opened.</summary>
    public IReadOnlyList<RevokedKey> Revoked { get; }

    /// <summary>Gets whether any key is revoked, and a key is built in, so that the list is shown.</summary>
    public bool HasRevoked => HasKey && Revoked.Count > 0;

    /// <summary>Gets the licences, built in from <c>THIRD_PARTY.md</c>, or <see langword="null"/>.</summary>
    public GuideSection? Licenses { get; } = Settings.Licenses.Load();

    /// <summary>Gets whether the fingerprint was just copied (<em>Kopierat</em>).</summary>
    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

    /// <summary>Gets whether the licences are folded out.</summary>
    [ObservableProperty]
    public partial bool IsShowingLicenses { get; private set; }

    /// <summary>Folds the licences out, or in.</summary>
    [RelayCommand]
    public void ToggleLicenses() => IsShowingLicenses = !IsShowingLicenses;

    /// <summary>Says that the fingerprint is copied; the view puts it on the clipboard.</summary>
    public void MarkCopied() => IsCopied = HasKey;

    /// <summary>Leaves the page as it was first shown: the licences in, and <em>Kopiera</em> again.</summary>
    public void Reset()
    {
        IsCopied = false;
        IsShowingLicenses = false;
    }

    /// <summary>Splits a fingerprint into its groups of four, upper case.</summary>
    /// <param name="fingerprint">The fingerprint's hexadecimal digits.</param>
    /// <returns>The groups.</returns>
    public static IReadOnlyList<string> GroupsOf(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return [.. fingerprint.ToUpperInvariant().Chunk(GroupLength).Select(group => new string(group))];
    }

    /// <summary>Lays a fingerprint out in lines of groups.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>The lines.</returns>
    private static IEnumerable<FingerprintLine> LinesOf(string fingerprint) =>
        GroupsOf(fingerprint).Chunk(GroupsPerLine).Select(groups => new FingerprintLine(groups));

    /// <summary>Gives a revoked key's short and full text.</summary>
    /// <param name="fingerprint">Its fingerprint.</param>
    /// <returns>The key as the list shows it.</returns>
    private static RevokedKey RevokedOf(string fingerprint)
    {
        var groups = GroupsOf(fingerprint);
        var shortText = groups.Count > 2 ? $"{groups[0]} {groups[1]} … {groups[^1]}" : string.Join(' ', groups);
        return new RevokedKey(shortText, string.Join(' ', groups));
    }
}
