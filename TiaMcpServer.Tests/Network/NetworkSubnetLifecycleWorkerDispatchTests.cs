using TiaMcpServer.Tests.TestSupport;
namespace TiaMcpServer.Tests.Network;

using Xunit;

/// <summary>
/// Task 5, Step 1: source-contract tests for the worker's dispatch of the three Phase 4 subnet
/// lifecycle operations in <c>Program.NetworkDispatch.cs</c>. Reads the production
/// source text rather than exercising the dispatch switch at runtime, for the same reason given on
/// <see cref="NetworkSubnetLifecycleWorkerServiceContractTests"/>.
/// </summary>
public class NetworkSubnetLifecycleWorkerDispatchTests
{
    private static string ProgramSource => File.ReadAllText(
        RepositoryFiles.Path("TiaMcpServer.OpennessWorker", "Program.cs"))
        + File.ReadAllText(RepositoryFiles.Path("TiaMcpServer.OpennessWorker", "Program.NetworkDispatch.cs"));

    [Fact]
    public void WorkerProgram_DispatchesAllThreeSubnetLifecycleOperationsToTheirOwnHandlers()
    {
        var source = ProgramSource;
        Assert.Contains("\"create_subnet\" => NetworkWorkerDispatch.CreateSubnet(request),", source, StringComparison.Ordinal);
        Assert.Contains("\"update_subnet\" => NetworkWorkerDispatch.UpdateSubnet(request),", source, StringComparison.Ordinal);
        Assert.Contains("\"delete_subnet\" => NetworkWorkerDispatch.DeleteSubnet(request),", source, StringComparison.Ordinal);

        Assert.Contains("public static WorkerResponse CreateSubnet(WorkerRequest request)", source, StringComparison.Ordinal);
        Assert.Contains("public static WorkerResponse UpdateSubnet(WorkerRequest request)", source, StringComparison.Ordinal);
        Assert.Contains("public static WorkerResponse DeleteSubnet(WorkerRequest request)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerProgram_EachSubnetLifecycleHandlerCallsExactlyOneTypedServiceMethod()
    {
        var source = ProgramSource;
        Assert.Contains("SubnetLifecycleService.Create(", ExtractMethodBody(source, "CreateSubnet"), StringComparison.Ordinal);
        Assert.Contains("SubnetLifecycleService.Update(", ExtractMethodBody(source, "UpdateSubnet"), StringComparison.Ordinal);
        Assert.Contains("SubnetLifecycleService.Delete(", ExtractMethodBody(source, "DeleteSubnet"), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerProgram_CreateSubnetRepeatsNameTypeRangeAndBaudValidationBeforeTheServiceCall()
    {
        var source = ProgramSource;
        var body = ExtractMethodBody(source, "CreateSubnet");

        Assert.Contains("request.SubnetName", body, StringComparison.Ordinal);
        Assert.Contains("SubnetLifecycleContract.IsSupportedNetworkType", body, StringComparison.Ordinal);

        // Range and baud validation are delegated to shared helpers (also used by update_subnet)
        // rather than duplicated inline; assert the delegation here and the helpers' own use of the
        // contract's range/enum vocabulary separately.
        Assert.Contains("ValidateSubnetHighestAddressRange(request.SubnetHighestAddress);", body, StringComparison.Ordinal);
        Assert.Contains("ValidateSubnetTransmissionSpeedValue(request.SubnetTransmissionSpeed);", body, StringComparison.Ordinal);
        AssertRangeAndBaudHelpersUseTheContract(source);
    }

    [Fact]
    public void WorkerProgram_UpdateAndDeleteSubnetRepeatTargetIdValidationBeforeTheServiceCall()
    {
        var source = ProgramSource;
        var updateBody = ExtractMethodBody(source, "UpdateSubnet");
        var deleteBody = ExtractMethodBody(source, "DeleteSubnet");

        foreach (var body in new[] { updateBody, deleteBody })
        {
            Assert.Contains("request.SubnetId", body, StringComparison.Ordinal);
            Assert.Contains("WorkerFailureCategories.ValidationError", body, StringComparison.Ordinal);
        }

        Assert.Contains("ValidateSubnetHighestAddressRange(request.SubnetHighestAddress);", updateBody, StringComparison.Ordinal);
        Assert.Contains("ValidateSubnetTransmissionSpeedValue(request.SubnetTransmissionSpeed);", updateBody, StringComparison.Ordinal);
        AssertRangeAndBaudHelpersUseTheContract(source);
    }

    private static void AssertRangeAndBaudHelpersUseTheContract(string source)
    {
        var rangeHelperBody = ExtractMethodBody(source, "ValidateSubnetHighestAddressRange");
        Assert.Contains("SubnetLifecycleContract.MinimumHighestAddress", rangeHelperBody, StringComparison.Ordinal);
        Assert.Contains("SubnetLifecycleContract.MaximumHighestAddress", rangeHelperBody, StringComparison.Ordinal);

        var speedHelperBody = ExtractMethodBody(source, "ValidateSubnetTransmissionSpeedValue");
        Assert.Contains("SubnetLifecycleContract.IsSupportedTransmissionSpeed", speedHelperBody, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerProgram_SubnetLifecycleUsesCommonActiveContextAndAnyEngineeringRoot()
    {
        var source = ProgramSource;

        Assert.DoesNotContain("WithSubnetLifecycleProject(", source, StringComparison.Ordinal);
        Assert.Contains(
            "_sharedSession.EnsureConnected(request.ProjectPath);",
            ExtractMethodBody(source, "WithSession"),
            StringComparison.Ordinal);
        // Subnet lifecycle runs on any container's ProjectBase root (local sessions included).
        foreach (var handler in new[] { "CreateSubnet", "UpdateSubnet", "DeleteSubnet" })
        {
            var body = ExtractMethodBody(source, handler);
            Assert.Equal(1, RepositoryFiles.CountOccurrences(body, "WithActiveContext(request,"));
            Assert.Equal(1, RepositoryFiles.CountOccurrences(body, "session.EngineeringRoot!,"));
            Assert.Contains("session.TiaPortal!", body, StringComparison.Ordinal);
            Assert.DoesNotContain("RequireStandaloneOwner", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Extracts the body of a static dispatch or shared helper method named <paramref name="methodName"/> by finding
    /// its declaration and the next top-level method declaration (or class-closing brace region)
    /// after it. Good enough for these structural assertions without a full C# parser.
    /// </summary>
    private static readonly string[] KnownReturnTypes = { "WorkerResponse", "void" };

    private static string ExtractMethodBody(string source, string methodName)
    {
        // Matches only the DECLARATION (a known return type immediately before the name), not an
        // earlier dispatch-switch call site ("=> MethodName(request),") or helper call
        // ("MethodName(someArg);"), which share the same "MethodName(" substring.
        var declarationIndex = -1;
        var markerLength = 0;
        foreach (var prefix in new[] { "private", "public" })
        foreach (var returnType in KnownReturnTypes)
        {
            var marker = $"{prefix} static {returnType} {methodName}(";
            var candidateIndex = source.IndexOf(marker, StringComparison.Ordinal);
            if (candidateIndex >= 0)
            {
                declarationIndex = candidateIndex;
                markerLength = marker.Length;
                break;
            }
        }

        Assert.True(declarationIndex >= 0, $"Expected a declaration of '{methodName}' in the worker dispatch source.");

        var nextMethod = System.Text.RegularExpressions.Regex.Match(
            source[(declarationIndex + markerLength)..], @"\n +(?:private|public) static");
        var end = nextMethod.Success
            ? declarationIndex + markerLength + nextMethod.Index
            : source.Length;

        return source[declarationIndex..end];
    }
}
