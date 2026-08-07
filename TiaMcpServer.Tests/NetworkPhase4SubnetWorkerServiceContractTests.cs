namespace TiaMcpServer.Tests;

using System.Linq;
using System.Text.RegularExpressions;
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

    [Fact]
    public void Service_CapturesRootDeviceCountBeforeAndComparesAfterEveryOperation()
    {
        var source = ServiceSource;

        // Brief Step 4, bullet 1 mandates this exact "before" capture verbatim — kept as a literal
        // string match. The "after" re-read has no brief-mandated variable name, so it is asserted
        // as a pattern (re-reads Devices.Count and compares it to whatever the "before" value was
        // named) rather than pinned to the accidental identifier "deviceCountAfter".
        Assert.Equal(3, CountOccurrences(source, "var deviceCountBefore = project.Devices.Count;"));

        foreach (var body in new[]
                 {
                     ExtractPublicMethodBody(source, "Create"),
                     ExtractPublicMethodBody(source, "Update"),
                     ExtractPublicMethodBody(source, "Delete"),
                 })
        {
            // "before" capture, then a second, later read of project.Devices.Count for comparison.
            Assert.Equal(2, CountOccurrences(body, "project.Devices.Count"));
            Assert.Matches(new Regex(@"==\s*deviceCountBefore\b"), body);
        }
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
    public void Service_DeletePostconditionFailsClosedRatherThanTreatingAnUnreadableSubnetIdAsDeleted()
    {
        // Every other postcondition in this service already fails closed on an unreadable identity
        // because "no match found" is a FAILURE condition there (Create/Update need exactly one
        // match). delete_subnet is the one operation where "no match found" is the SUCCESS
        // condition, so an unreadable-but-still-present subnet must not be silently treated as
        // "gone" — this guards that specific asymmetry.
        var deleteBody = ExtractPublicMethodBody(ServiceSource, "Delete");

        // Reads the post-transaction match, additionally reporting how many candidates' identity
        // could not be read at all (distinct from "read fine but didn't match").
        Assert.Matches(new Regex(@"out\s+var\s+\w*[Uu]nreadable\w*"), deleteBody);

        var throwIndex = deleteBody.IndexOf("throw PostconditionFailed(", StringComparison.Ordinal);
        Assert.True(throwIndex >= 0, "Expected a PostconditionFailed throw in Delete's body.");
        var guardCondition = deleteBody[..throwIndex];

        // The unreadable count must be checked as > 0 and OR'd into the same guard that already
        // covers a nonzero match count and a changed device count — not read-and-ignored.
        Assert.Matches(new Regex(@"[Uu]nreadable\w*\s*>\s*0"), guardCondition);
    }

    [Fact]
    public void Service_DeleteNeverFallsBackToAnEmptyNameWhenTheSubnetsOwnNameIsUnreadable()
    {
        // The captured Name read for delete_subnet's result must never be silently replaced with
        // an empty string when it can't be read. NetworkPayloadContract.ValidateSubnetLifecycleResult
        // rejects a blank Name as a malformed result (protocol_error), which would misreport a
        // delete that actually committed as "never meaningfully forwarded" instead of the
        // fail-closed postcondition_failed this asymmetry actually deserves.
        var deleteBody = ExtractPublicMethodBody(ServiceSource, "Delete");

        Assert.DoesNotContain("capturedName = string.Empty;", deleteBody, StringComparison.Ordinal);

        // The unreadable-name case must feed the same PostconditionFailed guard as every other
        // fail-closed check in Delete — not be swallowed into a fabricated fallback value.
        var throwIndex = deleteBody.IndexOf("throw PostconditionFailed(", StringComparison.Ordinal);
        Assert.True(throwIndex >= 0, "Expected a PostconditionFailed throw in Delete's body.");
        var guardCondition = deleteBody[..throwIndex];
        Assert.Matches(new Regex(@"capturedName\s+is\s+null"), guardCondition);
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
