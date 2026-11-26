# Issue #224 PR 3: `patch_block_logic` implementation plan

Date: 2026-11-26. Status: prepared for implementation; tasks have not been executed.

**Goal:** Let a client update a PLC block by sending unique text replacements, while retaining the existing content hash, diff, guards, import, verification and audit guarantees.

**Order:** This is the next PR: **PR 1 → PR 3 → PR 2**, as specified in the [approved design](../specs/2026-11-26-issue-224-large-block-writes-design.md), section 5. PR 1 is present on local `main` at `83842c3e` (merge of PR #239). The shared parser is `TiaMcpServer.Contracts.Block.BlockBundleText`; worker adapters remain in place. PR 2's encoder changes are outside this plan.

**Architecture:** Resolve edits in the host against the fresh plan-time export, then create one internal `update_block_logic` execution item. A per-call map supplies that item to mutation, typed payload decoding and verification; the pipeline retains the original public request for response names and audit. Add one optional domain audit hook to record the full resolved input selected for a patch attempt.

**Stack:** C#; host/tests net10.0, Contracts netstandard2.0, worker net48; xunit. No new dependency or worker method.

**Session boundary:** This deliverable is a plan only. Do not implement its tasks or create commits in the planning session. Commands below describe later implementation checks, except the documentation validation recorded at the end.

## Requirements that every task inherits

- Add one operation to the existing `plc_write` tool; keep its tool registration and access modes. Writes become contract `2.1`; reads stay `1.0`. The server still exposes 9 tools in read-only and 21 in writable modes.
- Public fields: `operationId`, `operation: "patch_block_logic"`, `blockPath`, optional `format`, `expectedContentHash`, and one or more `edits: [{oldText,newText}]`. Default format is XML. Types remain full-document writes.
- Validate the public request and authorize its public operation name before normalization, binding or worker reads. Preserve the 1–50 item rule and all existing argument-shape behavior.
- The hash is over the **whole served export**, including XML bundle companions and separator bytes. Check it before resolving anchors. Do not calculate the precondition hash from the XML part or from patched text.
- XML matching and replacement operate on the authoritative `.xml` document only; source matching operates on the whole source. Return XML-only content for the existing importer, rather than recomposing a bundle.
- Each anchor must be nonblank and match exactly once, counting overlapping occurrences. All anchors refer to the unchanged original base. Reject overlapping edit spans; allow adjacent spans and empty replacements.
- CRLF and LF are equal for matching. Map normalized positions back to original offsets and preserve every unmatched character. Preserve lone CR as ordinary text.
- Preserve all existing binding leases, guards, worker hash rechecks, typed outcomes, compile-result separation, verification, stop-on-first-failure behavior and audit. No rollback or automatic replay.
- Keep call state in one `PlcWriteDomain` instance. No persistence, file references, caller-supplied filesystem access, working copies, translation layer, new export format or changed Openness import path.
- Preserve existing dependency tracking. Do not make all successive content writes dependent merely to support this feature.
- A new host source file must be explicitly linked by `TiaMcpServer.Tests.csproj`. `Safety/Pipeline` already uses a source glob. The host product project uses normal SDK inclusion.

## Current code and the implementation decisions

| Current source | Consequence for this PR |
| --- | --- |
| `Plc/PlcOperationRequest.cs`, `PlcOperationCatalog.cs`, `PlcWriteTools.cs` | Extend the existing public DTO, closed field catalog and tool description. There is no separate patch tool. |
| `Plc/PlcFormatNames.cs` | Every non-type operation already defaults to XML. Pin patch behavior with tests; no alias or production change is needed here. |
| `Plc/PlcWritePlanner.cs` | `ResolveAsync` currently calls `state.Resolve` before exporting content. A patch branch must resolve its text before entering that name-keyed path, and reuse its one export. |
| `Plc/PlcWriteDomain.cs` | Currently forwards public items to the invoker and verification. Introduce the per-call execution map here. |
| `Plc/PlcContentDiffSides.cs` | PR 1 already selects XML-only diff sides while the planner checks the whole export hash. Reuse it. |
| `Contracts/Block/BlockBundleText.cs` | `Parse`/`FindSimaticMlDocument` select scope; `Compose` is not byte-preserving and must not reconstruct patched XML bundles. |
| `Safety/Pipeline/WriteRun.cs` | Original `Items` feed `RequestedOperations`. Preserve them; add resolved input beside the existing per-item audit data. |
| `Tools/StrictArgumentsTool.cs`, `Tools/McpToolRegistration.cs` | PLC already closes nested schemas and turns deserialization failures into canonical argument rejections. Reuse this behavior. |

Paths in the table are relative to `TiaMcpServer/` except `Contracts/`, which means `TiaMcpServer.Contracts/`.

### Resolved item and presentation

Create a normalized request only after the patch's base hash and edits have succeeded:

```csharp
new PlcOperationRequest
{
    OperationId = requested.OperationId,
    Operation = "update_block_logic",
    BlockPath = requested.BlockPath,
    Format = resolvedFormat,
    ExpectedContentHash = requested.ExpectedContentHash,
    Content = resolvedContent
};
```

Do not set `Edits` on this internal request. Do not revalidate it as a public full-replacement request: an allowed deletion can produce an empty document, which the existing importer may refuse. Do not mutate `requested` or substitute a different list for `WriteRun.Items`.

Every existing execution site receives the internal name: `PlcWorkingState.Resolve`/`Apply`, content diff planning, `PlcWorkerInvoker`, `PlcPayloadContract.DecodeWrite` through `ProjectWrite`, and `PlcVerificationPolicy`. Public effects and projected batch entries retain `operation: "patch_block_logic"`.

### Audit field

Add conditional `WriteAuditItem.ResolvedOperation` (wire name `resolvedOperation`). For an attempted patch, it contains the complete normalized request above, including exact full `content`, resolved `format`, original hash and selector. `RequestedOperations` retains the public patch and its edits.

The field means **resolved input selected for attempted mutation**, not proof of dispatch or successful application. It is present for a patch attempt even if the worker rejects it, the transport fails, or the client reports `NotSent`; absent on previews, pre-mutation denials, skipped items and failed late replans. Capture the request immediately before invoking the worker. Existing dispatch/outcome evidence states whether the request reached the worker. Keep audit record version 2; this is an additive conditional member. Do not budget, abbreviate or omit its content according to public-response limits.

## Task 1: Public operation, nested edit shape and access gates

**Files**

- Create `TiaMcpServer/Plc/PlcTextEdit.cs`.
- Modify `TiaMcpServer/Plc/PlcOperationRequest.cs`.
- Modify `TiaMcpServer/Plc/PlcOperationCatalog.cs`.
- Modify `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs`.
- Modify `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs`.
- Modify the `PlcWriteToolRegistration` helper in `TiaMcpServer/Tools/StrictArgumentsTool.cs` and its transform reference in `TiaMcpServer/Tools/McpToolRegistration.cs`; keep the shared strict runtime validator unchanged.
- Modify `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` to link `PlcTextEdit.cs`.
- Modify `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs` for the optional edits member.
- Extend `TiaMcpServer.Tests/Plc/PlcOperationCatalogTests.cs`, `PlcGuardedWriteMcpTests.cs`, `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs`, and `TiaMcpServer.Tests/Project/ProjectCapabilityCatalogTests.cs`.

**Interfaces**

- `public sealed class PlcTextEdit` has required string `OldText` and `NewText`, with descriptions and closed unmapped-member handling.
- `PlcOperationRequest.Edits` is an optional `PlcTextEdit[]?` property with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. Omission means no edits. Explicit JSON null and null array elements are shape errors.
- Catalog entry: required `blockPath`, `expectedContentHash`, `edits`; optional `format`. Add `edits` to applicability/presence checking, rather than creating a second validator.
- Public `patch_block_logic` is `ProjectMutation` in `OperationPolicyCatalog`. Add it to `ProjectCapabilityCatalog.ProjectContentOperations` beside the other public PLC batch names: standalone/local-session supported, server-project unsupported. No new worker dispatch case or `OperationFor` alias.

- [ ] Add red catalog cases: valid XML-default/source patches; missing/empty edits; whitespace anchor; empty replacement accepted; `content` on patch, `edits` on a full update, and `typePath` on patch rejected. Malformed/mismatched hashes remain invalid, including a source hash with omitted XML-default format.
- [ ] Extend `ValidateWriteCall`'s existing create-then-content refusal to patch. Test both `PLC/Name` and equivalent `PLC/Blocks/Name`, in both spellings across the two items. The error must name the public operation and occur before worker activity.
- [ ] Test public-name access: read catalog refuses patch; read-only authorization denies it; read-write/full allow it. Test container capability descriptions as well as `Supports`.
- [ ] Add registered-tool shape cases for nonarray/null `edits`, null element, missing/null/nonstring `oldText` or `newText`, and unknown edit members. Assert canonical `validation_error`, `isError:true`, no worker requests and no audit. At this task's boundary, prove a literal `newText:""` passes strict deserialization/schema validation; defer its successful preview/apply assertion to Task 6.
- [ ] Implement shape validation without changing the shared strict validator: apply `[JsonRequired]` to both edit strings; use field-backed setters throwing `JsonException` for explicit null. `Edits`' setter rejects explicit null and null members; omission leaves its backing field null. Empty arrays and whitespace anchors remain entered catalog validation, with the normal audit behavior.
- [ ] Align the generated schema with these runtime rules. Rename the existing PLC helper `AddObEventClassEnum` to `TransformSchema`, retaining its signature and OB enum logic, and update its registration reference. Use the existing `ProjectReadToolRegistration.AlignSelectorSchemaNullability` small `RemoveNullType` pattern to remove null from `edits`, its items, and the edit strings. Test the generated schema directly: edits optional but array-only, items object-only, both strings required and string-only. Do not add a recursive validation engine or custom converter.
- [ ] Omit absent `Edits` from serialized requests so ordinary operations and internal normalized items do not acquire a null member that their input setter would reject. Register this conditional request member with the condition: supplied only on public patch requests, absent for other operations and normalized execution requests. Add `PlcOperationRequest` to the conditional-member scan in this task if it is not already included.
- [ ] Update `ExpectedWriteFields` and its hardcoded write counts from 14 to 15. Rename `WriteSpecsKeepTheRetiredBatchFieldSets` to describe the current dedicated catalog while keeping its field coverage.
- [ ] Run the focused public-shape/catalog tests. The new positive catalog case should initially fail because the operation is unknown; shape tests should initially expose that the nested DTO/schema is missing. Finish this task with the public surface tests green; full execution is covered later.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcOperationCatalogTests|FullyQualifiedName~PlcGuardedWriteMcpTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~ProjectCapability|FullyQualifiedName~ConditionalMemberRegisterTests" /p:UseTiaPortalReferenceStubs=true -m:1
```

## Task 2: Pure patch resolver and exact line-ending behavior

**Files**

- Create `TiaMcpServer/Plc/PlcBlockLogicPatch.cs`.
- Create `TiaMcpServer.Tests/Plc/PlcBlockLogicPatchTests.cs`.
- Link the new host source in `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.

**Interface**

```csharp
internal static class PlcBlockLogicPatch
{
    internal static string Apply(
        string format, string currentExport, IReadOnlyList<PlcTextEdit> edits);
}
```

The helper returns the exact worker `content`: patched `.xml` document for XML, patched complete text for source. It throws `ArgumentException` for patch violations; the planner translates that to `validation_error` with operation ID. Catch and translate `BlockBundleFormatException` at this boundary as well. Use zero-based `edits[0]` indices in diagnostics, saying `matched zero times`, `matched several times`, or identifying the two overlapping edit indices. Do not expose the whole export in an error.

- [ ] Write tests from the literal vectors below and run them red before creating the helper. Missing helper/type is the initial expected compile failure.
- [ ] For XML, call `BlockBundleText.Parse("current.xml", currentExport)` and `FindSimaticMlDocument`. Use its exact `Content`, including any separator-adjacent bare LF. No `.xml` means `validation_error` with the existing guidance quoted below. Source uses the whole string.
- [ ] Normalize CRLF to LF in the scope and anchors only for matching. Build a boundary map of normalized length + 1 back to original character offsets; a normalized LF maps across the full original CRLF. Leave lone CR unchanged.
- [ ] Find each anchor ordinally against the same normalized base, advancing by one character after a match so overlapping occurrences count. Stop after the second occurrence; zero/several are refusals, not a choice of match.
- [ ] Map unique matches to original half-open spans, sort by original start, and reject `next.Start < previous.End`. Equality permits adjacent replacements. Repeated edits of the same span are overlapping, even if replacements are identical.
- [ ] For each original matched span, use CRLF if it contains only CRLF terminators, LF if it contains only LF terminators. For no terminator or mixed terminators, use the terminator of the original line containing the match start; if that line has none, use CRLF.
- [ ] Normalize CRLF/LF in each `newText` to that chosen terminator, preserving lone CR. Assemble once over sorted original spans; do not repeatedly replace in the evolving output.
- [ ] Reject `BlockBundleText.ContainsDelimiterLine(newText)`, including malformed delimiter candidates. Also check logical lines created or joined across edit boundaries so a delimiter cannot be assembled from base plus replacement. Use the existing predicate, and restrict this check to introduced candidates. Track whether the qualifying delimiter prefix and line-start were inherited from one unchanged original span at an original line-start. A pre-existing source candidate remains allowed even when its suffix is edited; do not require whole-line equality or apply a blanket final-result ban.
- [ ] Permit a replacement that deletes the entire scope. Do not add an undocumented nonblank resolved-content rule; later existing import validation decides whether that document is acceptable.
- [ ] Run the helper tests green, then the existing `PlcContentDiffSidesTests` and `BlockBundleTextTests` unchanged.

Missing-XML message:

```text
This bundle contains no Simatic ML (.xml) document. The block could not be exported as Simatic ML, which usually means it is inconsistent — compile it in TIA Portal and read it again before writing.
```

| Test | Base / edits | Expected |
| --- | --- | --- |
| Unique replacement/deletion | `abc`; `b→B`, separately `b→""` | `aBc`, `ac` |
| Whole-scope deletion | `abc`; `abc→""` | Empty resolved string, no helper refusal |
| Whitespace anchor | `abc`; old text `" \r\n\t"` | Validation error |
| Missing anchor | `abc`; `z→Z` | `edits[0]`, zero matches |
| Overlapping occurrences | `aaaa`; `aaa→X` | Several matches, including the occurrence starting at index 1 |
| Intersecting edit spans | `abcdef`; `bcd→X`, `def→Y` | Overlap naming both indices |
| Adjacent/reversed edit order | `abcdef`; `bc→X`, `de→Y`; separately `ef→Z`, `ab→LONG` | `aXYf`; `LONGcdZ` |
| No cascading | `cat dog`; `cat→wolf`, `wolf→fox` | Second edit matches zero times |
| Repeated same edit | `abc`; `b→B`, `b→B` | Overlap |
| LF anchor on CRLF | `A\r\nB\r\nC`; `A\nB→X\nY` | `X\r\nY\r\nC` |
| CRLF anchor on LF | `A\nB\nC`; `A\r\nB→X\r\nY` | `X\nY\nC` |
| No terminator in match | `A\r\nB\r\n`; `B→X\nY` | `A\r\nX\r\nY\r\n` |
| Mixed matched terminators | `A\r\nB\nC`; `A\nB\nC→X\nY` | `X\r\nY` |
| Unterminated final line | `A\nB`; `B→X\nY` | `A\nX\r\nY` |
| Lone CR | `A\rB`; `A\nB→X` | Zero matches; literal `A\rB` does match |
| XML scope | XML and companion each contain `needle` once | Only XML counts; output has no bundle separators/companions |
| Companion-only anchor | `needle` appears only in `.s7dcl` | Zero matches |
| XML selection | Raw XML; bundle with `B.XML`; companion-only bundle; malformed bundle | First two patch; last two fail with guidance/parser validation |
| Separator-induced LF | Compose XML `A\r\nZ` followed by companion, yielding XML scope `A\r\nZ\n`; `Z→X\nY` | XML-only `A\r\nX\nY\n` |
| Delimiter in replacement | `newText` contains `\n--- FILE: B.xml ---\n` or `\n--- FILE: bad` | Validation error identifying edit index |
| Delimiter assembled at boundary | `--- X`; `X→FILE: B.xml ---` | Validation error before worker activity |
| Delimiter exposed by deletion | `x--- FILE: B.xml ---`; `x→""` | Validation error before worker activity |
| Inherited source delimiter | Source `--- FILE: existing\nvalue`; `value→changed` | Existing line retained, patch accepted |
| Inherited prefix with edited suffix | Source `--- FILE: existing\nvalue`; `existing→changed` | Existing delimiter prefix retained, patch accepted |
| Large document | 72,141-character base with a unique short anchor | Only requested span changes; no arbitrary new text-size/edit-count limit |

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcBlockLogicPatchTests|FullyQualifiedName~PlcContentDiffSidesTests|FullyQualifiedName~BlockBundleTextTests" /p:UseTiaPortalReferenceStubs=true -m:1
```

## Task 3: Resolve patches during planning, with one export

**Files**

- Modify `TiaMcpServer/Plc/PlcWritePlanner.cs`.
- Extend `TiaMcpServer.Tests/Plc/PlcWritePlannerTests.cs`.

**Interfaces**

Add PLC-local carriers in `PlcWritePlanner.cs`, leaving shared generic `PlanResult`/`ReplanResult` unchanged:

```csharp
public sealed record PlcWritePlanResult(
    WritePlan<PlcWriteEffect> Plan,
    IReadOnlyDictionary<string, IReadOnlyList<FiredGuard>> Guards,
    IReadOnlyDictionary<string, PlcOperationRequest> ResolvedOperations);

public sealed record PlcWriteReplanResult(
    ItemReplan<PlcWriteEffect> Replan,
    IReadOnlyList<FiredGuard> Guards,
    PlcOperationRequest? ResolvedOperation);
```

`PlcWritePlanner.PlanAsync` returns `Task<PlcWritePlanResult>`; `ReplanAsync` returns `Task<PlcWriteReplanResult>`. The map contains successful patch resolutions only. Extend the private item outcome to carry its resolved execution request; failed results supply an empty map/null. Existing tests retain `.Plan`, `.Guards` and `.Replan` access.

- [ ] Add a planner fixture using the existing fake-worker setup: current XML bundle with a companion, hash from the complete bundle using the existing planner-test hash helper, and one XML line changed by edits. Assert `ContentDiff` changes one line each side, precondition evidence describes the whole export, and the resolved request is XML-only with exact format/hash/path/ID.
- [ ] Test that using the XML-part hash is `state_changed`. A stale hash plus a missing anchor must return `state_changed` first. Patch lookup failures on a valid base hash are whole-plan `validation_error`; an earlier planned create must not have been dispatched.
- [ ] Test omitted XML format and explicit source format; unreadable/empty export and incomplete inventory retain `plc_state_unverifiable`; missing/ambiguous target retains its existing category. None may fabricate a runnable resolved item.
- [ ] In `ResolveAsync`, resolve inventory/PLC and working state as today. For patch, export the block once, check the whole-export hash, call the pure helper, and create the normalized execution request at this single point. Call `state.Resolve` with that execution request and return it on the private outcome.
- [ ] Keep state advancement in the outer `PlanAsync` loop: calculate `LastTouchedBy(effect.Target)` before changing state, then call `state.Apply(executionItem)` exactly once for a resolved patch, using the original item for ordinary operations. If a patch has only an unreadable/empty-export guarded outcome and no execution request, skip its `Apply`; never fall back to applying its public patch name. Store successful resolved patches in the result map. `ResolveAsync` and `ReplanAsync` must not introduce a second `Apply` or calculate a self-dependency.
- [ ] Reuse the already fetched export for `PlcContentDiffSides.Select` and `PlcContentDiff.Build`; do not route back through an unconditional second export. Factor a small shared content-read/diff path with normal full replacements if useful, preserving their read ordering and failure precedence.
- [ ] Preserve incomplete-inventory guard construction for the patch branch. An unreadable export uses the existing unresolved descriptive effect/guard behavior and leaves no execution item. Preserve the per-call diff excerpt budget.
- [ ] Public effects use `effect with { Operation = requested.Operation }`; target identity, diff, preconditions and dependency lookup come from the execution item. Apply working state using the execution item, not this presentation name.
- [ ] Implement the same resolution path in `ReplanAsync`, returning the new execution request only on successful resolution. Pin a worker-request count: one block export per initial patch plan, one per late replan, and no extra host export for bundle parsing or diffing. Worker mutation/reverification exports remain their existing separate stages.
- [ ] Run the planner tests green, including unchanged PR 1 XML-only diff tests.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcWritePlannerTests|FullyQualifiedName~PlcContentDiffSidesTests" /p:UseTiaPortalReferenceStubs=true -m:1
```

## Task 4: Execute and verify normalized items without changing public requests

**Files**

- Modify `TiaMcpServer/Plc/PlcWriteDomain.cs`.
- Extend `TiaMcpServer.Tests/Plc/PlcGuardedWriteDomainTests.cs`, `PlcWriteInvokerTests.cs`, `PlcWriteVerifierTests.cs`, `PlcWritePayloadContractTests.cs`, and `PlcWorkingStateTests.cs` where those fixtures own the assertions.
- Reuse/extend `TiaMcpServer.Tests/Plc/PlcGuardedWriteFixture.cs`; change the fake-worker fixture only if its existing scripted replies cannot represent the needed export sequence.

**Interfaces**

- A domain-owned `Dictionary<string, PlcOperationRequest>` retains resolved patch execution items for this call.
- A single `ExecutionItem(PlcOperationRequest requested)` lookup returns ordinary requests unchanged and returns a patch's resolved request. An absent patch resolution is an invariant violation that fails closed before dispatch; never pass the public patch name or null content to the worker.
- A separate attempted-input snapshot map will supply Task 5's audit hook; a later replan must not overwrite historical attempted input.

- [ ] Add a failing domain test proving the public patch currently does not dispatch as an update. Its final worker request must be `update_block_logic` with full resolved text and the caller's original hash; no `patch_block_logic` worker method or edits payload may be sent.
- [ ] In `PlanAsync`, retain the original `_items` for public identity, copy resolved patches from the planner, and cache guards/effects as today. In `ReplanAsync`, remove the previous execution entry before re-resolving, and replace it only on success along with refreshed guards/effects.
- [ ] `MutateAsync` obtains the execution item, snapshots attempted patch input, records the attempt, and invokes `PlcWorkerInvoker.InvokeWriteAsync`. Do not normalize again in the invoker or add a patch branch to worker IPC.
- [ ] `Project` passes the execution item to `PlcPayloadContract.ProjectWrite` so it decodes the typed block-import result, then restores `StructuredOperationItem.Operation` to the requested public operation. Preserve all failure evidence and warnings.
- [ ] `VerifyAsync` first filters original requests to attempted IDs, then maps them to execution items. Pass the attempted batch and cached effects to the existing policy. This order must not look up skipped, blocked or unresolved patches. Explicit resolved format must drive verification, including when the caller omitted it.
- [ ] Pin each existing name-keyed seam with a test: working-state update target; planner content path; invoker dispatch; typed success/failure decoding; format default; verification content read/hash and superseded handling. Test through the domain for execution-only seams so these helpers need no new patch-name cases.
- [ ] Use the existing conservative dependency rule to exercise late replan: create a Bool tag whose name equals the existing block name (`Main`) in the fixture's tag table, then patch that existing block. The tag's CPU-name touch gives the block patch `DependsOn`. An unchanged late export succeeds and replaces the cached resolution; a changed export fails `state_changed`, retains the earlier tag, sends no patch update and yields the existing partial-write warning/guard.
- [ ] Add a direct domain replan test showing the cache is discarded on patch resolution failure. Add a regression that two same-block content writes retain existing dependency semantics: do not invent dependency edges; the worker still refuses the second stale hash after the first changes content.
- [ ] Cover whole-scope deletion, native/import refusal, protocol-invalid block outcome, failed attempted import evidence, `portal_lost`, and a later same-target overwrite. They must retain the existing update behavior, public patch name, no replay and appropriate verification/superseded result.
- [ ] Run the focused domain/invoker/verification suite green; keep existing full-replacement behavior unchanged.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcGuardedWriteDomainTests|FullyQualifiedName~PlcWriteInvokerTests|FullyQualifiedName~PlcWriteVerifierTests|FullyQualifiedName~PlcWritePayloadContractTests|FullyQualifiedName~PlcWorkingStateTests|FullyQualifiedName~PlcWritePlannerTests" /p:UseTiaPortalReferenceStubs=true -m:1
```

## Task 5: Record the exact resolved input in audit

**Files**

- Modify `TiaMcpServer/Safety/Pipeline/IWriteDomain.cs`, `WriteAudit.cs`, and `WriteRun.cs`.
- Modify `TiaMcpServer/Plc/PlcWriteDomain.cs`.
- Extend `TiaMcpServer.Tests/Safety/Pipeline/WriteExecutionTests.cs`, `WriteExecutionFailureAuditTests.cs`, `JsonlWriteAuditSinkTests.cs`, and `TiaMcpServer.Tests/Plc/PlcGuardedWriteDomainTests.cs`.
- Modify `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`.

**Interfaces**

```csharp
// IWriteDomain: default implementation; other domains need no changes.
JsonElement? ResolvedOperationForAudit(TItem item) => null;

// WriteAuditItem: additive init property, not a new required constructor argument.
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public JsonElement? ResolvedOperation { get; init; }
```

- [ ] Add a red actual-write audit test: original `requestedOperations` still says `patch_block_logic` with caller edits; matching item `resolvedOperation` says `update_block_logic` and contains the full exact worker input, including CRLF, separator-induced LF and resolved format. Compare string values with the captured worker request, not with a recomputed desired hash.
- [ ] Add tests for native rejection/transport failure, a client `NotSent` outcome, and the unexpected-exception audit path. A previously captured attempted request must remain present and accurately labeled even when dispatch or application is unproved. Earlier successful patch input must survive a later item's failure.
- [ ] Test preview, initial plan error, blocked call, skipped patch and failed late replan: no attempted resolved input. For normal full replacements and other domains, the conditional field and absent edits member stay omitted; existing audit fields/semantics remain intact, aside from the intentional PLC write contract-version change.
- [ ] Add the default domain hook and set the init property in `WriteRun.AuditItem`, so normal and unexpected audit paths share it. The PLC implementation returns only the snapshot from immediately before worker invocation; it does not perform reads or new resolution during audit.
- [ ] Serialize/snapshot with the existing canonical JSON helpers. Keep `ResponseText`/`ResponseHash` tied to the exact delivered response, and retain the full resolved content separately even when the public response has an omission.
- [ ] Register `WriteAuditItem.ResolvedOperation` with its exact appearance condition and retain Task 1's `PlcOperationRequest.Edits` registration. Ensure both declaring types participate in the conditional-member scan. Audit stays version 2 and the other tools' contract versions remain unchanged.
- [ ] Run the audit/pipeline/conditional tests green, including domains that rely on the default hook.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Safety.Pipeline|FullyQualifiedName~PlcGuardedWriteDomainTests|FullyQualifiedName~ConditionalMemberRegisterTests" /p:UseTiaPortalReferenceStubs=true -m:1
```

## Task 6: Publish contract 2.1 and prove the registered MCP path

**Files**

- Modify `TiaMcpServer/Plc/PlcContractVersion.cs` and `PlcWriteTools.cs`.
- Finish descriptions in `PlcOperationRequest.cs` and `PlcTextEdit.cs`.
- Extend `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`, `PlcWriteBudgetTests.cs`, and relevant `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs` probes.

- [ ] Add a registered-tool preview/apply pair using small edits, a complete eligible read hash and existing fake-worker exports. Preview must produce a bounded one-line diff without mutation; apply must send the same resolved full document through the worker and report verification. Caller arguments contain no full replacement document.
- [ ] Add a mixed batch with a valid early item and invalid patch anchor: initial planning returns root `validation_error` with no mutations. Distinguish this from Task 4's late replan failure after an earlier actual mutation.
- [ ] Prove canonical text and `structuredContent` equality, typed block-import success/failure evidence, public patch operation names, audit presence and absence rules, and no server elicitation. Keep the existing `isError` distinction between planner denial and attempted failure.
- [ ] Test registered schema: nested edit objects are closed, `oldText`/`newText` are required non-null strings, deletion is accepted, and root inputs remain `operations` and optional boolean `dryRun`. Strict shape failures must not create audit records.
- [ ] Change only `PlcWriteContractVersion.Current` to `2.1`; reads remain `1.0`. Update the literal assertion in `PlcGuardedWriteMcpTests` and the three PLC write-budget fixtures that currently construct `"2.0"` responses. Do not rewrite intentional historical/other-domain version assertions.
- [ ] Update tool/field descriptions to list the 15 operations and explain patch fields, XML-only scope, whole-export hash, normalized line matching, original-base semantics and preview diff. Describe import/compile outcomes as applying to patches too.
- [ ] Add a budget regression with an eligible large base and a short patch: protected summaries, bounded diff, omissions and execution success retain current semantics. Patching does not give omitted reads a content hash or bypass item/document limits. The 72,141-character pure-helper case is separate from public-read eligibility; use raised effective item limits if testing that size through public reads.
- [ ] Run all focused tests together and ensure discovery remains 9/21 tools.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Plc|FullyQualifiedName~ConditionalMemberRegisterTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~ProjectCapability|FullyQualifiedName~Safety.Pipeline" /p:UseTiaPortalReferenceStubs=true -m:1
```

## Task 7: User and architecture documentation

**Files**

- `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`: operation table, content writes, failure/verification behavior, current contract version and resolved audit input.
- `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`: retain PR 1's whole-bundle hash versus XML-only diff distinction and extend its applicability to resolved patches.
- `docs/guides/mcp-client-configuration.md`: replace the whole-document-only instruction; include patch in the no-hash/omitted-read limitation.
- `docs/ARCHITECTURE.md`: current PLC write version, planning/execution map, one normalization point and conditional per-item audit field.
- `AGENTS.md`: PLC count 14→15, current write version 2.1, patch scope/resolution/audit invariants. Keep instructions about unrelated domains unchanged.
- `CHANGELOG.md`: additive patch operation and write contract bump; no relaxed-encoder claim.

- [ ] Document the exact public request and an ordinary read → patch preview → patch apply → inspect verification flow. The client resends small edits on apply; the preview is not a reusable token and each call resolves against fresh state.
- [ ] Explain zero-based edit indices, overlapping occurrences, non-overlapping original spans, no cascading, newline choice, empty replacement, delimiter refusal and source/XML scope. Patch edits cannot target companions or types.
- [ ] Explain initial planning failure affects the whole call, whereas late replan/worker failures can leave earlier mutations. Preserve whole-export hash, worker recheck, unchanged import/compile reporting and content-hash verification guidance.
- [ ] Explain that the full resolved candidate is in audit `items[].resolvedOperation.content`, paired with original `requestedOperations`. Audit data on a failed attempt identifies the input selected for that attempt; it does not establish dispatch or success.
- [ ] State that patching still needs a hash-bearing eligible read in the same format; it does not make blocks omitted by the effective read budget writable. Do not promise removed full-document reads, working copies or translation fidelity.
- [ ] Update present-tense version/count statements only. Do not relabel historical acceptance reports or close PR 2's escaping-inflation follow-up.
- [ ] Run documentation validation and inspect all changed documentation links. Do not introduce links from durable docs to this transient plan/spec.

```powershell
python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30
```

## Task 8: Final verification, review and handoff

- [ ] Recheck repository status and the implementation diff. New production files should be the host patch helper/DTO only; Contracts changes are limited to public policy/capability names. No worker request shape, import method, canonical encoder or filesystem API change belongs here.
- [ ] Run tests/builds serially (`-m:1`); do not run concurrent builds or tests against shared outputs. Capture the first failing test before each behavior step and its final passing result. Do not reuse PR 1's Linux failure baseline on this Windows workspace.
- [ ] Run the full non-packaging suite and stub solution build. No product project/packaging file is planned to change; editing the test project's source links alone does not trigger packaging tests. If scope expands to a product `.csproj`, `Directory.Build.props`, `global.json`, packaging scripts or reference stubs, run `Category=Packaging` too.

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging" /p:UseTiaPortalReferenceStubs=true -m:1
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.slnx -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30
```

- [ ] The installed-assembly build verifies compilation only. Independently review all execution-name seams and audit paths; verify every new host file is linked into tests. Seek review of the resolved-content map and shared audit hook.
- [ ] Report offline and live evidence separately. This plan authorizes no live TIA mutations. After implementation, an owner-authorized disposable-fixture run may check source/XML preview/apply, stale hash, exact diff/audit and fresh verification hash, and measure request size/model effort against the original full-replacement workflow. Do not add fidelity probes or claim live success from fake-worker tests. Working copies remain deferred until PR 3 is measured.
- [ ] Before a later PR merge, follow the repository's transient-file lifecycle: move durable behavior/decisions/evidence to existing owning docs and prune this PR's working files. This planning session makes no commits, push or PR changes.

## Coverage map

| Approved PR 3 requirement | Task / evidence |
| --- | --- |
| New public operation, exact fields, block-only scope, writable access | 1, 6 |
| Fresh whole-export hash, XML part/source scope, missing-XML guidance | 2, 3 |
| Unique overlapping matches, original offsets, no cascading/overlap | 2 |
| CRLF/LF matching and replacement terminator selection | 2 |
| Delimiter refusal and edit-index diagnostics | 1, 2, 3 |
| Planning-wide refusal and no initial mutation | 3, 6 |
| One normalization point and existing execution-name seams | 3, 4 |
| Per-call resolution, dependent replan, explicit format | 3, 4 |
| Existing diff, worker recheck, verification and guarded pipeline | 3, 4, 6 |
| Original requested edits plus full resolved attempted input in audit | 5 |
| Contract 2.1 and conditional-member registration | 1, 5, 6 |
| PLC/tool/agent/client documentation | 6, 7 |
| No working copy, translation, encoder or changed import path | Inherited constraints, 8 |

## Planning-session validation

Planning evidence: approved design read; initial clean local `main` and PR 1 merge verified; source/index refreshed and integration points inspected; independent patch, pipeline, contract and plan reviews completed. Review corrections cover dependency/guard ordering, schema nullability, conditional-member registration, delimiter-prefix provenance and attempted-input audit wording. All referenced existing file paths were checked; the only absent exact paths are the three files explicitly planned for creation.

Documentation check: `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` passed with **0 errors, 3 warnings** for the existing PR 1 plan, approved design and this new transient plan. Whitespace validation passed. Implementation tests/builds and live TIA actions have not been run. Only this plan file was added; no commits were created.
