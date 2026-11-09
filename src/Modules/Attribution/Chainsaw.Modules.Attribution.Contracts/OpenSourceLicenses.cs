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

    /// <summary>Gives the licence and attribution of an open source built into Chainsaw.</summary>
    /// <param name="sourceName">The source's name, as a label's origin states it.</param>
    /// <returns>The licence, or <see langword="null"/> for a source Chainsaw does not know.</returns>
    public static string? Of(string? sourceName) => sourceName switch
    {
        RansomwhereName => Ransomwhere,
        _ => null,
    };
}
