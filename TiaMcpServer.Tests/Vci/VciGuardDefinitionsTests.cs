using TiaMcpServer.Tools;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciGuardDefinitionsTests
{
    [Fact]
    public void SixGuardsHaveFixedSeveritiesAndProductionCatalogHasNoDuplicates()
    {
        var catalog = WriteGuardRegistration.ProductionCatalog();
        Assert.Equal(6, VciGuardDefinitions.Definitions.Count);
        foreach (var id in new[] { "vci_file_key_collision", "vci_name_not_representable", "vci_workspace_exists", "vci_state_unverifiable" })
            Assert.Equal("block", catalog.Get(id).Severity);
        foreach (var id in new[] { "vci_files_remain", "vci_import_unvalidated" })
            Assert.Equal("info", catalog.Get(id).Severity);
        Assert.Equal(6, VciGuardDefinitions.Definitions.Select(g => g.Id).Distinct().Count());
    }
}
