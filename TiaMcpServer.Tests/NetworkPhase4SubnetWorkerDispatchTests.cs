namespace TiaMcpServer.Tests;

using Xunit;

/// <summary>
/// Task 5, Step 1: source-contract tests for the worker's dispatch of the three Phase 4 subnet
/// lifecycle operations in <c>TiaMcpServer.OpennessWorker/Program.cs</c>. Reads the production
/// source text rather than exercising the dispatch switch at runtime, for the same reason given on
/// <see cref="NetworkPhase4SubnetWorkerServiceContractTests"/>.
/// </summary>
public class NetworkPhase4SubnetWorkerDispatchTests
{
    private static string ProgramSource => File.ReadAllText(
        FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

    [Fact]
    public void WorkerProgram_DispatchesAllThreeSubnetLifecycleOperationsToTheirOwnHandlers()
    {
        var source = ProgramSource;
        Assert.Contains("\"create_subnet\" => CreateSubnet(request),", source, StringComparison.Ordinal);
        Assert.Contains("\"update_subnet\" => UpdateSubnet(request),", source, StringComparison.Ordinal);
        Assert.Contains("\"delete_subnet\" => DeleteSubnet(request),", source, StringComparison.Ordinal);

        Assert.Contains("private static WorkerResponse CreateSubnet(WorkerRequest request)", source, StringComparison.Ordinal);
        Assert.Contains("private static WorkerResponse UpdateSubnet(WorkerRequest request)", source, StringComparison.Ordinal);
        Assert.Contains("private static WorkerResponse DeleteSubnet(WorkerRequest request)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerProgram_EachSubnetLifecycleHandlerRequiresConfirm()
    {
        var source = ProgramSource;
        var createBody = ExtractMethodBody(source, "CreateSubnet");
        var updateBody = ExtractMethodBody(source, "UpdateSubnet");
        var deleteBody = ExtractMethodBody(source, "DeleteSubnet");

        foreach (var body in new[] { createBody, updateBody, deleteBody })
        {
            Assert.Contains("if (!request.Confirm)", body, StringComparison.Ordinal);
            Assert.Contains("WorkerFailureCategories.ValidationError", body, StringComparison.Ordinal);
        }
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
    public void WorkerProgram_UsesASharedHelperForTheRepeatedSessionAndProjectRequirement()
    {
        var source = ProgramSource;

        // The three method-name strings stay explicit in the dispatch switch and (already, from
        // Task 3) the policy catalog — only the repeated session/project plumbing is shared.
        var helperOccurrences = CountOccurrences(source, "WithSubnetLifecycleProject(request,");
        Assert.Equal(3, helperOccurrences);
        Assert.Contains("private static WorkerResponse WithSubnetLifecycleProject(", source, StringComparison.Ordinal);
        Assert.Contains("session.EnsureConnected();", ExtractMethodBody(source, "WithSubnetLifecycleProject"), StringComparison.Ordinal);
        Assert.Contains("session.TiaPortal", ExtractMethodBody(source, "WithSubnetLifecycleProject"), StringComparison.Ordinal);
        Assert.Contains("session.Project", ExtractMethodBody(source, "WithSubnetLifecycleProject"), StringComparison.Ordinal);
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

    /// <summary>
    /// Extracts the body of a private static method named <paramref name="methodName"/> by finding
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
        foreach (var returnType in KnownReturnTypes)
        {
            var marker = $"private static {returnType} {methodName}(";
            var candidateIndex = source.IndexOf(marker, StringComparison.Ordinal);
            if (candidateIndex >= 0)
            {
                declarationIndex = candidateIndex;
                markerLength = marker.Length;
                break;
            }
        }

        Assert.True(declarationIndex >= 0, $"Expected a declaration of '{methodName}' in Program.cs.");

        var nextMethodIndex = source.IndexOf(
            "\n    private static",
            declarationIndex + markerLength,
            StringComparison.Ordinal);
        var end = nextMethodIndex >= 0 ? nextMethodIndex : source.Length;

        return source[declarationIndex..end];
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
