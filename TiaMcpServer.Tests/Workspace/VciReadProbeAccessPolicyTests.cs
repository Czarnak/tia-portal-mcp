using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

/// <summary>
/// Verifies the internal <c>probe_vci_read_contract</c> worker operation is classified exactly
/// like the existing internal network read probe (<c>probe_network_object_attributes</c>):
/// <see cref="OperationCapability.Observe"/>, allowed in read-only mode by both the shared
/// classification catalog and the worker's own defense-in-depth authorization layer.
/// </summary>
public class VciReadProbeAccessPolicyTests
{
    [Fact]
    public void ProbeVciReadContract_IsObserveAndAllowedInReadOnlyMode()
    {
        Assert.Equal(
            OperationCapability.Observe,
            OperationPolicyCatalog.GetCapability(VciReadProbeContract.OperationName));
        Assert.True(OperationPolicyCatalog.IsAllowed(
            McpAccessMode.ReadOnly,
            VciReadProbeContract.OperationName));
        Assert.Null(WorkerOperationAuthorization.Authorize(
            McpAccessMode.ReadOnly,
            VciReadProbeContract.OperationName));
    }

    [Fact]
    public void ProbeVciReadContract_IsAllowedInReadWriteMode()
    {
        Assert.True(OperationPolicyCatalog.IsAllowed(
            McpAccessMode.ReadWrite,
            VciReadProbeContract.OperationName));
        Assert.Null(WorkerOperationAuthorization.Authorize(
            McpAccessMode.ReadWrite,
            VciReadProbeContract.OperationName));
    }

    [Fact]
    public void ProbeVciReadContract_IsListedAmongKnownOperationNames()
    {
        Assert.Contains(VciReadProbeContract.OperationName, OperationPolicyCatalog.AllOperationNames);
    }
}
