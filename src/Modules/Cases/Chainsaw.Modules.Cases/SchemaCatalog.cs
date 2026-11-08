using System.Text.RegularExpressions;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Every module's schema contributor and sealed chain, checked once when the store is built: unique names, valid
/// identifiers and increasing step versions. The Cases module's own contributor always comes first.
/// </summary>
internal sealed partial class SchemaCatalog
{
    /// <summary>The Cases module's own tables that no other module may write.</summary>
    public static readonly IReadOnlyList<string> CaseTables =
        ["case_info", "chain_head", "schema_version", "sealed_chain"];

    /// <summary>
    /// Builds the catalog from copies of the contributors, taken and checked once: every later use reads the copies,
    /// so a contributor cannot answer differently after it has been checked.
    /// </summary>
    /// <param name="contributors">Every registered contributor, the Cases module's own included.</param>
    /// <exception cref="ArgumentException">A contributor or chain is not valid.</exception>
    public SchemaCatalog(IEnumerable<ICaseSchemaContributor> contributors)
    {
        var snapshots = contributors.Select(Snapshot).ToList();
        var ordered = snapshots
            .OrderBy(contributor => contributor.IsCases ? 0 : 1)
            .ThenBy(contributor => contributor.Module, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count(contributor => contributor.IsCases) != 1)
        {
            throw new ArgumentException(
                "The Cases module's own schema must be there exactly once.", nameof(contributors));
        }

        var modules = new HashSet<string>(StringComparer.Ordinal);
        var chains = new Dictionary<string, SealedChain>(StringComparer.Ordinal);
        var tables = new HashSet<string>(CaseTables, StringComparer.OrdinalIgnoreCase);
        foreach (var contributor in ordered)
        {
            Validate(contributor, modules);
            foreach (var chain in contributor.SealedChains)
            {
                Validate(chain);
                if (!chains.TryAdd(chain.Name, chain) || !tables.Add(chain.Table))
                {
                    throw new ArgumentException($"The sealed chain {chain.Name} is not unique.", nameof(contributors));
                }
            }
        }

        Contributors = ordered;
        Chains = [.. chains.Values.OrderBy(chain => chain.Name, StringComparer.Ordinal)];
        ModuleChains = [.. ordered.Where(contributor => !contributor.IsCases).SelectMany(item => item.SealedChains)];
    }

    /// <summary>Gets the copies of the contributors in the order their steps run, the Cases module's first.</summary>
    public IReadOnlyList<ContributorSnapshot> Contributors { get; }

    /// <summary>Gets every sealed chain, ordered by name.</summary>
    public IReadOnlyList<SealedChain> Chains { get; }

    /// <summary>Gets the sealed chains of other modules, the only ones a module may seal records in.</summary>
    public IReadOnlyList<SealedChain> ModuleChains { get; }

    /// <summary>
    /// Finds a chain that another module may seal records in: never the Cases module's own (the journal is written
    /// only through the journal, with the time and the operator added by the Cases module).
    /// </summary>
    /// <param name="name">The chain's name.</param>
    /// <returns>The chain.</returns>
    /// <exception cref="ArgumentException">No other module's chain has that name.</exception>
    public SealedChain ModuleChain(string name) =>
        ModuleChains.FirstOrDefault(chain => string.Equals(chain.Name, name, StringComparison.Ordinal))
        ?? throw new ArgumentException($"There is no sealed chain named {name} that a module may write.", nameof(name));

    /// <summary>Creates the guard for these tables.</summary>
    /// <returns>A new guard, one per connection.</returns>
    public SqlGuard CreateGuard() => new(CaseTables, Chains.Select(chain => chain.Table));

    /// <summary>A module name or identifier: lower-case ASCII letters, digits and underscores.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    internal static partial Regex Identifier();

    /// <summary>A chain name: an identifier that may also contain dots.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[a-z][a-z0-9_.]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChainName();

    /// <summary>Copies a contributor, reading each of its members once.</summary>
    /// <param name="contributor">The contributor.</param>
    /// <returns>The copy.</returns>
    private static ContributorSnapshot Snapshot(ICaseSchemaContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        var steps = (contributor.Steps ?? []).Select(step => step is null ? null! : step with { }).ToArray();
        var chains = (contributor.SealedChains ?? [])
            .Select(chain => new SealedChain(chain.Name, chain.Table, [.. chain.ContentColumns ?? []]))
            .ToArray();
        return new ContributorSnapshot(contributor.Module, contributor is CasesSchema, steps, chains);
    }

    /// <summary>Checks a contributor's name and steps.</summary>
    /// <param name="contributor">The contributor's copy.</param>
    /// <param name="modules">The module names seen so far.</param>
    private static void Validate(ContributorSnapshot contributor, HashSet<string> modules)
    {
        var nameHolds = contributor.Module is not null && Identifier().IsMatch(contributor.Module)
            && (contributor.IsCases == (contributor.Module == CasesSchema.ModuleName));
        if (!nameHolds || !modules.Add(contributor.Module!))
        {
            throw new ArgumentException($"The module name {contributor.Module} is not valid or not unique.");
        }

        var previous = 0;
        foreach (var step in contributor.Steps)
        {
            if (step is null || step.Version <= previous || string.IsNullOrWhiteSpace(step.Sql))
            {
                throw new ArgumentException($"The steps of {contributor.Module} must have increasing versions from 1.");
            }

            previous = step.Version;
        }
    }

    /// <summary>Checks a sealed chain's names.</summary>
    /// <param name="chain">The chain.</param>
    /// <exception cref="ArgumentException">A name is not valid.</exception>
    public static void Validate(SealedChain chain)
    {
        var columns = chain.ContentColumns;
        if (chain.Name is null || chain.Table is null
            || !ChainName().IsMatch(chain.Name) || !Identifier().IsMatch(chain.Table)
            || chain.Table.StartsWith("sqlite_", StringComparison.Ordinal)
            || columns.Count == 0
            || columns.Any(column => column is null
                || !Identifier().IsMatch(column) || column is HashChain.SequenceColumn or HashChain.HashColumn)
            || columns.Distinct(StringComparer.Ordinal).Count() != columns.Count)
        {
            throw new ArgumentException($"The sealed chain {chain.Name} is not valid.");
        }
    }
}
