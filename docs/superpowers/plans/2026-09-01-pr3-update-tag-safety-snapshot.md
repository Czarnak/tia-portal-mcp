# PR 3 Update Tag Safety Snapshot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bind `update_tag` safety tokens to an exact-target typed snapshot that includes resolved PLC identity plus the three nullable external-access flags, while still composing that strict snapshot with the existing broad `list_tag_tables` state until PR 5.

**Architecture:** Keep exact tag resolution and flag reads in the net48 worker, add a shared `TagUpdateSafetySnapshot` DTO in `TiaMcpServer.Contracts`, and reuse the ordinary bound host request seam so the strict snapshot read carries `ExpectedSessionIdentity` without duplicating transport or binding logic. Add a reusable `OperationCapability.SafetyRead` classification for side-effect-free internal reads whose result contributes to write-token state: it remains read-only safe, but unlike ordinary `Observe` it requires the exact verified worker/Portal/project identity. `BatchWorkerInvoker.ReadCurrentStateAsync` will special-case `update_tag` to compose the strict exact-target snapshot with the existing broad `ListTagTablesAsync` payload, fail closed when a requested flag is unreadable, and keep public `list_tag_tables` semantics unchanged until PR 5 narrows tag scopes.

**Tech Stack:** C#; .NET 8 host; .NET Framework 4.8 worker; `netstandard2.0` shared contracts; xUnit; FakeWorker; PowerShell 7 live harnesses; existing write-safety token, host binding, and worker IPC infrastructure.

**Spec:** [`docs/superpowers/specs/2026-09-01-write-safety-hardening-design.md`](../specs/2026-09-01-write-safety-hardening-design.md)

## Global Constraints

- Introduce a typed exact-target `TagUpdateSafetySnapshot` in `TiaMcpServer.Contracts` and a dedicated strict worker reader for `update_tag`.
- Introduce `OperationCapability.SafetyRead` as the shared classification for side-effect-free internal safety-state reads. It is allowed by read-only access policy but, unlike `Observe` and `TemporaryExport`, `OperationPolicyCatalog.RequiresExpectedSessionIdentity` must return `true` for it.
- Classify `read_update_tag_safety_snapshot` as `SafetyRead`, never as ordinary `Observe`, and keep it out of every public operation catalog. PR 5 and PR 6 must reuse this capability for their internal safety selectors rather than adding method-name exceptions.
- Prove both halves of the identity boundary: the ordinary bound client sends a non-null complete `ExpectedSessionIdentity`, and the worker enforcement path rejects the same safety read when identity is missing or mismatched.
- The snapshot binds deterministic PLC, folder, table, and tag identity plus every property the current mutator can change: name, data type, logical address, `ExternalAccessible`, `ExternalVisible`, and `ExternalWritable`.
- The PLC field in the snapshot must serialize the resolved PLC name returned by `PlcSoftwareLocator.Find`, not the raw caller input. When `plcName` input is omitted, the snapshot still records the selected PLC deterministically.
- The three external values are nullable so an actual `false` differs from a property that the selected PLC/tag does not expose.
- If the request intends to change a flag whose current value cannot be read, preview fails before issuing a token.
- For this milestone, `update_tag` composes the strict exact-target snapshot with the existing broad `ListTagTablesAsync` state instead of replacing it.
- The exact-target reader must not skip or partially serialize the target.
- The existing public `list_tag_tables` response, its best-effort behavior, and the other tag operation selectors remain unchanged.
- `IsSafety` is not added because the current write path rejects it before mutation.
- No whole-PLC strict scan in this PR. Scope narrowing and deduplication belong to PR 5.
- Multilingual per-tag comment binding remains deferred; this PR does not change `delete_tag` safety shape.
- PLC `start_plc` / `stop_plc` work remains deferred.
- Preview remains non-mutating. Apply still requires the unchanged request, `confirm=true`, and the matching single-use safety token.
- The mandatory live V21 gate stays within spec lines 213-215: real flag read, one flag-only drift on a disposable target, and stale-token rejection with `state_changed`.
- Requested-unavailable-flag rejection is mandatory contract, worker, FakeWorker, and registered-path evidence. A live unavailable-flag probe is optional and may run only when the authorized disposable target actually exposes such a flag-unavailable case.
- Offline, stub, and FakeWorker evidence are necessary but never sufficient for completion; this PR is incomplete without a separately authorized live TIA Portal V21 drift-acceptance run and a durable acceptance report.

---

## File and Interface Map

**Create**

- `TiaMcpServer.Contracts/TagUpdateSafetySnapshot.cs`
- `TiaMcpServer/Batch/TagUpdateSafetyCurrentState.cs`
- `TiaMcpServer.OpennessWorker/Openness/TagTargetResolver.cs`
- `TiaMcpServer.OpennessWorker/Openness/TagUpdateSafetySnapshotReader.cs`
- `TiaMcpServer.Tests/Batch/TagUpdateCurrentStateFakeWorkerTests.cs`
- `TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotContractTests.cs`
- `TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotSourceContractTests.cs`
- `TiaMcpServer.Tests/Batch/TagUpdateSafetyLiveHarnessContractTests.cs`
- `TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotIdentityEnforcementTests.cs`
- `TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotWorkerClientTests.cs`
- `scripts/live-test-update-tag-safety.ps1`
- `docs/superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md`

**Modify**

- `TiaMcpServer.Contracts/OperationCapability.cs`
- `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- `TiaMcpServer.OpennessWorker/Program.cs`
- `TiaMcpServer.OpennessWorker/Openness/TagMutationService.cs`
- `TiaMcpServer/Batch/BatchWorkerInvoker.cs`
- `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- `TiaMcpServer.FakeWorker/Program.cs`
- `docs/ARCHITECTURE.md`
- `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`
- `docs/IMPROVEMENT_LOG.md`
- `docs/README.md`
- `docs/superpowers/README.md`

**Interfaces**

- `OperationCapability.SafetyRead` — side-effect-free internal safety-state read, allowed in read-only mode but always identity-required.
- `public sealed record TagUpdateSafetySnapshot(string PlcName, string FolderPath, string TableName, string TagName, string DataType, string LogicalAddress, bool? ExternalAccessible, bool? ExternalVisible, bool? ExternalWritable);`
- `internal sealed record ResolvedTagTarget(string PlcName, string FolderPath, PlcTagTable Table, PlcTag Tag);`
- `public Task<WorkerCallResult> ReadUpdateTagSafetySnapshotAsync(string? plcName, string tableName, string? folderPath, string name, string? projectPath)`
- `public static TagUpdateSafetySnapshot Read(Project project, string? plcName, string tableName, string? folderPath, string name)`
- `internal static string Compose(TagUpdateSafetySnapshot snapshot, string broadTagTablesPayload)`
- `internal static string? ValidateRequestedExternalFlags(BatchOperationRequest op, TagUpdateSafetySnapshot snapshot)`

`ReadUpdateTagSafetySnapshotAsync` should reuse the ordinary bound host seam:

```csharp
public Task<WorkerCallResult> ReadUpdateTagSafetySnapshotAsync(
    string? plcName,
    string tableName,
    string? folderPath,
    string name,
    string? projectPath)
{
    return SendBoundProjectRequestAsync(
        "read_update_tag_safety_snapshot",
        projectPath,
        request =>
        {
            request.PlcName = plcName;
            request.TableName = tableName;
            request.FolderPath = folderPath;
            request.Name = name;
        },
        "{}");
}
```

`SendBoundProjectRequestCoreAsync` already sets `ExpectedSessionIdentity = bindingBeforeCall.ToWorkerIdentity()` for bound calls, so this PR needs a request-JSON regression proving that the full identity object is present, not a duplicate transport/binding implementation. Worker-side enforcement is a separate boundary: ordinary `Observe` permits a missing identity, therefore the new method must use `SafetyRead` and the shared `RequiresExpectedSessionIdentity` path must reject missing and mismatched identities before the snapshot reader runs.

### Task 1: Bind `update_tag` to the Exact Snapshot End to End

**Files:**

- Create: `TiaMcpServer.Contracts/TagUpdateSafetySnapshot.cs`
- Create: `TiaMcpServer/Batch/TagUpdateSafetyCurrentState.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/TagTargetResolver.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/TagUpdateSafetySnapshotReader.cs`
- Create: `TiaMcpServer.Tests/Batch/TagUpdateCurrentStateFakeWorkerTests.cs`
- Create: `TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotContractTests.cs`
- Create: `TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotSourceContractTests.cs`
- Create: `TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotIdentityEnforcementTests.cs`
- Create: `TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotWorkerClientTests.cs`
- Modify: `TiaMcpServer.Contracts/OperationCapability.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/TagMutationService.cs`
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Test: `TiaMcpServer.Tests/Batch/TagUpdateCurrentStateFakeWorkerTests.cs`
- Test: `TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotContractTests.cs`
- Test: `TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotSourceContractTests.cs`
- Test: `TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotIdentityEnforcementTests.cs`
- Test: `TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotWorkerClientTests.cs`

**Interfaces:**

- Consumes: `WriteBatchTools.PreviewWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations)`
- Consumes: `WriteBatchTools.ApplyWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations, bool confirm = false, string? safetyToken = null)`
- Consumes: `TagMutationService.UpdateTag(Project project, string? plcName, string tableName, string? folderPath, string name, string? newName, string? dataType, string? logicalAddress, bool? externalAccessible, bool? externalVisible, bool? externalWritable, bool? isSafety)`
- Produces: `ResolvedTagTarget TagTargetResolver.Resolve(Project project, string? plcName, string tableName, string? folderPath, string name)`
- Produces: `TagUpdateSafetySnapshotReader.Read(Project project, string? plcName, string tableName, string? folderPath, string name)`
- Produces: `OperationCapability.SafetyRead` and its reusable identity-required policy semantics.
- Produces: `OpennessWorkerClient.ReadUpdateTagSafetySnapshotAsync(string? plcName, string tableName, string? folderPath, string name, string? projectPath)`
- Produces: `TagUpdateSafetyCurrentState.Compose(TagUpdateSafetySnapshot snapshot, string broadTagTablesPayload)`
- Produces: `TagUpdateSafetyCurrentState.ValidateRequestedExternalFlags(BatchOperationRequest op, TagUpdateSafetySnapshot snapshot)`

- [ ] **Step 1: Write the failing registered runtime tests first**

Add focused FakeWorker-backed tests that drive the registered `WriteBatchTools` preview/apply path before touching production contracts or worker code:

```csharp
[Fact]
public async Task PreviewWriteBatch_UpdateTagRejectsRequestedUnavailableFlagBeforeTokenIssuance()
{
    var operations = new[] { UpdateTagOp(externalVisible: true) };

    var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

    Assert.Contains("externalVisible", preview, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("\"safetyToken\":", preview, StringComparison.Ordinal);
}

[Fact]
public async Task ApplyWriteBatch_UpdateTagFlagOnlyDriftFailsWithStateChanged()
{
    var operations = new[] { UpdateTagOp(externalAccessible: true) };
    var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
    var token = JsonDocument.Parse(preview).RootElement.GetProperty("safetyToken").GetString();

    var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);

    Assert.Contains("\"failureCategory\":\"state_changed\"", apply, StringComparison.Ordinal);
}
```

Use two FakeWorker scenarios:

- `tag-update-snapshot-unavailable-visible`: broad `list_tag_tables` succeeds, but the exact target would report `ExternalVisible = null`.
- `tag-update-flag-drift`: preview and apply see the same broad `list_tag_tables` payload while only one exact-target external flag changes between the two calls.

- [ ] **Step 2: Run the registered runtime tests and confirm meaningful RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~TagUpdateCurrentStateFakeWorkerTests"
```

Expected RED:

- preview still issues a token when a requested external flag is unavailable because `update_tag` is bound only to broad `list_tag_tables` state;
- apply does not reject a flag-only drift as `state_changed` because the exact snapshot is not yet part of the bound current-state hash.

- [ ] **Step 3: Write the failing contract, request-JSON, and source-contract tests**

```csharp
[Fact]
public void Snapshot_SerializesFalseDifferentlyFromUnavailable()
{
    var concrete = JsonSerializer.Serialize(new TagUpdateSafetySnapshot(
        "ResolvedPLC", "/", "Default tag table", "MotorReady", "Bool", "%I0.0", false, true, false));
    var unavailable = JsonSerializer.Serialize(new TagUpdateSafetySnapshot(
        "ResolvedPLC", "/", "Default tag table", "MotorReady", "Bool", "%I0.0", null, null, null));

    Assert.Contains("\"externalAccessible\":false", concrete, StringComparison.Ordinal);
    Assert.Contains("\"externalAccessible\":null", unavailable, StringComparison.Ordinal);
}

[Fact]
public async Task BoundSnapshotRead_RequestJsonStillCarriesExpectedSessionIdentity()
{
    var call = await client.ReadUpdateTagSafetySnapshotAsync(
        plcName: null,
        tableName: "Default tag table",
        folderPath: "/",
        name: "MotorReady",
        projectPath: "echo");

    Assert.True(call.Success, call.Error);
    using var echoed = JsonDocument.Parse(call.Payload);
    var expected = echoed.RootElement.GetProperty("expectedSessionIdentity");
    Assert.Equal(JsonValueKind.Object, expected.ValueKind);
    Assert.False(string.IsNullOrWhiteSpace(expected.GetProperty("workerSessionId").GetString()));
    Assert.True(expected.GetProperty("sessionGeneration").GetInt64() >= 0);
    Assert.True(expected.GetProperty("portalProcessId").GetInt32() > 0);
    Assert.False(string.IsNullOrWhiteSpace(expected.GetProperty("projectPath").GetString()));
}

[Fact]
public void SnapshotRead_IsAReusableIdentityRequiredSafetyRead()
{
    const string method = "read_update_tag_safety_snapshot";

    Assert.Equal(OperationCapability.SafetyRead, OperationPolicyCatalog.GetCapability(method));
    Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
    Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
}

[Fact]
public async Task WorkerRejectsSnapshotReadWithMissingIdentity()
{
    using var transport = CreateFakeWorkerTransport();
    var observed = await PrimeAndReadIdentityAsync(transport);
    var response = await transport.SendAsync(new WorkerRequest
    {
        Method = "read_update_tag_safety_snapshot",
        ProjectPath = observed.ProjectPath,
        PlcName = "PLC_1",
        TableName = "Default tag table",
        FolderPath = "/",
        Name = "MotorReady",
        ExpectedSessionIdentity = null
    });

    Assert.False(response.Success);
    Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
}

[Theory]
[InlineData("workerSessionId")]
[InlineData("sessionGeneration")]
[InlineData("portalProcessId")]
[InlineData("projectPath")]
public async Task WorkerRejectsEveryMismatchedSnapshotIdentityField(string field)
{
    using var transport = CreateFakeWorkerTransport();
    var observed = await PrimeAndReadIdentityAsync(transport);
    var response = await transport.SendAsync(new WorkerRequest
    {
        Method = "read_update_tag_safety_snapshot",
        ProjectPath = observed.ProjectPath,
        PlcName = "PLC_1",
        TableName = "Default tag table",
        FolderPath = "/",
        Name = "MotorReady",
        ExpectedSessionIdentity = CopyWithChangedField(observed, field)
    });

    Assert.False(response.Success);
    Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
}

[Fact]
public void Reader_UsesResolvedPlcNameFromLocator()
{
    var source = ReadRepositorySource("TiaMcpServer.OpennessWorker", "Openness", "TagUpdateSafetySnapshotReader.cs");
    Assert.Contains("resolved.PlcName", source, StringComparison.Ordinal);
    Assert.DoesNotContain("plcName ?? string.Empty", source, StringComparison.Ordinal);
}
```

Put the transport rejection tests in `TagUpdateSafetySnapshotIdentityEnforcementTests`; its `CopyWithChangedField` helper must copy all four identity fields and change exactly the selected field. They prove the shared policy through the executable FakeWorker enforcement path; do not substitute a source-text-only assertion for those missing/mismatched requests. Also add source-contract evidence that the real worker's `Program.cs` dispatches `"read_update_tag_safety_snapshot" => ReadUpdateTagSafetySnapshot(request)`, the handler runs through `WithProject(request, ...)`, and `AllowsMissingExpectedSessionIdentity` delegates to `!OperationPolicyCatalog.RequiresExpectedSessionIdentity(method)`. Assert that the internal method is absent from every public batch/network catalog.

- [ ] **Step 4: Run the seam tests and confirm RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~TagUpdateSafetySnapshotContractTests|FullyQualifiedName~TagUpdateSafetySnapshotSourceContractTests|FullyQualifiedName~TagUpdateSafetySnapshotIdentityEnforcementTests|FullyQualifiedName~TagUpdateSafetySnapshotWorkerClientTests"
```

Expected RED:

- `TagUpdateSafetySnapshot` and the new worker method do not exist;
- `OperationCapability.SafetyRead` and the explicit reusable policy do not exist; classifying the method as ordinary `Observe` would make the policy and missing-identity runtime assertions fail;
- the client wrapper does not exist;
- request-JSON evidence for the bound snapshot read cannot pass yet;
- the worker enforcement test cannot accept a missing or mismatched identity while still satisfying the new explicit classification contract;
- the reader cannot serialize resolved PLC identity because the resolver output does not exist yet.

- [ ] **Step 5: Add the reusable safety-read policy, shared contract, and resolved target resolver**

Add the capability once in the shared contract rather than special-casing `read_update_tag_safety_snapshot` inside host or worker transport code:

```csharp
public enum OperationCapability
{
    Observe,
    TemporaryExport,
    SafetyRead,
    // existing capabilities remain unchanged
}

// OperationPolicyCatalog.IsAllowed
return cap.Value is OperationCapability.Observe
    or OperationCapability.TemporaryExport
    or OperationCapability.SafetyRead;

// OperationPolicyCatalog.RequiresExpectedSessionIdentity, after the existing
// open_project/create_project establishment exception
return GetCapability(operation) switch
{
    OperationCapability.Observe => false,
    OperationCapability.TemporaryExport => false,
    OperationCapability.SafetyRead => true,
    _ => true
};
```

Add the method to the `BuildClassifications` dictionary initializer:

```csharp
["read_update_tag_safety_snapshot"] = OperationCapability.SafetyRead,
```

`SafetyRead` means side-effect-free but identity-required. Preserve fail-closed behavior for null, blank, and unknown operations. Do not add a second collection of identity-required method names: PR 5 and PR 6 must classify their internal safety selectors with the same capability.

```csharp
public sealed record TagUpdateSafetySnapshot(
    string PlcName,
    string FolderPath,
    string TableName,
    string TagName,
    string DataType,
    string LogicalAddress,
    bool? ExternalAccessible,
    bool? ExternalVisible,
    bool? ExternalWritable);

internal sealed record ResolvedTagTarget(
    string PlcName,
    string FolderPath,
    PlcTagTable Table,
    PlcTag Tag);

internal static class TagTargetResolver
{
    internal static ResolvedTagTarget Resolve(
        Project project,
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        PlcSoftware plcSoftware = PlcSoftwareLocator.Find(project, plcName);
        PlcTagTableGroup group = plcSoftware.TagTableGroup;
        foreach (var segment in SplitFolderPath(folderPath))
        {
            group = group.Groups.Find(segment)
                ?? throw new InvalidOperationException($"Tag table folder '{NormalizeFolderPath(folderPath)}' was not found.");
        }

        var table = group.TagTables.Find(tableName)
            ?? throw new InvalidOperationException($"Tag table '{tableName}' was not found in '{NormalizeFolderPath(folderPath)}'.");
        var tag = table.Tags.Find(name)
            ?? throw new InvalidOperationException($"Tag '{name}' was not found in tag table '{tableName}'.");

        return new ResolvedTagTarget(plcSoftware.Name, NormalizeFolderPath(folderPath), table, tag);
    }
}
```

Refactor `TagMutationService` to reuse `TagTargetResolver` so the write path and strict snapshot path share exact PLC, folder, table, and tag resolution.

- [ ] **Step 6: Implement the worker dispatch, reader, and ordinary bound client wrapper**

```csharp
private static WorkerResponse ReadUpdateTagSafetySnapshot(WorkerRequest request)
{
    return WithProject(request, project => Success(
        TagUpdateSafetySnapshotReader.Read(
            project,
            request.PlcName,
            request.TableName!,
            request.FolderPath,
            request.Name!)));
}

public static TagUpdateSafetySnapshot Read(
    Project project,
    string? plcName,
    string tableName,
    string? folderPath,
    string name)
{
    var resolved = TagTargetResolver.Resolve(project, plcName, tableName, folderPath, name);
    return new TagUpdateSafetySnapshot(
        resolved.PlcName,
        resolved.FolderPath,
        resolved.Table.Name,
        resolved.Tag.Name,
        resolved.Tag.DataTypeName,
        resolved.Tag.LogicalAddress,
        ReadOptionalFlag(() => resolved.Tag.ExternalAccessible),
        ReadOptionalFlag(() => resolved.Tag.ExternalVisible),
        ReadOptionalFlag(() => resolved.Tag.ExternalWritable));
}
```

`ReadOptionalFlag` must return `null` only when the selected live tag truly does not expose the property; it must never degrade by skipping the target or falling back to broad state.

Keep the handler on `WithProject(request, ...)`. That shared path evaluates `AllowsMissingExpectedSessionIdentity`, which now resolves `SafetyRead` to `false`, and therefore rejects a missing or mismatched identity before `TagUpdateSafetySnapshotReader.Read` reaches Siemens objects. Do not add handler-local validation or bypass the central policy.

- [ ] **Step 7: Compose the strict snapshot with broad state only for `update_tag`**

```csharp
internal static class TagUpdateSafetyCurrentState
{
    internal static string? ValidateRequestedExternalFlags(BatchOperationRequest op, TagUpdateSafetySnapshot snapshot)
    {
        if (op.ExternalAccessible.HasValue && snapshot.ExternalAccessible is null) return "externalAccessible";
        if (op.ExternalVisible.HasValue && snapshot.ExternalVisible is null) return "externalVisible";
        if (op.ExternalWritable.HasValue && snapshot.ExternalWritable is null) return "externalWritable";
        return null;
    }

    internal static string Compose(TagUpdateSafetySnapshot snapshot, string broadTagTablesPayload)
        => JsonSerializer.Serialize(new
        {
            exactTarget = snapshot,
            broadTagTables = JsonDocument.Parse(broadTagTablesPayload).RootElement
        });
}
```

Route only `update_tag` through that helper:

```csharp
"update_tag" => ReadUpdateTagCurrentStateAsync(client, op),
"create_tag_table" or "delete_tag_table"
    or "create_tag" or "delete_tag"
    or "create_user_constant" or "update_user_constant" or "delete_user_constant"
    => client.ListTagTablesAsync(op.PlcName, op.ProjectPath),
```

`ReadUpdateTagCurrentStateAsync` must:

- read the strict exact-target snapshot first;
- fail closed if that read fails;
- reject a requested external flag whose current value is `null`;
- read the existing broad `list_tag_tables` payload second;
- return one composed deterministic state string for token issuance/validation.

- [ ] **Step 8: Extend FakeWorker to express the new behavior**

Add scripted responses for:

- `read_update_tag_safety_snapshot` success with resolved PLC name and explicit `false`/`true` flag values;
- shared identity enforcement before scenario dispatch, so the safety read returns `binding_conflict` with no stamped response identity when `ExpectedSessionIdentity` is absent or differs in any binding field;
- `tag-update-snapshot-unavailable-visible` where `externalVisible` is omitted or `null` in the strict snapshot while broad `list_tag_tables` still succeeds;
- `tag-update-flag-drift` where only one strict-snapshot flag changes between preview and apply and the broad payload stays byte-stable;
- `tag-update-snapshot-read-fails` where the exact-target read returns a structural failure and preview issues no token;
- `tag-update-broad-best-effort-omission` where the broad `list_tag_tables` payload carries unrelated warnings but the strict target snapshot succeeds.

- [ ] **Step 9: Re-run focused tests and confirm GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~TagUpdateCurrentStateFakeWorkerTests|FullyQualifiedName~TagUpdateSafetySnapshotContractTests|FullyQualifiedName~TagUpdateSafetySnapshotSourceContractTests|FullyQualifiedName~TagUpdateSafetySnapshotIdentityEnforcementTests|FullyQualifiedName~TagUpdateSafetySnapshotWorkerClientTests"
```

Then run the broader slice:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~BatchFieldForwardingTests|FullyQualifiedName~BatchSafetyTokenTests|FullyQualifiedName~WriteSafetyServiceTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~TagUpdateCurrentStateFakeWorkerTests|FullyQualifiedName~TagUpdateSafetySnapshotContractTests|FullyQualifiedName~TagUpdateSafetySnapshotSourceContractTests|FullyQualifiedName~TagUpdateSafetySnapshotIdentityEnforcementTests|FullyQualifiedName~TagUpdateSafetySnapshotWorkerClientTests"
```

- [ ] **Step 10: Run full serial repository verification**

Run:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
git diff --check
git status --short
```

- [ ] **Step 11: Review the PR 3 boundary before any commit**

Verify all of these are true:

- the first observed RED came from the registered `WriteBatchTools` preview/apply path, not only from missing symbols;
- `SafetyRead` is a reusable shared capability, remains side-effect-free/read-only-safe, and requires expected session identity without a per-method allow/deny list;
- the bound host wrapper sends all expected identity fields, while executable worker-policy tests reject both missing and mismatched identity as `binding_conflict` before the reader runs;
- `update_tag` now binds resolved PLC identity, folder path, table name, tag name, data type, logical address, and the three external flags;
- `false` and `null` remain distinguishable in both serialization and current-state hashing;
- requested-unavailable-flag rejection is proven through contract, worker, FakeWorker, and registered-path evidence;
- `list_tag_tables` public behavior and best-effort skips are unchanged;
- no other tag operation is narrowed yet;
- PR 5 remains responsible for removing the broad composed state and deduplicating selectors.

- [ ] **Step 12: Stop at the commit boundary**

Do not commit without explicit authorization. When authorized, stage only:

```powershell
git add TiaMcpServer.Contracts/TagUpdateSafetySnapshot.cs TiaMcpServer.Contracts/OperationCapability.cs TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.OpennessWorker/Openness/TagTargetResolver.cs TiaMcpServer.OpennessWorker/Openness/TagUpdateSafetySnapshotReader.cs TiaMcpServer.OpennessWorker/Openness/TagMutationService.cs TiaMcpServer/Batch/TagUpdateSafetyCurrentState.cs TiaMcpServer/Batch/BatchWorkerInvoker.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Batch/TagUpdateCurrentStateFakeWorkerTests.cs TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotContractTests.cs TiaMcpServer.Tests/Batch/TagUpdateSafetySnapshotSourceContractTests.cs TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotIdentityEnforcementTests.cs TiaMcpServer.Tests/Worker/TagUpdateSafetySnapshotWorkerClientTests.cs
```

### Task 2: Add the Guarded Live Harness, Docs, and Durable Acceptance Report

**Files:**

- Create: `TiaMcpServer.Tests/Batch/TagUpdateSafetyLiveHarnessContractTests.cs`
- Create: `scripts/live-test-update-tag-safety.ps1`
- Create: `docs/superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`
- Modify: `docs/IMPROVEMENT_LOG.md`
- Modify: `docs/README.md`
- Modify: `docs/superpowers/README.md`
- Test: `TiaMcpServer.Tests/Batch/TagUpdateSafetyLiveHarnessContractTests.cs`

**Interfaces:**

- Consumes: `read_update_tag_safety_snapshot` (worker-level exact-target preflight)
- Consumes: `get_project_status` (worker session-identity establishment before a direct internal safety read)
- Consumes: `list_tag_tables`
- Consumes: `preview_write_batch`
- Consumes: `apply_write_batch`
- Produces: `scripts/live-test-update-tag-safety.ps1` modes `Read`, `PreviewDrift`, `ApplyDrift`, and optional `ProbeUnavailable`
- Produces: `docs/superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md`

- [ ] **Step 1: Write the failing live-harness contract tests**

```csharp
[Fact]
public void Script_DefaultModeIsReadOnly()
{
    var text = ReadScript();
    Assert.Matches(new Regex(@"\[ValidateSet\(\s*'Read'\s*,\s*'PreviewDrift'\s*,\s*'ApplyDrift'\s*,\s*'ProbeUnavailable'\s*\)\]"), text);
    Assert.Matches(new Regex(@"\[string\]\s*\$Mode\s*=\s*'Read'"), text);
}

[Fact]
public void Script_ApplyDriftRequiresExplicitAuthorizationAndPreflightedReadableFlag()
{
    var text = ReadScript();
    Assert.Matches(new Regex(@"\[switch\]\s*\$AllowApply"), text);
    Assert.Contains("$DriftFlagName", text, StringComparison.Ordinal);
    Assert.Contains("read_update_tag_safety_snapshot", text, StringComparison.Ordinal);
    Assert.Contains("state_changed", text, StringComparison.Ordinal);
}

[Fact]
public void Script_InternalSafetyReadCarriesObservedSessionIdentity()
{
    var text = ReadScript();
    Assert.Contains("get_project_status", text, StringComparison.Ordinal);
    Assert.Contains("sessionIdentity", text, StringComparison.Ordinal);
    Assert.Contains("expectedSessionIdentity", text, StringComparison.Ordinal);
    Assert.Contains("read_update_tag_safety_snapshot", text, StringComparison.Ordinal);
}

[Fact]
public void Script_OptionalUnavailableProbeUsesSeparateTargetInputs()
{
    var text = ReadScript();
    Assert.Contains("$ProbeTagName", text, StringComparison.Ordinal);
    Assert.Contains("$ProbeFlagName", text, StringComparison.Ordinal);
    Assert.Contains("'ProbeUnavailable'", text, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused tests and confirm RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~TagUpdateSafetyLiveHarnessContractTests"
```

Expected RED:

- the harness script and contract tests do not exist;
- there is no explicit mandatory drift run and no separate optional unavailable probe shape.

- [ ] **Step 3: Implement the harness with exact preflight and separate live scopes**

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [Parameter(Mandatory)][string]$TableName,
    [Parameter(Mandatory)][string]$TagName,
    [string]$PlcName,
    [ValidateSet('ExternalAccessible','ExternalVisible','ExternalWritable')][string]$DriftFlagName = 'ExternalVisible',
    [string]$ProbeTableName,
    [string]$ProbeTagName,
    [ValidateSet('ExternalAccessible','ExternalVisible','ExternalWritable')][string]$ProbeFlagName = 'ExternalVisible',
    [ValidateSet('Read','PreviewDrift','ApplyDrift','ProbeUnavailable')][string]$Mode = 'Read',
    [switch]$AllowApply
)
```

Harness behavior:

- Shared worker preflight: after the protocol handshake, call `get_project_status`, require a complete returned `sessionIdentity`, and copy that exact object into `expectedSessionIdentity` on every direct `read_update_tag_safety_snapshot` request. Never synthesize identity from `ProjectPath` and never retry the internal safety read without identity.
- `Read`: use the identity-bound helper to call `read_update_tag_safety_snapshot`, assert the resolved PLC name is present, assert `$DriftFlagName` is readable (`$null` is a hard live-gate failure for the drift run), then print the unchanged public `list_tag_tables` row.
- `PreviewDrift`: repeat the exact-target preflight, call `preview_write_batch` for an `update_tag` that changes only `$DriftFlagName`, and print the token plus the strict snapshot values that backed it.
- `ApplyDrift`: require `-AllowApply`, repeat the readable-flag preflight, issue the preview token, perform one separately authorized intermediate `update_tag` flag change against the disposable copy, call `apply_write_batch` with the original unchanged request, assert `failureCategory = state_changed`, then restore the original flag or discard the copy and print the final strict snapshot.
- `ProbeUnavailable`: optional only. Require `$ProbeTableName`, `$ProbeTagName`, and `$ProbeFlagName`, call `read_update_tag_safety_snapshot` for that second target, and stop with a non-passing result unless the selected flag is actually `null`. Only then attempt the preview that must fail before token issuance.

The harness may use the worker executable for the internal strict snapshot read and the real `TiaMcpServer` MCP JSON-RPC host for `list_tag_tables`, `preview_write_batch`, and `apply_write_batch`. A direct worker request is valid only after the harness has captured the worker-stamped session identity and supplied it unchanged as `expectedSessionIdentity`; the automated TDD suite, not a source inspection, separately proves missing/mismatched rejection. The harness must never claim the optional unavailable probe as part of the mandatory live gate.

- [ ] **Step 4: Make the live commands exact**

Mandatory preflight:

```powershell
pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode Read
```

Mandatory preview:

```powershell
pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode PreviewDrift
```

Mandatory drift acceptance:

```powershell
pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode ApplyDrift -AllowApply
```

Optional unavailable probe, only when an authorized second target actually exposes an unavailable flag:

```powershell
pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -ProbeTableName 'Legacy table' -ProbeTagName 'LegacyTag' -ProbeFlagName ExternalWritable -Mode ProbeUnavailable
```

- [ ] **Step 5: Write the durable acceptance report with mandatory and optional evidence separated**

Create `docs/superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md` with this exact structure:

```markdown
# Acceptance Test Report - PR 3 update_tag safety snapshot (live TIA Portal V21)

**Date:** 2026-09-01
**Runtime:** Real TIA Portal V21
**Harness:** `scripts/live-test-update-tag-safety.ps1`
**Boundary:** Disposable project copy; the mandatory live gate is exact-target read plus one authorized flag-only drift and stale-token rejection.

## Mandatory Live Target

- Project copy path
- Requested `plcName` input
- Resolved PLC name from `read_update_tag_safety_snapshot`
- Tag table name
- Tag name
- Drift flag proved

## Mandatory Calls Performed

- `get_project_status` identity establishment for the worker-level preflight
- `read_update_tag_safety_snapshot` preflight on the drift target
- `list_tag_tables` comparison read
- `preview_write_batch`
- one authorized intermediate flag-only drift write on the disposable copy
- stale-token `apply_write_batch`
- restoration or discard step

## Mandatory Live Results

| Criterion | Command | Observed |
|---|---|---|
| Identity-bound internal safety read succeeds with the worker-stamped identity | `... -Mode Read` | PASS/FAIL |
| Exact-target snapshot resolves PLC identity and the chosen drift flag is readable | `... -Mode Read` | PASS/FAIL |
| Flag-only drift causes stale-token `state_changed` before mutation | `... -Mode ApplyDrift -AllowApply` | PASS/FAIL |
| Public `list_tag_tables` semantics remain unchanged | `... -Mode Read` | PASS/FAIL |

## Required Offline and Registered Evidence

| Criterion | Evidence source | Observed |
|---|---|---|
| Bound client sends a complete expected identity; worker rejects missing and mismatched identity as `binding_conflict` | `TagUpdateSafetySnapshotWorkerClientTests` plus `TagUpdateSafetySnapshotIdentityEnforcementTests` | PASS/FAIL |
| Requested unavailable flag fails before token issuance | `TagUpdateCurrentStateFakeWorkerTests` plus contract/worker tests | PASS/FAIL |

## Optional Live Unavailable Probe

| Criterion | Command | Observed |
|---|---|---|
| Second target exposes an unavailable flag and preview rejects it before token issuance | `... -Mode ProbeUnavailable` | PASS/FAIL/NOT RUN |

If the authorized disposable target does not expose an unavailable flag for the chosen second target, record `NOT RUN` here and keep the acceptance gate anchored to the mandatory live rows plus the required offline/registered evidence.
```

- [ ] **Step 6: Update current docs and indexes**

Apply these documentation changes:

- `docs/ARCHITECTURE.md`: document that `update_tag` now binds one strict exact-target snapshot plus the legacy broad tag-table payload until PR 5, that the snapshot records resolved PLC identity, and that reusable `SafetyRead` methods are side-effect-free/read-only-safe but require the exact expected session identity.
- `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`: document that `update_tag` preview fails before token issuance when a requested external flag is unreadable, while `list_tag_tables` remains best-effort and unchanged.
- `docs/IMPROVEMENT_LOG.md`: close the missing external-flag safety snapshot gap and keep PR 5 narrowing, multilingual per-tag comment binding, and PLC start/stop work deferred.
- `docs/README.md` and `docs/superpowers/README.md`: add the new plan/report references so the artifacts are discoverable.

- [ ] **Step 7: Re-run the harness contract test and full serial verification**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~TagUpdateSafetyLiveHarnessContractTests"
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
git diff --check
git status --short
```

- [ ] **Step 8: Run the separately authorized live V21 acceptance**

Run only after explicit authorization, in this order:

1. Mandatory preflight `-Mode Read`
2. Mandatory preview `-Mode PreviewDrift`
3. Mandatory drift acceptance `-Mode ApplyDrift -AllowApply`
4. Optional `-Mode ProbeUnavailable` only if an authorized second target actually exposes an unavailable flag

The live gate fails if:

- the worker-level preflight cannot establish a complete session identity or the identity-bound safety read does not succeed;
- the mandatory drift target does not expose the chosen drift flag as readable;
- the stale apply does not fail with `state_changed`;
- the disposable target is neither restored nor explicitly discarded and recorded.

The live gate does not fail merely because no authorized second target exposes an unavailable flag; in that case the optional live probe is `NOT RUN` and the unavailable-flag behavior stays proven by required offline and registered evidence.

- [ ] **Step 9: Stop at the commit boundary**

Do not commit without explicit authorization. When authorized, stage only:

```powershell
git add scripts/live-test-update-tag-safety.ps1 TiaMcpServer.Tests/Batch/TagUpdateSafetyLiveHarnessContractTests.cs docs/ARCHITECTURE.md docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md docs/IMPROVEMENT_LOG.md docs/README.md docs/superpowers/README.md docs/superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md
```

## Deferred and Out of Scope

- No whole-PLC strict tag scan in PR 3. Keep using `ListTagTablesAsync` broad state composition for `update_tag` until PR 5 replaces it with scoped collision selectors.
- No public `list_tag_tables` schema or behavior change. Best-effort skipped tag or user-constant reads remain public read semantics, not preview failures by themselves.
- No multilingual per-tag comment binding work in this PR. That remains deferred for future `delete_tag` hardening.
- No selector narrowing for `create_tag`, `delete_tag`, tag tables, or user constants beyond what this PR needs for `update_tag`.
- `SafetyRead` lands here as reusable infrastructure only. PR 5 and PR 6 remain responsible for defining and classifying their own scoped selector methods; this PR must not implement those selectors.
- No PLC `start_plc` / `stop_plc` work.
- No acceptance claim based only on offline, stub, or FakeWorker evidence.
- No commit, push, or live mutation without explicit authorization for the exact target and command.
