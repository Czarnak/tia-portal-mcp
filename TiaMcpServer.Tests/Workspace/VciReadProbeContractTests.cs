using System;
using System.Linq;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

/// <summary>
/// Locks the read-only VCI probe's typed wire vocabulary: operation name, schema version, the
/// exact 20-case ID vocabulary, the six outcome strings, and the semantic rules
/// <see cref="VciReadProbeContract.Validate"/> enforces on a <see cref="VciProbeRequestInfo"/>.
///
/// <para>
/// This is a Phase 1 Task 1 test — it exercises only the shared contract, never Siemens
/// Openness. No worker dispatch, no live TIA Portal.
/// </para>
/// </summary>
public class VciReadProbeContractTests
{
    private static VciProbeRequestInfo ValidRequest(string caseId = "R-SVC") => new()
    {
        SchemaVersion = VciReadProbeContract.SchemaVersion,
        RunId = "run-1",
        SessionId = "session-1",
        CaseId = caseId,
        CaseInstanceId = "instance-1",
    };

    [Fact]
    public void OperationName_IsProbeVciReadContract()
    {
        Assert.Equal("probe_vci_read_contract", VciReadProbeContract.OperationName);
    }

    [Fact]
    public void SchemaVersion_IsVciReadProbeV1()
    {
        Assert.Equal("vci-read-probe/v1", VciReadProbeContract.SchemaVersion);
    }

    [Fact]
    public void CaseIds_ExposesExactLockedVocabulary()
    {
        Assert.Equal(
            new[]
            {
                "N-FMT-FOREIGN", "N-FMT-NULL", "N-FMT-UNSUPPORTED",
                "N-GRP-FIND-EMPTY", "N-GRP-FIND-MISSING", "N-GRP-FIND-NULL",
                "N-GRP-FIND-WHITESPACE", "N-MAP-INACCESSIBLE-FILE",
                "N-MAP-MISSING-FILE", "N-WS-FIND-EMPTY", "N-WS-FIND-MISSING",
                "N-WS-FIND-NULL", "N-WS-FIND-WHITESPACE", "R-CANARY", "R-FMT",
                "R-GRP", "R-MAP", "R-REP", "R-SVC", "R-WS",
            },
            VciReadProbeContract.CaseIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void CaseIds_HasExactlyTwentyEntriesWithNoDuplicates()
    {
        Assert.Equal(20, VciReadProbeContract.CaseIds.Count);
        Assert.Equal(VciReadProbeContract.CaseIds.Count, VciReadProbeContract.CaseIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Outcomes_ExposesExactSixValues()
    {
        Assert.Equal(
            new[] { "returned", "returned_null", "not_observable", "threw", "timed_out", "process_lost" },
            VciReadProbeContract.Outcomes);
    }

    [Theory]
    [InlineData("R-SVC")]
    [InlineData("N-GRP-FIND-MISSING")]
    [InlineData("N-FMT-FOREIGN")]
    public void IsKnownCase_ReturnsTrueForEveryLockedCaseId(string caseId)
    {
        Assert.True(VciReadProbeContract.IsKnownCase(caseId));
    }

    [Theory]
    [InlineData("r-svc")]
    [InlineData("R-SVC-2")]
    [InlineData("")]
    [InlineData(null)]
    public void IsKnownCase_ReturnsFalseForAnythingOutsideTheLockedVocabulary(string? caseId)
    {
        Assert.False(VciReadProbeContract.IsKnownCase(caseId));
    }

    [Fact]
    public void Validate_AcceptsAMinimalWellFormedRequest()
    {
        Assert.Null(VciReadProbeContract.Validate(ValidRequest()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsBlankRunId(string blank)
    {
        var request = ValidRequest();
        request.RunId = blank;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("runId", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsBlankSessionId(string blank)
    {
        var request = ValidRequest();
        request.SessionId = blank;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("sessionId", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsBlankCaseInstanceId(string blank)
    {
        var request = ValidRequest();
        request.CaseInstanceId = blank;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("caseInstanceId", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownCaseId()
    {
        var request = ValidRequest();
        request.CaseId = "R-DOES-NOT-EXIST";

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("caseId", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsMismatchedSchemaVersion()
    {
        var request = ValidRequest();
        request.SchemaVersion = "vci-read-probe/v2";

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("schemaVersion", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsInvalidMaxGroupDepth(int invalid)
    {
        var request = ValidRequest();
        request.MaxGroupDepth = invalid;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("maxGroupDepth", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsInvalidMaxGroups(int invalid)
    {
        var request = ValidRequest();
        request.MaxGroups = invalid;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("maxGroups", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsInvalidMaxWorkspaces(int invalid)
    {
        var request = ValidRequest();
        request.MaxWorkspaces = invalid;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("maxWorkspaces", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsInvalidMaxMappings(int invalid)
    {
        var request = ValidRequest();
        request.MaxMappings = invalid;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("maxMappings", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsInvalidMaxEngineeringObjects(int invalid)
    {
        var request = ValidRequest();
        request.MaxEngineeringObjects = invalid;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("maxEngineeringObjects", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsInvalidMaxCollectionItems(int invalid)
    {
        var request = ValidRequest();
        request.MaxCollectionItems = invalid;

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("maxCollectionItems", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsRFmtCaseMissingWorkspaceSelector()
    {
        var request = ValidRequest("R-FMT");
        request.EngineeringObject = new VciEngineeringObjectSelectorInfo();

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("workspace", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsRFmtCaseMissingEngineeringObjectSelector()
    {
        var request = ValidRequest("R-FMT");
        request.Workspace = new VciWorkspaceSelectorInfo { WorkspaceName = "WS1" };

        var error = VciReadProbeContract.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("engineeringObject", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsRFmtCaseWithBothSelectors()
    {
        var request = ValidRequest("R-FMT");
        request.Workspace = new VciWorkspaceSelectorInfo { WorkspaceName = "WS1" };
        request.EngineeringObject = new VciEngineeringObjectSelectorInfo();

        Assert.Null(VciReadProbeContract.Validate(request));
    }

    [Theory]
    [InlineData("N-GRP-FIND-NULL")]
    [InlineData("N-WS-FIND-NULL")]
    [InlineData("N-FMT-NULL")]
    public void ExplicitNullCases_RemainConstructibleWithoutSelectors(string caseId)
    {
        var request = ValidRequest(caseId);

        Assert.Null(VciReadProbeContract.Validate(request));
    }

    [Fact]
    public void VciProbeCaseResultInfo_HasStableEnvelopeShape()
    {
        var result = new VciProbeCaseResultInfo();

        Assert.Equal(VciReadProbeContract.SchemaVersion, result.SchemaVersion);
        Assert.Equal(string.Empty, result.RunId);
        Assert.Equal(string.Empty, result.SessionId);
        Assert.Equal(string.Empty, result.CaseId);
        Assert.Equal(string.Empty, result.CaseInstanceId);
        Assert.Equal(string.Empty, result.Outcome);
        Assert.Null(result.Return);
        Assert.Null(result.Snapshot);
        Assert.Null(result.Exception);
        Assert.Null(result.Repeatability);
        Assert.Null(result.NotObservableReason);
        Assert.NotNull(result.ProjectState);
        Assert.NotNull(result.Omissions);
        Assert.Empty(result.Omissions);
    }

    [Fact]
    public void VciProbeRequestInfo_HasExpectedDefaults()
    {
        var request = new VciProbeRequestInfo();

        Assert.Equal(VciReadProbeContract.SchemaVersion, request.SchemaVersion);
        Assert.Equal(string.Empty, request.RunId);
        Assert.Equal(string.Empty, request.SessionId);
        Assert.Equal(string.Empty, request.CaseId);
        Assert.Equal(string.Empty, request.CaseInstanceId);
        Assert.Null(request.TargetName);
        Assert.Null(request.Workspace);
        Assert.Null(request.EngineeringObject);
        Assert.Null(request.SecondaryProjectPath);
        Assert.Equal(16, request.MaxGroupDepth);
        Assert.Equal(500, request.MaxGroups);
        Assert.Equal(500, request.MaxWorkspaces);
        Assert.Equal(5000, request.MaxMappings);
        Assert.Equal(200, request.MaxEngineeringObjects);
        Assert.Equal(5000, request.MaxCollectionItems);
    }
}
