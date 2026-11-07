using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Attribution.Tests;

public sealed class AttributionModuleTests
{
    [Fact]
    public void AddAttributionModule_returns_the_same_container()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddAttributionModule());
    }

    [Fact]
    public void AddAttributionModule_refuses_a_missing_container()
    {
        Assert.Throws<ArgumentNullException>(() => AttributionModule.AddAttributionModule(null!));
    }

    [Fact]
    public void The_module_registers_its_table_and_services()
    {
        using var harness = new Harness();

        Assert.NotNull(harness.Labels);
        Assert.NotNull(harness.Sanctions);
        Assert.NotNull(harness.Library);
        Assert.NotNull(harness.Classifier);
        Assert.True(harness.Verify().IsIntact);
        Assert.Contains(harness.Verify().Chains, chain => chain.Chain == "attribution.labels");
    }

    [Fact]
    public void The_default_data_folder_is_chainsaw_under_local_application_data()
    {
        var options = new AttributionOptions();

        Assert.EndsWith("Chainsaw", options.Directory, StringComparison.Ordinal);
        Assert.EndsWith("Sanktionslistor", options.SanctionDirectory, StringComparison.Ordinal);
        Assert.EndsWith("Etiketter", options.LibraryDirectory, StringComparison.Ordinal);
    }
}
