namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// What an analysis's result asks of the user (CLAUDE.md, <em>Förvirring analyseras</em>): a check has done its work by
/// itself, a suggestion waits for the user to approve or dismiss it.
/// </summary>
public enum CheckKind
{
    /// <summary>
    /// A check that has done its work by itself (<em>Gjort av sig självt</em>); nothing waits for a decision.
    /// </summary>
    Check,

    /// <summary>A suggestion that the user approves or dismisses (<em>Att avgöra</em>).</summary>
    Suggestion,
}

/// <summary>
/// What sort of area a result is drawn as behind the actors in the graph (sketch KF1b), and what the menu
/// <em>Områden i grafen</em> chooses between.
/// </summary>
public enum AreaSort
{
    /// <summary>Obfuscation: mixers, CoinJoin, peel chains and the like (red or sand).</summary>
    Obfuscation,

    /// <summary>Wallets: addresses the clustering proposes have the same owner (grey).</summary>
    Wallet,

    /// <summary>Services: actors with the same service's label (blue).</summary>
    Service,

    /// <summary>Another chain: money that continues on another chain (purple).</summary>
    OtherChain,

    /// <summary>Time: actors the money passed within a short time (grey, dotted).</summary>
    Time,

    /// <summary>The user's own areas (green), sealed in the case with who, when and why.</summary>
    Own,
}

/// <summary>A user's decision about a suggestion.</summary>
public enum CheckVerdict
{
    /// <summary>The user approved the suggestion; it is still a judgement, the user's (principle 2).</summary>
    Approved,

    /// <summary>The user dismissed the suggestion; it does not come back in the same version of the analysis.</summary>
    Dismissed,

    /// <summary>
    /// The user took back (<em>Ångra</em>) the decision in force about the same candidate or suggestion; it waits
    /// again. The earlier decision stays sealed (principle 1).
    /// </summary>
    Withdrawn,
}
