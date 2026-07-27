using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// The external-source block write route has no other automated coverage, and this is the only
/// signal it produces that a write landed somewhere other than the block it was addressed to.
/// </summary>
public class BlockSourceWriteWarningsTests
{
    private const string BlockPath = "PLC_1/Blocks/Data/Simulation_DB";

    private const string ResidualNodeWarning =
        "A temporary external source node could not be removed and is still in the project. "
        + "Delete it in TIA Portal under the PLC's external source files.";

    [Fact]
    public void Returns_no_warning_when_exactly_one_object_was_generated_and_the_node_was_removed()
    {
        // Arrange
        var address = BlockAddress.Parse(BlockPath);

        // Act
        var warnings = BlockSourceWriteWarnings.Build(
            address, projectNodeRemoved: true, generatedObjectCount: 1);

        // Assert
        Assert.Empty(warnings);
    }

    [Fact]
    public void Warns_when_no_object_was_generated()
    {
        // Arrange
        var address = BlockAddress.Parse(BlockPath);

        // Act
        var warnings = BlockSourceWriteWarnings.Build(
            address, projectNodeRemoved: true, generatedObjectCount: 0);

        // Assert
        var warning = Assert.Single(warnings);
        Assert.Equal(
            "TIA Portal generated 0 objects from the submitted source; expected 1. Inspect "
            + "'PLC_1/Blocks/Data/Simulation_DB' and its PLC for objects this write was not "
            + "addressed to.",
            warning);
    }

    [Fact]
    public void Warns_when_more_than_one_object_was_generated()
    {
        // Arrange
        var address = BlockAddress.Parse(BlockPath);

        // Act
        var warnings = BlockSourceWriteWarnings.Build(
            address, projectNodeRemoved: true, generatedObjectCount: 2);

        // Assert
        var warning = Assert.Single(warnings);
        Assert.Contains("generated 2 objects", warning);
        Assert.Contains("expected 1", warning);
        Assert.Contains("PLC_1/Blocks/Data/Simulation_DB", warning);
    }

    [Fact]
    public void Warns_when_the_temporary_external_source_node_survived()
    {
        // Arrange
        var address = BlockAddress.Parse(BlockPath);

        // Act
        var warnings = BlockSourceWriteWarnings.Build(
            address, projectNodeRemoved: false, generatedObjectCount: 1);

        // Assert
        var warning = Assert.Single(warnings);
        Assert.Equal(ResidualNodeWarning, warning);
    }

    [Fact]
    public void Reports_both_a_surviving_node_and_an_unexpected_object_count_together()
    {
        // Arrange
        var address = BlockAddress.Parse(BlockPath);

        // Act
        var warnings = BlockSourceWriteWarnings.Build(
            address, projectNodeRemoved: false, generatedObjectCount: 3);

        // Assert
        Assert.Equal(2, warnings.Count);
        Assert.Equal(ResidualNodeWarning, warnings[0]);
        Assert.Contains("generated 3 objects", warnings[1]);
    }

    [Fact]
    public void Names_a_software_unit_scoped_block_by_its_full_path()
    {
        // A unit-scoped write landing on the top-level PLC is the failure this warning exists to
        // catch, so the path it prints has to identify the unit.
        // Arrange
        var address = BlockAddress.Parse("PLC_1/Units/Motion/Blocks/Simulation_DB");

        // Act
        var warnings = BlockSourceWriteWarnings.Build(
            address, projectNodeRemoved: true, generatedObjectCount: 2);

        // Assert
        var warning = Assert.Single(warnings);
        Assert.Contains("'PLC_1/Units/Motion/Blocks/Simulation_DB'", warning);
    }
}
