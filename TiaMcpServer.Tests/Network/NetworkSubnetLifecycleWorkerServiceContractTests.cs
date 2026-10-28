namespace TiaMcpServer.Tests.Network;

using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Task 5, Step 1: source-contract tests for the production
/// <c>TiaMcpServer.OpennessWorker.Openness.Network.SubnetLifecycleService</c>. The worker cannot instantiate
/// Siemens Openness objects in an ordinary unit test (Openness uses .NET remoting that only works
/// inside a real TIA Portal-attached process), so these tests read the production source text and
/// assert structural properties instead of exercising runtime behavior.
/// </summary>
public class NetworkSubnetLifecycleWorkerServiceContractTests
{
    [Fact]
    public void Update_ResolvesTheSubnetOnceInsideItsTransaction()
    {
        var source = ServiceSource;
        var body = source[source.IndexOf("public static SubnetLifecycleResultInfo Update(", StringComparison.Ordinal)..source.IndexOf("public static SubnetLifecycleResultInfo Delete(", StringComparison.Ordinal)];
        const string resolve = "ResolveExactSubnetOrThrow(project, subnetId, \"update_subnet\")";
        Assert.Equal(1, CountOccurrences(body, resolve));
        Assert.True(body.IndexOf(resolve, StringComparison.Ordinal) > body.IndexOf("exclusiveAccess.Transaction(project, \"update_subnet\")", StringComparison.Ordinal));
    }

    [Fact]
    public void Lifecycle_HasNoDeviceCountOrAffectedNodeCapture()
    {
        Assert.DoesNotContain("project.Devices.Count", ServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureAffectedNodes", ServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NetworkMutationVerifier", ServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_DoesNotRejectProfibusAttributesByType()
    {
        var source = ServiceSource;
        var body = source[source.IndexOf("public static SubnetLifecycleResultInfo Update(", StringComparison.Ordinal)..source.IndexOf("public static SubnetLifecycleResultInfo Delete(", StringComparison.Ordinal)];
        Assert.DoesNotContain("ProfibusTypeIdentifier, StringComparison.Ordinal))", body, StringComparison.Ordinal);
    }

    private static string ServiceSource => File.ReadAllText(
        FindRepositoryFile("TiaMcpServer.OpennessWorker", "Openness", "Network", "SubnetLifecycleService.cs"));

    [Fact]
    public void Service_IsADistinctFileFromTheMutationProbe()
    {
        var path = FindRepositoryFile("TiaMcpServer.OpennessWorker", "Openness", "Network", "SubnetLifecycleService.cs");
        Assert.True(File.Exists(path), $"Expected a distinct production service file at {path}.");

        var source = ServiceSource;
        Assert.DoesNotContain("SubnetLifecycleMutationProbeService.Run", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class SubnetLifecycleMutationProbeService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_MapsOnlyTheTwoApprovedTypeIdentifiers()
    {
        var source = ServiceSource;
        Assert.Contains("SubnetLifecycleContract.TypeIdentifierFor(networkType)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string EthernetTypeIdentifier", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string ProfibusTypeIdentifier", source, StringComparison.Ordinal);
        Assert.Contains("WorkerFailureCategories.ValidationError", source, StringComparison.Ordinal);
        Assert.Contains("NetworkType '{networkType}' is not supported. Valid values: ", source, StringComparison.Ordinal);
        Assert.Contains("{SubnetLifecycleContract.Ethernet}, {SubnetLifecycleContract.Profibus}.", source, StringComparison.Ordinal);
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
        Assert.Contains("ProjectBase project", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_LateZeroOrMultipleSubnetMatchesFailAsPostconditionDriftWithoutFallback()
    {
        var source = ServiceSource;
        Assert.Contains("StringComparison.Ordinal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkerFailureCategories.TargetNotFound", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkerFailureCategories.TargetAmbiguous", source, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"throw\s+PostconditionFailed\(\s*operationName"), source);
        Assert.Contains("ResolveExactSubnetOrThrow(project, subnetId, \"update_subnet\")", source, StringComparison.Ordinal);
        Assert.Contains("ResolveExactSubnetOrThrow(project, subnetId, \"delete_subnet\")", source, StringComparison.Ordinal);

        // No unvalidated fallback to a first/any match — every match count is checked (0 or >1
        // both throw) before a single resolved subnet is ever used. Asserted by pattern rather than
        // by the accidental local variable name ("matches") so a harmless rename cannot silently
        // defeat this test.
        Assert.DoesNotContain("FirstOrDefault", source, StringComparison.Ordinal);
        Assert.DoesNotContain("First()", source, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\.Count == 0"), source);
        Assert.Matches(new Regex(@"\.Count > 1"), source);
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
    public void Service_DeleteResolvesAndCapturesNameInsideTransactionBeforeDelete()
    {
        var deleteBody = ExtractPublicMethodBody(ServiceSource, "Delete");

        Assert.DoesNotContain("var existing =", deleteBody, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(deleteBody, "ResolveExactSubnetOrThrow("));

        var transactionIndex = deleteBody.IndexOf(
            "exclusiveAccess.Transaction(project, \"delete_subnet\")",
            StringComparison.Ordinal);
        var resolveIndex = deleteBody.IndexOf(
            "ResolveExactSubnetOrThrow(project, subnetId, \"delete_subnet\")",
            StringComparison.Ordinal);
        var nameIndex = deleteBody.IndexOf(
            "ReadRequiredSubnetNameOrThrow(subnet, subnetId)",
            StringComparison.Ordinal);
        var deleteIndex = deleteBody.IndexOf("subnet.Delete();", StringComparison.Ordinal);
        var commitIndex = deleteBody.IndexOf("transaction.CommitOnDispose();", StringComparison.Ordinal);

        Assert.True(transactionIndex >= 0);
        Assert.True(transactionIndex < resolveIndex);
        Assert.True(resolveIndex < nameIndex);
        Assert.True(nameIndex < deleteIndex);
        Assert.True(deleteIndex < commitIndex);
    }

    [Fact]
    public void Service_CommitsOnlyAfterEverySetterOrMutationCallHasAlreadyRunWithinEachOperation()
    {
        // The occurrence-count test above proves each shape exists once per operation, but proves
        // nothing about ORDER — an implementation that committed as the very FIRST statement inside
        // the transaction (before any setter ran) would still pass those counts. This test closes
        // that gap: within each operation's own method body, every mutating call must appear
        // strictly BEFORE that same body's "transaction.CommitOnDispose();".
        var source = ServiceSource;

        var createBody = ExtractPublicMethodBody(source, "Create");
        AssertOccursBeforeCommit(createBody, "project.Subnets.Create(");
        AssertOccursBeforeCommit(createBody, "ApplyProfibusAttributes(");

        var updateBody = ExtractPublicMethodBody(source, "Update");
        AssertOccursBeforeCommit(updateBody, "ApplyProfibusAttributes(");
        AssertOccursBeforeCommit(updateBody, "ReadRequiredSubnetNameOrThrow(");

        var deleteBody = ExtractPublicMethodBody(source, "Delete");
        AssertOccursBeforeCommit(deleteBody, "subnet.Delete();");
    }

    private static void AssertOccursBeforeCommit(string methodBody, string mutatingCall)
    {
        var commitIndex = methodBody.IndexOf("transaction.CommitOnDispose();", StringComparison.Ordinal);
        var mutatingCallIndex = methodBody.IndexOf(mutatingCall, StringComparison.Ordinal);

        Assert.True(commitIndex >= 0, "Expected 'transaction.CommitOnDispose();' in the method body.");
        Assert.True(mutatingCallIndex >= 0, $"Expected '{mutatingCall}' in the method body.");
        Assert.True(
            mutatingCallIndex < commitIndex,
            $"Expected '{mutatingCall}' (at index {mutatingCallIndex}) to occur before "
            + $"'transaction.CommitOnDispose();' (at index {commitIndex}) — a commit must never run "
            + "before the mutation it is supposed to be committing.");
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
    public void Service_DeleteRejectsUnreadableOrBlankNameBeforeDelete()
    {
        var source = ServiceSource;
        var deleteBody = ExtractPublicMethodBody(source, "Delete");
        var helperIndex = source.IndexOf(
            "private static string ReadRequiredSubnetNameOrThrow(",
            StringComparison.Ordinal);

        Assert.True(helperIndex >= 0);
        Assert.Contains("catch (EngineeringException)", source[helperIndex..], StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(name)", source[helperIndex..], StringComparison.Ordinal);
        Assert.Contains("throw PostconditionFailed(", source[helperIndex..], StringComparison.Ordinal);
        Assert.DoesNotContain("capturedName = string.Empty;", deleteBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_DoesNotTraverseConnectionsBeforeDeletingASubnet()
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

        // Naming the excluded type is not enough: a broad "catch (Exception ...)" (or any other
        // catch wider than the narrow EngineeringException this file actually uses) would swallow
        // NonRecoverableException too without ever mentioning it by name. Reject the broad forms
        // directly, then enumerate every catch clause in the file and require each one to name
        // exactly the one approved, narrow type.
        Assert.DoesNotContain("catch (Exception", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (SystemException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (ApplicationException", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"catch\s*\{"), source); // bare "catch { ... }" with no type at all

        var caughtTypes = Regex.Matches(source, @"catch\s*\(\s*([A-Za-z0-9_.]+)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(caughtTypes);
        Assert.All(caughtTypes, caughtType => Assert.Equal("EngineeringException", caughtType));
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
