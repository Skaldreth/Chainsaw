namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>What an open case says about itself; written once when the case is created and never changed.</summary>
/// <param name="Id">A random identifier that also binds the file's key envelopes to this case.</param>
/// <param name="CaseNumber">The case number the case was created with.</param>
/// <param name="CreatedUtc">When the case was created, in UTC.</param>
public sealed record CaseInfo(Guid Id, string CaseNumber, DateTimeOffset CreatedUtc);
