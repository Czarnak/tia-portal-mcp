using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcGuardDefinitionsTests
{
    [Fact]
    public void OnlyBlockAndInfoSeverities()
    {
        var byId = PlcGuardDefinitions.Definitions.ToDictionary(guard => guard.Id, guard => guard.Severity);

        Assert.Equal(new Dictionary<string, string>
        {
            [PlcGuardDefinitions.StateUnverifiable] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.NameCollision] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.BlockExists] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.DefaultTagTable] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.AttributeUnreadable] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.ObSingletonExists] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.DeletesBlock] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesGroupContents] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesTableContents] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.AddressOverlap] = WriteGuardSeverities.Info,
        }, byId);
        Assert.Equal("plc_state_unverifiable", PlcGuardDefinitions.StateUnverifiable);
        Assert.Equal("plc_address_overlap", PlcGuardDefinitions.AddressOverlap);
        Assert.Equal("plc_ob_singleton_exists", PlcGuardDefinitions.ObSingletonExists);
        _ = new WriteGuardCatalog(PlcGuardDefinitions.Definitions);
    }

    [Fact]
    public void AcknowledgeRegistrationIsRejected()
    {
        var definitions = PlcGuardDefinitions.Definitions
            .Append(new WriteGuardDefinition("plc_needs_ack", WriteGuardSeverities.Acknowledge, "x"));

        Assert.Throws<ArgumentException>(() => PlcGuardDefinitions.Validate(definitions));
    }
}
