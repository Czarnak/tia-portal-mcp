# Acceptance Criteria: Round 4 — Session Binding and Contract Integrity

**Spec:** `docs/superpowers/specs/2026-07-20-round4-binding-and-contract-integrity-design.md`
**Date:** 2026-07-21
**Status:** Approved

---

## Criteria

| ID | Description | Test Type | Preconditions | Expected Result |
|----|-------------|-----------|---------------|-----------------|
| AC-001 | Safety-token presentation JSON is immutable after startup. | Logic | `TiaJson` is loaded. | `Presentation.IsReadOnly` is true; mutation throws `InvalidOperationException`; serialization stays compact camel case. |
| AC-002 | A worker response reports its resolved project path and the host adopts only successful worker-reported ground truth. | Logic | Scripted/FakeWorker response and an unbound `ProjectSessionBinding`. | A successful response with `ResolvedProjectPath` binds to that path; failed or pathless responses do not bind; caller-requested paths are not adopted instead. |
| AC-003 | Session-path resolution is non-mutating before worker success. | Logic | Cover each bound/requested path pair in the resolve matrix. | An unbound session stays unbound for both null and explicit requests; a bound session resolves its own or matching canonical path; a different requested path returns an error. |
| AC-004 | Project-open policy makes a deterministic use/open/refuse decision for project-scoped reads. | Logic | Pure `ProjectOpenPolicy.Decide` cases with null, equal (case/path-normalized), and different paths. | No attached project plus explicit path opens; same or omitted path uses attached; a different active project refuses with the documented `open_project` recovery instruction. |
| AC-005 | Write tools use the injected safety service instead of a process-wide audit singleton. | Logic | Tool-layer preview/apply uses a temporary audit directory. | The temporary directory receives the audit record, the default audit directory is unchanged, no `WriteSafetyService.Shared` reference remains, and injected `safety` is absent from generated MCP input schemas. |
| AC-006 | The batch catalog declares the exact supported required and optional field surface. | Logic | Enumerate `BatchOperationCatalog.All`. | Exactly 25 expected operation specs exist; every category, required list, and optional list matches the forwarding contract; universal fields are never optional and required/optional lists never overlap. |
| AC-007 | Batch validation rejects populated fields that the selected operation would discard. | Logic | Validate requests with invalid `deviceItemName`, invalid `create_tag` external attributes, valid `update_tag` attributes, and a range error. | Each invalid field yields one aggregated error naming valid optional fields; valid fields are accepted; depth/max-result range errors remain enforced. |
| AC-008 | Every declared batch field survives the real host-to-worker request path. | Logic | Build FakeWorker and run each catalog operation through `BatchWorkerInvoker` with its `projectPath` set to `echo`. | FakeWorker echoes the raw worker request; each required/optional sentinel reaches it by value, including the multiplicity of repeated boolean sentinels after subtracting an all-null baseline. |
| AC-009 | User documentation describes delivered contract behavior and all hardware-gated deferrals accurately. | Logic | Inspect `README.md` and `docs/IMPROVEMENT_PLAN.md`. | Documentation records immutable JSON, DI audit isolation, validation errors, forwarding proof, hardware-gated create-tag attributes, and `open_project` as the deliberate switch mechanism; no stale `WriteSafetyService.Shared` or project-open-alongside claim remains. |
| AC-010 | The known `get_project_status(projectPath)` lifecycle exception is carried forward explicitly rather than misstated as enforced read policy. | Logic | Inspect README, improvement plan, and the status lifecycle path. | Both documents state that `get_project_status(projectPath)` remains the human-approved Round 5 exception because the RPC also serves write-state probes; they do not present it as a supported switching mechanism. |
| AC-011 | Deferred scope remains excluded from Round 4 behavior. | Logic | Inspect the change set and improvement plan. | No create-tag external-attribute forwarding, network configurator reflection redesign, double-dispatch collapse, or payload-budget collapse is introduced; hardware follow-up remains listed as deferred. |
