namespace TiaMcpServer.Tests;

using System.Linq;
using Xunit;

/// <summary>
/// Task 5, Step 1: source-contract tests for the production
/// <c>TiaMcpServer.OpennessWorker.Openness.SubnetLifecycleService</c>. The worker cannot instantiate
/// Siemens Openness objects in an ordinary unit test (Openness uses .NET remoting that only works
/// inside a real TIA Portal-attached process), so these tests read the production source text and
/// assert structural properties instead of exercising runtime behavior.
/// </summary>
public class NetworkPhase4SubnetWorkerServiceContractTests
{
    private static string ServiceSource => File.ReadAllText(
        FindRepositoryFile("TiaMcpServer.OpennessWorker", "Openness", "SubnetLifecycleService.cs"));

    [Fact]
    public void Service_IsADistinctFileFromTheMutationProbe()
    {
        var path = FindRepositoryFile("TiaMcpServer.OpennessWorker", "Openness", "SubnetLifecycleService.cs");
        Assert.True(File.Exists(path), $"Expected a distinct production service file at {path}.");

        var source = ServiceSource;
        Assert.DoesNotContain("SubnetLifecycleMutationProbeService.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class SubnetLifecycleMutationProbeService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_MapsOnlyTheTwoApprovedTypeIdentifiers()
    {
        var source = ServiceSource;
        Assert.Contains(
            "private const string EthernetTypeIdentifier = \"System:Subnet.Ethernet\";",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private const string ProfibusTypeIdentifier = \"System:Subnet.Profibus\";",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Service_DeclaresTheThreeTypedEntryPoints()
    {
        var source = ServiceSource;
        Assert.Contains(
            "public static SubnetLifecycleResultInfo Create(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static SubnetLifecycleResultInfo Update(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static SubnetLifecycleResultInfo Delete(",
            source,
            StringComparison.Ordinal);
        Assert.Contains("TiaPortal tiaPortal", source, StringComparison.Ordinal);
        Assert.Contains("Project project", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_UsesOrdinalExactOneSubnetIdLookupWithNoFallback()
    {
        var source = ServiceSource;
        Assert.Contains("StringComparison.Ordinal", source, StringComparison.Ordinal);
        Assert.Contains("WorkerFailureCategories.TargetNotFound", source, StringComparison.Ordinal);
        Assert.Contains("WorkerFailureCategories.TargetAmbiguous", source, StringComparison.Ordinal);

        // No unvalidated fallback to a first/any match — every match count is checked (0 or >1
        // both throw) before a single resolved subnet is ever used.
        Assert.DoesNotContain("FirstOrDefault", source, StringComparison.Ordinal);
        Assert.DoesNotContain("First()", source, StringComparison.Ordinal);
        Assert.Contains("matches.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("matches.Count > 1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_UsesOneExclusiveAccessTransactionPerOperationWithCommitOnDisposeAfterEverySetter()
    {
        var source = ServiceSource;
        Assert.Equal(3, CountOccurrences(source, "tiaPortal.ExclusiveAccess("));
        Assert.Equal(3, CountOccurrences(source, "exclusiveAccess.Transaction(project,"));
        Assert.Equal(3, CountOccurrences(source, "transaction.CommitOnDispose();"));
    }

    [Fact]
    public void Service_SetsHighestAddressViaSetAttributeAndParsesTransmissionSpeedFromTheCurrentAttributeType()
    {
        var source = ServiceSource;
        Assert.Contains("engineeringObject.SetAttribute(\"HighestAddress\",", source, StringComparison.Ordinal);
        Assert.Contains("engineeringObject.GetAttribute(\"TransmissionSpeed\")", source, StringComparison.Ordinal);
        Assert.Contains(
            "Enum.Parse(currentValue.GetType(), transmissionSpeed, ignoreCase: false)",
            source,
            StringComparison.Ordinal);

        // Never bound to a guessed/hardcoded Siemens enum type name.
        Assert.DoesNotContain("typeof(TransmissionSpeed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Siemens.Engineering.HW.TransmissionSpeed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_CapturesRootDeviceCountBeforeAndComparesAfterEveryOperation()
    {
        var source = ServiceSource;
        Assert.Equal(3, CountOccurrences(source, "var deviceCountBefore = project.Devices.Count;"));
        Assert.Equal(3, CountOccurrences(source, "var deviceCountAfter = project.Devices.Count;"));
        Assert.Contains("deviceCountAfter == deviceCountBefore", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_ThrowsPostconditionFailedOnAnyMismatchForEveryOperation()
    {
        var source = ServiceSource;

        // At least one postcondition-failure throw site per operation (Create legitimately may
        // check more than one condition, but each of the three public methods must guard its own
        // postcondition before returning).
        var createBody = ExtractPublicMethodBody(source, "Create");
        var updateBody = ExtractPublicMethodBody(source, "Update");
        var deleteBody = ExtractPublicMethodBody(source, "Delete");
        foreach (var body in new[] { createBody, updateBody, deleteBody })
        {
            Assert.Contains("throw PostconditionFailed(", body, StringComparison.Ordinal);
        }

        Assert.Contains(
            "WorkerFailureCategories.PostconditionFailed",
            source,
            StringComparison.Ordinal);
    }

    private static string ExtractPublicMethodBody(string source, string methodName)
    {
        var marker = $"public static SubnetLifecycleResultInfo {methodName}(";
        var declarationIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(declarationIndex >= 0, $"Expected a declaration of '{methodName}' in SubnetLifecycleService.cs.");

        var nextMethodIndex = source.IndexOf(
            "\n    public static",
            declarationIndex + marker.Length,
            StringComparison.Ordinal);
        var nextPrivateIndex = source.IndexOf(
            "\n    private static",
            declarationIndex + marker.Length,
            StringComparison.Ordinal);
        var candidates = new[] { nextMethodIndex, nextPrivateIndex }.Where(index => index >= 0).ToArray();
        var end = candidates.Length > 0 ? candidates.Min() : source.Length;

        return source[declarationIndex..end];
    }

    [Fact]
    public void Service_NeverCallsSaveCompileOrDeletesADevice()
    {
        var source = ServiceSource;
        Assert.DoesNotContain(".Save(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Compile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", source, StringComparison.Ordinal);

        // subnet.Delete() is the only Delete() call — a device is never deleted.
        Assert.Equal(1, CountOccurrences(source, ".Delete()"));
        Assert.Contains("subnet.Delete();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_NeverTraversesConnectedNodesOrIoSystemsBeforeDeletingASubnet()
    {
        var source = ServiceSource;
        Assert.DoesNotContain(".Nodes", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".IoSystems", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectedNodeNames", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_NeverRetriesAndNeverCatchesNonRecoverableException()
    {
        var source = ServiceSource;

        // No automatic retry loop or retry policy anywhere. (The prose "before retrying" in a
        // postcondition-failure message is human guidance, not machinery, so it is deliberately
        // not what these checks look for.)
        Assert.DoesNotContain("while (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("for (", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RetryPolicy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxRetries", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RetryCount", source, StringComparison.Ordinal);

        // Never caught at all — an uncaught NonRecoverableException propagates straight to
        // Program.Execute's dedicated catch rather than being swallowed here.
        Assert.DoesNotContain("NonRecoverableException", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_RejectsInapplicableProfibusOnlyChangesOnUpdateBeforeAnyTransaction()
    {
        var source = ServiceSource;
        var updateStart = source.IndexOf("public static SubnetLifecycleResultInfo Update(", StringComparison.Ordinal);
        var deleteStart = source.IndexOf("public static SubnetLifecycleResultInfo Delete(", StringComparison.Ordinal);
        Assert.True(updateStart >= 0 && deleteStart > updateStart);
        var updateBody = source[updateStart..deleteStart];

        var firstTransactionIndex = updateBody.IndexOf("tiaPortal.ExclusiveAccess(", StringComparison.Ordinal);
        var validationIndex = updateBody.IndexOf("WorkerFailureCategories.ValidationError", StringComparison.Ordinal);
        Assert.True(validationIndex >= 0 && firstTransactionIndex > validationIndex);
        Assert.Contains("ProfibusTypeIdentifier", updateBody, StringComparison.Ordinal);
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
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
