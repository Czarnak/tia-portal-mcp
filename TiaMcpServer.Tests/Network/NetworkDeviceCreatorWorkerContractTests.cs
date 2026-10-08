using System.Text.RegularExpressions;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkDeviceCreatorWorkerContractTests
{
    [Fact]
    public void Preflight_AllDependencySelectorsPrecedeFirstScalarSetter()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", "NetworkDeviceConfigurator.cs");
        var firstSetter = source.IndexOf("ApplyNodeAttribute(node,", StringComparison.Ordinal);
        var preflight = source[..firstSetter];
        Assert.Contains("FindExactlyOneSubnet(project, subnetId!)", preflight);
        Assert.Contains("FindExactlyOneIoSystem(ioSystemSubnet, ioSystemNumber.Value)", preflight);
        Assert.Contains("networkInterface.IoConnectors", preflight);
        Assert.Contains("NetworkPostconditionChecks.IoSystemSkipReason(", source);
    }

    [Fact]
    public void PnDeviceNameAutoGeneration_IsSetBeforePnDeviceName()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", "NetworkDeviceConfigurator.cs");
        var autoGeneration = source.IndexOf("\"PnDeviceNameAutoGeneration\", generated", StringComparison.Ordinal);
        var name = source.IndexOf("ApplyNodeAttribute(node, \"PnDeviceName\", pnDeviceName", StringComparison.Ordinal);
        Assert.True(autoGeneration >= 0 && name > autoGeneration);
        var verifier = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", "NetworkMutationVerifier.cs");
        Assert.Contains("case \"PnDeviceNameAutoGeneration\":", verifier);
    }

    [Fact]
    public void SelectionCertainty_DeviceAndInterfaceDiscoveryCannotDropUnreadableCandidates()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", "NetworkDeviceConfigurator.cs");
        var firstSetter = source.IndexOf("ApplyNodeAttribute(node,", StringComparison.Ordinal);
        var preflight = source[..firstSetter];
        Assert.Contains("NetworkObjectSelectorResolver.ResolveNode(project, target)", preflight);
        Assert.Contains("if (!selection.Success) throw new WorkerOperationException(selection.FailureCategory!, selection.Error!);", preflight);

        // Selection certainty now belongs to the shared resolver, including bare requests.
        var resolver = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", "NetworkObjectSelectorResolver.cs");
        Assert.Contains("string.IsNullOrWhiteSpace(candidate.Name)", resolver);
        Assert.Contains("The device-name namespace has unreadable required identity evidence.", resolver);
        Assert.Contains("The device-name namespace could not be read completely.", resolver);
        Assert.Contains("NetworkNodeReadSelectorBuilder.MatchNode(EnumerateNodes(deviceMatch.Value!), target.NodeId,", resolver);
        Assert.Contains("The device's full node namespace could not be read completely.", resolver);
        Assert.Contains("The selected owner's network interface could not be read.", resolver);
        Assert.Contains("The selected interface node collection could not be read.", resolver);
        Assert.DoesNotContain("Skipping network interface lookup", resolver);
        var matcher = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", "NetworkNodeReadSelectorBuilder.cs");
        Assert.Contains("Node namespace has unreadable identity evidence.", matcher);
        Assert.Contains("Node namespace could not be read completely.", matcher);
        var subnet = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", "SubnetLifecycleService.cs");
        Assert.Contains("FindMatches(project, subnetId, out var unreadableCount)", subnet);
        Assert.Contains("NetworkPostconditionChecks.ClassifySelection(matches.Count, unreadableCount == 0)", subnet);
        Assert.Contains("string.IsNullOrWhiteSpace(candidateId)", subnet);
    }

    [Fact]
    public void Dispatch_AttachesImmediateChecksBeforeSerializingBothDeviceMutationResults()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Program.cs");
        foreach (var method in new[] { "VerifyAddedDevice", "VerifyConfiguration" })
        {
            var check = source.IndexOf("result.Verification = NetworkMutationVerifier." + method, StringComparison.Ordinal);
            Assert.True(check >= 0);
            Assert.True(source.IndexOf("return Success(result);", check, StringComparison.Ordinal) > check);
        }
    }

    [Fact]
    public void Create_UsesTypeItemNameThenDeviceNameForCreateWithItem()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker",
            "Openness",
            "Network",
            "NetworkDeviceCreator.cs");
        var normalizedSource = Regex.Replace(source, @"\s+", " ");
        const string expectedCall =
            @"project\s*\.\s*Devices\s*\.\s*CreateWithItem\s*\(\s*typeIdentifier\s*,\s*deviceItemName\s*,\s*deviceName\s*\)";
        const string reversedCall =
            @"project\s*\.\s*Devices\s*\.\s*CreateWithItem\s*\(\s*typeIdentifier\s*,\s*deviceName\s*,\s*deviceItemName\s*\)";

        Assert.Single(Regex.Matches(normalizedSource, expectedCall));
        Assert.Empty(Regex.Matches(normalizedSource, reversedCall));
    }

    [Fact]
    public void CreatorAndImmediateVerifier_SelectCreatedItemAmongTopLevelItemsOnly()
    {
        foreach (var file in new[] { "NetworkDeviceCreator.cs", "NetworkMutationVerifier.cs" })
        {
            var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "Network", file);
            Assert.Contains("NetworkPostconditionChecks.SelectCreatedItem(device.DeviceItems.Cast<DeviceItem>()", source);
        }
    }

    [Fact]
    public void SelectCreatedItem_HeadModuleWithSameNamedChild_SelectsTopLevelItem()
    {
        // ET200SP shape: the child sub-item repeats the head module name; children are never searched.
        var head = new TopLevelItem("MCP_Test_IM");
        var selected = NetworkPostconditionChecks.SelectCreatedItem(
            new[] { new TopLevelItem("Rack_0"), head }, item => item.Name, "MCP_Test_IM");
        Assert.Same(head, selected);
    }

    [Fact]
    public void SelectCreatedItem_DuplicateTopLevelName_IsNotUnique()
    {
        var selected = NetworkPostconditionChecks.SelectCreatedItem(
            new[] { new TopLevelItem("IM"), new TopLevelItem("IM") }, item => item.Name, "IM");
        Assert.Null(selected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SelectCreatedItem_BlankSiblingName_DoesNotBlockUniqueTarget(string? siblingName)
    {
        var head = new TopLevelItem("IM");
        var selected = NetworkPostconditionChecks.SelectCreatedItem(
            new[] { new TopLevelItem(siblingName), head }, item => item.Name, "IM");
        Assert.Same(head, selected);
    }

    private sealed record TopLevelItem(string? Name);

    private static string ReadRepositorySource(params string[] pathSegments)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n");
            }

            current = Path.GetDirectoryName(current);
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{Path.Combine(pathSegments)}'.");
    }
}
