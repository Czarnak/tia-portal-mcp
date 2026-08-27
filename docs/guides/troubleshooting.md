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
