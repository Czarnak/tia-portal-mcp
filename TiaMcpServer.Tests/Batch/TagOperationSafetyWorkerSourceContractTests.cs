using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetyWorkerSourceContractTests
{
    [Fact]
    public void SafetyReader_UsesCompleteStrictDiscoveryAndTheTestedUniqueSelection()
    {
        var text = File.ReadAllText(Source("TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotReader.cs"));
        Assert.Contains("TagOperationSafetySnapshotBuilder.ResolveUniquePlc(", text, StringComparison.Ordinal);
        Assert.Contains("ProjectDeviceEnumerator.Enumerate(project)", text, StringComparison.Ordinal);
        Assert.Contains("item.GetService<SoftwareContainer>()", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PlcSoftwareLocator.Find", text, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (EngineeringException", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_DispatchesEveryInternalTagSafetyRead()
    {
        var text = File.ReadAllText(Source("TiaMcpServer.OpennessWorker/Program.cs"));
        Assert.Contains("\"read_create_tag_table_safety_snapshot\" => ReadCreateTagTableSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_delete_tag_table_safety_snapshot\" => ReadDeleteTagTableSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_create_tag_safety_snapshot\" => ReadCreateTagSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_update_tag_safety_snapshot\" => ReadUpdateTagSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_delete_tag_safety_snapshot\" => ReadDeleteTagSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_create_user_constant_safety_snapshot\" => ReadCreateUserConstantSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_update_user_constant_safety_snapshot\" => ReadUpdateUserConstantSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_delete_user_constant_safety_snapshot\" => ReadDeleteUserConstantSafetySnapshot(request)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationPolicyCatalog_RegistersTagSafetyReadersAsSafetyReads()
    {
        var text = File.ReadAllText(Source("TiaMcpServer.Contracts/OperationPolicyCatalog.cs"));
        Assert.Contains("[\"read_create_tag_table_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_delete_tag_table_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_create_tag_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_update_tag_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_delete_tag_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_create_user_constant_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_update_user_constant_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_delete_user_constant_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
    }

    private static string Source(string relative)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative));
}
