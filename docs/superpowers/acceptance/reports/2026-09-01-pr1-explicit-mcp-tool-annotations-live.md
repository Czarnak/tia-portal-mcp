# PR 1 Explicit MCP Tool Annotations — Live TIA Portal V21 Acceptance

## Status

**Pending live acceptance.** This is an evidence template, not a live-run report. The harness has
not been run against TIA Portal V21, no project path or version is recorded here, and PR 1 remains
incomplete until a separately authorized non-mutating live run replaces this template.

## Intended harness and report path

- Harness: `scripts/live-test-write-tool-metadata.ps1`
- Intended report path: `docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md`
- Required inputs: an approved disposable `.ap21` project path and the explicit report path above.

## Required live evidence

The authorized harness must launch the real `TiaMcpServer` host in both `--read-only` and
`--read-write` modes, use the public `initialize`, `notifications/initialized`, `tools/list`, and
benign `get_project_status` protocol calls, and record:

- exact TIA Portal V21 product version and tested project-copy path;
- the exact 4-tool read-only and 14-tool read-write surfaces;
- emitted annotation hints for `preview_write_batch`, `apply_write_batch`, `open_project`,
  `create_project`, `save_project`, `save_project_as`, `archive_project`, and `close_project`;
- a result summary for the benign `get_project_status` call in each access mode; and
- the evidence boundary: only that host/project/session combination is live-proven.

## Non-mutation and deferred scope

The harness must not call a write, preview, apply, compile, network-write, or PLC-control route.
It proves metadata and a benign read only; it does not weaken the server-enforced preview/token/
apply safety model. PLC `start_plc` and `stop_plc` remain deferred.

## Evidence boundary

Offline, reference-stub, FakeWorker, and static source-contract checks are necessary development
evidence but are not live TIA Portal V21 acceptance. No live result, tool surface, annotation value,
TIA Portal version, or project path is asserted by this pending template.
