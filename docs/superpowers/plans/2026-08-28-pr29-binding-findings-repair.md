# PR #29 Binding Findings Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Repair the three approved PR #29 findings: permit genuinely unbound reads in read-write mode, make FakeWorker-backed network tests establish an honest worker/project identity, and make same-path forceRebind rotate the binding instead of silently preserving it.

**Architecture:** Put the expected-session requirement in OperationPolicyCatalog so the real worker and FakeWorker consume one fail-closed rule. Make the FakeWorker validate a supplied identity tuple against its process-local worker, Portal, generation, and canonical project state before scenario dispatch. Establish network test bindings by reading the requested scenario path and retaining the exact identity returned. Preserve the ProjectSessionBinding state machine, but allow the same-path fast path only when forceRebind is false.

**Tech Stack:** C# 12, .NET 8 host/FakeWorker/tests, .NET Standard 2.0 contracts, .NET Framework 4.8 Openness worker, xUnit, System.Text.Json, newline-delimited JSON IPC.

**Spec:** [PR #29 binding findings repair design](../specs/2026-08-28-pr29-binding-findings-repair-design.md)

## Global Constraints

- Work on the currently checked-out rebased PR branch. Do not create or switch branches or worktrees.
- Scope is exactly the original three findings. Do not repair or refactor the two unrelated suite-level flakes observed after rebase.
- Use behavioral TDD for each finding: add the regression, run it and record a meaningful RED, implement the smallest production change, then rerun to GREEN.
- Keep OperationPolicyCatalog as the single source of truth for whether a worker request may omit ExpectedSessionIdentity.
- Access authorization and session-identity authorization remain separate decisions. Do not weaken OperationAccessPolicy or WorkerOperationAuthorization.
- Observe and TemporaryExport operations may omit ExpectedSessionIdentity. open_project and create_project may also omit it because they establish identity. Compile, lifecycle operations other than open/create, mutations, online control, empty operation names, and unknown operation names require identity.
- If ExpectedSessionIdentity is supplied, validate it even for an operation that could have omitted it.
- Identity validation compares WorkerSessionId, SessionGeneration, PortalProcessId, and canonical ProjectPath. A protected request fails with binding_conflict before FakeWorker scenario execution when any field is missing or different.
- Do not add or change request/response DTO fields, safety-token formats, MCP schemas, or tool flows.
- Keep all evidence offline. Do not launch TIA Portal, connect to a live project, or execute a live write.
- Run build/test commands serially with --no-restore -m:1 --disable-build-servers. Use -p:UseTiaPortalReferenceStubs=true for the solution build.
- Do not commit, push, or post PR comments without a fresh explicit user authorization. Each task ends at a reviewable checkpoint and includes only a suggested commit message.

---

### Task 1: Centralize the missing-identity policy and make the real worker consume it

**Files:**

- Modify: TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs
- Modify: TiaMcpServer.Contracts/OperationPolicyCatalog.cs
- Modify: TiaMcpServer.OpennessWorker/Program.cs
- Modify: TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs

**Interfaces:**

- Add: OperationPolicyCatalog.RequiresExpectedSessionIdentity(string operation) -> bool
- Preserve: OperationPolicyCatalog.IsAllowed(McpAccessMode mode, string operation)
- Replace the real worker's local access-mode shortcut in AllowsMissingExpectedSessionIdentity with the shared catalog rule.
- Preserve the host invariant that an unbound read returns the worker identity but does not bind ProjectSessionBinding.

- [ ] **Step 1: Add the policy regression**

Add this theory to ReadOnlyModeTests:

~~~csharp
[Theory]
[InlineData("read_hardware_config", false)]
[InlineData("get_block_content", false)]
[InlineData("open_project", false)]
[InlineData("create_project", false)]
[InlineData("compile_check", true)]
[InlineData("probe_project_status_for_lifecycle", true)]
[InlineData("update_block_logic", true)]
[InlineData("start_plc", true)]
[InlineData("unknown-operation", true)]
[InlineData("", true)]
public void ExpectedSessionIdentityPolicy_IsFailClosed(
    string operation,
    bool expected)
{
    Assert.Equal(
        expected,
        OperationPolicyCatalog.RequiresExpectedSessionIdentity(operation));
}
~~~

- [ ] **Step 2: Run the focused policy test and verify RED**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ReadOnlyModeTests.ExpectedSessionIdentityPolicy_IsFailClosed"
~~~

Expected RED: compilation fails because OperationPolicyCatalog has no RequiresExpectedSessionIdentity method. This is the intended contract-first failure.

- [ ] **Step 3: Implement the shared fail-closed policy**

Add this method beside IsAllowed in OperationPolicyCatalog:

~~~csharp
/// <summary>
/// True when a worker request must carry the exact currently verified
/// worker/Portal/project identity.
/// </summary>
public static bool RequiresExpectedSessionIdentity(string operation)
{
    if (string.IsNullOrWhiteSpace(operation))
    {
        return true;
    }

    if (string.Equals(operation, "open_project", StringComparison.Ordinal) ||
        string.Equals(operation, "create_project", StringComparison.Ordinal))
    {
        return false;
    }

    var capability = GetCapability(operation);
    return capability != OperationCapability.Observe &&
           capability != OperationCapability.TemporaryExport;
}
~~~

The nullable comparison is intentional: an unknown operation produces null and therefore requires identity.

Replace the real worker helper in TiaMcpServer.OpennessWorker/Program.cs with:

~~~csharp
private static bool AllowsMissingExpectedSessionIdentity(string method)
    => !OperationPolicyCatalog.RequiresExpectedSessionIdentity(method);
~~~

Do not retain an access-mode condition in this helper. Read-only versus read-write authorization is already enforced separately.

- [ ] **Step 4: Strengthen the existing unbound read integration regression**

In UnboundSession_UnrelatedReadSuccess_DoesNotBindSession, construct OpennessWorkerClient with:

~~~csharp
accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite)
~~~

Keep the two unrelated read calls, then require:

~~~csharp
Assert.True(succeeded.Success);
Assert.NotNull(succeeded.SessionIdentity);
Assert.True(differentProject.Success);
Assert.NotNull(differentProject.SessionIdentity);

var snapshot = binding.CaptureSnapshot();
Assert.Equal(ProjectBindingSnapshot.UnboundState, snapshot.State);
Assert.False(snapshot.IsVerified);
Assert.Null(snapshot.ProjectPath);
~~~

This explicitly proves the mode named in the finding and prevents a successful read response from becoming an implicit host binding.

- [ ] **Step 5: Run the policy and unbound-read tests and verify GREEN**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ReadOnlyModeTests.ExpectedSessionIdentityPolicy_IsFailClosed|FullyQualifiedName~OpennessWorkerClientIntegrationTests.UnboundSession_UnrelatedReadSuccess_DoesNotBindSession"
~~~

Expected GREEN: all selected cases pass.

- [ ] **Step 6: Build the contracts and real worker through the stub path**

Run:

~~~powershell
dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj --no-restore -m:1 --disable-build-servers --nologo -p:UseTiaPortalReferenceStubs=true
~~~

Expected GREEN: the netstandard contract addition and net48 worker delegation compile without a Siemens installation.

- [ ] **Step 7: Review checkpoint**

Inspect only the four files in this task. Confirm the operation classifications did not change and no DTO or access-policy behavior moved. Suggested commit if separately authorized: fix: centralize worker session identity policy

---

### Task 2: Make FakeWorker enforce the shared worker/project identity precondition

**Files:**

- Create: TiaMcpServer.Tests/Worker/FakeWorkerIdentityEnforcementTests.cs
- Modify: TiaMcpServer.FakeWorker/Program.cs

**Interfaces:**

- Consume: OperationPolicyCatalog.RequiresExpectedSessionIdentity(string)
- Consume: WorkerRequest.ExpectedSessionIdentity
- Return: WorkerResponse with FailureCategory = WorkerFailureCategories.BindingConflict for identity failures
- Preserve: hello handshake, scripted scenario keys, worker restart identity, and existing response stamping

- [ ] **Step 1: Add FakeWorker identity enforcement tests**

Create FakeWorkerIdentityEnforcementTests.cs:

~~~csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Worker;

public sealed class FakeWorkerIdentityEnforcementTests
{
    [Fact]
    public async Task ProtectedRequestWithoutExpectedIdentityFailsBeforeScenarioDispatch()
    {
        using var transport = CreateTransport();
        await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip"
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Theory]
    [InlineData("workerSessionId")]
    [InlineData("sessionGeneration")]
    [InlineData("portalProcessId")]
    [InlineData("projectPath")]
    public async Task ProtectedRequestRejectsEveryMismatchedIdentityField(string field)
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip",
            ExpectedSessionIdentity = Change(observed, field)
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Fact]
    public async Task ProtectedRequestRejectsARequestPathOutsideTheExpectedProject()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip-other",
            ExpectedSessionIdentity = observed
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    private static PersistentWorkerTransport CreateTransport()
        => new(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));

    private static async Task<WorkerSessionIdentity> PrimeAsync(
        PersistentWorkerTransport transport)
    {
        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_hardware_config",
            ProjectPath = "network-roundtrip"
        });

        Assert.True(response.Success, response.Error);
        return Assert.IsType<WorkerSessionIdentity>(response.SessionIdentity);
    }

    private static WorkerSessionIdentity Change(
        WorkerSessionIdentity source,
        string field)
        => new()
        {
            WorkerSessionId = field == "workerSessionId"
                ? source.WorkerSessionId + "-different"
                : source.WorkerSessionId,
            SessionGeneration = field == "sessionGeneration"
                ? source.SessionGeneration + 1
                : source.SessionGeneration,
            PortalProcessId = field == "portalProcessId"
                ? source.PortalProcessId + 1
                : source.PortalProcessId,
            ProjectPath = field == "projectPath"
                ? source.ProjectPath + ".different"
                : source.ProjectPath
        };
}
~~~

If Assert.IsType is unavailable for nullable flow in the pinned xUnit version, use Assert.NotNull followed by response.SessionIdentity! without changing the assertions.

- [ ] **Step 2: Run the new tests and verify RED**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~FakeWorkerIdentityEnforcementTests"
~~~

Expected RED: responses fall through to scripted unknown/unexpected-method failures instead of binding_conflict because FakeWorker does not inspect ExpectedSessionIdentity.

- [ ] **Step 3: Parse ExpectedSessionIdentity once per FakeWorker request**

Add one process-wide case-insensitive serializer option:

~~~csharp
var requestJsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};
~~~

Add a per-request variable beside currentProjectPath/currentMethod, reset it at the top of every loop, and populate it inside the existing JsonDocument parse:

~~~csharp
WorkerSessionIdentity? currentExpectedSessionIdentity = null;

if (doc.RootElement.TryGetProperty(
        "expectedSessionIdentity",
        out var expectedIdentity) &&
    expectedIdentity.ValueKind == JsonValueKind.Object)
{
    currentExpectedSessionIdentity =
        expectedIdentity.Deserialize<WorkerSessionIdentity>(requestJsonOptions);
}
~~~

Keep hello handling before engineering-request validation.

- [ ] **Step 4: Add a fail-closed validation helper**

Add local helpers near Respond:

~~~csharp
WorkerResponse? ValidateExpectedSessionIdentity(
    string? method,
    string? requestedProjectPath,
    WorkerSessionIdentity? expected)
{
    var requiresIdentity =
        OperationPolicyCatalog.RequiresExpectedSessionIdentity(method ?? string.Empty);

    if (expected is null)
    {
        return requiresIdentity
            ? BindingConflict(
                "This operation requires expected worker/Portal/project session identity.")
            : null;
    }

    var expectedPath =
        ProjectPathNormalization.Canonicalize(expected.ProjectPath);
    var activePath =
        ProjectPathNormalization.Canonicalize(fakeProjectPath);

    if (string.IsNullOrWhiteSpace(expected.WorkerSessionId) ||
        expected.SessionGeneration < 0 ||
        expected.PortalProcessId is null ||
        expected.PortalProcessId <= 0 ||
        expectedPath is null ||
        activePath is null ||
        !string.Equals(
            expected.WorkerSessionId,
            workerSessionId,
            StringComparison.Ordinal) ||
        expected.SessionGeneration != fakeSessionGeneration ||
        expected.PortalProcessId != FakePortalProcessId ||
        !string.Equals(expectedPath, activePath, StringComparison.OrdinalIgnoreCase))
    {
        return BindingConflict(
            "The expected worker/Portal/project session identity does not match the FakeWorker session.");
    }

    var establishesProject =
        string.Equals(method, "open_project", StringComparison.Ordinal) ||
        string.Equals(method, "create_project", StringComparison.Ordinal);
    var requestedPath =
        ProjectPathNormalization.Canonicalize(requestedProjectPath);

    if (!establishesProject &&
        requestedPath is not null &&
        !string.Equals(
            expectedPath,
            requestedPath,
            StringComparison.OrdinalIgnoreCase))
    {
        return BindingConflict(
            "The request project path does not match the expected project session identity.");
    }

    return null;
}

WorkerResponse BindingConflict(string error)
    => new()
    {
        Success = false,
        FailureCategory = WorkerFailureCategories.BindingConflict,
        Error = error
    };
~~~

Immediately after hello handling and before switch (scenario), call the helper:

~~~csharp
var identityFailure = ValidateExpectedSessionIdentity(
    currentMethod,
    currentProjectPath,
    currentExpectedSessionIdentity);
if (identityFailure is not null)
{
    Respond(JsonSerializer.Serialize(identityFailure), includeSessionIdentity: false);
    continue;
}
~~~

Using includeSessionIdentity: false is required: a rejected request must not mutate fakeProjectPath, fakeSessionGeneration, or any scenario-owned mutable state.

- [ ] **Step 5: Run the FakeWorker identity tests and verify GREEN**

Run the Step 2 command. Expected GREEN: all missing/mismatched cases return binding_conflict.

- [ ] **Step 6: Run the existing FakeWorker transport and unbound-read regressions**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~WorkerProtocolHandshakeTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests.UnboundSession_UnrelatedReadSuccess_DoesNotBindSession|FullyQualifiedName~OpennessWorkerClientIntegrationTests.RestartedWorkerInvalidatesVerifiedBindingEvenWhenProjectPathIsUnchanged"
~~~

Expected GREEN: hello remains unaffected, optional unbound reads work, and worker restarts remain observable.

- [ ] **Step 7: Review checkpoint**

Confirm identity validation occurs after hello but before the scenario switch, supplied identities are always checked, unknown operations require identity, and rejected requests call Respond with includeSessionIdentity: false. Suggested commit if separately authorized: test: enforce binding identity in fake worker

---

### Task 3: Establish network fixture bindings from the exact requested-project read

**Files:**

- Create: TiaMcpServer.Tests/Network/NetworkVerifiedWriteFixtureTests.cs
- Modify: TiaMcpServer.Tests/Network/NetworkVerifiedWriteFixture.cs
- Modify: TiaMcpServer.FakeWorker/Program.cs

**Interfaces:**

- Consume: OpennessWorkerClient.ReadHardwareConfigAsync(string projectPath, ...)
- Consume unchanged: WorkerCallResult.SessionIdentity
- Consume unchanged: ProjectSessionBinding.BindVerified(WorkerSessionIdentity, bool, out string?)
- Remove the fixture's synthetic WorkerSessionIdentity construction.

- [ ] **Step 1: Add honest-binding fixture regressions**

Create NetworkVerifiedWriteFixtureTests.cs:

~~~csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkVerifiedWriteFixtureTests
{
    [Fact]
    public async Task VerifyAsync_BindsTheExactIdentityReportedForTheTargetPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);

        await NetworkVerifiedWriteFixture.VerifyAsync(
            client,
            binding,
            "network-roundtrip");

        var snapshot = binding.CaptureSnapshot();
        Assert.True(snapshot.IsVerified);
        Assert.Equal(
            ProjectPathNormalization.Canonicalize("network-roundtrip"),
            snapshot.ProjectPath);

        var followUp = await client.ReadHardwareConfigAsync("network-roundtrip");
        Assert.True(followUp.Success, followUp.Error);
        Assert.NotNull(followUp.SessionIdentity);
        Assert.Equal(snapshot.WorkerSessionId, followUp.SessionIdentity!.WorkerSessionId);
        Assert.Equal(snapshot.SessionGeneration, followUp.SessionIdentity.SessionGeneration);
        Assert.Equal(snapshot.PortalProcessId, followUp.SessionIdentity.PortalProcessId);
        Assert.Equal(snapshot.ProjectPath, followUp.SessionIdentity.ProjectPath);
    }

    [Fact]
    public async Task VerifyAsync_RejectsAWorkerReportedDifferentProjectWithoutBinding()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NetworkVerifiedWriteFixture.VerifyAsync(
                client,
                binding,
                "network-binding-mismatch"));

        Assert.Contains("reported project", exception.Message, StringComparison.OrdinalIgnoreCase);
        var snapshot = binding.CaptureSnapshot();
        Assert.Equal(ProjectBindingSnapshot.UnboundState, snapshot.State);
        Assert.Null(snapshot.ProjectPath);
    }

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));
}
~~~

- [ ] **Step 2: Add a mismatched-path FakeWorker scenario**

Add this scenario next to network-roundtrip:

~~~csharp
case "network-binding-mismatch":
    Respond(ReadMethod(line) == "read_hardware_config"
        ? SuccessWithResolvedPath(
            HardwareConfigPayload(),
            @"C:\FakeWorker\Different.ap21")
        : $$"""{"success":false,"error":"expected read_hardware_config, got '{{ReadMethod(line)}}'"}""");
    break;
~~~

Add the serializer helper beside Success:

~~~csharp
string SuccessWithResolvedPath(string payload, string resolvedProjectPath)
    => JsonSerializer.Serialize(new
    {
        success = true,
        payload,
        resolvedProjectPath
    });
~~~

This scenario must use a contract-valid HardwareConfigInfo payload. Only the worker-reported project path is intentionally wrong.

- [ ] **Step 3: Run the fixture tests and verify RED**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~NetworkVerifiedWriteFixtureTests"
~~~

Expected RED after Task 2 enforcement:

- VerifyAsync_BindsTheExactIdentityReportedForTheTargetPath fails because the fixture first observes scenario ok, rewrites only its path, and then presents an identity FakeWorker never held.
- VerifyAsync_RejectsAWorkerReportedDifferentProjectWithoutBinding fails because the fixture does not probe the requested path or reject the worker-reported mismatch.

- [ ] **Step 4: Replace identity manufacture with an exact target-path read**

Replace VerifyAsync with:

~~~csharp
internal static async Task VerifyAsync(
    OpennessWorkerClient client,
    ProjectSessionBinding binding,
    string projectPath)
{
    var probe = await client
        .ReadHardwareConfigAsync(projectPath)
        .ConfigureAwait(false);
    if (!probe.Success || probe.SessionIdentity is null)
    {
        throw new InvalidOperationException(
            $"The FakeWorker target-project read failed: " +
            $"{probe.Error ?? "missing session identity"}");
    }

    var requestedPath = ProjectPathNormalization.Canonicalize(projectPath);
    var reportedPath = ProjectPathNormalization.Canonicalize(
        probe.SessionIdentity.ProjectPath);
    if (requestedPath is null ||
        reportedPath is null ||
        !string.Equals(
            requestedPath,
            reportedPath,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"The FakeWorker reported project '{reportedPath ?? "<missing>"}' " +
            $"for requested project '{requestedPath ?? "<missing>"}'.");
    }

    if (!binding.BindVerified(
            probe.SessionIdentity,
            forceRebind: false,
            out var error))
    {
        throw new InvalidOperationException(
            $"Could not establish the FakeWorker project binding: {error}");
    }
}
~~~

Update the class summary so it says the fixture reads the requested scenario path and keeps the exact identity. Remove every claim about probing ok or changing only ProjectPath.

- [ ] **Step 5: Run the fixture tests and verify GREEN**

Run the Step 3 command. Expected GREEN: exact target identity binds; wrong reported path throws while binding remains unbound.

- [ ] **Step 6: Run all network FakeWorker suites**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~NetworkOperationFakeWorkerTests|FullyQualifiedName~NetworkSubnetLifecycleFakeWorkerTests|FullyQualifiedName~NetworkToolsTests|FullyQualifiedName~NetworkVerifiedWriteFixtureTests"
~~~

Expected GREEN: existing preview/apply and subnet lifecycle tests work with the honest binding.

- [ ] **Step 7: Review checkpoint**

Search NetworkVerifiedWriteFixture.cs for new WorkerSessionIdentity and GetProjectStatusAsync("ok"); both must be absent. Confirm BindVerified receives probe.SessionIdentity directly. Suggested commit if separately authorized: test: bind network fixture to reported project identity

---

### Task 4: Make same-path forceRebind create a fresh unverified binding

**Files:**

- Modify: TiaMcpServer.Tests/Project/ProjectSessionBindingTests.cs
- Modify: TiaMcpServer.Tests/Safety/WriteSafetyServiceTests.cs
- Modify: TiaMcpServer.Contracts/ProjectSessionBinding.cs

**Interfaces:**

- Preserve: ProjectSessionBinding.Bind(string projectPath, bool forceRebind, out string? error)
- Preserve: same-path, forceRebind: false is idempotent and retains verified identity.
- Change: same-path, forceRebind: true clears verified identity and transitions to a new ConfiguredUnverified snapshot.

- [ ] **Step 1: Add the forced same-path state-machine regression**

Add beside ReassertingSamePathDoesNotDiscardVerifiedWorkerIdentity:

~~~csharp
[Fact]
public void ForceReassertingSamePathCreatesFreshConfiguredUnverifiedRevision()
{
    var binding = new ProjectSessionBinding(null);
    Assert.True(binding.BindVerified(
        new WorkerSessionIdentity
        {
            WorkerSessionId = "worker-a",
            SessionGeneration = 3,
            PortalProcessId = 4242,
            ProjectPath = @"C:\Projects\Line.ap21"
        },
        forceRebind: false,
        out _));
    var before = binding.CaptureSnapshot();

    Assert.True(binding.Bind(
        "C:/Projects/Line.ap21",
        forceRebind: true,
        out var error));

    Assert.Null(error);
    var after = binding.CaptureSnapshot();
    Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, after.State);
    Assert.False(after.IsVerified);
    Assert.Equal(before.ProjectPath, after.ProjectPath);
    Assert.NotEqual(before.BindingId, after.BindingId);
    Assert.True(after.Revision > before.Revision);
    Assert.Null(after.WorkerSessionId);
    Assert.Null(after.SessionGeneration);
    Assert.Null(after.PortalProcessId);
    Assert.False(binding.TryGetVerified(
        @"C:\Projects\Line.ap21",
        out _,
        out _));
}
~~~

- [ ] **Step 2: Add the safety-token consequence regression**

Add beside TokenIsRejectedAfterSamePathSessionGenerationChanges:

~~~csharp
[Fact]
public void TokenIsRejectedAfterSamePathForcedRebindBecomesUnverified()
{
    using var audit = new TempAuditDirectory();
    var binding = new ProjectSessionBinding(null);
    Assert.True(binding.BindVerified(
        new WorkerSessionIdentity
        {
            WorkerSessionId = "worker-a",
            SessionGeneration = 1,
            PortalProcessId = 4242,
            ProjectPath = @"C:\p.ap21"
        },
        forceRebind: false,
        out _));
    var service = new WriteSafetyService(
        binding,
        () => DateTimeOffset.UtcNow,
        TimeSpan.FromMinutes(10),
        audit.Path);
    var token = ReadToken(service.CreatePreview(
        "apply_write_batch",
        @"C:\p.ap21",
        new { t = 1 },
        "s",
        new { i = 1 },
        "state"));

    Assert.True(binding.Bind(
        @"C:\p.ap21",
        forceRebind: true,
        out _));

    var result = service.ValidateEnvelope(
        token,
        "apply_write_batch",
        @"C:\p.ap21",
        new { t = 1 },
        new { i = 1 });

    Assert.False(result.IsValid);
    Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
}
~~~

- [ ] **Step 3: Run both regressions and verify RED**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ProjectSessionBindingTests.ForceReassertingSamePathCreatesFreshConfiguredUnverifiedRevision|FullyQualifiedName~WriteSafetyServiceTests.TokenIsRejectedAfterSamePathForcedRebindBecomesUnverified"
~~~

Expected RED: Bind returns true from the same-path fast path, leaving the verified snapshot and token unchanged.

- [ ] **Step 4: Gate the same-path fast path on forceRebind being false**

Change only the condition in ProjectSessionBinding.Bind:

~~~csharp
var current = _verifiedIdentity?.ProjectPath ?? _configuredProjectPath;
if (!forceRebind &&
    (_state == ProjectBindingSnapshot.ConfiguredUnverifiedState ||
     _state == ProjectBindingSnapshot.VerifiedState) &&
    current is not null &&
    IsSameProject(current, canonical))
{
    // An ordinary same-path assertion is idempotent and retains a complete worker identity.
    return true;
}
~~~

Leave the existing assignment and TransitionTo calls below it unchanged. They already clear _verifiedIdentity, increment Revision, create a fresh BindingId, and transition to ConfiguredUnverified.

- [ ] **Step 5: Run the new and existing same-path tests and verify GREEN**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ProjectSessionBindingTests.ReassertingSamePathDoesNotDiscardVerifiedWorkerIdentity|FullyQualifiedName~ProjectSessionBindingTests.ForceReassertingSamePathCreatesFreshConfiguredUnverifiedRevision|FullyQualifiedName~WriteSafetyServiceTests.TokenIsRejectedAfterSamePath"
~~~

Expected GREEN: force false is idempotent; force true rotates the binding and invalidates the prior token.

- [ ] **Step 6: Run the complete binding and safety test classes**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~WriteSafetyServiceTests|FullyQualifiedName~LifecycleIdentityContinuityTests"
~~~

Expected GREEN: no lifecycle continuity or existing safety behavior regresses.

- [ ] **Step 7: Review checkpoint**

Confirm the production diff is a one-condition change and the existing forceRebind: false regression remains unchanged. Suggested commit if separately authorized: fix: rotate binding on forced same-path rebind

---

### Task 5: Verify the complete three-finding repair offline

**Files:**

- Review: all files changed by Tasks 1-4
- Do not modify unrelated production or test files in response to a known suite flake.

**Verification boundary:**

- Establishes: contract compilation, real-worker stub compilation, FakeWorker IPC behavior, host binding behavior, network test-fixture behavior, safety-token invalidation, and full offline suite status.
- Does not establish: live TIA Portal attachment, a real project identity transition, Siemens Openness runtime behavior, or any live write.

- [ ] **Step 1: Run the complete focused repair set**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ExpectedSessionIdentityPolicy_IsFailClosed|FullyQualifiedName~UnboundSession_UnrelatedReadSuccess_DoesNotBindSession|FullyQualifiedName~FakeWorkerIdentityEnforcementTests|FullyQualifiedName~NetworkVerifiedWriteFixtureTests|FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~WriteSafetyServiceTests"
~~~

Expected GREEN: every new regression and its neighboring state-machine/safety tests pass.

- [ ] **Step 2: Build the complete solution with Siemens stubs**

Run:

~~~powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers --nologo -p:UseTiaPortalReferenceStubs=true
~~~

Expected GREEN: all projects compile, including net48 OpennessWorker and net8 FakeWorker/tests.

- [ ] **Step 3: Run the full serial offline test suite**

Run:

~~~powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo
~~~

Expected: all tests pass. If exactly one of the already observed suite-only failures appears:

- OpennessWorkerClientIntegrationTests.UncertainOutcome_IssuesFailedWriteOnce_ThenRestartedWorkerServesTheNextRequests("crash"), or
- WriteSafetyLeaseConcurrencyTests.ConcurrentApplies_SecondReReadsStateInsideLeaseAndDoesNotExecuteMutation,

rerun only that exact test once with --no-restore -m:1 --disable-build-servers. Record both outcomes. Do not edit those tests or their production paths under this plan. Any new failure in the changed binding/policy/FakeWorker/network areas is in scope and must be diagnosed before completion.

- [ ] **Step 4: Inspect scope and whitespace**

Run:

~~~powershell
git diff --check origin/main
git status --short
git diff --stat origin/main
git diff origin/main -- TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer.Contracts/ProjectSessionBinding.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs TiaMcpServer.Tests/Safety/WriteSafetyServiceTests.cs TiaMcpServer.Tests/Project/ProjectSessionBindingTests.cs TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs TiaMcpServer.Tests/Worker/FakeWorkerIdentityEnforcementTests.cs TiaMcpServer.Tests/Network/NetworkVerifiedWriteFixture.cs TiaMcpServer.Tests/Network/NetworkVerifiedWriteFixtureTests.cs docs/README.md docs/superpowers/README.md docs/superpowers/specs/2026-08-28-pr29-binding-findings-repair-design.md docs/superpowers/plans/2026-08-28-pr29-binding-findings-repair.md
~~~

Confirm:

- No request/response contract shape changed.
- No operation classification changed.
- No synthetic WorkerSessionIdentity remains in NetworkVerifiedWriteFixture.
- No access-mode bypass exists in AllowsMissingExpectedSessionIdentity.
- Same-path forceRebind: false preserves verified identity.
- Same-path forceRebind: true produces ConfiguredUnverified with a new binding ID/revision.
- Rejected FakeWorker identities do not execute or mutate scenario state.
- No unrelated flake fix, formatting sweep, or generated artifact entered the diff.

- [ ] **Step 5: Final report and optional integration decision**

Report:

- the three repaired findings;
- the focused test, stub build, and full-suite results;
- any narrow rerun of a known suite flake, with both outcomes;
- that live TIA behavior remains unverified and no live mutation was attempted;
- the exact uncommitted/committed state.

Stop before commit, push, merge, or PR comments unless the user explicitly authorizes the next action. Suggested squash title only if later requested: fix: enforce deterministic project session binding
