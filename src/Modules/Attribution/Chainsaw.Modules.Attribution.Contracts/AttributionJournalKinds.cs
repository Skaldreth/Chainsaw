namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>The journal codes the Attribution module writes.</summary>
public static class AttributionJournalKinds
{
    /// <summary>A label was set in the case.</summary>
    public const string LabelAdded = "attribution.label-added";

    /// <summary>A label was taken back.</summary>
    public const string LabelRetracted = "attribution.label-retracted";

    /// <summary>A label was marked <em>Sprid inte</em>, or allowed to spread again.</summary>
    public const string LabelSpreadChanged = "attribution.label-spread-changed";

    /// <summary>Addresses were checked against a sanction list and hits were labelled.</summary>
    public const string SanctionsMatched = "attribution.sanctions-matched";

    /// <summary>Addresses were found on Chainsaw's list of known contracts and labelled.</summary>
    public const string KnownContractsMatched = "attribution.known-contracts-matched";

    /// <summary>Labels from other cases came in from the label library.</summary>
    public const string LabelsFromLibrary = "attribution.labels-from-library";

    /// <summary>A supplier of attribution was asked about an address, and its response sealed.</summary>
    public const string ProviderAsked = "attribution.provider-asked";
}
