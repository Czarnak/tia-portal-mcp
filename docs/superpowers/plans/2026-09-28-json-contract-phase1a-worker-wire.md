# JSON Contract Phase 1a: Worker Wire Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Define the host-worker wire format once in `TiaMcpServer.Contracts`. Make the worker's per-type null policy declarative, give the FakeWorker the production policy, and decode the rebind-state payload strictly. The real worker's output bytes stay the same.

**Architecture:**
- A new `WorkerJson` class holds the envelope options and renders payloads.
- A payload contract writes explicit nulls unless it carries `[LegacyNullOmission(reason)]`. Every type that omits nulls today gets that marker, with the reason it has not switched.
- The worker, `PersistentWorkerTransport` and the FakeWorker all use `WorkerJson`.
- `probe_open_project_rebind` payloads decode through one strict typed contract.

**Tech Stack:** C# (net10.0 host, net48 worker, netstandard2.0 Contracts), System.Text.Json 10.0.12, xUnit 2.9.3.

## Global Constraints

- Roadmap: `docs/roadmap/json-contract.md`, Phase 1. This plan is Phase **1a**. Phase 1b is a separate PR.
- User decisions (2026-09-28):
  - Batch-only payload types keep their current null behavior until the batch redesign.
  - Payload types behind `get_project_status`, `compile_check` and the lifecycle tools keep it until their Phase 2/3 migration.
  - Phase 1 ships as two PRs.
- **The real worker's payload and envelope bytes must not change in 1a.** Explicit-null payload types stay exactly `NetworkObjectListInfo`, `ProjectTreeBrowseResultInfo` and `ProjectRebindStateInfo`. Every other `Success<T>` root keeps omitting nulls.
- No MCP tool input schema, output schema or response shape changes.
- Build: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`. Test: `dotnet test TiaMcpServer.Tests`. Run `dotnet` commands with `/p:` flags from PowerShell, not Bash.
- `TiaMcpServer.Tests` links host and worker sources through `<Compile Include>`. A new host file under `TiaMcpServer/Worker/` must be added to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Commit messages are one-line conventional commits.
- Baseline at `cf32acb`: build 0 errors (7 existing xUnit2031 warnings). Tests 3,836: in the full parallel run, 4 `DoctorPackageVerificationScriptTests` cases fail; all 23 pass when that class is run alone. Treat those four as environmental and re-check them in isolation.

## File Structure

| File | Responsibility |
| --- | --- |
| Create `TiaMcpServer.Contracts/WorkerJson.cs` | Envelope options and payload rendering: the only worker-wire serializer policy |
| Create `TiaMcpServer.Contracts/LegacyNullOmissionAttribute.cs` | Marker and reason enum for payload contracts that still omit null members |
| Modify 16 Contracts DTO files and 3 worker files | Add `[LegacyNullOmission(...)]` to the 28 payload roots that omit nulls today |
| Modify `TiaMcpServer.OpennessWorker/Program.cs` | Use `WorkerJson` for the envelope and `Success<T>`; delete the two options fields and the type check |
| Modify `TiaMcpServer.Tests/TestUtilities/WorkerSerializationHarness.targets` | Stop requiring options fields in the worker's `Program.cs` |
| Modify `TiaMcpServer/Worker/PersistentWorkerTransport.cs` | Use `WorkerJson.Envelope` |
| Modify `TiaMcpServer.FakeWorker/Program.cs` | Use `WorkerJson` for request parsing, the hello envelope and every DTO fixture |
| Create `TiaMcpServer/Worker/ProjectRebindStatePayloadContract.cs` | Strict typed decode of `probe_open_project_rebind` payloads |
| Modify `TiaMcpServer.Contracts/ProjectRebindStateInfo.cs` | `[JsonRequired]` on all five members |
| Modify `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer/Tools/ProjectWriteTools.cs` | Use the rebind contract instead of untyped and lenient decodes |
| Create tests under `TiaMcpServer.Tests/Worker/` | `WorkerJsonTests`, `WorkerPayloadNullPolicyRegisterTests`, `FakeWorkerWireParityTests`, `ProjectRebindStatePayloadContractTests` |
| Modify 8 test files | Replace hand-copied worker options with `WorkerJson` |
| Modify docs | Roadmap Phase 1a/1b, ARCHITECTURE §6, `TiaJson.cs` comment, AGENTS.md, plan indexes |

Deliberately not touched in 1a:
- `NetworkObjectCursorCodec` (cursor token format; cursor normalization is separate).
- `IoSystemQualificationEvidence` (temporary Phase 5 probe with its own bounded serializer).
- `CompileReportProjection.ReportJson` (a conservative size estimator, not a wire serializer).
- The three hand-written `RequireMembers` validators (Phase 1b).

## Payload root inventory

Every `Success<T>` call site in the worker's `Program.cs` at `cf32acb` (37 sites) and its policy:

| Reason | Types |
| --- | --- |
| Explicit nulls (unmarked) | `NetworkObjectListInfo`, `ProjectTreeBrowseResultInfo`, `ProjectRebindStateInfo` |
| `BatchRedesign` | `CreateBlockSafetySnapshotInfo`, `CreateBlockGroupSafetySnapshotInfo`, `DeleteBlockGroupSafetySnapshotInfo`, `CreateTagTableSafetySnapshotInfo`, `DeleteTagTableSafetySnapshotInfo`, `CreateTagSafetySnapshotInfo`, `UpdateTagSafetySnapshotInfo`, `DeleteTagSafetySnapshotInfo`, `CreateUserConstantSafetySnapshotInfo`, `UpdateUserConstantSafetySnapshotInfo`, `DeleteUserConstantSafetySnapshotInfo`, `CrossReferenceReport`, `TagTableInfo` (list element), `TagMutationResultInfo`, `BlockMutationResultInfo`, `PlcOnlineResultInfo`, worker `PlcTypeImportResult` |
| `ToolMigration` | `CompileCheckReport`, `ProjectLifecycleResultInfo` |
| `RequiredMemberEnforcement` | `HardwareConfigInfo`, `HardwarePageCandidateResultInfo`, `NetworkObjectInspectionInfo`, `CatalogEntryInfo` (list element), `AddDeviceResultInfo`, `ConfigureNetworkDeviceResultInfo`, `SubnetLifecycleResultInfo`, worker `NetworkAttributeProbeInfo`, worker `SubnetLifecycleMutationProbeResult` |

---

### Task 1: WorkerJson and the legacy null-omission marker

**Files:**
- Create: `TiaMcpServer.Contracts/WorkerJson.cs`
- Create: `TiaMcpServer.Contracts/LegacyNullOmissionAttribute.cs`
- Test: `TiaMcpServer.Tests/Worker/WorkerJsonTests.cs`

**Interfaces:**
- Produces:
  - `WorkerJson.Envelope`, `WorkerJson.ExplicitNullPayload`, `WorkerJson.LegacyNullOmittingPayload` (all `JsonSerializerOptions`);
  - `WorkerJson.SerializePayload<T>(T payload) : string`;
  - `WorkerJson.PayloadOptionsFor(Type payloadType) : JsonSerializerOptions`;
  - `WorkerJson.OmitsNullMembers(Type payloadType) : bool`;
  - `LegacyNullOmissionAttribute(LegacyNullOmissionReason reason)` with a `Reason` property;
  - `enum LegacyNullOmissionReason { BatchRedesign, ToolMigration, RequiredMemberEnforcement }`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// WorkerJson is the only worker-wire serializer policy. A payload contract writes null members
/// unless it carries <see cref="LegacyNullOmissionAttribute"/>; a collection payload follows its
/// element contract; the envelope reads case-insensitively and omits null members.
/// </summary>
public sealed class WorkerJsonTests
{
    [Fact]
    public void SerializePayload_WritesNullMembersOfAnUnmarkedContract()
    {
        var json = WorkerJson.SerializePayload(new UnmarkedPayload { Name = "a" });

        Assert.Equal("""{"name":"a","optional":null}""", json);
    }

    [Fact]
    public void SerializePayload_OmitsNullMembersOfAMarkedContract()
    {
        var json = WorkerJson.SerializePayload(new MarkedPayload { Name = "a" });

        Assert.Equal("""{"name":"a"}""", json);
    }

    [Fact]
    public void SerializePayload_AppliesTheElementPolicyToCollections()
    {
        Assert.Equal(
            """[{"name":"a"}]""",
            WorkerJson.SerializePayload(new List<MarkedPayload> { new() { Name = "a" } }));
        Assert.Equal(
            """[{"name":"a","optional":null}]""",
            WorkerJson.SerializePayload(new[] { new UnmarkedPayload { Name = "a" } }));
    }

    [Fact]
    public void SerializePayload_UsesTheRuntimeTypeOfTheRoot()
    {
        object payload = new MarkedPayload { Name = "a" };

        Assert.Equal("""{"name":"a"}""", WorkerJson.SerializePayload(payload));
    }

    [Fact]
    public void Envelope_ReadsCaseInsensitivelyAndOmitsNullMembersOnWrite()
    {
        var response = JsonSerializer.Deserialize<WorkerResponse>(
            """{"Success":true,"PAYLOAD":"{}"}""",
            WorkerJson.Envelope)!;

        Assert.True(response.Success);
        Assert.Equal("{}", response.Payload);
        Assert.Equal(
            """{"success":true,"payload":"{}"}""",
            JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = "{}" }, WorkerJson.Envelope));
    }

    private sealed class UnmarkedPayload
    {
        public string Name { get; set; } = string.Empty;

        public string? Optional { get; set; }
    }

    [LegacyNullOmission(LegacyNullOmissionReason.BatchRedesign)]
    private sealed class MarkedPayload
    {
        public string Name { get; set; } = string.Empty;

        public string? Optional { get; set; }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerJsonTests"`
Expected: build error CS0103/CS0246, because `WorkerJson` and `LegacyNullOmission` do not exist.

- [ ] **Step 3: Write the attribute**

`TiaMcpServer.Contracts/LegacyNullOmissionAttribute.cs`:

```csharp
using System;

namespace TiaMcpServer.Contracts;

/// <summary>Why a worker payload contract still omits null members on the wire.</summary>
public enum LegacyNullOmissionReason
{
    /// <summary>Consumed only by the batch tools, which keep their output until the batch redesign.</summary>
    BatchRedesign,

    /// <summary>Returned by a tool still on the legacy text contract; switches when that tool migrates (roadmap Phases 2-3).</summary>
    ToolMigration,

    /// <summary>A network payload; switches together with required-member enforcement (roadmap Phase 1b).</summary>
    RequiredMemberEnforcement,
}

/// <summary>
/// Marks a worker payload contract that still omits null members on the wire. Unmarked contracts
/// write every member, including nulls (docs/roadmap/json-contract.md). Remove the marker when its
/// reason is resolved; never add one without a reason from <see cref="LegacyNullOmissionReason"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class LegacyNullOmissionAttribute : Attribute
{
    public LegacyNullOmissionAttribute(LegacyNullOmissionReason reason) => Reason = reason;

    public LegacyNullOmissionReason Reason { get; }
}
```

- [ ] **Step 4: Write WorkerJson**

`TiaMcpServer.Contracts/WorkerJson.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

/// <summary>
/// The single definition of the host-worker wire format.
///
/// <para>
/// <see cref="Envelope"/> covers <see cref="WorkerRequest"/> and <see cref="WorkerResponse"/> in
/// both directions. <see cref="SerializePayload{T}"/> renders the <see cref="WorkerResponse.Payload"/>
/// document: members are written even when null, unless the payload contract carries
/// <see cref="LegacyNullOmissionAttribute"/>. A collection payload follows its element contract.
/// </para>
/// </summary>
public static class WorkerJson
{
    /// <summary>Request and response envelope, both directions: camelCase, case-insensitive read, null members omitted.</summary>
    public static JsonSerializerOptions Envelope { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    /// <summary>Payload options for unmarked contracts: every member is written, null or not.</summary>
    public static JsonSerializerOptions ExplicitNullPayload { get; } = Create(JsonIgnoreCondition.Never);

    /// <summary>Payload options for contracts marked with <see cref="LegacyNullOmissionAttribute"/>.</summary>
    public static JsonSerializerOptions LegacyNullOmittingPayload { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    public static string SerializePayload<T>(T payload)
        => JsonSerializer.Serialize(payload, PayloadOptionsFor(payload?.GetType() ?? typeof(T)));

    public static JsonSerializerOptions PayloadOptionsFor(Type payloadType)
        => OmitsNullMembers(payloadType) ? LegacyNullOmittingPayload : ExplicitNullPayload;

    public static bool OmitsNullMembers(Type payloadType)
    {
        if (payloadType is null)
        {
            throw new ArgumentNullException(nameof(payloadType));
        }

        return ContractType(payloadType)
            .GetCustomAttribute<LegacyNullOmissionAttribute>(inherit: false) is not null;
    }

    private static Type ContractType(Type type)
    {
        if (type == typeof(string))
        {
            return type;
        }

        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        var enumerable = new[] { type }
            .Concat(type.GetInterfaces())
            .FirstOrDefault(candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0] ?? type;
    }

    private static JsonSerializerOptions Create(JsonIgnoreCondition nullMembers)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = nullMembers
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerJsonTests"`
Expected: 5 passed.

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer.Contracts/WorkerJson.cs TiaMcpServer.Contracts/LegacyNullOmissionAttribute.cs TiaMcpServer.Tests/Worker/WorkerJsonTests.cs
git commit -m "feat(worker): add shared worker wire JSON policy"
```

---

### Task 2: Mark every payload contract that omits nulls today

**Files:**
- Modify (add one attribute line above each type): `TiaMcpServer.Contracts/ProjectTreeSafetySnapshotInfo.cs`, `TagOperationSafetySnapshotInfo.cs`, `CrossReferenceReport.cs`, `TagTableInfo.cs`, `TagMutationResultInfo.cs`, `BlockMutationResultInfo.cs`, `PlcOnlineResultInfo.cs`, `CompileCheckReport.cs`, `ProjectLifecycleResultInfo.cs`, `HardwareConfigInfo.cs`, `HardwarePageCandidateInfo.cs`, `NetworkObjectInspectionInfo.cs`, `CatalogEntryInfo.cs`, `AddDeviceResultInfo.cs`, `ConfigureNetworkDeviceResultInfo.cs`, `SubnetLifecycleResultInfo.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs` (`PlcTypeImportResult`), `TiaMcpServer.OpennessWorker/NetworkAttributeProbeInfo.cs` (`NetworkAttributeProbeInfo`), `TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleMutationProbeService.cs` (`SubnetLifecycleMutationProbeResult`)
- Test: `TiaMcpServer.Tests/Worker/WorkerPayloadNullPolicyRegisterTests.cs`

**Interfaces:**
- Consumes: `LegacyNullOmissionAttribute`, `LegacyNullOmissionReason` from Task 1.
- Produces: the 28 marked payload contracts listed in "Payload root inventory".

- [ ] **Step 1: Write the failing register test**

```csharp
using System.Reflection;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// Register of the Contracts payload types that still omit null members on the wire, with the
/// reason each has not switched (docs/roadmap/json-contract.md). The register only shrinks:
/// Phase 1b removes the RequiredMemberEnforcement entries, Phases 2-3 the ToolMigration entries,
/// and the batch redesign the rest. Adding or removing a marker without updating it fails here, so
/// a wire-policy change is always a reviewed change. The three worker-assembly contracts
/// (PlcTypeImportResult, NetworkAttributeProbeInfo, SubnetLifecycleMutationProbeResult) are not
/// visible to this assembly and are listed in the plan instead.
/// </summary>
public sealed class WorkerPayloadNullPolicyRegisterTests
{
    private static readonly IReadOnlyDictionary<string, LegacyNullOmissionReason> Expected =
        new Dictionary<string, LegacyNullOmissionReason>(StringComparer.Ordinal)
        {
            ["CreateBlockSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["CreateBlockGroupSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["DeleteBlockGroupSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["CreateTagTableSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["DeleteTagTableSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["CreateTagSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["UpdateTagSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["DeleteTagSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["CreateUserConstantSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["UpdateUserConstantSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["DeleteUserConstantSafetySnapshotInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["CrossReferenceReport"] = LegacyNullOmissionReason.BatchRedesign,
            ["TagTableInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["TagMutationResultInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["BlockMutationResultInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["PlcOnlineResultInfo"] = LegacyNullOmissionReason.BatchRedesign,
            ["CompileCheckReport"] = LegacyNullOmissionReason.ToolMigration,
            ["ProjectLifecycleResultInfo"] = LegacyNullOmissionReason.ToolMigration,
            ["HardwareConfigInfo"] = LegacyNullOmissionReason.RequiredMemberEnforcement,
            ["HardwarePageCandidateResultInfo"] = LegacyNullOmissionReason.RequiredMemberEnforcement,
            ["NetworkObjectInspectionInfo"] = LegacyNullOmissionReason.RequiredMemberEnforcement,
            ["CatalogEntryInfo"] = LegacyNullOmissionReason.RequiredMemberEnforcement,
            ["AddDeviceResultInfo"] = LegacyNullOmissionReason.RequiredMemberEnforcement,
            ["ConfigureNetworkDeviceResultInfo"] = LegacyNullOmissionReason.RequiredMemberEnforcement,
            ["SubnetLifecycleResultInfo"] = LegacyNullOmissionReason.RequiredMemberEnforcement,
        };

    [Fact]
    public void ContractsMarkedToOmitNullMembers_AreExactlyTheRegisteredSet()
    {
        var marked = typeof(WorkerJson).Assembly.GetTypes()
            .Select(type => (Type: type, Marker: type.GetCustomAttribute<LegacyNullOmissionAttribute>(inherit: false)))
            .Where(entry => entry.Marker is not null)
            .ToDictionary(entry => entry.Type.Name, entry => entry.Marker!.Reason, StringComparer.Ordinal);

        Assert.Equal(
            Expected.OrderBy(entry => entry.Key, StringComparer.Ordinal),
            marked.OrderBy(entry => entry.Key, StringComparer.Ordinal));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerPayloadNullPolicyRegisterTests"`
Expected: FAIL. The marked set is empty and the expected set has 25 entries.

- [ ] **Step 3: Add the markers**

Add the attribute line directly above each type declaration, with the reason from the inventory table. Examples:

```csharp
[LegacyNullOmission(LegacyNullOmissionReason.BatchRedesign)]
public sealed record CreateTagTableSafetySnapshotInfo(
```

```csharp
[LegacyNullOmission(LegacyNullOmissionReason.ToolMigration)]
public class ProjectLifecycleResultInfo
```

```csharp
[LegacyNullOmission(LegacyNullOmissionReason.RequiredMemberEnforcement)]
public class HardwareConfigInfo
```

The three worker types get the same line. `PlcTypeImportResult` uses `BatchRedesign`; the other two use `RequiredMemberEnforcement`. Add `using TiaMcpServer.Contracts;` where the file does not already have it. Do not mark `NetworkObjectListInfo`, `ProjectTreeBrowseResultInfo` or `ProjectRebindStateInfo`, and do not mark nested member types: the policy follows the payload root.

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerPayloadNullPolicyRegisterTests"`
Expected: PASS. Then build the solution with the stub command from Global Constraints to prove the worker and FakeWorker still compile: expected 0 errors.

- [ ] **Step 5: Commit**

```bash
git add TiaMcpServer.Contracts TiaMcpServer.OpennessWorker TiaMcpServer.Tests/Worker/WorkerPayloadNullPolicyRegisterTests.cs
git commit -m "feat(worker): mark payload contracts that still omit nulls"
```

---

### Task 3: The worker renders through WorkerJson

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Program.cs` (the `JsonOptions` and `NetworkObjectListJsonOptions` fields, `Main`, `HandleLine…` request parse, `Success<T>`)
- Modify: `TiaMcpServer.Tests/TestUtilities/WorkerSerializationHarness.targets`
- Test: `TiaMcpServer.Tests/Project/ProjectTreeWorkerProducerContractTests.cs` (replace `LegacyAndUnrelatedPayloads_KeepOmittingNulls`)

**Interfaces:**
- Consumes: `WorkerJson.Envelope`, `WorkerJson.SerializePayload` (Task 1); the markers (Task 2).

- [ ] **Step 1: Replace the pinned-default test with policy tests through the real worker `Success<T>`**

In `ProjectTreeWorkerProducerContractTests.cs`, replace the method `LegacyAndUnrelatedPayloads_KeepOmittingNulls` with:

```csharp
    [Fact]
    public void MarkedPayloadContracts_KeepOmittingNullMembers()
    {
        Assert.Equal(
            """{"success":true,"operation":"start_plc","plcName":"PLC_1"}""",
            WorkerSerializationHarness.Serialize(new PlcOnlineResultInfo { Operation = "start_plc", PlcName = "PLC_1" }).Payload);
        Assert.Equal(
            """{"success":true,"operation":"save_project"}""",
            WorkerSerializationHarness.Serialize(new ProjectLifecycleResultInfo { Operation = "save_project" }).Payload);
        Assert.Equal(
            """[{"typeName":"CPU","typeIdentifier":"OrderNumber:X"}]""",
            WorkerSerializationHarness.Serialize(new List<CatalogEntryInfo>
            {
                new() { TypeName = "CPU", TypeIdentifier = "OrderNumber:X" }
            }).Payload);
    }

    [Fact]
    public void UnmarkedPayloadContracts_WriteNullMembers()
    {
        Assert.Equal(
            """{"name":"unchanged","optional":null}""",
            WorkerSerializationHarness.Serialize(new { Name = "unchanged", Optional = (string?)null }).Payload);
    }
```

- [ ] **Step 2: Run them to verify the unmarked case fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectTreeWorkerProducerContractTests"`
Expected: `UnmarkedPayloadContracts_WriteNullMembers` FAILS (the old worker switch omits nulls for any type outside its three). `MarkedPayloadContracts_KeepOmittingNullMembers` passes, which is the unchanged-bytes guard.

- [ ] **Step 3: Switch the worker to WorkerJson**

In `TiaMcpServer.OpennessWorker/Program.cs`:
- delete the `JsonOptions` and `NetworkObjectListJsonOptions` field declarations;
- in `Main`, serialize responses with `WorkerJson.Envelope`: `Console.Out.WriteLine(JsonSerializer.Serialize(response, WorkerJson.Envelope));`;
- deserialize requests with it: `JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)`;
- replace `Success<T>` with:

```csharp
    private static WorkerResponse Success<T>(T payload)
    {
        return new WorkerResponse
        {
            Success = true,
            Payload = WorkerJson.SerializePayload(payload)
        };
    }
```

Search the file for any remaining `JsonOptions` reference and point it at `WorkerJson.Envelope`. The file must compile with no local `JsonSerializerOptions` field left.

- [ ] **Step 4: Relax the harness extraction**

In `WorkerSerializationHarness.targets`, the task fails when `fields.Length == 0`. Change the guard condition from `fields.Length == 0 || success.Count != 1` to `success.Count != 1`. Update the comment above `UsingTask` to: "Compile the real worker's `Success<T>` (and any serializer fields it still declares) without Program's TIA session/static initialization." Leave the rest unchanged: the generated file already has `using TiaMcpServer.Contracts;`, so `WorkerJson` resolves.

- [ ] **Step 5: Run them to verify both pass, then run the harness-based suites**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectTreeWorkerProducerContractTests"`
Expected: all pass, including `NetworkObjectList_StillPreservesRequiredNullCursor` and the tree strict-decoder theories (explicit nulls preserved for the three unmarked contracts).

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.Tests/TestUtilities/WorkerSerializationHarness.targets TiaMcpServer.Tests/Project/ProjectTreeWorkerProducerContractTests.cs
git commit -m "refactor(worker): render worker responses through WorkerJson"
```

---

### Task 4: Host transport and FakeWorker use WorkerJson

**Files:**
- Modify: `TiaMcpServer/Worker/PersistentWorkerTransport.cs` (`JsonOptions` field, request write, response read)
- Modify: `TiaMcpServer.FakeWorker/Program.cs` (`requestJsonOptions`; the hello `WorkerResponse`; `ToCamelCaseJson`; the inline `JsonSerializerOptions` in the `tag-safety-dedup-proof` case)
- Test: `TiaMcpServer.Tests/Worker/FakeWorkerWireParityTests.cs`

**Interfaces:**
- Consumes: `WorkerJson` (Task 1), the markers (Task 2). Uses the existing `OpennessWorkerClient.ReadUpdateTagSafetySnapshotAsync`, `FakeWorkerBinding.BindVerifiedAsync` and `FakeWorkerLocator.Locate()`.

- [ ] **Step 1: Write the failing parity test**

```csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// The FakeWorker must render payload contracts exactly as the real worker does. Before
/// WorkerJson, its DTO fixtures wrote explicit nulls where production omits them, so IPC tests
/// exercised a wire shape production never sends.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class FakeWorkerWireParityTests
{
    [Fact]
    public async Task FakeWorker_OmitsNullMembersOfABatchPayloadLikeTheRealWorker()
    {
        const string scenario = "tag-update-snapshot-unavailable-visible";
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var result = await client.ReadUpdateTagSafetySnapshotAsync("PLC_1", "Inputs", folderPath: null, "Start", scenario);

        Assert.True(result.Success, result.Error);
        Assert.DoesNotContain("\"externalVisible\"", result.Payload, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~FakeWorkerWireParityTests"`
Expected: FAIL. The payload contains `"externalVisible":null`, written by the FakeWorker's explicit-null `ToCamelCaseJson`.

- [ ] **Step 3: Point the transport and FakeWorker at WorkerJson**

`PersistentWorkerTransport.cs`: delete the `JsonOptions` field and use `WorkerJson.Envelope` in `JsonSerializer.Serialize(request, …)` and `JsonSerializer.Deserialize<WorkerResponse>(responseLine, …)`. Request bytes now omit null members. The worker reads them identically, because the only `WorkerRequest` members with initializers (`Method`, `Rebind`, `SaveBeforeArchive`, `SaveBeforeClose`) are never null.

`TiaMcpServer.FakeWorker/Program.cs`:
- `var requestJsonOptions = WorkerJson.Envelope;`
- the hello response: `Console.Out.WriteLine(JsonSerializer.Serialize(new WorkerResponse { … }, WorkerJson.Envelope));`
- replace the `ToCamelCaseJson` body and its comment:

```csharp
// Renders a payload exactly as the real worker does (WorkerJson.SerializePayload), from a real
// Contracts DTO: the CLR type decides which members exist, and the shared policy decides whether
// null members are written, so a fixture can never show the host a wire shape production does not.
string ToCamelCaseJson<T>(T value) => WorkerJson.SerializePayload(value);
```

- in the `tag-safety-dedup-proof` case, replace `JsonSerializer.Serialize(new DeleteTagSafetySnapshotInfo(…), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })` with `ToCamelCaseJson(new DeleteTagSafetySnapshotInfo(…))`.

- [ ] **Step 4: Run the parity test, then the full suite**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~FakeWorkerWireParityTests"` (expected: PASS). Then run the full `dotnet test TiaMcpServer.Tests`.
Expected: all pass except possibly the four environmental `DoctorPackageVerificationScriptTests` cases (re-run that class alone). A new failure from a fixture that relied on an explicit null the production worker omits is a production/fake divergence. **Stop and report it with the failing member; do not patch the fixture or the validator to hide it.**

- [ ] **Step 5: Commit**

```bash
git add TiaMcpServer/Worker/PersistentWorkerTransport.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Worker/FakeWorkerWireParityTests.cs
git commit -m "test(worker): give the FakeWorker the production wire policy"
```

---

### Task 5: Tests use WorkerJson instead of copied worker options

**Files:**
- Modify the `JsonOptions` field in: `TiaMcpServer.Tests/Block/CompileCheckInfoTests.cs`, `Block/CrossReferenceInfoTests.cs`, `Network/AddDeviceResultInfoTests.cs`, `Network/CatalogEntryInfoTests.cs`, `Network/ConfigureNetworkDeviceResultInfoTests.cs`, `Network/HardwareConfigInfoTests.cs`, `Project/ProjectMetadataTests.cs`, `Worker/WorkerResponseJsonTests.cs`

**Interfaces:**
- Consumes: `WorkerJson.PayloadOptionsFor`, `WorkerJson.Envelope` (Task 1).

- [ ] **Step 1: Replace each hand-copied options block**

Each file declares `private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };`, a copy of the worker's old options. Replace it with the payload root that carries the DTO on the wire:

| File | Replacement |
| --- | --- |
| `CompileCheckInfoTests.cs` | `WorkerJson.PayloadOptionsFor(typeof(CompileCheckReport))` |
| `CrossReferenceInfoTests.cs` | `WorkerJson.PayloadOptionsFor(typeof(CrossReferenceReport))` |
| `AddDeviceResultInfoTests.cs` | `WorkerJson.PayloadOptionsFor(typeof(AddDeviceResultInfo))` |
| `CatalogEntryInfoTests.cs` | `WorkerJson.PayloadOptionsFor(typeof(CatalogEntryInfo))` |
| `ConfigureNetworkDeviceResultInfoTests.cs` | `WorkerJson.PayloadOptionsFor(typeof(ConfigureNetworkDeviceResultInfo))` |
| `HardwareConfigInfoTests.cs` | `WorkerJson.PayloadOptionsFor(typeof(HardwareConfigInfo))` |
| `ProjectMetadataTests.cs` | `WorkerJson.PayloadOptionsFor(typeof(ProjectLifecycleResultInfo))` (metadata travels inside it) |
| `WorkerResponseJsonTests.cs` | `WorkerJson.Envelope` |

Remove `using System.Text.Json.Serialization;` where it becomes unused.

- [ ] **Step 2: Run the affected classes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~CompileCheckInfoTests|FullyQualifiedName~CrossReferenceInfoTests|FullyQualifiedName~AddDeviceResultInfoTests|FullyQualifiedName~CatalogEntryInfoTests|FullyQualifiedName~ConfigureNetworkDeviceResultInfoTests|FullyQualifiedName~HardwareConfigInfoTests|FullyQualifiedName~ProjectMetadataTests|FullyQualifiedName~WorkerResponseJsonTests"`
Expected: all pass unchanged. The resolved options have the same settings as the copies. If one fails, the copy did not match production: report it rather than editing the assertion.

- [ ] **Step 3: Commit**

```bash
git add TiaMcpServer.Tests
git commit -m "test(worker): use WorkerJson instead of copied worker options"
```

---

### Task 6: Strict typed decode of the rebind-state payload

**Files:**
- Create: `TiaMcpServer/Worker/ProjectRebindStatePayloadContract.cs`
- Modify: `TiaMcpServer.Contracts/ProjectRebindStateInfo.cs` (`[JsonRequired]` on all five properties)
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs` (`ProbeOpenProjectRebindAsync`, the untyped `JsonDocument` block)
- Modify: `TiaMcpServer/Tools/ProjectWriteTools.cs` (`ReadOpenProjectCurrentStateAsync`, the `JsonSerializerDefaults.Web` decode)
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (link the new host file)
- Test: `TiaMcpServer.Tests/Worker/ProjectRebindStatePayloadContractTests.cs`

**Interfaces:**
- Produces: `ProjectRebindStatePayloadContract.Decode(string payload, string source, string destination) : ProjectRebindStateInfo`. Throws `JsonException` for any payload that is not the strict contract or does not describe `source → destination`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// probe_open_project_rebind payloads have one strict decoder. It replaced an untyped member
/// check in OpennessWorkerClient and a lenient Web-defaults decode in ProjectWriteTools, which
/// could disagree about the same bytes.
/// </summary>
public sealed class ProjectRebindStatePayloadContractTests
{
    private const string Source = "C:/Projects/A.ap21";
    private const string Destination = "C:/Projects/B.ap21";
    private const string Valid =
        """{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""";

    [Fact]
    public void Decode_AcceptsAStateForThePinnedSourceAndDestination()
    {
        var state = ProjectRebindStatePayloadContract.Decode(Valid, Source, Destination);

        Assert.True(state.WillCloseSource);
        Assert.False(state.SourceIsModified);
    }

    [Theory]
    [InlineData("sourceProjectPath")]
    [InlineData("destinationProjectPath")]
    [InlineData("sourceIsModified")]
    [InlineData("sourceOpenedByWorker")]
    [InlineData("willCloseSource")]
    public void Decode_RejectsAMissingMember(string member)
    {
        var payload = JsonNode.Parse(Valid)!.AsObject();
        payload.Remove(member);

        Assert.ThrowsAny<JsonException>(() =>
            ProjectRebindStatePayloadContract.Decode(payload.ToJsonString(), Source, Destination));
    }

    [Theory]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true,"extra":1}""")]
    [InlineData("""{"SourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":null,"destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":null,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/C.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":false}""")]
    public void Decode_RejectsAPayloadThatIsNotThePinnedStrictContract(string payload)
    {
        Assert.ThrowsAny<JsonException>(() =>
            ProjectRebindStatePayloadContract.Decode(payload, Source, Destination));
    }
}
```

The six rejection cases are: unknown member, wrong-case member, null source, null modified state, another destination, and an inconsistent close decision.

- [ ] **Step 2: Link the new file and run the tests to verify they fail**

Add to `TiaMcpServer.Tests.csproj`, next to the other `TiaMcpServer\Worker\` links:

```xml
    <Compile Include="..\TiaMcpServer\Worker\ProjectRebindStatePayloadContract.cs"
      Link="Host\ProjectRebindStatePayloadContract.cs" />
```

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectRebindStatePayloadContractTests"`
Expected: build error. The linked file does not exist yet.

- [ ] **Step 3: Write the contract and mark the members required**

`TiaMcpServer/Worker/ProjectRebindStatePayloadContract.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Worker;

/// <summary>
/// The only decoder of <c>probe_open_project_rebind</c> payloads. A payload that does not decode
/// strictly as <see cref="ProjectRebindStateInfo"/>, or that describes a different source,
/// destination, or close decision than the pinned request, is rejected rather than trusted.
/// </summary>
internal static class ProjectRebindStatePayloadContract
{
    public static ProjectRebindStateInfo Decode(string payload, string source, string destination)
    {
        var state = CanonicalJson.Deserialize<ProjectRebindStateInfo>(payload);
        var willCloseSource = state.SourceOpenedByWorker && !SamePath(source, destination);
        if (state.SourceProjectPath is null
            || state.SourceIsModified is null
            || !SamePath(state.SourceProjectPath, source)
            || !SamePath(state.DestinationProjectPath, destination)
            || state.WillCloseSource != willCloseSource)
        {
            throw new JsonException(
                "The rebind-state payload does not describe the pinned source and destination.");
        }

        return state;
    }

    private static bool SamePath(string first, string second)
        => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
```

In `ProjectRebindStateInfo.cs`, add `using System.Text.Json.Serialization;` and `[JsonRequired]` above each of the five properties. The worker never deserializes this type, and it writes every member (the contract is unmarked), so presence always holds on the real wire.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectRebindStatePayloadContractTests"`
Expected: 12 passed.

- [ ] **Step 5: Use the contract at both host call sites**

In `OpennessWorkerClient.ProbeOpenProjectRebindAsync`, replace the `try { using var document = JsonDocument.Parse(result.Payload); … }` block with:

```csharp
        try
        {
            ProjectRebindStatePayloadContract.Decode(result.Payload, source, destination);
            return result;
        }
        catch (JsonException)
        {
            // A malformed worker payload is a protocol failure, never a caller-visible echo.
        }
```

The existing `WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, "The rebind-state probe returned an invalid snapshot.")` after the block stays.

In `ProjectWriteTools.ReadOpenProjectCurrentStateAsync`, replace the `ProjectRebindStateInfo? state; try { … JsonSerializerDefaults.Web … } …` block and the `if (state is null || …)` check that follows it with:

```csharp
        ProjectRebindStateInfo state;
        try
        {
            state = ProjectRebindStatePayloadContract.Decode(probe.Payload, source, destination);
        }
        catch (JsonException)
        {
            return WorkerCallResult.Fail(
                WorkerFailureCategories.ProtocolError,
                "The rebind-state probe did not match the pinned source and destination.");
        }
```

`source` and `destination` are already proven non-null a few lines earlier in that method.

- [ ] **Step 6: Run the lifecycle and rebind suites**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Rebind|FullyQualifiedName~Lifecycle|FullyQualifiedName~ProjectWriteTools|FullyQualifiedName~OpenProject"`
Expected: all pass. A failure means a test supplied a rebind payload that the old untyped or `Web` decoders accepted and the strict contract rejects. Report the payload shape rather than loosening the contract.

- [ ] **Step 7: Commit**

```bash
git add TiaMcpServer/Worker/ProjectRebindStatePayloadContract.cs TiaMcpServer.Contracts/ProjectRebindStateInfo.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer/Tools/ProjectWriteTools.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/Worker/ProjectRebindStatePayloadContractTests.cs
git commit -m "fix(project): decode rebind-state payloads strictly"
```

---

### Task 7: Documentation

**Files:**
- Modify: `docs/roadmap/json-contract.md`, `docs/ARCHITECTURE.md` (§6 Worker transport), `TiaMcpServer/Json/TiaJson.cs` (class comment), `AGENTS.md` (Key conventions), `docs/superpowers/README.md` (Plans table), `docs/README.md` (Latest process entries)

- [ ] **Step 1: Roadmap**

In `docs/roadmap/json-contract.md`:
- Status line: "Phase 0 is complete. Phase 1a (worker wire normalization) is complete; Phase 1b and Phases 2-4 are not started."
- Current State: the worker null bullet now says null handling is declared per payload contract through `[LegacyNullOmission]`, with the three reasons, and that the FakeWorker renders through the same policy.
- Replace the Phase 1 section with "Phase 1a: Worker Wire Normalization — Complete" and "Phase 1b: Required-Member Enforcement", recording:
  - the 2026-09-28 decisions (batch-only and legacy-tool payload types keep omitting nulls; two PRs);
  - that 1b removes the `RequiredMemberEnforcement` markers together with the generic required-member work and the three hand-written validators;
  - that the FakeWorker `status-with-metadata` fixture returns a bare `ProjectStatusInfo` while the real worker wraps it in `ProjectLifecycleResultInfo`. This shape divergence is left for Phase 2.

- [ ] **Step 2: Architecture, TiaJson and AGENTS**

- ARCHITECTURE §6: add a paragraph. `TiaMcpServer.Contracts/WorkerJson.cs` is the single wire definition: `Envelope` in both directions, and `SerializePayload` writing null members unless the payload root carries `[LegacyNullOmission]`. It is used by the worker, `PersistentWorkerTransport` and the FakeWorker.
- `TiaJson.cs`: replace the sentences from "The host↔worker wire format is not shared from here either" to the end of the paragraph with "The host↔worker wire format lives in TiaMcpServer.Contracts.WorkerJson."
- AGENTS.md Key conventions, new bullet: "**Worker payload JSON** goes through `WorkerJson.SerializePayload`. A new payload contract writes null members; `[LegacyNullOmission]` is only for the reasons in `LegacyNullOmissionReason`, and adding or removing one updates `WorkerPayloadNullPolicyRegisterTests`."

- [ ] **Step 3: Plan indexes**

- `docs/superpowers/README.md` Plans table, new first row: `| 2026-09-28 | [JSON contract Phase 1a — worker wire normalization](plans/2026-09-28-json-contract-phase1a-worker-wire.md) |`.
- `docs/README.md` "Latest process entries": prepend `the [JSON contract Phase 1a worker-wire plan](superpowers/plans/2026-09-28-json-contract-phase1a-worker-wire.md),`.

- [ ] **Step 4: Verify links and commit**

Check that every relative link in the changed docs resolves. Then:

```bash
git add docs AGENTS.md TiaMcpServer/Json/TiaJson.cs
git commit -m "docs(mcp-protocol): record Phase 1a worker wire normalization"
```

---

### Final verification

- [ ] Stub build: 0 errors, the same 7 existing warnings.
- [ ] Full `dotnet test TiaMcpServer.Tests`: all pass. The four environmental packaging cases (if they fail in the parallel run) must pass when `DoctorPackageVerificationScriptTests` runs alone.
- [ ] `git diff cf32acb --stat -- TiaMcpServer.OpennessWorker` shows only `Program.cs` and the three marked worker types.
- [ ] Record in the PR that no tool output, schema, or real-worker wire byte changed. The only wire difference is host requests now omitting null members, which the worker reads identically.
