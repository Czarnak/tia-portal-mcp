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

`save_project_as` with `rebind: false` is resolved: it is rejected up front with a
`validation_error` response, before any preview, safety-token issuance, Siemens `SaveAs` call, or
audit write, so it has no side effects. `rebind: true` is required; see
[Write safety](#write-safety). A supported SaveAs rebinds both host and worker to the
worker-reported copied project path; verify it with a subsequent status or read call.
