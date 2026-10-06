using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class HardwareReadMessageTests
{
    private const string TypeIdentifierField = "Device item 'X1' type identifier";

    [Fact]
    public void AddReadMessage_EmitsDiagnosticWithoutRepeatingTheField()
    {
        var messages = new List<string>();

        NetworkObjectDiscoveryEvidence.AddReadMessage(
            messages, NetworkObjectDiscoveryEvidence.ReadString(null, TypeIdentifierField));

        Assert.Equal(new[] { "Device item 'X1' type identifier was null; selector not available." }, messages);
    }

    [Fact]
    public void AddReadMessage_UsableEvidence_AddsNothing()
    {
        var messages = new List<string>();

        NetworkObjectDiscoveryEvidence.AddReadMessage(
            messages, NetworkObjectDiscoveryEvidence.ReadString("CPU", TypeIdentifierField));

        Assert.Empty(messages);
    }

    [Fact]
    public void AddReadMessage_SuccessfullyReadNull_IsNotReportedWhenNullIsOrdinaryData()
    {
        var messages = new List<string>();

        NetworkObjectDiscoveryEvidence.AddReadMessage(
            messages, NetworkObjectDiscoveryEvidence.ReadString(null, TypeIdentifierField), reportNull: false);

        Assert.Empty(messages);
    }

    [Fact]
    public void AddReadMessage_BlankWrongTypeAndUnreadable_StillReportWhenNullIsOrdinaryData()
    {
        var messages = new List<string>();

        NetworkObjectDiscoveryEvidence.AddReadMessage(
            messages, NetworkObjectDiscoveryEvidence.ReadString(" ", TypeIdentifierField), reportNull: false);
        NetworkObjectDiscoveryEvidence.AddReadMessage(
            messages, NetworkObjectDiscoveryEvidence.ReadString(42, TypeIdentifierField), reportNull: false);
        NetworkObjectDiscoveryEvidence.AddReadMessage(
            messages, NetworkObjectDiscoveryEvidence.UnreadableString(TypeIdentifierField), reportNull: false);

        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public void PageMaterialization_DeduplicatesMessagesOrdinally()
    {
        var materialized = HardwarePageCandidateMaterialization.ForDevice(
            new DeviceInfo { Name = "S1" },
            new[] { "a", "a", "A", "b", "a" });

        Assert.Equal(new[] { "a", "A", "b" }, materialized.Messages);
    }

    [Fact]
    public void HardwareConfigReader_DeduplicatesMessagesAndDoesNotReportNullTypeIdentifier()
    {
        var source = ReadWorkerSource("HardwareConfigReader.cs");

        Assert.Contains("result.Messages = result.Messages.Distinct(StringComparer.Ordinal).ToList();", source, StringComparison.Ordinal);
        Assert.Contains("NetworkObjectDiscoveryEvidence.AddReadMessage(messages, typeIdentifier, reportNull: false);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void AddReadMessage", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HardwareConfigReader.cs")]
    [InlineData("NetworkObjectSelectorResolver.cs")]
    public void DeviceItemReads_DoNotProbeTheNonexistentAddressAttribute(string file)
    {
        var source = ReadWorkerSource(file);

        Assert.DoesNotContain("(IEngineeringObject)item,\n                \"Address\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("(IEngineeringObject)item, \"Address\"", source, StringComparison.Ordinal);
    }

    private static string ReadWorkerSource(string file)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "TiaMcpServer.OpennessWorker", "Openness", file);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n");
            }

            current = Path.GetDirectoryName(current);
        }

        throw new FileNotFoundException($"Could not find worker source '{file}'.");
    }
}
