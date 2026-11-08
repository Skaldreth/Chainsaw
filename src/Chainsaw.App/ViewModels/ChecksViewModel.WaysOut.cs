using Chainsaw.App.Graph;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>The approved ways out, read from the stored checks whatever the panel shows.</summary>
public sealed partial class ChecksViewModel
{
    /// <summary>
    /// Gets every candidate the user approved among a suggestion's (sketch KF2b), from the actor the suggestion is about
    /// to the candidate's actor, both in the graph shown; each once. The view Exponering draws them beyond the actor
    /// (TODO, <em>Exponering, mixerns troliga utgångar</em>), as judgements, never facts (principle 2).
    /// </summary>
    public IReadOnlyList<ApprovedWayOut> ApprovedWaysOut
    {
        get
        {
            if (_scene is not { } scene)
            {
                return [];
            }

            var graph = scene.Snapshot.Graph;
            var ways = new List<ApprovedWayOut>();
            foreach (var check in _checks.Where(check => check.Finding.Addresses.Count > 0))
            {
                if (graph.NodeOf(check.Finding.Addresses[0]) is not { } from)
                {
                    continue;
                }

                foreach (var candidate in check.Finding.Candidates)
                {
                    if (check.DecisionFor(candidate.Key)?.Verdict == CheckVerdict.Approved
                        && graph.NodeOf(candidate.Address) is { } to
                        && to.Key != from.Key)
                    {
                        ways.Add(new ApprovedWayOut(from.Key, to.Key));
                    }
                }
            }

            return [.. ways.Distinct()];
        }
    }
}
