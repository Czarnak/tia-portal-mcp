using TiaMcpServer.Tools;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciGuardDefinitionsTests
{
    [Fact]
    public void FiveGuardsHaveFixedSeveritiesAndProductionCatalogHasNoDuplicates()
    {
        var catalog = WriteGuardRegistration.ProductionCatalog();
        Assert.Equal(5, VciGuardDefinitions.Definitions.Count);
        Assert.DoesNotContain(VciGuardDefinitions.Definitions, g => g.Id == "vci_file_key_collision");
        foreach (var id in new[] { "vci_name_not_representable", "vci_workspace_exists", "vci_state_unverifiable" })
            Assert.Equal("block", catalog.Get(id).Severity);
        foreach (var id in new[] { "vci_files_remain", "vci_import_unvalidated" })
            Assert.Equal("info", catalog.Get(id).Severity);
        Assert.Equal(5, VciGuardDefinitions.Definitions.Select(g => g.Id).Distinct().Count());
    }
}
