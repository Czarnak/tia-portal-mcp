# Troubleshooting

Common failures and their causes, plus TIA Portal V21 behaviors verified against a real
installation that are surprising but expected.

## Troubleshooting

- `System.Runtime.Remoting.RemotingException` / `TypeLoadException` in .NET 8: Siemens Openness must run in the net48 worker. Rebuild the solution and make sure the host output contains `openness-worker\TiaMcpServer.OpennessWorker.exe`.
- Openness DLL not found: verify TIA Portal V21 is installed and set `TiaPortalV21Dir` to the `PublicAPI\V21\net48` folder if your install path is non-standard.
- Build uses `ref/` on a developer machine: verify `TiaPortalV21Dir` points to the local V21 `PublicAPI\V21\net48` folder, or force local references with `/p:UseTiaPortalReferenceStubs=false`.
- No running TIA Portal instance: start TIA Portal V21 before calling tools that attach to the current project.
- Access denied or attach failure: confirm the Windows user belongs to the `Siemens TIA Openness` user group, then sign out and back in.
- `dotnet` selects the wrong SDK: install .NET SDK 8.0.4xx or update `global.json` to a locally installed .NET 8 SDK feature band.
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
exported SIMATIC ML document bundle through a guarded batch, expect one import followed by compile
and re-export verification, and treat a structural/unsafe-document rejection as a no-change result.
An edited bundle is likewise compiled and re-exported. Do not automatically retry a write with an
uncertain worker outcome; inspect the current block instead.

SCL `create_block` calls are verified: the generated SCL source contains a non-empty compile unit,
the requested block resolves at its requested path, and `compile_check` confirms it compiles. The
same guarded preview/token/apply flow applies to SCL and GlobalDB block creation.

S7-300/S7-400 block reads are verified against a CPU 314C-2 PN/DP (`6ES7 314-6EH04-0AB0/V3.3`).
`PlcBlock.ExportAsDocuments` is rejected outright by those CPU families, but `PlcBlock.Export`
produces the authoritative Simatic ML XML for GlobalDB, InstanceDB, STL FC and LAD FC blocks on the
same CPU. `get_block_content` therefore succeeds with `format=xml` and carries one warning naming
the missing document package. `format=source` remains restricted to global data blocks and
SCL-language FB/FC/OB.

`save_project_as` with `rebind: false` is resolved: it is rejected up front with a
`validation_error` response, before any preview, safety-token issuance, Siemens `SaveAs` call, or
audit write, so it has no side effects. `rebind: true` is required; see
[Write safety](#write-safety). A supported SaveAs rebinds both host and worker to the
worker-reported copied project path; verify it with a subsequent status or read call.
