using Chainsaw.App.Graph;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>The approved candidates, read from the stored checks whatever the panel shows.</summary>
public sealed partial class ChecksViewModel
{
    /// <summary>
    /// Gets every candidate the user approved among a suggestion's (sketch KF2b), as a mixer's likely payout, with the
    /// analysis that suggested it: judgements, never facts (principle 2). The report writes them among its conclusions
    /// (BACKLOG, <em>Rapport</em>).
    /// </summary>
    public IReadOnlyList<ApprovedPayout> ApprovedPayouts
    {
        get
        {
            if (_scene is not { } scene)
            {
                return [];
            }

            var graph = scene.Snapshot.Graph;
            var approved = new List<ApprovedPayout>();
            foreach (var check in _checks.Where(check => check.Finding.Addresses.Count > 0))
            {
                if (graph.NodeOf(check.Finding.Addresses[0]) is not { } from)
                {
                    continue;
                }

                foreach (var candidate in check.Finding.Candidates)
                {
                    if (check.DecisionFor(candidate.Key)?.Verdict == CheckVerdict.Approved)
                    {
                        approved.Add(new ApprovedPayout(
                            from.Key,
                            graph.NodeOf(candidate.Address)?.Key,
                            candidate.Address.Value,
                            candidate.Txid.Value,
                            check.Analysis.ToString()));
                    }
                }
            }

            return approved;
        }
    }

    /// <summary>
    /// Gets the approved candidates drawn in the graph as ways out of the actor the suggestion is about, each once. The
    /// view Exponering draws them beyond the actor (TODO, <em>Exponering, mixerns troliga utgångar</em>).
    /// </summary>
    public IReadOnlyList<ApprovedWayOut> ApprovedWaysOut =>
    [
        .. ApprovedPayouts
            .Where(candidate => candidate.To is not null && candidate.To != candidate.From)
            .Select(candidate => new ApprovedWayOut(candidate.From, candidate.To!))
            .Distinct(),
    ];
}
