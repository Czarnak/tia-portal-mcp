# JSON Contract Phase 2: Standalone Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task after maintainer review. Steps use checkbox (`- [x]`) syntax for tracking. Use subagents only when separately authorized by the active session.

**Goal:** Migrate `get_project_status` and `compile_check` to the canonical structured contract, strict worker decoding, and whole-value omission.

**Architecture:** Keep both existing public tool names, inputs, access policies, and binding behavior. Decode one typed worker payload per operation and project it into a typed standalone outcome. Render the final response through `StructuredToolResult`; retain the legacy lifecycle and batch machinery for its remaining consumers.

**Tech Stack:** C#, host/tests net10.0, contracts netstandard2.0, Siemens worker net48, System.Text.Json, MCP C# SDK, xUnit and FakeWorker.

**Spec:** [JSON contract roadmap](../../roadmap/json-contract.md), especially target contract and Phase 2; [write-safety design](../specs/2026-09-29-write-safety-redesign-design.md), for release sequencing and the Phase 3 boundary.

**Status:** Implementation authorized 2026-09-30 and completed for Tasks 1–4 on branch `phase2-json-contract`, based on `ddeeae0`. The maintainer approved the plan and confirmed that `compile_check.success` means compilation passed. Task 5 offline qualification is in progress; live V21 acceptance requires separate authorization.

## Global Constraints

- Use the existing canonical seam; one final serialization supplies text and structured content.
- Every response advertises `UseStructuredContent = true` and its concrete output schema.
- `isError` is true only for rejection before the requested operation runs. Top-level `error` is non-null exactly in that case.
- Worker payloads use the strict worker-payload reader; malformed success payloads become fixed-message `protocol_error` outcomes and are never echoed.
- No JSON inside strings and no substring cuts of JSON values.
- Keep the two-process architecture and Siemens-free host/contracts. Introduce no dependencies.
- Compile stays an engineering action: read-write and full, `ReadOnly=false`, `Destructive=false`. It does not acquire lifecycle write parameters or a token flow.
- This PR does not normalize Network/tree envelopes, migrate lifecycle writes, retire batch tools, or adopt C# `required` members.
- Keep a separate PR and plan for Phase 3. Revalidate against the actual base before execution.
- Do not commit, push, publish, or run live TIA without the applicable explicit authorization. No package tag during the write-safety transition; accumulate migration notes for the final major release.

## Review Focus

- Shared status/lifecycle payload types: migrating the direct read must not change the six legacy lifecycle tools' wire bytes.
- A compiler report with errors is a completed diagnostic operation, not a tool rejection; the report must survive `success: false`.
- Oversized malformed payloads must fail strict decoding rather than disappear behind an omission.
- A direct status read must not bind an unbound session, switch projects, or close an attached project.
- Compiler-report normalization must not change null-omitting batch import outcome envelopes that embed the same CLR report type.

## Public Shape

Use a single-result envelope rather than pretending these tools accept a batch. Concrete schemas are `GetProjectStatusResponse` and `CompileCheckResponse`.

Each has `tool`, `contractVersion`, `success`, `error`, `warnings`, and `result`. First structured version: `1.0`; the package change remains breaking relative to legacy text.

`result` is a typed `StandaloneToolOutcome<TPayload>` with:

- `status`: existing operation vocabulary, `succeeded`, `failed`, or `omitted`;
- `value`: `ProjectStatusInfo` or `CompileCheckReport`, or null;
- `failure`: existing `StructuredOperationFailure`, or null;
- `omission`: existing `StructuredOperationOmission`, or null.

All four members are present. Rejection sets top-level `error`, `result: null`, `success: false`, and `isError: true`. An attempted failure sets `error: null`, `result.failure`, `success: false`, and `isError: false`. Omission sets `status: omitted`, `value: null`, the omission record, `success: false`, and `isError: false`. This wrapper preserves execution failures without misusing the roadmap's rejection-only `error` member.

Compiler errors set outcome `status: failed` while retaining the report in `value`; compiler diagnostics explain the failure, so `failure` need not duplicate them. Warning-only compilations pass. Unknown or incomplete compiler states must not be promoted to success.

## Current Code Findings

- `ProjectReadTools.GetProjectStatus` and `ProjectEngineeringTools.CompileCheck` return `Task<string>` through `StandaloneToolResultFormatter`, which cuts worker payload strings at 60,000 characters.
- The actual direct status worker root is `ProjectLifecycleResultInfo`; `ProjectStatusInfo.Metadata` contains `ProjectMetadataInfo`. The roadmap's type shorthand must not lead to decoding only metadata or discarding basic status.
- `ProjectLifecycleResultInfo` and `CompileCheckReport` carry `LegacyNullOmission(ToolMigration)`. Removing the former marker globally in this PR would prematurely change lifecycle outputs.
- `ProjectLifecycleTools.GetProjectStatus` is a compatibility wrapper; its signature and callers need adjustment here even though the wrapper is deleted in Phase 3.
- The production conformance harness already uses full mode, which is necessary to see the complete legacy register after access-mode Phase 1b.

## Task 1: Typed Wire and Standalone Response Contracts

**Files:** Create `TiaMcpServer.Contracts/ProjectStatusResultInfo.cs`, `TiaMcpServer/Tools/StandaloneToolResponses.cs`, `TiaMcpServer/Tools/StandalonePayloadContract.cs`, and `TiaMcpServer/Tools/StructuredStandaloneResult.cs`. Modify `TiaMcpServer.Contracts/CompileCheckReport.cs`, `TiaMcpServer.OpennessWorker/Program.cs`, `TiaMcpServer.FakeWorker/Program.cs`, and `TiaMcpServer.Tests/Worker/WorkerPayloadNullPolicyRegisterTests.cs`. Add tests under `TiaMcpServer.Tests/Tools/` and update linked source includes where necessary.

**Interfaces:** `ProjectStatusResultInfo` preserves the current status root's `success`, `operation`, `projectPath`, and `project` fields but carries no legacy marker. `StandalonePayloadContract.DecodeStatus(WorkerCallResult)` and `DecodeCompile(WorkerCallResult)` decode their single declared CLR root and return a typed outcome. Concrete response records use `StandaloneToolOutcome<ProjectStatusInfo>` and `StandaloneToolOutcome<CompileCheckReport>` respectively.

- [x] Add `DirectStatusPayload_WritesExplicitNulls_WithoutChangingLifecyclePayloads`: compare explicit-null direct status against golden legacy open/create/save/save-as/archive/close payloads.
- [x] Add `CompileRoot_WritesExplicitNulls_BatchImportEnvelopeKeepsItsNullPolicy`: test both root and nested paths, not only `PayloadOptionsFor`.
- [x] Add decoder cases for missing members, extra members, invalid root types, invalid nulls/null collection elements, malformed JSON, and failed worker envelopes. Assert a fixed `protocol_error` message and no raw-payload echo.
- [x] Run the focused tests and observe the expected failures before production edits.
- [x] Introduce the dedicated status root only for worker `get_project_status`. Keep the basic-status and lifecycle-probe roots unchanged until Phase 3. Preserve host identity extraction and binding promotion through the same root property names.
- [x] Remove `CompileCheckReport` from the legacy marker register with its marker; preserve legacy enclosing roots. Add semantic validation for the report's collections, counts, and compile-state consistency, using actual producer states rather than a guessed Siemens enum list.
- [x] Implement concrete response schemas and typed projection; use `CanonicalJson.DeserializeWorkerPayload<T>` rather than independent required-member lists. Repeat focused tests.

## Task 2: Whole-Value Budgeting and Status Tool Migration

**Files:** Modify `TiaMcpServer/Tools/ProjectReadTools.cs`, `TiaMcpServer/Tools/ProjectLifecycleTools.cs`, the new standalone renderer, `TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs`, `ProjectMetadataTests.cs`, and direct-status worker-client integration tests.

**Interfaces:** `StructuredStandaloneResult.Create<TPayload>(string tool, StandaloneToolOutcome<TPayload>? outcome, IReadOnlyList<string> warnings, StructuredOperationFailure? rejection = null)` produces the concrete schema associated with that tool and a `CallToolResult`. The tool/type pairing is a closed internal mapping. A non-null `rejection` requires a null outcome and yields `isError: true`; an attempted failure is represented by the outcome with no rejection. Reuse `StructuredOperationOmission` and the structured budget's reason vocabulary and constants; keep one final canonical serialization.

- [x] Replace substring-truncation expectations with `StatusOversize_IsWholeValueOmission`, testing below/at/above 60,000 canonical value characters. Also cap the complete canonical response at 180,000 characters and account for escaped strings, warnings, and omission metadata.
- [x] Add `OversizedMalformedStatus_IsProtocolFailureBeforeBudgetProjection` and `StatusMetadata_NullAndUnavailableRemainDistinctFromEmptyValues`.
- [x] Run the tests and observe failures.
- [x] Implement decode-before-budget projection. Omit the complete value with reason, original character count, limit, retry tool, and guidance; do not fabricate a smaller successful status. If warnings need trimming for the document budget, remove whole entries and disclose that in bounded guidance.
- [x] Change direct status and its temporary wrapper to `Task<CallToolResult>`, declare its output schema, and adapt callers to the structured response.
- [x] Keep `StandaloneToolResultFormatter` for remaining callers. Run status metadata, worker identity/binding, and budget tests; assert unchanged no-project, configured-path, verified-path mismatch, and unbound-read behavior.

## Task 3: Compile Tool and Truthful Compilation Outcome

**Files:** Modify `TiaMcpServer/Tools/ProjectEngineeringTools.cs`, the standalone payload contract/renderer, `TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs`, compile contract tests under `TiaMcpServer.Tests/Block/`, and FakeWorker compile scenarios.

**Interfaces:** `StandaloneCompileOutcome.IsPassed(CompileCheckReport report)` classifies actual producer compile states and error counts; errors and unsuccessful/incomplete compiler states fail, while successful warning-only reports pass. `CompileCheck` keeps its existing verified-binding gate and pinned lease and returns `Task<CallToolResult>`.

- [x] Add `CompileErrors_ReturnTypedReport_SuccessFalse_IsErrorFalse_ErrorNull` and `CompileWarnings_ReturnTypedReport_SuccessTrue`.
- [x] Add pre-dispatch binding/access rejection, attempted worker failure, malformed success payload, inconsistent report, and oversized report cases. Attempted failures use `result.failure`; an omitted report is not a successful delivery.
- [x] Run focused tests and observe failures.
- [x] Implement the classification and structured tool projection without changing compilation, argument forwarding, or access-mode behavior. Keep producer truncation/omission evidence visible; bounded compiler diagnostics are not proof of an exhaustive message list.
- [x] Repeat compile, worker-client binding, read-only/read-write ceiling, and nested import-outcome regression tests.

## Task 4: Production Protocol Guard

**Files:** Modify `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs` and `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs` if its existing scenario hooks need extension; add FakeWorker scenarios in `TiaMcpServer.FakeWorker/Program.cs`.

- [x] Add successful and rejected probes for both tools, plus compiler-error, malformed-success, and whole-value-omission protocol probes.
- [x] Assert advertised schemas, explicit null members, one identical canonical document, and `StructuredContractInspector` acceptance. Test actual SDK serialization rather than only calling static methods.
- [x] Remove only the two Phase 2 names from `LegacyTextContractTools`. Leave the six lifecycle and three batch entries.
- [x] Verify discovery and direct authorization across all three access modes. `get_project_status` stays available in all modes; `compile_check` is absent/denied in read-only.
- [x] Run the protocol collection serially and the related focused regressions.

## Task 5: Documentation, Verification, and Handoff

**Files:** Update `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`, `README.md`, `docs/ARCHITECTURE.md` section 7a, `docs/roadmap/json-contract.md`, and existing engineering/migration notes as appropriate. Index any new document in `docs/README.md`; keep README cross-document links absolute GitHub URLs.

- [ ] Document the structured schemas, `success`/`isError` distinction, compiler-error example, omission guidance, and unchanged tool inputs. Correct the roadmap's status-root shorthand.
- [ ] Run `dotnet restore TiaMcpServer.slnx`, then `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`, then `dotnet test TiaMcpServer.Tests --no-build`. Do not parallelize builds or test runs.
- [ ] Run the repository's existing coverage check; require its 80% CI gate. Review the final diff and run `git diff --check`.
- [ ] If worker producer code changes, qualify direct status/no-project, compile pass/errors, and legacy lifecycle wire compatibility against installed V21 on a separately authorized disposable fixture. Freeze the candidate first; offline gates do not constitute live acceptance. Inspect unknown outcomes instead of replaying them.
- [ ] Record exact commands, counts, candidate identity, and evidence limits. Keep this draft's boxes unchecked until execution supplies evidence; add migration notes without tagging a package release.
- [ ] After merge, revalidate and finalize the separate Phase 3 plan against fresh `main`. This plan itself does not authorize a commit, push, merge, or live run.

## Self-Review and Discussion Boundary

The plan covers both Phase 2 tools, strict/null policy, omission, protocol probes, compatibility, and documentation. Compilation outcome semantics are maintainer-confirmed. The maintainer authorized execution of the proposed standalone wrapper and version `1.0`. Future schema changes must revise response/probe assertions together.
