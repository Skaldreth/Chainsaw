using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// What a new case is created with, checked by the panel before it is given to <see cref="CaseWorkflow"/>.
/// </summary>
/// <param name="CaseNumber">The case number, valid by <c>CaseNumberRules</c>.</param>
/// <param name="Title">The title, valid by <c>CaseTitleRules</c>; empty for none.</param>
/// <param name="StartPoints">The recognised start points, in the order they were written.</param>
public sealed record NewCaseRequest(string CaseNumber, string Title, IReadOnlyList<ChainStartPoint> StartPoints);
