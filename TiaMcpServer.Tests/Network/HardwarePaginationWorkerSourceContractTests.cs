using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePaginationWorkerSourceContractTests
{
    private const string WorkerMethod = "read_hardware_page_candidates";

    [Fact]
    public void InternalCandidateMethod_IsDispatchedAndIdentityValidatedBeforeReaderMaterialization()
    {
        var source = RepositoryFiles.ReadSource("TiaMcpServer.OpennessWorker", "Program.cs");
        var handler = ExtractMethod(
            RepositoryFiles.ReadSource("TiaMcpServer.OpennessWorker", "Program.NetworkDispatch.cs"),
            "public static WorkerResponse ReadHardwarePageCandidates(WorkerRequest request)",
            "public static WorkerResponse ListNetworkObjects(WorkerRequest request)");

        Assert.Contains($"\"{WorkerMethod}\" => NetworkWorkerDispatch.ReadHardwarePageCandidates(request)", source, StringComparison.Ordinal);
        Assert.Contains("WithEngineeringRoot(request", handler, StringComparison.Ordinal);
        AssertBefore(
            handler,
            "ValidateHardwarePageContinuationIdentity(request)",
            "HardwarePageCandidateReader.Read(");
        Assert.Contains("Success(HardwarePageCandidateReader.Read(", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void InternalCandidateMethod_IsObserveOnlyButNotAPublicNetworkOperation()
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(WorkerMethod));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, WorkerMethod));
        Assert.DoesNotContain(WorkerMethod, NetworkOperationCatalog.ReadOperationNames);
        Assert.DoesNotContain(WorkerMethod, NetworkOperationCatalog.WriteOperationNames);
    }

    [Fact]
    public void CandidatePayload_LeavesObservedSessionIdentityOnTheWorkerEnvelope()
    {
        Assert.DoesNotContain(
            typeof(HardwarePageCandidateResultInfo).GetProperties(),
            property => string.Equals(property.Name, nameof(WorkerResponse.SessionIdentity), StringComparison.Ordinal));
        Assert.NotNull(typeof(WorkerResponse).GetProperty(nameof(WorkerResponse.SessionIdentity)));
    }

    [Fact]
    public void SiemensWiring_EnumeratesDescriptorsAndUsesNarrowCandidateMaterializers()
    {
        var factorySource = RepositoryFiles.ReadSource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwarePageCandidateSourceFactory.cs");
        var readerSource = RepositoryFiles.ReadSource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareConfigReader.cs");

        Assert.Contains("ProjectDeviceEnumerator", factorySource, StringComparison.Ordinal);
        Assert.Contains("EnumerateWithLocations(project)", factorySource, StringComparison.Ordinal);
        Assert.Contains("new HardwarePageDescriptorSet(descriptors)", factorySource, StringComparison.Ordinal);
        Assert.Contains("HardwareConfigReader.ReadDevicePageCandidate(", factorySource, StringComparison.Ordinal);
        Assert.Contains("HardwareConfigReader.ReadSubnetPageCandidate(", factorySource, StringComparison.Ordinal);
        Assert.Contains("ReadDevice(device, nameEvidence, messages, includeIoDetails, tagIndex,", readerSource, StringComparison.Ordinal);
        Assert.Contains("ReadSubnet(subnet, subnetId, messages,", readerSource, StringComparison.Ordinal);
    }

    private static void AssertBefore(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Expected to find '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected to find '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected to find method marker '{startMarker}'.");
        Assert.True(end > start, $"Expected to find method boundary '{endMarker}'.");
        return source[start..end];
    }
}
