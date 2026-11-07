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

    /// <summary>Builds the catalog.</summary>
    /// <param name="contributors">Every registered contributor, the Cases module's own included.</param>
    /// <exception cref="ArgumentException">A contributor or chain is not valid.</exception>
    public SchemaCatalog(IEnumerable<ICaseSchemaContributor> contributors)
    {
        var ordered = contributors
            .OrderBy(contributor => contributor.Module == CasesSchema.ModuleName ? 0 : 1)
            .ThenBy(contributor => contributor.Module, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0 || ordered[0].Module != CasesSchema.ModuleName)
        {
            throw new ArgumentException("The Cases module's own schema is missing.", nameof(contributors));
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
        Chains = chains.Values.OrderBy(chain => chain.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Gets the contributors in the order their steps run.</summary>
    public IReadOnlyList<ICaseSchemaContributor> Contributors { get; }

    /// <summary>Gets every sealed chain, ordered by name.</summary>
    public IReadOnlyList<SealedChain> Chains { get; }

    /// <summary>Finds a chain by name.</summary>
    /// <param name="name">The chain's name.</param>
    /// <returns>The chain.</returns>
    /// <exception cref="ArgumentException">No chain has that name.</exception>
    public SealedChain Chain(string name) =>
        Chains.FirstOrDefault(chain => string.Equals(chain.Name, name, StringComparison.Ordinal))
        ?? throw new ArgumentException($"There is no sealed chain named {name}.", nameof(name));

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

    /// <summary>Checks a contributor's name and steps.</summary>
    /// <param name="contributor">The contributor.</param>
    /// <param name="modules">The module names seen so far.</param>
    private static void Validate(ICaseSchemaContributor contributor, HashSet<string> modules)
    {
        if (!Identifier().IsMatch(contributor.Module) || !modules.Add(contributor.Module))
        {
            throw new ArgumentException($"The module name {contributor.Module} is not valid or not unique.");
        }

        var previous = 0;
        foreach (var step in contributor.Steps)
        {
            if (step.Version <= previous || string.IsNullOrWhiteSpace(step.Sql))
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
        if (!ChainName().IsMatch(chain.Name) || !Identifier().IsMatch(chain.Table)
            || chain.Table.StartsWith("sqlite_", StringComparison.Ordinal)
            || columns.Count == 0
            || columns.Any(column =>
                !Identifier().IsMatch(column) || column is HashChain.SequenceColumn or HashChain.HashColumn)
            || columns.Distinct(StringComparer.Ordinal).Count() != columns.Count)
        {
            throw new ArgumentException($"The sealed chain {chain.Name} is not valid.");
        }
    }
}
