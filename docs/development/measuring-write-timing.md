# Measuring write timing

`scripts/timing/measure_write_timing.py` measures the server-side duration of a write tool against a disposable project, using the installed `tia-mcp` global tool. It replaces the per-PR copies of the HMI timing client, validator and self-test. Python 3.11+, standard library only, Windows.

**Status: covered by offline tests only; not live-verified.** The behavior was carried over from the archived HMI toolchain, which did run live, but this consolidated script has not been run against a Portal.

## Prerequisites

- The tool installed globally at the version you intend to measure (`dotnet tool list --global` shows `tiamcpserver`); see [Packaging](packaging.md).
- A disposable `.ap21` copy of the fixture project, its SHA-256, and a running Portal with that project open (its process id). The script closes and reopens the project (without saving) before every workload and refuses to start if the file hash differs.
- A workload file mapping case names to tool arguments. [`scripts/timing/workloads/hmi-write.json`](../../scripts/timing/workloads/hmi-write.json) is the HMI example; its `CP1029_*`, `HM1029_*` and `HMI_RT_1` names describe the disposable project it was written for.
- Optionally a warm-read file (`{"name": "hmi_read", "args": {...}}`) run before each workload so the read snapshot is hot, and an inventory file the read result must equal.

## One worked command

```powershell
python -I scripts/timing/measure_write_timing.py run `
  --tool hmi_write --contract-version 2.0 --expected-version 4.2.0-local.113.g31d1a44 `
  --project D:\timing\copy.ap21 --project-sha256 <64 hex> --portal-pid 12345 `
  --workloads scripts/timing/workloads/hmi-write.json --warm-read warm-read.json `
  --runs 4 --out D:\timing\out-2026-11-01
```

`HMI_TIMING_PROJECT` and `HMI_TIMING_PROJECT_SHA256` replace `--project` and `--project-sha256`. The server starts with `--access-mode full --max-item-chars 1000000 --max-document-chars 1000000 --worker-timeout-minutes 15` unless you repeat `--server-arg`. The asserted `serverInfo.version` is derived from `--expected-version` (`4.2.0-local...` becomes `4.2.0.0`); override it with `--server-version`.

The schedule is: startup `bind_project`, a discarded warm read, then for each run and case a restore (close without saving, open, status check), the workload, and a warm read; a final inventory read ends the run. Run 0 of every case is discarded. Each request has a 900 s deadline (`--request-timeout`); on timeout the script writes `pending-call.json` and stops without replaying. A failed tool call also stops scheduling. Existing result files are never overwritten, so use a new `--out` directory for every run. `run --stdin` instead reads one JSON command per line (`{"restore":true,"label":...}`, `{"name":...,"args":...,"label":...}`, `{"stop":true}`) for hand-driven captures.

Each write is matched to its audit v2 record in `%LOCALAPPDATA%\TiaMcpServer\audit\writes-*.jsonl` by identical `responseText`. The text block must equal `structuredContent`, the hash and contract version must match, and the record must be fresh.

## Output files

In `--out`: per-call `<label>.json` and `<label>-request.json`, `measurements.jsonl`, `audits.jsonl`, `initialize.json`, `provenance.json`, `server-stderr.log`, and the archive set `timing-writes.jsonl`, `hmi-reads.jsonl`, `lifecycle-status-projections.json`, `workloads.json` and `summary.json`. `summary.json` holds per-run `rootMs` (audit duration), `itemSumMs` (sum of item durations) and `remainderMs` (their difference), and the per-case medians over the non-discarded runs, each column taken independently.

## Validating an archive

```powershell
python -I scripts/timing/measure_write_timing.py validate D:\timing\out-2026-11-01
python -I scripts/timing/measure_write_timing.py self-test
```

`validate` is offline. It derives the expected write, read, projection, measured and discarded counts from the cases and runs recorded in `summary.json`, then re-checks each canonical write/audit pair, the labels, the medians and read consistency.

Archived outputs are not kept in-tree; the earlier per-PR timing data is in the git tag `docs-archive-2026-10-30`.
