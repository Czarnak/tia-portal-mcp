namespace TiaMcpServer.Tests.Network;

using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestUtilities;
using TiaMcpServer.Worker;
using Xunit;

public class NetworkIntrospectionWorkerDispatchTests
{
    [Fact]
    public void WorkerProgram_DispatchesListNetworkObjectsToTheNarrowHandler()
    {
        var source = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

        Assert.Contains("\"list_network_objects\" => ListNetworkObjects(request)", source);
        Assert.Contains("private static WorkerResponse ListNetworkObjects(WorkerRequest request)", source);
        Assert.Contains("NetworkObjectIndexReader", source);
        Assert.Contains("ValidateListNetworkObjectsRequest(request)", source);
        Assert.Contains("NetworkObjectKinds.Subnet", source);
        Assert.Contains("NetworkObjectKinds.IoSystem", source);
    }

    [Fact]
    public void WorkerProgram_PreservesRequiredNullMembersInNetworkObjectListPayloads()
    {
        var payload = WorkerSerializationHarness.Serialize(new NetworkObjectListInfo()).Payload!;
        using var json = JsonDocument.Parse(payload);

        Assert.False(WorkerJson.OmitsNullMembers(typeof(NetworkObjectListInfo)));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
    }

    // A successfully read CLR null is a typed value of kind "null", and the host contract requires
    // its "value" member. The inspection payload still omits other null members, so the real
    // worker's bytes must keep this one explicit or every inspection with a null attribute fails.
    [Fact]
    public void WorkerProgram_InspectionWithANullAttributeValue_PassesTheHostPayloadContract()
    {
        var payload = WorkerSerializationHarness.Serialize(new NetworkObjectInspectionInfo
        {
            Target = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.Node, DeviceName = "PLC_1", NodeId = "node-1" },
            Evidence = new NetworkObjectEvidenceInfo { Name = "X1" },
            Attributes = new List<NetworkAttributeInfo>
            {
                new()
                {
                    Name = "nullAttribute",
                    Source = "modeled",
                    Access = "readOnly",
                    Availability = "available",
                    Value = new NetworkAttributeValueInfo { Kind = "null", Value = null },
                },
            },
        }).Payload!;
        var diagnostics = new List<string>();

        var item = NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "inspect-1", Operation = "inspect_network_object" },
            WorkerCallResult.Ok(payload),
            diagnostics.Add);

        Assert.Empty(diagnostics);
        Assert.Equal("succeeded", item.Status);
    }

    [Fact]
    public void WorkerProgram_DispatchesInspectNetworkObjectThroughResolverAndInspector()
    {
        var source = File.ReadAllText(FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

        Assert.Contains("\"inspect_network_object\" => InspectNetworkObject(request)", source);
        Assert.Contains("private static WorkerResponse InspectNetworkObject(WorkerRequest request)", source);
        Assert.Contains("NetworkObjectSelectorResolver.Resolve", source);
        Assert.Contains("NetworkObjectInspector.Inspect", source);
    }

    [Fact]
    public void SelectorResolver_UsesTypedTraversalAndStableSelectionCategories()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectSelectorResolver.cs"));

        Assert.Contains("GetService<NetworkInterface>()", source);
        Assert.Contains("ItemAt(siblings, requestedSegment.Index)", source);
        Assert.Contains("requestedSegment.PositionNumber != positionNumber", source);
        Assert.Contains("StringComparison.Ordinal", source);
        Assert.Contains("WorkerFailureCategories.TargetNotFound", source);
        Assert.Contains("WorkerFailureCategories.TargetAmbiguous", source);
        Assert.Contains("WorkerFailureCategories.TargetEvidenceMismatch", source);
        Assert.Contains("WorkerFailureCategories.TargetKindUnsupported", source);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IoSystemDiscoverySelector_CarriesNameEvidenceUsedToDisambiguateDuplicateNumbers()
    {
        var discoverySource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectIndexReader.cs"));
        var resolverSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectSelectorResolver.cs"));

        Assert.Contains("NetworkSelectorFactory.IoSystem(", discoverySource, StringComparison.Ordinal);
        Assert.Contains("ioSystemIndex,", discoverySource, StringComparison.Ordinal);
        Assert.Contains("ioSystemName.IsUsable ? ioSystemName.Value : null", discoverySource, StringComparison.Ordinal);
        Assert.Contains("target.IoSystemIndex", resolverSource, StringComparison.Ordinal);
        Assert.Contains("candidateIndex", resolverSource, StringComparison.Ordinal);
        Assert.Contains("target.IoSystemName", resolverSource, StringComparison.Ordinal);
        Assert.Contains("candidate.Name", resolverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeDiscoverySelector_CarriesOwningPathAndSiblingIndexToDisambiguateDuplicateIds()
    {
        var discoverySource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectIndexReader.cs"));
        var resolverSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectSelectorResolver.cs"));

        Assert.Contains("NetworkSelectorFactory.Node(deviceName.Value, nodeId.Value, itemPath, nodeIndex)", discoverySource, StringComparison.Ordinal);
        Assert.Contains("target.NodeIndex", resolverSource, StringComparison.Ordinal);
        Assert.Contains("MatchDeviceItem(project, target)", resolverSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeDiscoverySelector_RequiresCompleteOwningPathEvidence()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectIndexReader.cs"));
        var nodeBlockStart = source.IndexOf("foreach (Node node in networkInterface.Nodes)", StringComparison.Ordinal);
        var nodeBlockEnd = source.IndexOf("private static void ReadSubnets", nodeBlockStart, StringComparison.Ordinal);

        Assert.True(nodeBlockStart >= 0 && nodeBlockEnd > nodeBlockStart);
        var nodeBlock = source[nodeBlockStart..nodeBlockEnd];
        Assert.Contains("itemPathDiagnostics", nodeBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("CombineDiagnostics(\n                Array.Empty<string>()", nodeBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeledAdapters_DoNotUseReflectionOrArbitraryToStringPublication()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkModeledAttributeAdapters.cs"));

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryReader_UsesTypedV21TraversalAndExactDynamicStringReads()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectIndexReader.cs"));

        Assert.Contains("device.Name", source, StringComparison.Ordinal);
        Assert.Contains("item.Name", source, StringComparison.Ordinal);
        Assert.Contains("item.PositionNumber", source, StringComparison.Ordinal);
        Assert.Contains("item.TypeIdentifier", source, StringComparison.Ordinal);
        Assert.Contains("node.Name", source, StringComparison.Ordinal);
        Assert.Contains("node.NodeId", source, StringComparison.Ordinal);
        Assert.Contains("subnet.Name", source, StringComparison.Ordinal);
        Assert.Contains("foreach (IoSystem ioSystem in subnet.IoSystems)", source, StringComparison.Ordinal);
        Assert.Contains("ioSystem.Name", source, StringComparison.Ordinal);
        Assert.Contains("ioSystem.Number", source, StringComparison.Ordinal);
        Assert.Contains("GetAttribute(\"Name\")", source, StringComparison.Ordinal);
        Assert.Contains("GetAttribute(\"SubnetId\")", source, StringComparison.Ordinal);
        Assert.Contains("NetworkObjectDiscoveryEvidence.ReadString", source, StringComparison.Ordinal);
        Assert.Contains("NetworkObjectDiscoveryEvidence.ReadInt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessReflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadEnumerableProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryReader_OptionalNamesDoNotGateOtherwiseCompleteSelectors()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectIndexReader.cs"));

        Assert.DoesNotContain("interfaceName.Diagnostic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nodeName.Diagnostic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("subnetName.Diagnostic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ioSystemName.Diagnostic", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareReader_UsesTypedIdentityReadsWithoutReflectionOrStringCoercion()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs"));

        Assert.Contains("item.Name", source, StringComparison.Ordinal);
        Assert.Contains("item.PositionNumber", source, StringComparison.Ordinal);
        Assert.Contains("item.TypeIdentifier", source, StringComparison.Ordinal);
        Assert.Contains("node.NodeId", source, StringComparison.Ordinal);
        Assert.Contains("ioSystem.Number", source, StringComparison.Ordinal);
        Assert.Contains("ReadExactStringAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadPropertyOrAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadEnumerableProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessReflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("value.ToString()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareReader_NormalizesNegativeIoAddressesAndCoercesDynamic64BitChannelAttributes()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareIoMapReader.cs"));

        // Diagnosis-type addresses can report StartAddress = -1 (and Length = -1) on V21; the
        // worker turns those into null plus a messages entry, never a negative payload member.
        Assert.Contains("ReadOptionalNonNegativeInt", source, StringComparison.Ordinal);
        Assert.Contains("() => address.StartAddress", source, StringComparison.Ordinal);
        Assert.Contains("() => address.Length", source, StringComparison.Ordinal);
        Assert.Contains("the reported value was negative", source, StringComparison.Ordinal);

        // Dynamic ChannelAddress/ChannelWidth values are coerced through the pure helper (which
        // accepts Int64/UInt64 within the DTO range) instead of an exact `is int`/`is uint` test.
        Assert.Contains("DynamicNumericAttribute.CoerceInt32", source, StringComparison.Ordinal);
        Assert.Contains("DynamicNumericAttribute.CoerceUInt32", source, StringComparison.Ordinal);
        Assert.DoesNotContain("value is int", source, StringComparison.Ordinal);
        Assert.DoesNotContain("value is uint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("value.ToString()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringAttributeInspector_UsesReadOnlyDynamicMetadataSurface()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "EngineeringAttributeInspector.cs"));

        Assert.Equal(1, CountOccurrences(source, "GetAttributeInfos()"));
        Assert.Contains("GetAttribute(", source);
        Assert.Contains("NetworkAttributeMetadataProcessor.Process", source, StringComparison.Ordinal);
        Assert.Contains("ReadName = () => info.Name", source, StringComparison.Ordinal);
        Assert.Contains("ReadAccess = () => Access(info.AccessMode)", source, StringComparison.Ordinal);
        Assert.Contains("ReadSupportedTypes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetAttribute(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetAttributes(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionReader_RoutesLocalIdReadFailureIntoSelectorIdentityDiagnostics()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "CommunicationConnectionReader.cs"));

        Assert.Contains("RequiresLocalConnectionId(connectionType)", source, StringComparison.Ordinal);
        Assert.Contains("AddDiagnostic(identityDiagnostics, idDiagnostic)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddMessage(messages, idDiagnostic)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectorResolver_RequiresAndVerifiesNonHmiLocalConnectionIdEvidence()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "NetworkObjectSelectorResolver.cs"));

        Assert.Contains("RequiresLocalConnectionId(connectionType!)", source, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(target.LocalConnectionId)", source, StringComparison.Ordinal);
        Assert.Contains("localConnectionId,", source, StringComparison.Ordinal);
        Assert.Contains("target.LocalConnectionId,", source, StringComparison.Ordinal);
        Assert.Contains("does not expose local-ID evidence", source, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = source.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.slnx")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
