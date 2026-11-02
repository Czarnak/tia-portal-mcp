# F1 Legacy Null Omission Retirement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the unused payload-wide null-omission escape hatch without changing current production JSON.

**Architecture:** Serialize worker payloads with existing explicit-null options. Remove marker discovery and its matching decoder refusal. Keep conditional members, request/envelope policies and strict required-member decoding intact.

**Tech Stack:** C#, System.Text.Json, netstandard2.0 Contracts, net10.0 host/tests, net48 worker, xUnit.

**Spec:** `docs/adr/records/2026-10-17-codebase-simplification-review.md` F-1/Q2; ADR 0011 records the accepted decision.

## Complexity and verification boundary

F1 is medium complexity because the policy spans host, worker and contract fixtures. F2 is a separate low-complexity branch: move the unchanged binding class, link it into tests, fix its cross-assembly XML reference and correct Contracts wording. F4 is excluded.

Neither requires live TIA testing at this scope: no Siemens calls, operation handlers, binding transitions or production wire shapes are intended to change. Serial builds and contract/FakeWorker tests provide offline qualification, not new live acceptance.

## Global Constraints

- Preserve Request, Envelope, ExplicitNullPayload, member-level conditional omission and strict required-member enforcement.
- Preserve runtime-type payload members, array/list null members and nested decoder validation.
- Introduce no dependency, abstraction, tool contract change or F4 consolidation.
- Preserve accepted ADRs and frozen review/appendix; update maintained guidance only.
- Keep F2's namespace/API stable and worktrees separate. Controller serializes restore/build/test runs.
- Do not push, merge, deploy, mutate TIA or prune this plan without further delivery instructions.

## Review Focus

- Object-typed roots retain runtime-type members.
- Arrays/lists retain explicit null members.
- Conditional members remain absent when null and present when populated.
- Envelopes omit nulls while requests write them.
- Strict decoding still rejects incomplete nested objects and array elements.

### Task 1: Retire the unused policy and update its consumers

**Files:** WorkerJson.cs, CanonicalJson.cs, LegacyNullOmissionAttribute.cs, WorkerPayloadNullPolicyRegisterTests.cs; affected Worker/Json/Network/Project/Block/Vci tests; AGENTS.md, docs/ARCHITECTURE.md and docs/roadmap/json-contract.md.

**Interfaces:** Keep `SerializePayload<T>(T payload)` and `ExplicitNullPayload`. Remove `LegacyNullOmittingPayload`, `PayloadOptionsFor`, `OmitsNullMembers`, `ContractType`, marker/enum and `RefuseLegacyNullOmittingPayload`.

- [ ] Inventory all references; confirm no production marker applications.
- [ ] Run focused existing serializer/decoder tests before production edits. Use characterization for behavior-preserving deletion rather than inventing a failing behavioral requirement.
- [ ] Preserve coverage of explicit-null roots, runtime-type roots, arrays/lists, conditional fields, strict decoding and request/envelope policies.
- [ ] Remove unused policy, empty register and legacy-only fixtures/assertions. Replace test `PayloadOptionsFor` calls with `ExplicitNullPayload`.
- [ ] Update maintained guidance and stale comments; leave frozen historical records intact.
- [ ] Run focused tests, serial solution build and full non-Packaging suite with repository coverage settings. Meet repository threshold. Run Packaging only if documented trigger files change.
- [ ] Run docs verifier and `git diff --check`, obtain independent review and resolve findings.
- [ ] Commit as `refactor: retire legacy worker payload null omission`.

## Coordination and closeout

F2 keeps namespace/signatures, so neither branch depends on the other. Coordinate separate AGENTS.md edits when integrating later. Leave both branches/worktrees local and reviewable; main stays unchanged.
