using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePaginationWorkerSourceContractTests
{
    private const string WorkerMethod = "read_hardware_page_candidates";

    [Fact]
    public void InternalCandidateMethod_IsDispatchedAndIdentityValidatedBeforeReaderMaterialization()
    {
        var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Program.cs");
        var handler = ExtractMethod(
            source,
            "private static WorkerResponse ReadHardwarePageCandidates(WorkerRequest request)",
            "private static WorkerResponse ListNetworkObjects(WorkerRequest request)");

        Assert.Contains($"\"{WorkerMethod}\" => ReadHardwarePageCandidates(request)", source, StringComparison.Ordinal);
        Assert.Contains("WithProject(request", handler, StringComparison.Ordinal);
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
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs");

        Assert.Contains("ProjectDeviceEnumerator", source, StringComparison.Ordinal);
        Assert.Contains("EnumerateWithLocations(project)", source, StringComparison.Ordinal);
        Assert.Contains("new HardwarePageDescriptorSet(descriptors)", source, StringComparison.Ordinal);
        Assert.Contains("ReadDevicePageCandidate(", source, StringComparison.Ordinal);
        Assert.Contains("ReadSubnetPageCandidate(", source, StringComparison.Ordinal);
        Assert.Contains("ReadDevice(device, nameEvidence, messages, includeIoDetails, tagIndex)", source, StringComparison.Ordinal);
        Assert.Contains("ReadSubnet(subnet, subnetId, messages)", source, StringComparison.Ordinal);
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
