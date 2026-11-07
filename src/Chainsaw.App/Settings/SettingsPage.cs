namespace Chainsaw.App.Settings;

/// <summary>The pages of the settings dialog (sketch ST1a), in the order of its menu.</summary>
public enum SettingsPage
{
    /// <summary>How Chainsaw reaches the sources: Windows or VPN, and the exits.</summary>
    Network,

    /// <summary>Where Chainsaw asks the chains, per chain (sketch KL1a).</summary>
    Sources,

    /// <summary>When Chainsaw locks itself.</summary>
    Lock,

    /// <summary>The lock screen's field. Not built yet.</summary>
    Look,

    /// <summary>The <c>chainsaw:</c> links. Not built yet.</summary>
    Links,

    /// <summary>The currency and the rate's resolution.</summary>
    Pricing,

    /// <summary>The operator profile and the master key.</summary>
    Profile,

    /// <summary>The version, the recovery key's fingerprint and the licences.</summary>
    About,
}
