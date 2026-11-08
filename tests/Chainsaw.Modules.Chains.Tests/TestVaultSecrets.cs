using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>The vault's named secrets in memory, which the Cases module needs for the generation of its anchors.</summary>
internal sealed class TestVaultSecrets : IVaultSecrets
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public IReadOnlyList<string> List(string prefix) =>
        [.. _values.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    public string? Find(string name) => _values.GetValueOrDefault(name);

    public void Save(string name, string value) => _values[name] = value;

    public bool Remove(string name) => _values.Remove(name);
}
