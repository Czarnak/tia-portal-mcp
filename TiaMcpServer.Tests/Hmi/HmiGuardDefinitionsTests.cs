using TiaMcpServer.Hmi;
using TiaMcpServer.Tools;
using Xunit;
namespace TiaMcpServer.Tests.Hmi;
public class HmiGuardDefinitionsTests
{
    [Fact] public void ProductionCatalog_RegistersExactlyFourHmiGuardsWithFixedSeverities()
    {
        var catalog = WriteGuardRegistration.ProductionCatalog();
        Assert.Equal(4, HmiGuardDefinitions.Definitions.Count);
        Assert.Equal("block", catalog.Get("hmi_name_collision").Severity);
        Assert.Equal("block", catalog.Get("hmi_state_unverifiable").Severity);
        Assert.Equal("info", catalog.Get("hmi_deletes_table_contents").Severity);
        Assert.Equal("info", catalog.Get("hmi_deletes_referenced_tag").Severity);
    }
}
