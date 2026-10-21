# C-1 Root and Nested Argument Strictness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** All 19 tools reject argument-shape errors (root or nested) with their own canonical document, `validation_error`, `isError: true`, and no audit record.

**Architecture:**
- One wrapper (`StrictArgumentsTool`) checks every call in four steps:
  1. unknown root keys, derived from the tool's input schema;
  2. retired item members;
  3. missing required keys;
  4. pre-deserialization of each argument, with the SDK's own serializer options.
- A single registration table (`WithProductionTools`) gives each tool type its canonical reject function.

**Tech Stack:** C#/.NET 10, ModelContextProtocol 2.2.0 (`McpServerTool.Create`, `DelegatingMcpServerTool`), System.Text.Json, xUnit with `McpProtocolTestHarness` and FakeWorker.

**Spec:**
- Review plan item C-1 and decision Q4: [review](2026-10-17-codebase-simplification-review.md); findings TOOLS-1, TOOLS-2 and TOOLS-13.
- Sequencing and the null rule: [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c1-closed-tool-arguments`, worktree `.worktrees/C-1`. Merge it last in wave 1.

## Global Constraints

The [Workstream C global constraints](2026-10-21-workstream-c-plan.md#global-constraints-apply-to-every-wave-1-plan) apply. In addition:

- **Null rule.** JSON `null` is accepted only where the schema advertises null. Today that is only `bind_project.forceRebind`. Everywhere else it is `validation_error`.
- **No allow-lists.** Allowed root keys are derived from `ProtocolTool.InputSchema.properties`, never hand-written. This is what makes C-0's later `projectPath` removal reject automatically.
- **No side effects on rejection.** No `contractVersion` bump. A rejection never reaches `WriteExecution`/`WriteRun`, an `IWriteAuditSink`, the worker or elicitation.
- **Output schemas stay identical.** Every tool's `tools/list` **outputSchema** must match `main` exactly. Only input schemas may change.
- **`bind_project` keeps its contract.** Its per-action key and type validation and its `forceRebind: null` acceptance are documented in `docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md` and must not change.
- **`browse_project_tree` keeps its categories:** `validation_error`, `invalid_cursor`, `invalid_selector`.

## Review Focus

1. **A real client sends a stray key or a null optional boolean.** The 11 newly closed tools now reject it. The Task 5 smoke is the gate. Stop and escalate if the client sends null for omitted booleans.
2. **The pre-check and the SDK binder disagree.** For example, `"5"` for an `int?` must still bind, as the SDK allows. Both must use `McpJsonUtilities.DefaultOptions` (pinned in Task 1).
3. **`Compose` is called on a fresh write domain without `Validate`/`PlanAsync`.** It must not dereference state set there. The Task 2 conformance test covers all five write tools, so a null reference would surface there.
4. **`JsonException.Path` echoes caller-supplied member names.** Cap it at 256 characters, and never echo values.
5. **Closing the schema also changes `outputSchema`.** The Task 2 capture-and-compare step pins it.

## Contract change (release note)

> All 19 tools now reject unknown, missing, mistyped or retired arguments, at the root or inside items, with the tool's own canonical JSON document (`validation_error`, `isError: true`) and no audit record. Previously the writes and `vci_read` returned plain text, and 11 tools silently ignored unknown root keys.

- **Newly closed (11 tools).** These tools are registered today with plain `WithTools<>`: `plc_read`, `network_read`, `hmi_read`, `read_cross_references`, `compile_check`, plus the six lifecycle tools.
  - Their input schemas gain `additionalProperties: false`, nested item schemas included. Item types already carry `[JsonUnmappedMemberHandling(Disallow)]`, so for nested items the schema only advertises what the runtime already enforces.
- **Runtime check only.** `get_project_status` already advertises a closed schema but never enforced it.
- **New rejection shape (5 tools).** `network_write`, `plc_write`, `hmi_write`, `vci_write` and `vci_read` move from plain text to their canonical document.
  - The four writes return `phase: "error"`, `success: false`, `error.category: "validation_error"`, built with `IWriteDomain.Compose`.
  - The `plc_write` retired members `yamlContent` and `sourceContent` keep their hint inside `error.message`.
- **Become canonical:**
  - SDK binding errors: unmapped item members, mistyped root or nested values, and missing required root arguments.
  - Lifecycle `null` booleans.
- **Live TIA verification is not needed.** The check is host-only and runs before tool entry. A scoped real-client smoke runs instead (Task 5).

## SDK feasibility (verified during planning)

- **Default options.** `ModelContextProtocol.Core` 2.2.0 binds non-DI parameters from `CallToolRequestParams.Arguments` using `McpServerToolCreateOptions.SerializerOptions`. When none are set, it falls back to `McpJsonUtilities.DefaultOptions`: Web defaults, `WhenWritingNull`, and numbers allowed from strings. No registration in the repo sets `SerializerOptions`.
- **Shared options.** `CreateClosedTool` passes `McpJsonUtilities.DefaultOptions` explicitly. It pre-deserializes each supplied argument with `JsonSerializer.Deserialize(element, options.GetTypeInfo(parameterType))`. Because both sides use the same options, the pre-check throws exactly where the binder would.
- **Parameter map.** Parameters are matched by schema property name, which is `ParameterInfo.Name`. DI services, `CancellationToken` and `McpServer?` are not in the schema, so they are skipped. C-7 adds a `CancellationToken` to the four read tools, and this covers it.
- **Exceptions.** Only `JsonException` is mapped. Every other exception keeps the SDK's handler path.
- **Known residual gap (out of scope).** JSON `null` for a required non-nullable reference argument still reaches the tool. Examples: `open_project.projectPath`, `read_cross_references.target`. The tools' entered validation handles these. Record the gap in IMPROVEMENT_LOG.

## Files

- **Modify `TiaMcpServer/Tools/StrictArgumentsTool.cs`:**
  - Rewrite `StrictArgumentsTool`.
  - Add `CreateClosedTool`, `RootKeys`, `Message` and `WithClosedTools`.
  - Delete `VciReadToolRegistration`, `VciWriteToolRegistration` and `HmiWriteToolRegistration`.
  - Shrink `PlcWriteToolRegistration` to `internal` `AddObEventClassEnum` and `RetiredItemMembers`.
- **Modify `TiaMcpServer/Tools/McpToolRegistration.cs`:**
  - Add `WithProductionTools(Type)` with the reject table and a private generic `WriteRejection` helper.
  - `WithAccessModeTools` calls it for its 13 tool types, in today's order.
- **Delete `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs`.** Remove its `<Compile Include>` from `TiaMcpServer.Tests.csproj`. No new host source file is added.
- **Modify `TiaMcpServer/Tools/ProjectReadTools.cs`:**
  - Delete `WithProjectReadTools`, and make `AlignSelectorSchemaNullability` `internal`.
  - In `ProjectTreeArgumentValidatingTool`, use `RootKeys(innerTool)`, and give `ValidationFailure(string category, string message)` `internal` visibility.
- **Modify `TiaMcpServer/Tools/ProjectBindingTools.cs`:** delete `WithProjectBindingTools`, and make `AllowNullForceRebind` `internal`.
- **Modify the four read tools:** `Plc/PlcReadTools.cs`, `Network/NetworkReadTools.cs`, `Hmi/HmiReadTools.cs` and `Vci/VciReadTools.cs`. Each one's `Error(string, string)` changes from `private` to `internal`.
- **Modify `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`:** `RegisterToolType<TTools>` becomes `builder.WithProductionTools(typeof(TTools))`.
- **Create tests:**
  - `TiaMcpServer.Tests/Tools/StrictArgumentsToolTests.cs`
  - `TiaMcpServer.Tests/TestSupport/ArgumentRejectionAssert.cs`
- **Modify tests:**
  - `Tools/ToolOutputContractConformanceTests.cs`
  - `Tools/McpToolSchemaTests.cs`
  - `Tools/LifecycleMcpProtocolTests.cs`
  - `Tools/BindProjectToolProtocolTests.cs`
  - `Project/ProjectTreeStructuredProtocolTests.cs`
  - `Plc/PlcGuardedWriteMcpTests.cs`
  - `Network/NetworkGuardedWriteMcpTests.cs`
  - `Network/NetworkStructuredProtocolTests.cs`
  - `Hmi/HmiGuardedWriteMcpTests.cs`
  - `Vci/VciGuardedWriteMcpTests.cs`
  - `Vci/VciReadMcpTests.cs`

## Interfaces

**Produced:**
- `internal sealed class StrictArgumentsTool(McpServerTool inner, MethodInfo method, Func<string, CallToolResult> reject, IReadOnlyDictionary<string, string>? retiredItemMembers = null) : DelegatingMcpServerTool(inner)`
  - The root-key set, the required set and the parameter-type map are built once, in the constructor.
- `internal static McpServerTool CreateClosedTool(MethodInfo method, IServiceProvider services, Func<string, CallToolResult> reject, Func<AIJsonSchemaCreateContext, JsonNode, JsonNode>? transformSchemaNode = null, IReadOnlyDictionary<string, string>? retiredItemMembers = null)`
- `internal static IReadOnlySet<string> RootKeys(McpServerTool tool)`
- `internal static string Message(string toolName, string detail, IReadOnlySet<string> acceptedKeys)`. It returns `"<tool> arguments do not match its input schema: <detail>. Accepted arguments: <sorted keys, or none>."`
- `internal static IMcpServerBuilder WithClosedTools(this IMcpServerBuilder builder, Type toolType, Func<IServiceProvider, string, string, CallToolResult> reject, Func<AIJsonSchemaCreateContext, JsonNode, JsonNode>? transformSchemaNode = null, IReadOnlyDictionary<string, string>? retiredItemMembers = null, Func<string, McpServerTool, McpServerTool>? decorate = null)`
  - It enumerates the public static `[McpServerTool]` methods ordered by `MetadataToken`.
- `public static IMcpServerBuilder WithProductionTools(this IMcpServerBuilder builder, Type toolType)`
  - It throws `ArgumentOutOfRangeException` for a type that is not in the table.

**Consumed (all exist on main):**
- `StructuredToolResult.CreateCanonical(string, bool)`
- `CanonicalJson.Serialize`
- `StructuredStandaloneResult.Create<TPayload>(tool, null, [], new StructuredOperationFailure(category, message))`, with these payloads:
  - `ProjectStatusInfo` for `get_project_status`
  - `CompileCheckReport` for `compile_check`
  - `CrossReferenceReport` for `read_cross_references`
  - `ProjectBindingResult` for `bind_project`
- `IWriteDomain<…>.Compose(new WriteReport<TEffect,TVerification>(WritePhases.Error, false, new WriteToolError(WorkerFailureCategories.ValidationError, msg), [], [], [], null, default))`. This is the same shape `WriteRun.Finish` serializes, without its audit append.

**Reject table (in `WithProductionTools`):**

| Tool type | Reject | Extras |
|---|---|---|
| `PlcReadTools`, `NetworkReadTools`, `HmiReadTools`, `VciReadTools` | `X.Error(ValidationError, msg)` | none |
| `ProjectReadTools` | `get_project_status`: standalone result. `browse_project_tree`: `ProjectTreeArgumentValidatingTool.ValidationFailure`. | decorate `browse_project_tree` with `ProjectTreeArgumentValidatingTool`; transform `AlignSelectorSchemaNullability` |
| `ProjectBindingTools` | standalone result | decorate with `BindProjectArgumentValidatingTool`; transform `AllowNullForceRebind` |
| `CrossReferenceReadTools`, `ProjectEngineeringTools` | standalone result | none |
| `NetworkWriteTools`, `PlcWriteTools`, `HmiWriteTools`, `VciWriteTools`, `ProjectWriteTools` | `WriteRejection(new XWriteDomain(...), msg)`. VCI resolves `VciBreadcrumbLocation`; lifecycle passes `new LifecycleWriteItem(toolName)`. | `plc_write` passes `AddObEventClassEnum` and `RetiredItemMembers` |

**Data flow:**
1. The bespoke outer validator runs first, for `bind_project` and `browse_project_tree` only.
2. `StrictArgumentsTool` runs its checks in order:
   1. unknown root key;
   2. retired item member;
   3. missing required key;
   4. pre-deserialization of each argument. The detail is `argument '<name>' is malformed at <Path>`, with the path included only up to 256 characters, and values never echoed.
3. The inner SDK tool runs.

---

### Task 1: Strict wrapper core

**Files:** `Tools/StrictArgumentsTool.cs`, `TiaMcpServer.Tests/Tools/StrictArgumentsToolTests.cs`

- [ ] Write `StrictArgumentsToolTests` against a harness that registers one closed fixture tool through `CreateClosedTool`.
  - The fixture is `Echo(int[] values, bool flag = false, string? name = null, Item[]? items = null)`, which returns the text `"entered"`.
  - `Item` is a test record with `[JsonUnmappedMemberHandling(Disallow)]`.
  - `reject` returns `IsError = true` with the text `"REJECT:" + msg`.
  - Test cases:
    - `UnknownRootKey_IsRejectedBeforeEntry`: the text contains `Accepted arguments: flag, items, name, values.`
    - `MissingRequiredArgument_IsRejected`: `{}` produces a message that mentions `'values'`.
    - `WrongRootKind_IsRejected`: `flag: "true"`.
    - `NullForNonNullableBoolean_IsRejected`.
    - `NullForNullableString_Enters`.
    - `NumberFromString_EntersLikeTheSdk`: `values: ["1"]` returns `"entered"`.
    - `NestedUnmappedMember_IsRejectedWithPath`: the text contains `$[0]`.
    - `RetiredItemMember_IsRejectedWithHint`.
    - `ValidCall_Enters`.
- [ ] Add the new signatures with pass-through bodies. Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~StrictArgumentsToolTests`. The rejection cases fail.
- [ ] Implement the wrapper and helpers. The old `With*Tools` registrations call `CreateClosedTool` with a temporary plain-text reject, so they keep compiling.
- [ ] Run the filter again. All tests pass. Commit `refactor: derive strict tool arguments from schema and binding types`.

### Task 2: Canonical rejection for all 19 tools through one table

**Files:** `McpToolRegistration.cs`, `NetworkWriteArgumentValidatingTool.cs` (delete), `ProjectReadTools.cs`, `ProjectBindingTools.cs`, the four read tools, `TiaMcpServer.Tests.csproj`, `McpProtocolTestHarness.cs`, `ArgumentRejectionAssert.cs`, and the modified tests listed above.

- [ ] Capture every tool's `tools/list` `outputSchema` on `main` in a test-local snapshot, for the comparison below.
- [ ] Add `ArgumentRejectionAssert.IsCanonicalValidationError(CallToolResult reply, string tool)`. It asserts:
  - `StructuredContractInspector.FindViolations(reply)` is empty;
  - `IsError` is true;
  - the category is `validation_error`, read from `error.category`, or from `failure.category` for `browse_project_tree`.
- [ ] Add `ToolOutputContractConformanceTests.EveryRegisteredToolRejectsArgumentShapeErrorsCanonically`, over the production surface in Full mode with a `FakeWorkerRequestLog`. For every listed tool, send:
  - `{unexpectedArgument: true}`;
  - `null` for each root property whose schema type is exactly `"boolean"`;
  - when the tool has `operations`, one item carrying `unexpectedMember: 1`.

  Expect `ArgumentRejectionAssert` to pass for every call. Also expect the worker method log to be unchanged and no audit lines.
- [ ] Add `McpToolSchemaTests.EveryRegisteredToolSchemaIsClosed`: each tool's `inputSchema.additionalProperties` is `false`.
- [ ] Add `McpToolSchemaTests.OutputSchemasMatchMain`: every tool's `outputSchema` equals the captured snapshot.
- [ ] Strengthen the existing rejection rows to `ArgumentRejectionAssert`. Writes also check `phase == "error"` and the tool name. Keep the no-worker and no-audit assertions.
  - `PlcGuardedWriteMcpTests.LegacyOrMalformedArguments_DoNotReachWorker`. The `yamlContent`/`sourceContent` rows check that `error.message` contains `use 'content' with 'expectedContentHash'`.
  - `NetworkGuardedWriteMcpTests.LegacyOrMalformedArguments_DoNotReachWorker`.
  - `HmiGuardedWriteMcpTests.StrictWrapperRejectsWithoutWorkerOrAudit`.
  - `VciGuardedWriteMcpTests.StrictArgumentsRejectBeforeWorkerAndAudit`.
  - `VciReadMcpTests.MalformedInputsRejectBeforeWorker`.
  - `NetworkStructuredProtocolTests.NetworkWrite_RejectsMalformedNestedInputAtTheProtocolBoundary`: all four rows are canonical.
- [ ] Add `LifecycleMcpProtocolTests.LifecycleArgumentShapeErrors_AreCanonicalAndUnaudited`, in read-write mode with the existing elicitation-counting `ClientOptions(handler)`.
  - Rows:
    - `save_project {confirm: true}`
    - `close_project {saveBeforeClose: null}`
    - `open_project {projectPath: <fixture>, forceRebind: null}`
    - `archive_project {…, dryRun: "true"}`
  - Expected for each: canonical, no `writes-*.jsonl` lines, 0 elicitations.
- [ ] Run these test classes:
  - `ToolOutputContractConformanceTests`
  - `McpToolSchemaTests`
  - `GuardedWriteMcpTests`
  - `VciReadMcpTests`
  - `NetworkStructuredProtocolTests`
  - `LifecycleMcpProtocolTests`

  These failures are expected:
  - the open tools and the cases that today return SDK text;
  - the 11 tools whose schemas are not closed yet;
  - the plain-text rows;
  - the lifecycle null and type rows.
- [ ] Implement the change:
  - `WriteRejection` serializes `domain.Compose(report)`, then `StructuredToolResult.CreateCanonical(text, isError: true)`, with no audit sink.
  - Add `WithProductionTools` and call it from `WithAccessModeTools`.
  - Change the four read `Error` methods to `internal`.
  - Delete the old registrations, `NetworkWriteArgumentValidatingTool.cs` and its csproj link.
  - Switch `RegisterToolType` to `WithProductionTools`.
  - If closing the input schema also changes an output schema, restrict the closing to the input schema.
- [ ] Rerun the same filter. Everything passes, including the existing `StructuredToolReturnsOneCanonicalDocumentWithoutNestedJson` and `Inspection_InvalidArgumentsRejectedBeforeDispatch`. Commit `refactor: register all tools through one closed-argument helper`.

### Task 3: `browse_project_tree` wording and `bind_project` regression lock

**Files:** `ProjectReadTools.cs`, `ProjectTreeStructuredProtocolTests.cs`, `BindProjectToolProtocolTests.cs`

- [ ] Add two tests:
  - `ProjectTreeStructuredProtocolTests.UnknownRootKey_UsesGenericValidationMessage`: `failure.message` starts with `browse_project_tree arguments do not match its input schema`. It fails today.
  - `BindProjectToolProtocolTests.ForceRebindNull_IsStillAccepted`: the result is the same as omitting the key. It passes today and serves as a regression lock.
- [ ] `ProjectTreeArgumentValidatingTool`:
  - Replace `AllowedArguments` with `RootKeys(innerTool)`.
  - Pass `StrictArgumentsTool.Message(...)` on its `validation_error` path.
  - The `invalid_cursor` and `invalid_selector` texts stay unchanged.
- [ ] Both tests pass. Commit `refactor: derive browse_project_tree root keys from its schema`.

### Task 4: Offline verification and review

- [ ] Build and run the full suite with coverage, as described in the global constraints, under `TestResults/c1`. If a test relied on a silently dropped key, fix the test by removing the stray key; never loosen the check.
- [ ] Confirm that every changed executable line in `StrictArgumentsTool.cs` and in the `WithProductionTools` table is covered.
- [ ] Get an independent review against this plan's Review Focus. Commit any fixes as `fix: …`.

### Task 5: Real-client smoke (scoped live)

- [ ] Install the candidate. In Claude Code, in read-write mode, with any project or none open, check:
  1. In `tools/list`, every `inputSchema.additionalProperties` is `false`, and `outputSchema` is unchanged.
  2. `plc_read` with valid `operations` plus root `projectPath: "x"` returns canonical `validation_error`.
  3. `save_project {dryRun: true}` passes the argument check and returns a preview or a binding error.
  4. `network_write` with an item carrying an unknown member returns canonical `validation_error`, and no new audit line appears.
  5. `bind_project {forceRebind: null}` is still accepted.
  6. Note whether the client ever sends `null` for an omitted boolean. If it does, stop and escalate.
- [ ] Record the candidate SHA and the outcomes in the private test notes.

### Task 6: Docs and evidence (last)

- [ ] Replace the "SDK/wrapper rejection is a normal MCP error, no audit" wording with "argument-shape errors return the tool's canonical document with `validation_error`, `isError: true`, no audit" in:
  - `AGENTS.md` (Network writes bullet)
  - `README.md` (absolute links only)
  - `docs/ARCHITECTURE.md`
  - `docs/roadmap/json-contract.md` (~296–299: remove the plain-text exception, and add the pre-entry rule under Delivery)
  - `docs/roadmap/network-operations.md`
  - `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
  - `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`
  - `docs/guides/mcp-client-configuration.md`
  - `docs/guides/troubleshooting.md`
- [ ] `PROJECT_OPERATIONS_SUMMARY.md`: state that lifecycle root arguments are strict, and give the null rule.
- [ ] `IMPROVEMENT_LOG.md`: record C-1 as done and log the required-reference-null gap.
- [ ] Review plan:
  - Under C-1, add "Regression evidence" and "Full offline evidence" sub-bullets, plus the smoke result.
  - Change the C-0 line "Do C-0 before C-1" to point at the Workstream C plan.
- [ ] Commit `docs: document canonical argument rejection for all tools`.
