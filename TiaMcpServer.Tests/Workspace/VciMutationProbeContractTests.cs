using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

public class VciMutationProbeContractTests
{
    private static readonly string[] ExpectedCaseIds =
    {
        "P-INVENTORY",
        "M-CANARY",
        "M-GROUP",
        "M-WORKSPACE-ROOT",
        "M-WORKSPACE-LANGUAGE",
        "M-EXPORT",
        "M-DISCONNECT",
        "M-CONNECT",
        "M-P2W",
        "M-W2P",
        "M-DELETE-MAPPING",
        "M-DELETE-WORKSPACE",
        "M-DELETE-GROUP",
        "M-TX-GROUP",
        "M-TX-WORKSPACE",
        "M-TX-EXPORT",
        "M-TX-CONNECT",
        "M-TX-P2W",
        "M-TX-W2P",
        "M-TX-DISCONNECT",
        "M-TX-DELETE-WORKSPACE",
        "M-TX-DELETE-GROUP",
        "N-GROUP-NULL",
        "N-GROUP-EMPTY",
        "N-GROUP-WHITESPACE",
        "N-GROUP-DUPLICATE",
        "N-GROUP-INVALID",
        "N-WORKSPACE-NULL",
        "N-WORKSPACE-EMPTY",
        "N-WORKSPACE-WHITESPACE",
        "N-WORKSPACE-DUPLICATE",
        "N-WORKSPACE-INVALID",
        "N-WORKSPACE-PATH-RELATIVE",
        "N-WORKSPACE-PATH-MISSING-PARENT",
        "N-WORKSPACE-PATH-CONFLICT",
        "N-WORKSPACE-PATH-FILE",
        "N-WORKSPACE-LANGUAGE-NULL",
        "N-WORKSPACE-LANGUAGE-INVALID",
        "N-WORKSPACE-GLOBAL-LIBRARY-NULL",
        "N-WORKSPACE-GLOBAL-LIBRARY-INVALID",
        "N-OBJECT-NULL",
        "N-OBJECT-UNSUPPORTED",
        "N-OBJECT-FOREIGN",
        "N-OBJECT-DISPOSED",
        "N-OBJECT-ALREADY-MAPPED",
        "N-OBJECT-DELETED",
        "N-FORMAT-NULL",
        "N-FORMAT-EMPTY",
        "N-FORMAT-UNSUPPORTED",
        "N-FORMAT-WRONG-CASE",
        "N-FORMAT-MISMATCH",
        "N-FILENAME-INVALID",
        "N-FILENAME-ABSOLUTE",
        "N-FILENAME-TRAVERSAL",
        "N-FILENAME-COLLISION",
        "N-CONNECT-MISSING",
        "N-CONNECT-MALFORMED",
        "N-CONNECT-WRONG-OBJECT",
        "N-CONNECT-PARTIAL-FILE-SET",
        "N-SYNC-MISSING",
        "N-SYNC-MALFORMED",
        "N-SYNC-UNCHANGED",
        "N-SYNC-PROJECT-ONLY",
        "N-SYNC-WORKSPACE-ONLY",
        "N-SYNC-BOTH-SIDES",
        "N-SYNC-INVALID-ENUM",
        "N-DELETE-NONEMPTY",
        "N-DELETE-TWICE",
        "N-STALE-MAPPING-PROXY",
    };

    [Fact]
    public void MutationContract_LocksOperationSchemaAndCaseVocabulary()
    {
        Assert.Equal("probe_vci_mutation_contract", VciMutationProbeContract.OperationName);
        Assert.Equal("vci-mutation-probe/v1", VciMutationProbeContract.SchemaVersion);
        Assert.Equal(
            ExpectedCaseIds.OrderBy(x => x, StringComparer.Ordinal),
            VciMutationProbeContract.CaseIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void MutationContract_LocksOutcomeVocabulary()
    {
        Assert.Equal(
            new[] { "returned", "returned_null", "not_observable", "threw", "timed_out", "process_lost" },
            VciMutationProbeContract.Outcomes);
    }

    [Fact]
    public void WorkerRequest_CarriesOneTypedMutationProbeEnvelope()
    {
        Assert.Equal(
            typeof(VciMutationProbeRequestInfo),
            typeof(WorkerRequest).GetProperty(nameof(WorkerRequest.VciMutationProbe))!.PropertyType);
    }

    [Fact]
    public void MutationRequest_HasOnlyTheReviewedTypedSurface()
    {
        var expected = new[]
        {
            "CaseId", "CaseInstanceId", "EngineeringObject", "FileFormat", "FileName", "GroupName",
            "Mapping", "MaxCollectionItems", "MaxEngineeringObjects", "MaxGroupDepth", "MaxGroups",
            "MaxMappings", "MaxWorkspaces", "Mode", "NestedGroupName", "RelativeDirectory",
            "RollbackTransaction", "RunId", "ScenarioId", "SchemaVersion", "SeedRelativePath", "SessionId",
            "SynchronizationMode", "Workspace", "WorkspaceLanguage", "WorkspaceName", "WorkspaceRoot",
        };

        Assert.Equal(
            expected,
            typeof(VciMutationProbeRequestInfo).GetProperties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void MutationResult_UsesOrderedEvidenceCollectionsAndMutationSpecificState()
    {
        var result = new VciMutationProbeCaseResultInfo();

        Assert.IsType<List<VciMutationArgumentInfo>>(result.SanitizedArguments);
        Assert.IsType<List<VciMutationCheckInfo>>(result.Preconditions);
        Assert.IsType<List<VciMutationCheckInfo>>(result.SafetyInvariants);
        Assert.IsType<VciMutationTransactionInfo>(result.Transaction);
        Assert.IsType<VciMutationCanaryInfo>(result.Canary);
        Assert.Equal(VciMutationProbeContract.SchemaVersion, result.SchemaVersion);
    }

    [Theory]
    [InlineData("schema", "'schemaVersion' must be 'vci-mutation-probe/v1' (received 'schema').")]
    [InlineData("", "'runId' must be a nonblank string.", "runId")]
    [InlineData("", "'sessionId' must be a nonblank string.", "sessionId")]
    [InlineData("", "'scenarioId' must be a nonblank string.", "scenarioId")]
    [InlineData("", "'caseInstanceId' must be a nonblank string.", "caseInstanceId")]
    public void Validate_RejectsInvalidIdentityFields(string value, string expected, string field = "schemaVersion")
    {
        var request = ValidRequest();
        SetStringProperty(request, field, value);

        Assert.Equal(expected, VciMutationProbeContract.Validate(request));
    }

    [Theory]
    [InlineData("inventory", "'mode' must be 'Inventory' or 'Apply' (received 'inventory').")]
    [InlineData("Describe", "'mode' must be 'Inventory' or 'Apply' (received 'Describe').")]
    public void Validate_RequiresExactMode(string mode, string expected)
    {
        var request = ValidRequest();
        request.Mode = mode;

        Assert.Equal(expected, VciMutationProbeContract.Validate(request));
    }

    [Fact]
    public void Validate_RequiresInventoryModeOnlyForInventoryCase()
    {
        var request = ValidRequest();
        request.Mode = "Apply";

        Assert.Equal("'mode' must be 'Inventory' for case 'P-INVENTORY'.", VciMutationProbeContract.Validate(request));

        request = ValidRequest("M-CANARY");
        request.Mode = "Inventory";
        Assert.Equal("'mode' must be 'Apply' for mutation case 'M-CANARY'.", VciMutationProbeContract.Validate(request));
    }

    [Fact]
    public void Validate_RejectsUnknownCase()
    {
        var request = ValidRequest();
        request.CaseId = "M-ARBITRARY";

        Assert.Equal(
            "'caseId' value 'M-ARBITRARY' is not a recognised mutation probe case.",
            VciMutationProbeContract.Validate(request));
    }

    [Theory]
    [InlineData("MaxGroupDepth", "'maxGroupDepth' must be 1 or greater.")]
    [InlineData("MaxGroups", "'maxGroups' must be 1 or greater.")]
    [InlineData("MaxWorkspaces", "'maxWorkspaces' must be 1 or greater.")]
    [InlineData("MaxMappings", "'maxMappings' must be 1 or greater.")]
    [InlineData("MaxEngineeringObjects", "'maxEngineeringObjects' must be 1 or greater.")]
    [InlineData("MaxCollectionItems", "'maxCollectionItems' must be 1 or greater.")]
    public void Validate_RequiresPositiveBudgets(string propertyName, string expected)
    {
        var request = ValidRequest();
        typeof(VciMutationProbeRequestInfo).GetProperty(propertyName)!.SetValue(request, 0);

        Assert.Equal(expected, VciMutationProbeContract.Validate(request));
    }

    [Fact]
    public void Validate_RequiresAnAbsoluteWorkspaceRoot()
    {
        var request = ValidRequest();
        request.WorkspaceRoot = "relative-root";

        Assert.Equal("'workspaceRoot' must be an absolute path.", VciMutationProbeContract.Validate(request));
    }

    [Theory]
    [InlineData("M-EXPORT")]
    [InlineData("M-TX-EXPORT")]
    public void Validate_RequiresEngineeringObjectAndExactSimaticMlForExportCases(string caseId)
    {
        var request = ValidRequest(caseId);
        request.EngineeringObject = null;

        Assert.Equal($"'engineeringObject' is required for case '{caseId}'.", VciMutationProbeContract.Validate(request));

        request.EngineeringObject = new VciEngineeringObjectSelectorInfo();
        request.FileFormat = "simaticml";
        Assert.Equal($"'fileFormat' must be exactly 'SimaticML' for case '{caseId}'.", VciMutationProbeContract.Validate(request));
    }

    [Theory]
    [InlineData("M-P2W", "ProjectToWorkspace")]
    [InlineData("M-W2P", "WorkspaceToProject")]
    [InlineData("M-TX-P2W", "ProjectToWorkspace")]
    [InlineData("M-TX-W2P", "WorkspaceToProject")]
    public void Validate_LocksSynchronizationDirection(string caseId, string expectedMode)
    {
        var request = ValidRequest(caseId);
        request.Workspace = new VciWorkspaceSelectorInfo();
        request.Mapping = new VciMappingSelectorInfo();
        request.SynchronizationMode = expectedMode == "ProjectToWorkspace" ? "WorkspaceToProject" : "ProjectToWorkspace";

        Assert.Equal(
            $"'synchronizationMode' must be '{expectedMode}' for case '{caseId}'.",
            VciMutationProbeContract.Validate(request));
    }

    [Fact]
    public void Validate_BindsRollbackTransactionToTheTransactionCaseFamily()
    {
        var transactionCase = ValidRequest("M-TX-GROUP");
        transactionCase.RollbackTransaction = false;
        Assert.Equal(
            "'rollbackTransaction' must be true for transaction case 'M-TX-GROUP'.",
            VciMutationProbeContract.Validate(transactionCase));

        var ordinaryCase = ValidRequest("M-CANARY");
        ordinaryCase.RollbackTransaction = true;
        Assert.Equal(
            "'rollbackTransaction' must be false for non-transaction case 'M-CANARY'.",
            VciMutationProbeContract.Validate(ordinaryCase));
    }

    [Theory]
    [InlineData("N-GROUP-NULL")]
    [InlineData("N-WORKSPACE-NULL")]
    [InlineData("N-OBJECT-NULL")]
    [InlineData("N-FORMAT-NULL")]
    [InlineData("N-WORKSPACE-LANGUAGE-NULL")]
    [InlineData("N-WORKSPACE-GLOBAL-LIBRARY-NULL")]
    public void Validate_ExplicitNullCasesDoNotRequireTheArgumentTheyCharacterize(string caseId)
    {
        var request = ValidRequest(caseId);

        Assert.Null(VciMutationProbeContract.Validate(request));
    }

    [Fact]
    public void Validate_AcceptsMinimalInventoryAndApplyRequests()
    {
        Assert.Null(VciMutationProbeContract.Validate(ValidRequest()));
        Assert.Null(VciMutationProbeContract.Validate(ValidRequest("M-CANARY")));
    }

    private static VciMutationProbeRequestInfo ValidRequest(string caseId = "P-INVENTORY")
        => new()
        {
            RunId = "run",
            SessionId = "session",
            ScenarioId = "scenario",
            CaseId = caseId,
            CaseInstanceId = "instance",
            Mode = caseId == "P-INVENTORY" ? "Inventory" : "Apply",
            WorkspaceRoot = Path.GetFullPath(Path.Combine("build", "vci-probe", "run")),
            RollbackTransaction = caseId.StartsWith("M-TX-", StringComparison.Ordinal),
            EngineeringObject = caseId is "P-INVENTORY" or "M-EXPORT" or "M-TX-EXPORT"
                ? new VciEngineeringObjectSelectorInfo()
                : null,
            FileFormat = caseId is "P-INVENTORY" or "M-EXPORT" or "M-TX-EXPORT" ? "SimaticML" : null,
        };

    private static void SetStringProperty(VciMutationProbeRequestInfo request, string field, string value)
    {
        var propertyName = char.ToUpperInvariant(field[0]) + field.Substring(1);
        typeof(VciMutationProbeRequestInfo).GetProperty(propertyName)!.SetValue(request, value);
    }
}
