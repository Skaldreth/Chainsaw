using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A start point as shown under the field of the first case in the setup (FS1a): the chain's tag, the value
/// shortened, and what it is. What is not built yet is shown toned (<see cref="IsSupported"/> false), and what is
/// not recognised is marked so that the case is not created with it.
/// </summary>
/// <param name="StartPoint">The recognised start point.</param>
public sealed record StartPointLine(ChainStartPoint StartPoint)
{
    /// <summary>Gets the chain's tag, for example <c>BTC</c>.</summary>
    public string ChainLabel => CaseTexts.ChainLabel(StartPoint.Chain);

    /// <summary>Gets the value, shortened.</summary>
    public string Short => CaseTexts.Shorten(StartPoint.Value);

    /// <summary>Gets what it is, for example <c>Bitcoin-adress</c> or <c>TRON-adress · inte byggt än</c>.</summary>
    public string KindText => CaseTexts.Describe(StartPoint);

    /// <summary>Gets whether the start point can be traced now (shown in full, not toned).</summary>
    public bool IsSupported => StartPoint.IsSupported;

    /// <summary>Gets whether the line was not recognised as an address or a tx on any chain.</summary>
    public bool IsUnknown => !StartPoint.IsRecognised;
}
