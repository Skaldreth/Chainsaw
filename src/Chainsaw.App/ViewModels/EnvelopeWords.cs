using System.Globalization;
using System.Text;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The Swedish words for a case's envelopes and its files (sketch ÄR1b), in one place for the three dialogs.
/// </summary>
public static class EnvelopeWords
{
    /// <summary>What the dialogs say under the envelopes: every change is in the journal.</summary>
    public const string JournalNote = "Varje kuvert som läggs till, byts eller tas bort står i journalen.";

    /// <summary>What the tab Kuvert says first.</summary>
    public const string EnvelopesLead =
        "Filen öppnas av den som har ett av kuverten. Varje kuvert lindar in ärendets egen filnyckel.";

    /// <summary>The months, short, as the dialogs tell a time.</summary>
    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "maj", "jun", "jul", "aug", "sep", "okt", "nov", "dec"];

    /// <summary>The number of hexadecimal characters in a group of a fingerprint.</summary>
    private const int FingerprintGroup = 4;

    /// <summary>The bytes in a megabyte, for a file's size.</summary>
    private const double Megabyte = 1024 * 1024;

    /// <summary>The bytes in a kilobyte, for a file's size.</summary>
    private const double Kilobyte = 1024;

    /// <summary>The Swedish culture, for decimal commas.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Gives an envelope's short label, as on the small cards in the dialog for a colleague's file.</summary>
    /// <param name="kind">The envelope's kind.</param>
    /// <returns>The label.</returns>
    public static string Label(CaseEnvelopeKind kind) => kind switch
    {
        CaseEnvelopeKind.Yours => "Du",
        CaseEnvelopeKind.OtherVault => "Ett annat valv",
        CaseEnvelopeKind.SharingPassword => "Ärendelösenord",
        CaseEnvelopeKind.Recovery => "Organisationen",
        _ => "Okänt kuvert",
    };

    /// <summary>Gives what an envelope is, said on hovering it.</summary>
    /// <param name="kind">The envelope's kind.</param>
    /// <returns>The text.</returns>
    public static string Tip(CaseEnvelopeKind kind) => kind switch
    {
        CaseEnvelopeKind.Yours => "Ditt kuvert: filen öppnas med din huvudnyckel",
        CaseEnvelopeKind.OtherVault => "En kollegas huvudnyckel. Inte din.",
        CaseEnvelopeKind.SharingPassword => "Det här kuvertet öppnar du med lösenordet",
        CaseEnvelopeKind.Recovery => "Organisationens återställningsnyckel",
        _ => "Ett kuvert i ett format den här versionen inte känner. Det ligger kvar orört.",
    };

    /// <summary>Writes a fingerprint in groups of four, in capitals, as ST2a does: <c>3F2A 91C0 …</c>.</summary>
    /// <param name="fingerprint">The fingerprint in hexadecimal.</param>
    /// <returns>The grouped text.</returns>
    public static string GroupFingerprint(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        var grouped = new StringBuilder(fingerprint.Length + (fingerprint.Length / FingerprintGroup));
        for (var index = 0; index < fingerprint.Length; index += FingerprintGroup)
        {
            if (index > 0)
            {
                grouped.Append(' ');
            }

            grouped.Append(fingerprint.AsSpan(index, Math.Min(FingerprintGroup, fingerprint.Length - index)));
        }

        return grouped.ToString().ToUpperInvariant();
    }

    /// <summary>Tells a file's size: <c>4,2 MB</c>, <c>812 kB</c> or <c>96 byte</c>.</summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>The text.</returns>
    public static string Size(long bytes)
    {
        if (bytes >= Megabyte)
        {
            return (bytes / Megabyte).ToString("0.0", Swedish) + " MB";
        }

        return bytes >= Kilobyte
            ? (bytes / Kilobyte).ToString("0", Swedish) + " kB"
            : bytes.ToString(Swedish) + " byte";
    }

    /// <summary>Tells a time short in a time zone, <c>8 nov 09:12</c>, with the year when it is another.</summary>
    /// <param name="utc">The time.</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The text.</returns>
    public static string When(DateTimeOffset utc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var day = string.Create(CultureInfo.InvariantCulture, $"{local.Day} {Months[local.Month - 1]}");
        return local.Year == TimeZoneInfo.ConvertTime(nowUtc, zone).Year
            ? $"{day} {time}"
            : string.Create(CultureInfo.InvariantCulture, $"{day} {local.Year} {time}");
    }
}
