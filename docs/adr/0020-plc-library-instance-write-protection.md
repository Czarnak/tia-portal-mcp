# ADR-0020: PLC library instances are protected before content replacement and deletion

**Status:** Accepted
**Decided:** 2026-11-28   **Recorded:** 2026-11-28
**Area:** plc
**Current home:** [PLC write guards](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md#guards)

## Context

Native library-type probes found content replacement paths that wait behind a modal Portal dialog
or silently unlink an instance from its library type. A direct in-work UDT deletion also caused a
modal refusal. These are unsuitable unattended outcomes even though ordinary semantic rejections
remain Portal's authority. The retained probes do not establish native FB/FC or group-deletion
behavior, so protective coverage and live qualification must remain distinct.

## Decision

Use one typed library-instance inspector in preview and worker execution. Refuse content replacement
of linked FB, FC and PLC UDT instances, including committed versions and resolved block patches.
Refuse direct block or nested block-group deletion when it would remove an in-work test instance;
retain deliberate committed-instance deletion and plain-object controls. Missing required linkage,
version-state or descendant evidence fails closed. Preserve original fatal Portal-loss exceptions.
Check before temporary source registration and immediately before replacement, generation or deletion.
The internal bound observation shares the execution inspector; only exact targets proved absent and
created in the current simulation may defer preview inspection. Existing planning and write contract
`2.1` remain; this decision adds no public tool, preview reference or apply-planning bypass.

Rejected alternatives:

- Rely on ordinary native failure handling: a modal wait or silent unlink is not a safe unattended refusal.
- Block every linked deletion: this would remove the deliberate committed-instance deletion workflow.
- Infer a new target from a same-name dependency: conservative dependency keys can alias an existing target in another scope.

## Consequences

- Change linked content through the Library type-version workflow, using `create_type_version`.
- `plc_library_instance_edit` and `plc_library_test_instance_delete` are stable block-guard IDs;
  unavailable required evidence uses `plc_state_unverifiable`.
- The bound `inspect_plc_write_target` observation uses `WithEngineeringRoot` and the `plc_write`
  capability, preserving eligible standalone/local-session routing and existing access rules.
- A late linkage change can fail the current item after earlier items applied; the batch still has
  no rollback or automatic replay. Source cleanup and import-not-started evidence must stay truthful.
- Public-tool refusal proves the planning guard. Native-adjacent race and fatal-path protections
  require separate evidence; offline doubles and installed-reference builds are not live qualification.

## Evidence

- [November 7 native library-type probes](../superpowers/acceptance/reports/2026-11-07-library-types-native-probes.md):
  P11c/P12c modal replacement refusals, P15a direct in-work UDT deletion refusal and P15d released-instance unlinking.
- [PLC library-instance guard verification](../superpowers/acceptance/reports/2026-11-28-plc-library-instance-guards-verification.md)
  records implementation commit `fe604c57`, its offline/installed-reference build results, scoped
  executed V21 guard/control cases and verified fixture cleanup. Public refusals stop in planning;
  native-adjacent races/fatal paths, local sessions and the exhaustive format matrix remain
  unqualified live. The failed full instrumented Release run and pending PR CI are explicit.
