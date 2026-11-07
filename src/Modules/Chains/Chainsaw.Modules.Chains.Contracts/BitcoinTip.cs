namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>The height of the newest block the source knew about.</summary>
/// <param name="Height">The height.</param>
public sealed record BitcoinTip(long Height);
