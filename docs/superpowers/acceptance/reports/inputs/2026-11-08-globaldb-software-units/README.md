# GlobalDB software-unit evidence

Backs the [2026-11-08 qualification report](../../2026-11-08-globaldb-software-units-live-verification.md).

- `native-candidates.jsonl.gz`: direct Openness setup, nine import cases and cleanup.
  Each case identifies its exact input XML, native result/exception, resolved presence,
  compiler counts/state and the exported MemoryLayout element. The exception records
  include the native PlcBlockComposition.Import stack; no MCP envelope is present.
- `Issue221_*.xml`: the nine exact import documents referenced by the candidate trace.
- `native-readback.jsonl.gz`: independent direct Openness reads after the fixed
  worker exited; CPU type, executable hash, optimized layout, compiler counts/state
  and cleanup absence.
- `probe-candidates.ps1` and `probe-built-worker.ps1`: source used for the successful
  native comparison and completed fixed-worker/readback run. The latter also
  produces transient worker IPC traces, which are not retained here.

The scripts retain their run-time parameters/defaults and exact target assertions.
They are diagnostic evidence, not general-purpose tooling. When running the archived
scripts, supply absolute SourceGeneratorPath/WorkerPath and EvidenceDirectory values:
their original defaults assumed the .superpowers/issue-221-globaldb work directory.
The candidate probe requires the pre-fix generator from commit
`5af42c140269b74fd7cd3c69598e2b2a810bc67c`; the nine input documents preserve its
actual generated XML independently of future source changes.

The compressed traces decompress byte-for-byte to the original successful run files.
They were parsed and checked for nine candidate records (7 returned, 2 threw), three
optimized native readbacks, clean compilation and cleanup=true.
No redaction or field filtering was applied. The traces contain the disposable
project path under the local Windows user profile.
