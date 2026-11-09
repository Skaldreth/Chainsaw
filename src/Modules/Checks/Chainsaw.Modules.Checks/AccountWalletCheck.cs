using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Suggests the accounts the account heuristics propose may have the same owner but leave to the user (sketch KE1):
/// a sweep into an account without a service's label, or the same payer of the first gas. It reads the proposals the
/// graph carries, as <c>cross-chain</c> reads the graph's account payments; an approval merges the accounts as the
/// user's judgement (<c>user v1</c> in Clustering), see <see cref="CheckService"/>.
/// </summary>
internal sealed class AccountWalletCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = CheckAnalysisNames.AccountWallets;

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Plånböcker på Ethereum";

    /// <inheritdoc/>
    /// <remarks>
    /// Its input is the graph's account proposals, not the transactions, so it runs with the checks of the UTXO
    /// chains; the proposals carry their own chain.
    /// </remarks>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return
        [
            .. (input.Graph?.AccountProposals ?? []).Select(proposal => new CheckFinding(
                proposal.Key,
                CheckKind.Suggestion,
                AreaSort.Wallet,
                "Samma ägare troligt",
                proposal.Heuristic,
                $"{proposal.Reason} Godkänner du förslaget slås adresserna ihop som din bedömning (user v1), som kan "
                    + "brytas upp och ångras. Ett förslag är en bedömning, inte ett faktum.",
                [],
                [],
                [],
                [])
            {
                Accounts = proposal.Accounts,
            }),
        ];
    }
}
