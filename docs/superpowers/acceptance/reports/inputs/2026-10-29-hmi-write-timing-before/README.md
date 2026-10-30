# HMI installed-version timing evidence, 2026-10-29

The [timing report](../2026-10-29-hmi-write-timing-before.md) interprets this evidence. These are measurements of installed `4.2.0-local.79.g75c0568` (`75c0568`), using the protocol in [design §6.6](../../../../specs/2026-10-29-hmi-write-native-rejections-design.md). The design names `0b2640a`; that exact older baseline was not measured, and source equivalence is not claimed.

## Contents and verification

- [summary.json](summary.json): all 20 timing rows, five independent three-sample medians, runtime configuration, project-file hash and installed binary hashes.
- [workloads.json](workloads.json): exact H1–H5 public `hmi_write` argument objects, including H2's JSON number `conditionValue:30`.
- [timing-writes.jsonl.gz](timing-writes.jsonl.gz): exact 20 requests and canonical MCP response wrappers, matching audit projections, source response times and captured measurements. Only `audit.projectPath` and `audit.binding.projectPath` were replaced with `<disposable-copy>`; `responseText` and `responseHash` are unchanged. Each unredacted local audit-capture line also has a SHA-256 reference; this hashes the original collector line, not the server's original audit-file line.
- [hmi-reads.jsonl.gz](hmi-reads.jsonl.gz): exact startup, per-case warm-up and final HMI inventory request/response wrappers (22 captures). Each read is complete and records 101 tags, nine tables and four alarms.
- [lifecycle-status-projections.json](lifecycle-status-projections.json): 66 explicitly redacted projections of binding, lifecycle and status responses. These are derived evidence, not original canonical documents. They retain canonical-response SHA-256 references, successful exact configured-path comparisons, clean/open state and client elapsed time where captured. Actual project names and paths are omitted.
- [client.py](client.py) and [self_test.py](self_test.py): exact client source used and its offline self-check. Executing the client's main routine starts the installed tool; importing the client or running the self-test performs no live work.
- [validate_archive.py](validate_archive.py): offline archive checks, including independent median recomputation.
- [manifest.json](manifest.json): byte counts and SHA-256 for every other archive file, with additional decompressed hashes for gzip files. The manifest does not hash itself.
- [.gitattributes](.gitattributes): disables line-ending conversion in this evidence folder so exact source and manifest hashes survive checkout.

Run `python self_test.py` and `python validate_archive.py` from this folder. Neither command starts a server or calls TIA Portal.

## Replay protocol

Use the same disposable project file and qualified fixtures. The project file SHA-256 is in `summary.json`; it remained unchanged. The selected HMI is `HMI_RT_1`, with 101 tags, nine tables and four alarms before every case. H3 deletes `CP1029_TableB`, containing `CP1029_TagB`, referenced by the existing discrete alarm. H2's new alarms reference stable tags outside its temporary table so its final table deletion does not invalidate those alarms.

Set environment variables `HMI_TIMING_PROJECT` to the absolute `.ap21` path and `HMI_TIMING_PROJECT_SHA256` to its expected lowercase SHA-256. Run `python -u client.py` in a fresh writable capture folder containing the source. The client verifies the installed version and starts it in `full` mode with `--max-item-chars 1000000 --max-document-chars 1000000 --worker-timeout-minutes 15`. Each JSON-RPC request has a 900-second client deadline.

Send one JSON object per stdin line. Start with a `bind_project` command containing the exact already-open configured project path and Portal PID. The command form is `{"name":"bind_project","args":{"projectPath":"<absolute-ap21-path>","portalProcessId":12345},"label":"startup-bind"}`. Binding never opens or saves the project. Then perform a discarded untimed `hmi_read` containing `list_tag_tables`, `list_tags` with `limit:2000`, and `list_alarms` with `limit:2000`, all for the selected HMI. This warms the new worker; the startup read contributes no timing sample.

Run serially in round order: run 0 H1→H5, then run 1 H1→H5, run 2 H1→H5, and run 3 H1→H5. Run 0 is the discarded per-workload warm-up. Before every case, including H4 preview, send `{"restore":true,"label":"H1-run0"}` with the appropriate unique case/run label. This closes with `saveBeforeClose:false`, reopens the same file, verifies its hash, and asserts exact configured path plus `isOpen:true,isModified:false`. Perform the same untimed complete HMI read after every reopen. Then send `{"name":"hmi_write","args":<workloads.json case object>,"label":"H1-run0"}`. Requests and canonical responses are captured before validation; any failed call stops scheduling. Do not run builds, tests or other live requests alongside timing collection.

After the last write, explicitly restore once more, perform the complete HMI inventory read and get clean project status, then send `{"stop":true}`. The client does not automatically save, restore, retry or replay on stop or failure. A 15-minute request timeout writes pending-call evidence and stops scheduling; the outcome must be inspected before further work. No timeout occurred in this series. The timing helper exited with code 0; an independent status check confirmed the same project open and unmodified afterward.

Use audit v2 root `durationMs`, the sum of `items[].durationMs` (null contributes zero), and root minus that sum. Compute each median independently over runs 1–3. Do not time lifecycle, warm-up reads or startup. Source response modification times are retained for the client's original audit freshness check; extracted archive file timestamps are not measurement evidence. Preview H4 has null item durations and an item sum of zero. Dependent-item replanning lies inside the baseline item timers, so remainder is not all planning time.

The candidate replay must use the same requests, fixtures, budgets, reset/read sequence and measurement rules. Change the client's explicit expected-version check to the installed candidate while retaining the baseline source unchanged in this archive. No product code, refactor, HMI compile or candidate timing is qualified by this evidence.
