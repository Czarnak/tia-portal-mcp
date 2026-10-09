# Troubleshooting

Common failures and their causes, plus TIA Portal V21 behaviors verified against a real
installation that are surprising but expected.

## Troubleshooting

- `System.Runtime.Remoting.RemotingException` / `TypeLoadException` in .NET 10: Siemens Openness must run in the net48 worker. Rebuild the solution and make sure the host output contains `openness-worker\TiaMcpServer.OpennessWorker.exe`.
- Openness DLL not found: verify TIA Portal V21 is installed and set `TiaPortalV21Dir` to the `PublicAPI\V21\net48` folder if your install path is non-standard.
- Build uses `ref/` on a developer machine: verify `TiaPortalV21Dir` points to the local V21 `PublicAPI\V21\net48` folder, or force local references with `/p:UseTiaPortalReferenceStubs=false`.
- No running TIA Portal instance: start TIA Portal V21 before calling tools that attach to the current project.
- Access denied or attach failure: confirm the Windows user belongs to the `Siemens TIA Openness` user group, then sign out and back in.
- `dotnet` selects the wrong SDK: install a stable .NET 10 SDK (`10.0.400` or newer in the .NET 10 feature bands), or update `global.json` to a locally installed stable .NET 10 feature band.
- `get_block_content` on an S7-300/S7-400 CPU returns a warning that the s7dcl rung text is unavailable: expected. Those CPU families do not support Siemens document export at all. The Simatic ML XML document is exported by a separate API and is present, so the payload is complete for reading and for `update_block_logic`; only the supplementary human-readable rung text is missing.

### Hardware pagination cursor failures

- `invalid_cursor`: the cursor is malformed, has an unsupported shape/version, failed signature validation, or came from a previous MCP host process. Host restarts intentionally invalidate every hardware cursor. Start again without the cursor.
- `cursor_filter_mismatch`: `deviceName`, `plcName`, `includeIoDetails`, or `includeTagMatches` changed. Retry the unchanged request at the same cursor, or start a new sequence with the new fields; never combine the old cursor with changed fields.
- `cursor_binding_mismatch`: a repeated `projectPath` differs, the host binding/path changed, or the live worker session changed. Confirm the current project and start a new sequence.
- `cursor_snapshot_mismatch`: matching devices/subnets or their stable order changed. Start a new sequence against the new project snapshot.
- `cursor_out_of_range`: the saved combined offset is no longer valid. Start a new sequence.
- `protocol_error`: the worker response omitted its authoritative session identity or returned malformed/incoherent page evidence. Do not use the page; inspect host/worker diagnostics before starting over.

An omitted hardware page has not advanced. For `hardwarePageDiagnosticsExceededItemCharLimit` or
`hardwarePageEntityExceededItemCharLimit`, retry the unchanged request at the same cursor, or
start a new sequence with narrower filters or fewer detail options. A page may contain fewer
entities than `pageSize` because the host keeps only the largest complete canonical prefix at or
below 60,000 characters; continue until `nextCursor` is absent. The independent 180,000-character
batch limit can also omit a complete page, so place large hardware reads in their own call.

## Verified TIA Portal V21 behavior

The Phase 5 acceptance record documents the verified recovery guidance for these previously
problematic paths. Multi-document `update_block_logic` round trips are verified: submit the
exported SIMATIC ML document bundle through `plc_write` with its `expectedContentHash`, expect one import followed by compile
and re-export verification, and treat a structural/unsafe-document rejection as a no-change result.
An edited bundle is likewise compiled and re-exported. Do not automatically retry a write with an
uncertain worker outcome; inspect the current block instead.

SCL `create_block` calls are verified: the generated SCL source contains a non-empty compile unit,
the requested block resolves at its requested path, and `compile_check` confirms it compiles. The
same guarded `plc_write` flow applies to SCL and GlobalDB block creation.

S7-300/S7-400 block reads are verified against a CPU 314C-2 PN/DP (`6ES7 314-6EH04-0AB0/V3.3`).
`PlcBlock.ExportAsDocuments` is rejected outright by those CPU families, but `PlcBlock.Export`
produces the authoritative Simatic ML XML for GlobalDB, InstanceDB, STL FC and LAD FC blocks on the
same CPU. `get_block_content` therefore succeeds with `format=xml` and carries one warning naming
the missing document package. `format=source` remains restricted to global data blocks and
SCL-language FB/FC/OB.

`save_project_as` with `rebind: false` is rejected up front with a `validation_error` response,
before Siemens `SaveAs` dispatch. Guarded lifecycle rejections are audited. `rebind: true` is required;
see the [lifecycle reference](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).
A supported SaveAs rebinds both host and worker to the
worker-reported copied project path; verify it with a subsequent status or read call.

## Guarded lifecycle failures

- Local `unsupported_capability`: local sessions support adoption, open, basic status, content
  reads and writes, compile and `save_project`; standalone-only create/save-as/archive/close
  remain unavailable, even in full or a dry run. Do not use generic `close_project` to clean up a local session.
- Invalid local selector: use the observed typed `.amc21` owner path for bind/status/startup;
  an existing file-only `.als21` path belongs to `open_project`. Inventory directories and
  guessed filename relationships cannot resolve an owner or its opener provenance.
- `local_session_requires_terminal_operation`: a different open would replace a worker-owned
  local source. It blocks even when clean and with force/full. Preserve it until a separately
  authorized supported terminal operation is available.
- `local_session_source_preservation_unproved` or ambiguous owner/headless refusal: preservation
  or exact duplicate identity is unproved. Inspect exact Portal/AMC state and retain a UI/client;
  do not override, dispose a sole headless owner or reconstruct ALS provenance.
- Offline ALS opening can show a Siemens/TIA connection dialog. Both scoped read-write opens
  completed after operator dismissal; the original noninteractive checks failed. The MCP
  confirmation prompt is separate. Inspect the target before another request after a timeout
  or unresolved dialog; full policy confirmation does not guarantee no vendor dialog.
- Generic Project Server inventory failure is not typed proof of unavailability/authentication
  failure. A healthy local status does not prove remote connectivity or session mode; explicit
  endpoint observations cannot populate the currently unjoined active remote identity.

- `access_denied` naming elicitation: every actual lifecycle call in read-write requires one form
  acceptance with boolean `confirm:true`, even with no guards or only info guards. Decline, cancel,
  timeout, unsupported capability, or transport failure blocks mutation. Use a supporting client;
  full runs lifecycle under policy without server elicitation.
- `guard_blocked`: inspect `guards`. An existing create/save-as destination, archive inside the
  project folder, or forced close of a modified worker-owned source blocks in every mode. A dirty
  no-save close fires the acknowledge-severity `discards_unsaved_changes` guard, satisfied by client
  acceptance in read-write or policy in full. A dry run shows the guard without mutation or prompting.
- `binding_conflict` or changed-state refusal after a prompt: the target or prepared revision
  changed. Inspect current project identity/state and submit a new intentional call.
- `success:false` with `error:null` and MCP `isError:false`: dispatch or verification was
  attempted. Read the typed `result` and `verification` failures; a successful mutation can
  precede failed verification. After timeout, crash, disconnect, or uncertain mutation, inspect
  the project and destination artifacts before retrying. Lifecycle writes are never replayed
  automatically.

The former lifecycle agent confirmation array and the public `confirm` and safety-token inputs are removed.
Network now uses `dryRun:true` for preview and executes by default, without server elicitation, as does `plc_write`; no tool uses safety tokens. Removed Network root inputs fail before entry as a normal MCP error with no write audit. Entered denials are canonical and audited once; inspect typed partial/verification/omission outcomes before retry. The removed startup switch fails with:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Use `bind_project` to adopt or switch to an already-open project; a different configured or
previously bound path requires `forceRebind:true`. No request implicitly opens a project: only
`open_project` and `create_project` can open one. TIA's Openness access dialog may appear on
reattachment and requires a human answer; it is separate from lifecycle elicitation. Worker
ownership is lost across detach. Read-only never opens, creates, saves or closes, but explicit
binding can switch its session. Doctor's unbound-session Warning names `bind_project` as remediation.
Project-tree `cursor_binding_mismatch` means the binding ID/revision changed; start a cursor-free
browse, including after returning to the original path.

The [2026-10-03 live report](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md)
replaces the valid older 2026-10-01 lifecycle evidence for this candidate. All three functional
groups passed, including read-write confirmation refusals and full policy audit provenance. The
maintainer reported no new TIA dialog in read-only, completing the separate human observation. Offline/FakeWorker checks
and programmatic acceptance do not prove that a human saw a dialog or establish hardware acceptance.
