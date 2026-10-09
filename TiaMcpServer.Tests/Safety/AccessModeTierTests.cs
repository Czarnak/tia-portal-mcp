using TiaMcpServer.Contracts.Safety;
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
    public void Presets_ReadWriteAllowsLifecycle()
    {
        foreach (var operation in OperationPolicyCatalog.AllOperationNames)
        {
            var capability = OperationPolicyCatalog.GetCapability(operation);
            var readOnly = capability is OperationCapability.Observe
                or OperationCapability.TemporaryExport
                or OperationCapability.SessionSelection;
            var readWrite = readOnly || capability is OperationCapability.Compile
                or OperationCapability.ProjectMutation or OperationCapability.ProjectLifecycle;
            Assert.Equal(readOnly, OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
            Assert.Equal(readWrite, OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, operation));
            Assert.True(OperationPolicyCatalog.IsAllowed(Full, operation), operation);
        }
    }

    [Theory]
    [InlineData("compile_check", true)]
    [InlineData("update_tag", true)]
    [InlineData("update_type_content", true)]
    [InlineData("open_project", true)]
    [InlineData("create_project", true)]
    [InlineData("save_project", true)]
    [InlineData("save_project_as", true)]
    [InlineData("archive_project", true)]
    [InlineData("close_project", true)]
    [InlineData("probe_project_status_for_lifecycle", true)]
    [InlineData("probe_open_project_rebind", true)]
    [InlineData("get_basic_project_status", true)]
    public void ReadWrite_AndFull_AllowTheSameOperations(string operation, bool allowed)
    {
        Assert.Equal(allowed, OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, operation));
        Assert.True(OperationPolicyCatalog.IsAllowed(Full, operation));
    }

    [Fact]
    public void UnknownOperations_AndInvalidModes_AreDenied()
    {
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, Full, (McpAccessMode)999 })
        {
            Assert.False(OperationPolicyCatalog.IsAllowed(mode, null!));
            Assert.False(OperationPolicyCatalog.IsAllowed(mode, "   "));
            Assert.False(OperationPolicyCatalog.IsAllowed(mode, ""));
            Assert.False(OperationPolicyCatalog.IsAllowed(mode, "unknown_operation"));
        }
        Assert.False(OperationPolicyCatalog.IsAllowed((McpAccessMode)999, "update_tag"));
        Assert.False(OperationPolicyCatalog.IsAllowed((McpAccessMode)999, "get_project_status"));
    }

    [Fact]
    public void SessionSelection_NoExpectedIdentity()
    {
        Assert.Equal(OperationCapability.Observe,
            OperationPolicyCatalog.GetCapability("list_tia_portal_processes"));
        Assert.Equal(OperationCapability.SessionSelection,
            OperationPolicyCatalog.GetCapability("select_portal_project"));
        Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity("list_tia_portal_processes"));
        Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity("select_portal_project"));
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void Presets_SessionSelectionInEveryMode(McpAccessMode mode)
    {
        Assert.True(OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.SessionSelection));
        Assert.True(OperationPolicyCatalog.IsAllowed(mode, "select_portal_project"));
        Assert.True(OperationPolicyCatalog.IsAllowed(mode, "list_tia_portal_processes"));
    }

    [Fact]
    public void OnlineControlHasNoOperations()
    {
        Assert.DoesNotContain(OperationPolicyCatalog.AllOperationNames,
            name => OperationPolicyCatalog.GetCapability(name) == OperationCapability.OnlineControl);
        Assert.False(OperationPolicyCatalog.IsAllowed(Full, "start_plc"));
        Assert.False(OperationPolicyCatalog.IsAllowed(Full, "stop_plc"));
    }

    [Fact]
    public void ReadWriteAndFullAllowTheSameOperations()
    {
        foreach (var operation in OperationPolicyCatalog.AllOperationNames)
            Assert.Equal(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, operation),
                OperationPolicyCatalog.IsAllowed(Full, operation));
    }

    [Fact]
    public void EveryPlcWriteOperation_HasACapability()
    {
        foreach (var operation in TiaMcpServer.Plc.PlcOperationCatalog.WriteOperationNames)
            Assert.NotNull(OperationPolicyCatalog.GetCapability(operation));
    }
}
