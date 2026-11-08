namespace Chainsaw.App.Navigation;

/// <summary>The views of the shell, in the order of the work: follow the money, find the cash-out, ask, watch.</summary>
public enum AppView
{
    /// <summary>A new case, starting point or label.</summary>
    New,

    /// <summary>The graph, where most of the work is done (sketch GR4).</summary>
    Graph,

    /// <summary>Every transaction of the case in time order, a lane per actor (sketch TL3a).</summary>
    Timeline,

    /// <summary>The victims' money as bands, hop by hop (Sankey, sketch FL1a).</summary>
    Flow,

    /// <summary>Which services the money has reached, the cash-outs first.</summary>
    Exposure,

    /// <summary>Requests for information to exchanges.</summary>
    Requests,

    /// <summary>Addresses and clusters that are watched.</summary>
    Monitoring,

    /// <summary>The case's labels and those of the label library.</summary>
    Labels,

    /// <summary>Rapport: the report built from what is marked in the graph (sketch RP1b).</summary>
    Report,

    /// <summary>The explanations: concepts, heuristics and the visual language.</summary>
    Guide,

    /// <summary>Sources, exits and the vault.</summary>
    Settings,
}
