# ADR-0009: hmi_read is a batched read-only WinCC Unified contract with opt-in snapshot cursors

**Status:** Accepted
**Decided:** 2026-10-07 to 2026-10-12   **Recorded:** 2026-10-30
**Area:** hmi
**Current home:** [ARCHITECTURE: HMI reads](../ARCHITECTURE.md#hmi-reads-hmi_read); [HMI operations summary](../SupportedOperations/HMI_OPERATIONS_SUMMARY.md)

## Context
- HMI content had to be readable like `plc_read`, through the net48 worker, without new host-side Openness code.
- `OperationPolicyCatalog` is one flat namespace and PLC already owns names such as `list_tag_tables` and `create_tag`.
- Classic HMIs live in an assembly (`Siemens.Engineering.WinCC.dll`) the worker does not reference.
- A live spike showed that one property read (`HmiTag.ConfirmationType`) kills the Portal process, and that large projects make whole-project scans slow.
- Offset paging re-scanned the project on every page; Openness objects cannot leave the worker, so a continuation had to be held there.

## Decision
- One batched `hmi_read` tool clones the `plc_read` shape (1-50 items, items independent, available in every mode), built on the shared structured seam (see [ADR-0005](0005-canonical-json-contract.md)).
- Worker methods are named `hmi_<operation>`; `HmiOperationCatalog` is the single public-to-worker map, so public names inside the tool stay unprefixed.
- Classic HMIs are recognised by runtime type name and fail `target_kind_unsupported`. Reads enumerate compositions and avoid `Find` where a name filter is not needed, so they do not depend on how the license gate treats `Find`.
- Every result carries `isComplete` and `messages`: an unreadable property becomes null plus a message, an unreadable composition fails the item. Credential-named driver properties are never read.
- Paged operations get an opt-in snapshot cursor. The worker holds the name-ordered identity list (bounded, evicted when idle); the host seals the cursor. Identity is frozen at scan time and rows are read live, so staleness is an explicit error, not silent drift. Offset paging stays supported.
- `ConfirmationType`, `GmpRelevant` and `MandatoryCommenting` are permanently unread; there is no probe.
- `read_cross_references` gets a provisional `{hmi}` tag selector rather than HMI cross-references inside `hmi_read`.
- Rejected: reading all public properties by reflection (crashed Portal); a host-side row cache (stale and large); a cursor for unpaged `get_screen_navigation` (not needed); one unified HMI selector now (alarms and screens did not yet need it).

## Consequences
- HMI reads shipped without new tools beyond `hmi_read`; the cursor made repeated whole-project scans cheap and reused the project-tree cursor machinery and error categories.
- CI builds against a third generated stub (`Siemens.Engineering.WinCCUnified`); stub members must be added for any new Siemens call.
- Classic HMI is unsupported by design; the denylist of unread properties binds every future reader.
- A cursor does not see objects added after the scan; callers restart without a cursor.
- The `{hmi}` selector stays provisional. Per-item HMI re-location cost (O1) was retained by maintainer choice and remains an open follow-up in the improvement log.
- Writes were deferred to a later track and are decided separately; the same worker-method naming and cursor invalidation rules carry into them.

## Evidence
- In tree: [spike](../superpowers/acceptance/reports/2026-10-07-hmi-read-spike.md), [live acceptance](../superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md), [completion live acceptance](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - [design spec 2026-10-07](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-07-hmi-read-design.md)
  - [completion design spec 2026-10-11](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-11-hmi-read-completion-design.md)
  - [plan 2026-10-07](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-07-hmi-read.md)
  - [completion plan 2026-10-11](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-11-hmi-read-completion.md)
