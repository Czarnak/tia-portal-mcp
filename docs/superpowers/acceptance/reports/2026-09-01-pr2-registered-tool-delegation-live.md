# Acceptance Test Report - PR 2 Registered Tool Delegation (live TIA Portal V21)

**Date:** 2026-09-01
**Runtime:** Real TIA Portal V21 with modular Openness API `21.0.0.0`; the read-write MCP host was started with the exact `--project` path used for the run
**Harness:** `scripts/live-test-write-safety-pr2-registered-tools.ps1`
**Boundary:** Preview only. No `apply_write_batch`, no lifecycle apply call, no project save, and no PLC mode change were performed.

## Purpose

Document that the real host advertises the registered write surface and that both a generic batch preview and a self-previewing lifecycle call succeed without mutation.

## Tested Target And Runtime

- Project path: `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`
- Type path: `PLC_LAD/Types/AnalogInputSettings`
- Attached TIA Portal process: PID `23468`
- Host binding: the exact project path above
- Execution identity: `S1614W\LCZ`
- Startup timeout: `120` seconds
- Project state before the successful run: already open in TIA Portal

The first two attempts ran under the sandbox identity `S1614W\CodexSandboxOnline`, which was not a member of the Siemens TIA Openness group. Both stopped on the first worker-backed request before Attach completed and cleaned up fully. A subsequent read-only doctor run outside the sandbox as `S1614W\LCZ` passed the TIA Portal V21, modular Openness `21.0.0.0`, and Openness-group checks.

The first attached run as `S1614W\LCZ` used the supplied example path `PLC_1/Types/AnalogInputSettings`. It bound to PID `23468` and the exact project but returned `status=failed` with `Error: No PLC software named 'PLC_1' was found in the project.` This was a fixture-input mismatch, not a host or harness failure. A registered, read-only `browse_project_tree` call on the same binding discovered the actual path `PLC_LAD/Types/AnalogInputSettings`; the unchanged preview-only harness then passed with that path.

## Tool List Evidence

The host returned the exact nine-name registered census required by the harness:

```text
apply_write_batch
archive_project
close_project
create_project
execute_read_batch
open_project
preview_write_batch
save_project
save_project_as
```

The eight write-tool names were therefore present exactly as registered. `execute_read_batch` was also present and was used only for the baseline non-mutating `get_type_content` read. That operation completed successfully.

## Generic Batch Preview Evidence

- Tool: `preview_write_batch`
- Type path: `PLC_LAD/Types/AnalogInputSettings`
- Baseline read operation ID: `read-type-content`
- Preview operation ID: `preview-update-type-content`
- Safety token: token present (redacted)
- `requestedInputHash`: `91938cc04b8bedc2713e0d7ffe683a9f769b5748565fc17e4a4b47e414dc52e9`
- `currentStateHash`: `76cc0fbec79b29fd784e2fce51df63734155b6cfc433a0fff4c4dde45f194af7`
- Instructions: `Preview only — nothing was changed. To apply, call apply_write_batch with the identical operations list, confirm=true, and this safetyToken.`

The harness reused the successful `get_type_content` result verbatim as the one-item `update_type_content` preview input. It did not send the returned token back to the host.

## Lifecycle Preview Evidence

- Tool: `save_project`
- Safety token: token present (redacted)
- `requestedInputHash`: `88b726e98a70f6bed16ec689786ebd066ef227cf82111e4dd2fdbe23e93d31cd`
- `currentStateHash`: `85e21fa4ce3bed8808eb57ed8f179fb67970cf850dffe378852e46964f035c52`
- Instructions: `Preview only — nothing was changed. To apply, call save_project again with the same arguments plus confirm=true and this safetyToken.`

The `save_project` call was tokenless and self-previewing. No confirming lifecycle call followed it.

## Notes And Limits

The successful harness ended with:

```text
No apply call was issued; this harness performed preview and read calls only.
```

No `apply_write_batch`, lifecycle `confirm=true`, project save, PLC mode change, or audit write occurred. Post-run supporting evidence found no recent harness host or Openness worker process, no `%LOCALAPPDATA%\TiaMcpServer\audit\2026-09-01.jsonl`, and unchanged project metadata: length `151373` bytes and local last-write time `2026-08-09 16:42:21`. These artifacts support, but do not replace, the no-apply protocol trace.

This report is acceptance evidence only for the exact host build, attached TIA Portal session, authorized test project copy, type path, and preview-only calls recorded here. It is not production or plant acceptance. Wrapper deletion and PLC `start_plc` / `stop_plc` hardening remain deferred.
