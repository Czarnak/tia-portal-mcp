# C-1 Root and Nested Argument Strictness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** All 19 tools reject argument-shape errors, root or nested, with their own canonical document: `validation_error`, `isError: true`, and no audit record.

**Architecture:** One wrapper, `StrictArgumentsTool`, checks four things in order:
1. unknown root keys, derived from the tool's input schema;
2. retired item members;
3. missing required keys;
4. pre-deserialization of each argument, using the SDK's own serializer options.

A single registration table, `WithProductionTools`, gives each tool type its canonical reject function. Its optional bespoke validator wraps the strict tool.

**Tech Stack:** C#/.NET 10, ModelContextProtocol 2.2.0 (`McpServerTool.Create`, `DelegatingMcpServerTool`), System.Text.Json, xUnit with `McpProtocolTestHarness` and FakeWorker.

**Spec:** Review plan item C-1 and decision Q4 ([review](2026-10-17-codebase-simplification-review.md); findings TOOLS-1, TOOLS-2, TOOLS-13). Sequencing and the null rule come from the [Workstream C plan](2026-10-21-workstream-c-plan.md).

**Branch:** `refactor/c1-closed-tool-arguments`, worktree `.worktrees/C-1`. It merges last in wave 1.

**Execution:** subagent-driven for Tasks 1–3. Then the orchestrator runs branch verification and the maintainer runs the live gate. Task 4 (docs) comes last.

## Global Constraints

- **Null rule.** JSON `null` enters exactly where the bound CLR type accepts it. That covers nullable reference types, nullable value types and nullable arrays, all of which the generated schema advertises as nullable. Non-nullable `bool`/`int` parameters reject null with `validation_error`. `BindProjectArgumentValidatingTool` strips `bind_project`'s `forceRebind: null` before the strict check. Known gap, out of scope: a non-nullable `string` parameter still accepts null.
- **Root keys are derived.** Allowed root keys come from `ProtocolTool.InputSchema.properties`. Never hand-write them.
- **No side effects on rejection.** A rejection never reaches `WriteExecution`/`WriteRun`, an `IWriteAuditSink`, the worker or elicitation. No `contractVersion` changes.
- **Output schemas are unchanged.** Every tool's `tools/list` outputSchema must stay identical to `main`. `DisallowAdditionalProperties` closes the output schema too, so each tool keeps exactly the setting it has on `main`:
  - `true` for `get_project_status`, `browse_project_tree`, `bind_project`, `network_write`, `plc_write`, `hmi_write`, `vci_write` and `vci_read`;
  - `false` for the other 11 tools.

  The wrapper itself closes the input schema root.
- **`bind_project` keeps its contract:** per-action key and type validation, and `forceRebind: null` acceptance, as documented in `MULTIUSER_OPERATIONS_SUMMARY.md`.
- **`browse_project_tree` keeps its categories:** `validation_error`, `invalid_cursor`, `invalid_selector`.
- **Shell.** Run `dotnet` from PowerShell, never Bash, because Bash mangles `/p:`. Build with `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. Expect no warnings beyond the seven existing test-assembly nullable warnings.
- **Tests per task.** Run only each task's focused filters. Do not run the full suite per task; the orchestrator runs it once per branch, one worktree at a time.
- **Test project.** `TiaMcpServer.Tests` links host sources with `<Compile Include>`. Contracts is a `ProjectReference`.
- **Commits.** Commit after each task: a one-line conventional message with no body. Expected after each commit: one new commit, and `git status` is clean.
- **Docs come last,** after the live gate.
- **No TIA project names or paths** in the repo.
- **Check consumers before deleting a member.** Use `search_text` or `git grep`; jCodemunch `find_references` has returned false empties for C#.

## Review Focus

1. **Real clients sending stray keys or null optional booleans** are now rejected on 11 tools. The live gate covers this. The offline half (null booleans are rejected) is pinned by the conformance test in Task 2.
2. **The pre-check rejects input the SDK binder accepts**, for example `"5"` for an `int?`. Pinned by `NumberFromString_EntersLikeTheSdk` (Task 1).
3. **A bespoke validator runs after the strict check.** That would break `forceRebind: null` and the project-tree selector categories. Pinned by the existing `BindProject_NullForceRebind_UsesDefaultFalseThroughSdk` and `BrowseProjectTree_UnknownSelectorMemberReturnsCanonicalSelectorFailure`, both run in Task 2.
4. **Rejection messages echo caller data**, either values or paths longer than 256 characters. Pinned by `MalformedValue_IsNotEchoed` and `OverlongPath_IsOmitted` (Task 1).
5. **Closing input schemas changes an `outputSchema`.** Pinned by `OutputSchemasMatchMain` (Task 2).

## File Structure

- **`TiaMcpServer/Tools/StrictArgumentsTool.cs`** holds the wrapper and its schema- and binding-derived checks. After Task 2 it also holds `PlcWriteToolRegistration`'s schema transform and retired members.
- **`TiaMcpServer/Tools/McpToolRegistration.cs`** holds the single per-tool-type reject table.
- **`TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs`** is deleted.
- **`TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json`** is the outputSchema baseline captured from `main`.

**Release note:** All 19 tools now reject unknown, missing, mistyped or retired arguments, at the root or inside items, with the tool's own canonical JSON document (`validation_error`, `isError: true`) and no audit record. Previously the writes and `vci_read` returned plain text, and 11 tools silently ignored unknown root keys.

## After the tasks

These steps are not implementer tasks. They run after Task 3 and before Task 4.

### Branch verification (orchestrator, serialized across worktrees)

Run the stub build, then the full suite with coverage under `TestResults/c1`:
- process-local `TEMP`/`TMP` set to `TestResults/c1/temp`;
- `TiaMcpServer.Tests/coverage.runsettings`;
- the single-thread xUnit settings from `.github/workflows/ci.yml`.

Expected:
- 0 failed and 0 skipped;
- `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` passes;
- every changed executable line in `StrictArgumentsTool.cs` and in the `WithProductionTools` table is covered.

The final whole-branch review comes next, following the execution skill.

### Live gate (maintainer-authorized, not an implementer task)

Install the candidate. In Claude Code, in read-write mode, with any project open or none, check these:
1. In `tools/list`, every `inputSchema.additionalProperties` is `false`, and every `outputSchema` is unchanged.
2. `plc_read` with valid `operations` plus a root key `projectPath: "x"` returns canonical `validation_error`.
3. `save_project {dryRun: true}` gets past the argument check.
4. `network_write` whose item has an unknown member returns canonical `validation_error`, and no audit line is written.
5. `bind_project {forceRebind: null}` is still accepted.
6. Watch whether the client ever sends `null` for an omitted boolean. If it does, stop and escalate.

Record the candidate SHA and the outcomes privately. Pass both to the Task 4 implementer, together with the branch-verification counts.

---

### Task 1: Strict wrapper core

**Files:**
- Modify: `TiaMcpServer/Tools/StrictArgumentsTool.cs`. Rewrite `StrictArgumentsTool`, and add the helpers below. In Step 3, the existing `With*Tools` registrations in this file switch to `CreateClosedTool` with a temporary plain-text reject, passing `closeNestedSchemas: true`. That is their current setting.
- Create: `TiaMcpServer.Tests/Tools/StrictArgumentsToolTests.cs`.

**Interfaces:**
- Consumes (from main): `McpServerTool.Create(MethodInfo, object?, McpServerToolCreateOptions)` with `Services`, `SerializerOptions` and `SchemaCreateOptions`; `McpJsonUtilities.DefaultOptions`; `JsonSerializer.Deserialize(JsonElement, JsonTypeInfo)`; `McpProtocolTestHarness.StartAsync(McpAccessMode, Action<IMcpServerBuilder>)`.
- Produces:
  - `internal sealed class StrictArgumentsTool(McpServerTool inner, MethodInfo method, Func<string, CallToolResult> reject, IReadOnlyDictionary<string, string>? retiredItemMembers = null) : DelegatingMcpServerTool(inner)`.
    - It builds the root-key set, the required set and the parameter-type map once, in its constructor.
    - It overrides `ProtocolTool` to return a copy whose `InputSchema` root has `additionalProperties: false`.
  - `internal static McpServerTool CreateClosedTool(MethodInfo method, IServiceProvider services, Func<string, CallToolResult> reject, bool closeNestedSchemas, Func<AIJsonSchemaCreateContext, JsonNode, JsonNode>? transformSchemaNode = null, IReadOnlyDictionary<string, string>? retiredItemMembers = null)`.
    - It sets `SerializerOptions = McpJsonUtilities.DefaultOptions`.
    - It sets `DisallowAdditionalProperties = closeNestedSchemas`.
    - It returns the `StrictArgumentsTool`.
  - `internal static IReadOnlySet<string> RootKeys(McpServerTool tool)` reads the `properties` of `ProtocolTool.InputSchema`.
  - `internal static string Message(string toolName, string detail, IReadOnlySet<string> acceptedKeys)` returns exactly `"<tool> arguments do not match its input schema: <detail>. Accepted arguments: <keys sorted ordinally, comma-separated, or none>."`
  - `internal static IMcpServerBuilder WithClosedTools(this IMcpServerBuilder builder, Type toolType, Func<IServiceProvider, string, string, CallToolResult> reject, bool closeNestedSchemas, Func<AIJsonSchemaCreateContext, JsonNode, JsonNode>? transformSchemaNode = null, IReadOnlyDictionary<string, string>? retiredItemMembers = null, Func<string, McpServerTool, McpServerTool>? decorate = null)`.
    - It enumerates the public static `[McpServerTool]` methods ordered by `MetadataToken`.
    - `reject` receives `(services, toolName, message)`.
    - `decorate(toolName, strictTool)` receives the `StrictArgumentsTool`; its return value is what gets registered. A bespoke validator therefore runs before the strict check.

**Check order and detail text:**
1. Unknown root key, checked against the schema `properties`: `unknown argument '<key>'`.
2. Retired item member: `operation member '<name>' is retired: <hint>`.
3. Missing schema-`required` key: `missing required argument '<name>'`.
4. Each supplied argument is deserialized into its bound parameter type with `McpJsonUtilities.DefaultOptions`. On `JsonException`: `argument '<name>' is malformed at <Path>`. Drop the ` at <Path>` suffix when `JsonException.Path` is null or longer than 256 characters.
   - Never include values. Facts verified on SDK 2.2.0: an unmapped member yields path `$[0].bogus`, and a bad element yields `$[0]` with no value.
   - Parameters are matched by schema property name, which equals `ParameterInfo.Name`. DI services, `CancellationToken` and `McpServer?` are not in the schema and are skipped.
   - Only `JsonException` is mapped. Any other exception keeps the SDK handler path.

**"Rejected"** in the tests means: `IsError == true`, and the text starts with `REJECT:`.

- [ ] **Step 1: Write the tests.**
  - The harness registers one fixture tool through `CreateClosedTool(..., closeNestedSchemas: true)`.
  - The fixture is `Echo(int[] values, bool flag = false, string? name = null, Item[]? items = null)` and returns the text `"entered"`. `Item` is a test record with `[JsonUnmappedMemberHandling(Disallow)]` and one property, `int Id`.
  - `reject` returns `IsError = true` with the text `"REJECT:" + msg`.

  Cases:
  - `UnknownRootKey_IsRejectedBeforeEntry`: `{values:[1], extra:true}` is rejected. The text contains `unknown argument 'extra'` and `Accepted arguments: flag, items, name, values.`
  - `MissingRequiredArgument_IsRejected`: `{}` is rejected, and the text contains `missing required argument 'values'`.
  - `WrongRootKind_IsRejected`: `{values:[1], flag:"true"}` is rejected.
  - `NullForNonNullableBoolean_IsRejected`: `{values:[1], flag:null}` is rejected.
  - `NullForNullableString_Enters`: `{values:[1], name:null}` returns `"entered"`.
  - `NumberFromString_EntersLikeTheSdk`: `{values:["1"]}` returns `"entered"`.
  - `NestedUnmappedMember_IsRejectedWithPath`: `{values:[1], items:[{"bogus":1}]}` is rejected, and the text contains `$[0]`.
  - `MalformedValue_IsNotEchoed`: `{values:["secret-token-xyz"]}` is rejected, and the text does not contain `secret-token-xyz`.
  - `OverlongPath_IsOmitted`: `{values:[1], items:[{<300-char name>:1}]}` is rejected. The text contains neither ` at ` nor the 300-character name.
  - `RetiredItemMember_IsRejectedWithHint`: with the retired map `{ "old": "use 'new'" }`, `{values:[1], items:[{"old":1}]}` is rejected, and the text contains `use 'new'`.
  - `ValidCall_Enters`: `{values:[1]}` returns `"entered"`.
  - `InputSchemaRootIsClosed`: the fixture's `inputSchema.additionalProperties` is `false`.
- [ ] **Step 2: Add `CreateClosedTool` as a stub that returns the unwrapped SDK tool, plus stub `Message`, `RootKeys` and `WithClosedTools`.** Leave the existing `StrictArgumentsTool` and its registrations untouched.
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~StrictArgumentsToolTests`
  - Expected:
    - `UnknownRootKey_IsRejectedBeforeEntry` FAILS with `entered`.
    - The other rejection cases FAIL because the reply lacks `REJECT:`; it is the SDK error text.
    - The `_Enters` cases and `ValidCall_Enters` pass.
    - `InputSchemaRootIsClosed` passes, because the stub sets `DisallowAdditionalProperties = true`.
- [ ] **Step 3: Rewrite `StrictArgumentsTool` and implement the helpers as specified.** Switch the existing `With*Tools` registrations in this file to `CreateClosedTool(..., closeNestedSchemas: true)` with a temporary plain-text reject.
- [ ] **Step 4: Run the tests and build.**
  - Run: the Task test command below, then the stub build.
  - Expected: all PASS, and no new warnings.
- [ ] **Step 5: Commit.** `refactor: derive strict tool arguments from schema and binding types`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~StrictArgumentsToolTests|FullyQualifiedName~GuardedWriteMcpTests|FullyQualifiedName~VciReadMcpTests"`

### Task 2: Canonical rejection for all 19 tools through one table

**Files:**
- Modify: `TiaMcpServer/Tools/McpToolRegistration.cs`.
  - Add `WithProductionTools` and `private static CallToolResult WriteRejection<TItem,TEffect,TVerification,TResponse>(IWriteDomain<TItem,TEffect,TVerification,TResponse> domain, string message)`.
  - `WithAccessModeTools` calls `WithProductionTools` for its 13 tool types, in today's order. Every `WithTools<>` and `With*Tools()` call goes away.
- Modify: `TiaMcpServer/Tools/StrictArgumentsTool.cs`.
  - Delete `VciReadToolRegistration`, `VciWriteToolRegistration` and `HmiWriteToolRegistration`.
  - Shrink `PlcWriteToolRegistration` to `internal AddObEventClassEnum` and `internal RetiredItemMembers`.
- Delete: `TiaMcpServer/Tools/NetworkWriteArgumentValidatingTool.cs`, and remove its `<Compile Include>` from `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Modify: `TiaMcpServer/Tools/ProjectReadTools.cs`.
  - Delete `ProjectReadToolRegistration.WithProjectReadTools`, and make its `AlignSelectorSchemaNullability` `internal`.
  - Change `ProjectTreeArgumentValidatingTool.ValidationFailure` from `private static CallToolResult ValidationFailure(string category)` to `internal static CallToolResult ValidationFailure(string category, string message)`. Existing callers pass today's fixed text.
- Modify: `TiaMcpServer/Tools/ProjectBindingTools.cs`. Delete `ProjectBindingToolRegistration.WithProjectBindingTools`, and make its `AllowNullForceRebind` `internal`.
- Modify: the four read tools, `Plc/PlcReadTools.cs`, `Network/NetworkReadTools.cs`, `Hmi/HmiReadTools.cs` and `Vci/VciReadTools.cs`. Change `private static CallToolResult Error(string category, string message)` to `internal`.
- Modify: `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`. `RegisterToolType<TTools>` becomes `builder.WithProductionTools(typeof(TTools))`.
- Create:
  - `TiaMcpServer.Tests/TestSupport/ArgumentRejectionAssert.cs`
  - `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json`, registered in the test csproj as `<None Include="Tools\Snapshots\output-schemas.json" CopyToOutputDirectory="PreserveNewest" />`
- Modify tests:
  - `Tools/ToolOutputContractConformanceTests.cs`
  - `Tools/McpToolSchemaTests.cs`
  - `Tools/LifecycleMcpProtocolTests.cs`
  - `Plc/PlcGuardedWriteMcpTests.cs`
  - `Network/NetworkGuardedWriteMcpTests.cs`
  - `Network/NetworkStructuredProtocolTests.cs`
  - `Hmi/HmiGuardedWriteMcpTests.cs`
  - `Vci/VciGuardedWriteMcpTests.cs`
  - `Vci/VciReadMcpTests.cs`
  - any protocol test that sends a stray key (Step 6)

**Interfaces:**
- Consumes (Task 1): `CreateClosedTool`, `WithClosedTools` (with the `closeNestedSchemas` and `decorate` semantics), `Message`, `RootKeys`.
- Consumes (from main):
  - `StructuredToolResult.CreateCanonical(string, bool)` and `CanonicalJson.Serialize`.
  - `StructuredStandaloneResult.Create<TPayload>(tool, null, [], new StructuredOperationFailure(category, message))`, with `ProjectStatusInfo` (get_project_status), `CompileCheckReport` (compile_check), `CrossReferenceReport` (read_cross_references) and `ProjectBindingResult` (bind_project).
  - `IWriteDomain<…>.Compose(new WriteReport<TEffect,TVerification>(WritePhases.Error, false, new WriteToolError(WorkerFailureCategories.ValidationError, msg), [], [], [], null, default))`. This is the shape `WriteRun.Finish` serializes, without its audit append. Every `Compose` is safe on a freshly constructed domain.
  - The write-domain constructors: `(client)`; VCI `(client, VciBreadcrumbLocation)`; lifecycle `(client, new LifecycleWriteItem(toolName))`.
- Produces:
  - `public static IMcpServerBuilder WithProductionTools(this IMcpServerBuilder builder, Type toolType)`. It throws `ArgumentOutOfRangeException` for a type not in the table.
  - `internal static class ArgumentRejectionAssert { static void IsCanonicalValidationError(CallToolResult reply, string tool) }`. It asserts:
    - `StructuredContractInspector.FindViolations(reply)` is empty;
    - `IsError` is true;
    - the category is `validation_error`, read from `error.category`, or from `failure.category` for `browse_project_tree`.
  - `ProjectTreeArgumentValidatingTool.ValidationFailure(string category, string message)`, which Task 3 consumes.

**Reject table (`WithProductionTools`):**

| Tool type | Reject | `closeNestedSchemas` | Transform / decorate |
|---|---|---|---|
| `PlcReadTools`, `NetworkReadTools`, `HmiReadTools` | `X.Error(ValidationError, msg)` | false | — |
| `VciReadTools` | `VciReadTools.Error(ValidationError, msg)` | true | — |
| `ProjectReadTools` | `get_project_status`: standalone result. `browse_project_tree`: `ProjectTreeArgumentValidatingTool.ValidationFailure(ValidationError, msg)` | true | `ProjectReadToolRegistration.AlignSelectorSchemaNullability`; decorate `browse_project_tree` with `ProjectTreeArgumentValidatingTool` |
| `ProjectBindingTools` | standalone result | true | `ProjectBindingToolRegistration.AllowNullForceRebind`; decorate with `BindProjectArgumentValidatingTool` |
| `CrossReferenceReadTools`, `ProjectEngineeringTools` | standalone result | false | — |
| `NetworkWriteTools`, `PlcWriteTools`, `HmiWriteTools`, `VciWriteTools` | `WriteRejection(new XWriteDomain(...), msg)` | true | `plc_write`: `AddObEventClassEnum` and `RetiredItemMembers` |
| `ProjectWriteTools` (six lifecycle tools) | `WriteRejection(new LifecycleWriteDomain(client, new LifecycleWriteItem(toolName)), msg)` | false | — |

`WriteRejection` serializes `domain.Compose(report)` and returns `StructuredToolResult.CreateCanonical(text, isError: true)`. It uses no audit sink.

- [ ] **Step 1: Capture the output-schema baseline.** On the unchanged registration, start the production surface in Full mode. Write `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` as a JSON object mapping each tool name to its raw `outputSchema`, and add the csproj `<None>` entry.
  - Expected: 19 entries.
- [ ] **Step 2: Write the tests.**
  - `ToolOutputContractConformanceTests.EveryRegisteredToolRejectsArgumentShapeErrorsCanonically`: production surface in Full mode, with a `FakeWorkerRequestLog` wired as in `BindProjectToolProtocolTests.Fixture`. For every listed tool:
    - send `{unexpectedArgument: true}`;
    - send `null` for each root property whose schema `type` is exactly `"boolean"`;
    - if the tool has `operations`, send one item carrying `unexpectedMember: 1`.

    Every reply passes `ArgumentRejectionAssert`. The worker method log is unchanged, and there are no audit lines.
  - `McpToolSchemaTests.EveryRegisteredToolSchemaIsClosed`: every `inputSchema.additionalProperties` is `false`.
  - `McpToolSchemaTests.OutputSchemasMatchMain`: every `outputSchema` equals its snapshot entry, and the tool set equals the snapshot's 19 keys.
  - Strengthen these existing rows to use `ArgumentRejectionAssert`. Keep their no-worker and no-audit assertions. Write tools also check `phase == "error"` and the tool name.
    - `PlcGuardedWriteMcpTests.LegacyOrMalformedArguments_DoNotReachWorker`. The `yamlContent`/`sourceContent` rows require `error.message` to contain `use 'content' with 'expectedContentHash'`.
    - `NetworkGuardedWriteMcpTests.LegacyOrMalformedArguments_DoNotReachWorker`
    - `HmiGuardedWriteMcpTests.StrictWrapperRejectsWithoutWorkerOrAudit`
    - `VciGuardedWriteMcpTests.StrictArgumentsRejectBeforeWorkerAndAudit`
    - `VciReadMcpTests.MalformedInputsRejectBeforeWorker`
    - `NetworkStructuredProtocolTests.NetworkWrite_RejectsMalformedNestedInputAtTheProtocolBoundary`
  - `LifecycleMcpProtocolTests.LifecycleArgumentShapeErrors_AreCanonicalAndUnaudited`: read-write mode, with the existing elicitation-counting `ClientOptions(handler)`. Each row is canonical, writes no `writes-*.jsonl` lines and triggers 0 elicitations. Rows:
    - `save_project {confirm: true}`
    - `close_project {saveBeforeClose: null}`
    - `open_project {projectPath: "C:\\Fixture\\Project.ap21", forceRebind: null}`
    - `archive_project {archiveDirectory: "C:\\Archive", archiveName: "a", dryRun: "true"}`
- [ ] **Step 3: Run the failing tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~GuardedWriteMcpTests|FullyQualifiedName~VciReadMcpTests|FullyQualifiedName~NetworkStructuredProtocolTests|FullyQualifiedName~LifecycleMcpProtocolTests|FullyQualifiedName~BindProjectToolProtocolTests|FullyQualifiedName~ProjectTreeStructuredProtocolTests"`
  - Expected:
    - FAIL: the open tools and the cases that return SDK text in the conformance test; the 11 open schemas in `EveryRegisteredToolSchemaIsClosed`; the plain-text rows; the lifecycle null and type rows.
    - PASS: `OutputSchemasMatchMain`, `BindProject_NullForceRebind_UsesDefaultFalseThroughSdk`, `BrowseProjectTree_UnknownSelectorMemberReturnsCanonicalSelectorFailure` and `Inspection_InvalidArgumentsRejectedBeforeDispatch`.
- [ ] **Step 4: Implement.**
  - Add the table, `WriteRejection` and the visibility and signature changes.
  - Delete the old registrations, `NetworkWriteArgumentValidatingTool.cs` and its csproj link.
  - Switch `RegisterToolType` to `WithProductionTools`.
- [ ] **Step 5: Rerun the Step 3 filter and build.**
  - Run: the same filter, then the stub build.
  - Expected: all PASS, including `StructuredToolReturnsOneCanonicalDocumentWithoutNestedJson` and the four tests listed under Step 3's PASS. No new warnings.
- [ ] **Step 6: Clean up stray keys in protocol tests.**
  - Run: `git grep -l "McpProtocolTestHarness\.\|StartProductionSurfaceAsync" -- TiaMcpServer.Tests`. This gives about 25 files. Build a `FullyQualifiedName~<ClassName>` filter from their class names and run it.
  - Expected: PASS, or failures whose reply text contains `arguments do not match its input schema`.
  - Fix each failure by removing the stray key the test sent. Never loosen the check. If a stray key is required for the test's purpose, stop and report it. Rerun until everything passes.
- [ ] **Step 7: Commit.** `refactor: register all tools through one closed-argument helper`

**Task test command:** the Step 3 filter, followed by the Step 6 filter.

### Task 3: `browse_project_tree` uses the shared wording

**Files:**
- Modify: `TiaMcpServer/Tools/ProjectReadTools.cs` (`ProjectTreeArgumentValidatingTool`).
- Modify: `TiaMcpServer.Tests/Project/ProjectTreeStructuredProtocolTests.cs`.

**Interfaces:**
- Consumes (Task 1): `StrictArgumentsTool.RootKeys(McpServerTool)` and `StrictArgumentsTool.Message(string toolName, string detail, IReadOnlySet<string> acceptedKeys)`.
- Consumes (Task 2): `ProjectTreeArgumentValidatingTool.ValidationFailure(string category, string message)`. The validator wraps the strict tool, so it runs first.
- Produces: none.

- [ ] **Step 1: Write the tests.**
  - `ProjectTreeStructuredProtocolTests.UnknownRootKey_UsesGenericValidationMessage`: `{extra: true}` returns `failure.category == "validation_error"`. `failure.message` starts with `browse_project_tree arguments do not match its input schema: unknown argument 'extra'`.
  - `ProjectTreeStructuredProtocolTests.WrongRootType_UsesGenericValidationMessage`: `{pageSize: "x"}` returns `validation_error`. Its message starts with `browse_project_tree arguments do not match its input schema: argument 'pageSize' is malformed`.
- [ ] **Step 2: Run them.**
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~ProjectTreeStructuredProtocolTests`
  - Expected: both new tests FAIL, because today's message is fixed text. The existing tests pass.
- [ ] **Step 3: Implement.** Replace `AllowedArguments` with `RootKeys(innerTool)`. On every `validation_error` path, pass `Message(...)`, using detail `unknown argument '<key>'` or `argument '<name>' is malformed`. The `invalid_cursor` and `invalid_selector` texts stay unchanged.
- [ ] **Step 4: Run them again.**
  - Run: the same filter.
  - Expected: all PASS.
- [ ] **Step 5: Commit.** `refactor: derive browse_project_tree root keys from its schema`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~ProjectTreeStructuredProtocolTests`

### Task 4: Docs and evidence

Dispatch this task only after the live gate. The controller passes in the branch-verification counts, the coverage, and the live-gate SHA and outcomes. Use those values; do not run or invent them.

**Files:** modify:
- `AGENTS.md`
- `docs/roadmap/json-contract.md`
- `docs/roadmap/network-operations.md`
- `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`
- `docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md`
- `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`
- `docs/guides/mcp-client-configuration.md`
- `docs/guides/troubleshooting.md`
- `docs/IMPROVEMENT_LOG.md`
- `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`

**Interfaces:** none.

- [ ] **Step 1: Update the pre-entry rejection wording.** The current sites, which describe SDK/wrapper pre-entry rejection as a normal MCP error with no audit, are:
  - `AGENTS.md:112`
  - `NETWORK_OPERATIONS_SUMMARY.md:373`
  - `NETWORK_PHASE4_SUBNET_LIFECYCLE.md:214`
  - `mcp-client-configuration.md:56`
  - `troubleshooting.md:124`
  - `json-contract.md:299`
  - `network-operations.md:80`

  In each, say instead that argument-shape errors return the tool's canonical document with `validation_error` and `isError: true`, and no audit record. In `json-contract.md`, remove the plain-text exception and add the pre-entry rule under Delivery.
- [ ] **Step 2: Update `PROJECT_OPERATIONS_SUMMARY.md`.** State that lifecycle root arguments are strict, and give the null rule from Global Constraints.
- [ ] **Step 3: Update `IMPROVEMENT_LOG.md`.** Record C-1 as done. Log the gap: a non-nullable `string` parameter (for example `open_project.projectPath`) still accepts JSON null.
- [ ] **Step 4: Update the review plan.**
  - Under C-1, add "Regression evidence" and "Full offline evidence" sub-bullets in the format of the B-9 bullets, plus the live-gate result.
  - In the C-0 text, replace "Do C-0 before C-1" with a pointer to the Workstream C plan.
- [ ] **Step 5: Check.**
  - Run: `git grep -n -i "normal MCP error" -- AGENTS.md docs/roadmap docs/SupportedOperations docs/guides`
  - Expected: no output.
- [ ] **Step 6: Commit.** `docs: document canonical argument rejection for all tools`

**Task test command:** the Step 5 grep.
