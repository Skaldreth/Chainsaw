using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Commands;

/// <summary>
/// What text without a slash becomes in the command bar: an address or a tx is a start point in the open case, or,
/// with no case open, fills in Nytt ärende. Search for labels and the like is not built yet, and the answer says so.
/// </summary>
public sealed class StartPointInput
{
    /// <summary>Adds the start point to the open case.</summary>
    private readonly CaseWorkflow _workflow;

    /// <summary>The window's open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The panel that a start point fills in when no case is open.</summary>
    private readonly NewCaseViewModel _newCase;

    /// <summary>Creates the input.</summary>
    /// <param name="workflow">Adds the start point to the open case.</param>
    /// <param name="session">The window's open case.</param>
    /// <param name="newCase">The panel for a new case.</param>
    public StartPointInput(CaseWorkflow workflow, CaseSession session, NewCaseViewModel newCase)
    {
        _workflow = workflow;
        _session = session;
        _newCase = newCase;
    }

    /// <summary>Recognises the text and acts on it.</summary>
    /// <param name="text">The trimmed text, not empty.</param>
    /// <returns>The answer.</returns>
    public async Task<CommandReply> RunAsync(string text)
    {
        var startPoint = ChainStartPoint.Recognise(text);
        if (!startPoint.IsRecognised)
        {
            return CommandReply.Failed(
                "Sökning är inte byggd än. Skriv en adress eller en tx, eller /help för kommandona.");
        }

        if (!startPoint.IsSupported)
        {
            return CommandReply.Failed(CaseWorkflow.NotBuiltText(startPoint));
        }

        if (_session.Current is null)
        {
            _newCase.Show(startPoint.Value);
            return CommandReply.Done("Inget ärende är öppet, så Nytt ärende fylldes i med startpunkten.");
        }

        return await _workflow.AddStartPointAsync(startPoint);
    }
}
