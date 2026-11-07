namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>The journal codes the Attribution module writes.</summary>
public static class AttributionJournalKinds
{
    /// <summary>A label was set in the case.</summary>
    public const string LabelAdded = "attribution.label-added";

    /// <summary>A label was taken back.</summary>
    public const string LabelRetracted = "attribution.label-retracted";

    /// <summary>Addresses were checked against a sanction list and hits were labelled.</summary>
    public const string SanctionsMatched = "attribution.sanctions-matched";

    /// <summary>Labels from other cases came in from the label library.</summary>
    public const string LabelsFromLibrary = "attribution.labels-from-library";
}
