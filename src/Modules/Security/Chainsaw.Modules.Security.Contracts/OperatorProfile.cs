namespace Chainsaw.Modules.Security.Contracts;

/// <summary>Who uses Chainsaw on this computer, recorded in the journal and in requests to exchanges.</summary>
/// <param name="Name">The operator's full name.</param>
/// <param name="Title">The operator's title or role.</param>
/// <param name="Organization">The unit or organisation the operator works for.</param>
public sealed record OperatorProfile(string Name, string Title, string Organization);
