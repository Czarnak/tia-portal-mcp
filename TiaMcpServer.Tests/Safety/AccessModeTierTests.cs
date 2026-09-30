using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

public class AccessModeTierTests
{
    private const McpAccessMode Full = McpAccessMode.Full;

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, "read-only")]
    [InlineData(McpAccessMode.ReadWrite, "read-write")]
    [InlineData(McpAccessMode.Full, "full")]
    public void ModeNames_RoundTripAllPresets(McpAccessMode mode, string name)
    {
        Assert.Equal(name, McpAccessModeNames.ToName(mode));
        Assert.True(McpAccessModeNames.TryParse(name, out var parsed));
        Assert.Equal(mode, parsed);
        Assert.True(McpAccessModeNames.TryParse(" " + name.ToUpperInvariant() + " ", out parsed));
        Assert.Equal(mode, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("2")]
    public void ModeNames_RejectUnknownValues(string? value)
    {
        Assert.False(McpAccessModeNames.TryParse(value, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => McpAccessModeNames.ToName((McpAccessMode)999));
    }

    [Fact]
    public void Presets_EnforceEveryKnownCapability()
    {
        foreach (var operation in OperationPolicyCatalog.AllOperationNames)
        {
            var capability = OperationPolicyCatalog.GetCapability(operation);
            var readOnly = capability is OperationCapability.Observe
                or OperationCapability.TemporaryExport or OperationCapability.SafetyRead;
            var readWrite = readOnly || capability is OperationCapability.Compile or OperationCapability.ProjectMutation;
            Assert.Equal(readOnly, OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
            Assert.Equal(readWrite, OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, operation));
            Assert.True(OperationPolicyCatalog.IsAllowed(Full, operation), operation);
        }
    }

    [Theory]
    [InlineData("compile_check", true)]
    [InlineData("update_tag", true)]
    [InlineData("update_type_content", true)]
    [InlineData("open_project", false)]
    [InlineData("create_project", false)]
    [InlineData("save_project", false)]
    [InlineData("save_project_as", false)]
    [InlineData("archive_project", false)]
    [InlineData("close_project", false)]
    [InlineData("start_plc", false)]
    [InlineData("stop_plc", false)]
    [InlineData("probe_project_status_for_lifecycle", false)]
    [InlineData("probe_open_project_rebind", false)]
    public void ReadWrite_CeilingProtectsPersistenceAndRuntime(string operation, bool allowed)
    {
        Assert.Equal(allowed, OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, operation));
        Assert.True(OperationPolicyCatalog.IsAllowed(Full, operation));
    }

    [Fact]
    public void UnknownOperations_AndInvalidModes_AreDenied()
    {
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, Full, (McpAccessMode)999 })
        {
            Assert.False(OperationPolicyCatalog.IsAllowed(mode, ""));
            Assert.False(OperationPolicyCatalog.IsAllowed(mode, "unknown_operation"));
        }
        Assert.False(OperationPolicyCatalog.IsAllowed((McpAccessMode)999, "update_tag"));
        Assert.False(OperationPolicyCatalog.IsAllowed((McpAccessMode)999, "get_project_status"));
    }

    [Fact]
    public void EveryRegisteredBatchOperation_HasACapability()
    {
        foreach (var operation in TiaMcpServer.Batch.BatchOperationCatalog.All)
            Assert.NotNull(OperationPolicyCatalog.GetCapability(operation.Name));
    }
}
