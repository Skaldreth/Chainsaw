namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The licences of the open sources built into Chainsaw, with the attribution each asks for, stated once so that a
/// label's <em>Varför?</em>, the view Etiketter and the report all give it (CC BY requires the attribution wherever the
/// data is used).
/// </summary>
public static class OpenSourceLicenses
{
    /// <summary>The name Ransomwhere's labels carry as their source.</summary>
    public const string RansomwhereName = "Ransomwhere";

    /// <summary>Ransomwhere's licence and attribution (checked 2026-11-08 on Zenodo, record 13999026).</summary>
    public const string Ransomwhere = "CC BY 4.0, Jack Cable, Ransomwhere (ransomwhe.re)";

    /// <summary>
    /// The start of the origin id of every label from a built-in open source (followed by the file's hash), which no
    /// label the user sets has, so that a licence is never given to a source the user only named the same.
    /// </summary>
    public const string OriginPrefix = "open/";

    /// <summary>
    /// Gives the licence and attribution of a label's origin when it comes from an open source built into Chainsaw:
    /// its origin id is the built-in source's, never only its name (a source the user names in the form could have the
    /// same name).
    /// </summary>
    /// <param name="origin">The label's origin.</param>
    /// <returns>The licence, or <see langword="null"/> for any other origin.</returns>
    public static string? Of(LabelOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (origin.OriginalSource != LabelSource.OpenSource
            || !origin.OriginId.StartsWith(OriginPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return origin.SourceName switch
        {
            RansomwhereName => Ransomwhere,
            _ => null,
        };
    }
}
