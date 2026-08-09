# Acceptance Test Report — VCI Workspace Phase 1 Read-Only Probe

**Verdict:** PASS

**Run ID:** `20260809T072541470Z-d49e4677`

**Started:** 2026-08-09 07:25:41 UTC

**Completed:** 2026-08-09 08:09:29 UTC

**Plan:** [VCI workspace Phase 1 — read-only probe](../../plans/2026-08-08-vci-workspace-phase1-read-probe.md), Task 10

**Raw evidence:** `artifacts/live-vci-phase1/20260809T072541470Z-d49e4677/`

## Authorization and execution scope

The user separately supplied and authorized both absolute `.ap21` paths and the
exact command below. The run was authorized to attach to TIA Portal V21 twice,
read VCI/workspace engineering objects, read and hash discovered workspace
files, and write only the local evidence bundle. The secondary-project read was
explicitly enabled for `N-FMT-FOREIGN`.

```powershell
pwsh -NoProfile -File "C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\scripts\live-probe-vci-phase1-read.ps1" `
  -Mode Run `
  -ProjectPath "C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21" `
  -SecondaryProjectPath "C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\Anwis_TT_V1.9.8_copy\Anwis_TT_V1.9.8_copy.ap21" `
  -AllowSecondaryProjectRead `
  -WorkerExecutable "C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe" `
  -EvidenceRoot "C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\artifacts\live-vci-phase1" `
  -TimeoutSeconds 240
```

The command exited `0` with `overallPass: true`. The harness and both read-only
workers exited normally. Existing TIA Portal processes were left running.

## Provenance

| Item | Evidence |
| --- | --- |
| Repository | Commit `bf190ba1a3eb11d0989de2a59a2b557f1baa6e85`; clean; 0 tracked changes at run start |
| Script | `scripts/live-probe-vci-phase1-read.ps1`; SHA-256 `b9590fa12c2855f9e7cfb242da8dea149adda65f94c1462926f16227d1355a8f` |
| Worker | `TiaMcpServer.OpennessWorker.exe`; SHA-256 `312459f9711e361cfc982af9583a6fc20ddb9c6aa46c7eb5dd1aa39d6898766b` |
| Worker sessions | `session-1` PID 39080; `session-2` PID 17428; one distinct worker per session |
| Access boundary | `probe_vci_read_contract`; worker access mode `read-only`; `readOnly: true`; `mutatesProject: false` |
| Environment | Windows `10.0.26200.0`; PowerShell `7.6.3`; .NET SDK `10.0.302` |
| Primary project | `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21` |
| Authorized secondary project | `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\Anwis_TT_V1.9.8_copy\Anwis_TT_V1.9.8_copy.ap21` |

Fresh post-run hashing matched both script and worker hashes recorded in
`manifest.json`.

## Acceptance conditions

| Condition | Result | Evidence |
| --- | --- | --- |
| Complete immutable bundle | PASS | All seven declared files exist; `evidenceComplete: true`; no raw evidence was edited. |
| Exact case coverage | PASS | 128 records: 64 per session, sequences 1–64; all 20 planned case IDs present in each session; every record terminal. |
| Transport integrity | PASS | 128 `response` records; 0 transport failures, evidence failures, or process-loss records. |
| Independent sessions | PASS | Session 1 used only PID 39080; session 2 used only PID 17428; both workers completed. |
| Normalized repeatability | PASS | `normalizedMismatches: []`. |
| Project state | PASS | All 128 records observed `isModifiedBefore: false` and `isModifiedAfter: false`; invariant unchanged. |
| Workspace filesystem | PASS | Two roots; 84 files; 596,401 bytes; complete before and after; identical normalized inventory/hashes; 0 omissions. |
| Post-negative canaries | PASS | Both final `R-CANARY` records returned usable snapshots. |
| Snapshot-after coverage | PASS | Each session returned exactly `R-SVC`, `R-GRP`, `R-WS`, and `R-MAP` after its canary. |
| Exceptions and omissions | PASS | No `threw` observations, exception payloads, probe omissions, or filesystem omissions. |

The stable live inventory contained 354 reflected member observations, zero
child groups, two workspaces, 48 mappings, and zero preselected format
candidates. Empty group inventory was accepted as valid observed state.

## Case outcomes

Counts below combine both sessions. Each session produced the same outcome
distribution: 47 `returned`, 9 `not_observable`, and 8 `returned_null`.

| Case | `returned` | `returned_null` | `not_observable` | Live interpretation |
| --- | ---: | ---: | ---: | --- |
| `R-SVC` | 2 | 0 | 0 | Service/root-group snapshot returned in both sessions. |
| `R-GRP` | 2 | 0 | 0 | Group observation returned; child-group inventory was empty. |
| `R-WS` | 2 | 0 | 0 | Two workspaces observed consistently. |
| `R-MAP` | 2 | 0 | 0 | 48 mappings observed consistently. |
| `R-FMT` | 80 | 0 | 4 | Forty applicable pairs per session returned; two instances per session had no candidate pair. |
| `N-FMT-FOREIGN` | 0 | 0 | 4 | Authorized secondary project was available, but no applicable workspace was available for these instances. |
| `N-FMT-NULL` | 0 | 0 | 4 | No applicable workspace was available for these instances. |
| `N-FMT-UNSUPPORTED` | 0 | 0 | 4 | No applicable workspace was available for these instances. |
| `N-GRP-FIND-EMPTY` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `N-GRP-FIND-MISSING` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `N-GRP-FIND-NULL` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `N-GRP-FIND-WHITESPACE` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `N-MAP-INACCESSIBLE-FILE` | 0 | 0 | 2 | No naturally inaccessible mapping file existed. |
| `N-MAP-MISSING-FILE` | 2 | 0 | 0 | Missing-file observation returned in both sessions. |
| `N-WS-FIND-EMPTY` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `N-WS-FIND-MISSING` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `N-WS-FIND-NULL` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `N-WS-FIND-WHITESPACE` | 0 | 2 | 0 | Typed `Find` returned null in both sessions. |
| `R-REP` | 2 | 0 | 0 | Repeatability observation returned in both sessions. |
| `R-CANARY` | 2 | 0 | 0 | Both terminal canaries returned usable snapshots. |

### Cases that remained `not_observable`

| Case | Count | Exact reason |
| --- | ---: | --- |
| `R-FMT` | 4 | `no_workspace_candidate_pair` |
| `N-FMT-FOREIGN` | 4 | `no_workspace_available` |
| `N-FMT-NULL` | 4 | `no_workspace_available` |
| `N-FMT-UNSUPPORTED` | 4 | `no_workspace_available` |
| `N-MAP-INACCESSIBLE-FILE` | 2 | `no_naturally_inaccessible_mapping_file` |

These are state-dependent live observations, not infrastructure failures. In
particular, this run does not characterize the actual foreign/null/unsupported
format-call behavior because the required workspace condition was absent.

## Evidence boundaries

- **Vendor-free contract evidence:** the final static gate passed 204/204
  Workspace tests, 2172/2172 full tests, and a serialized reference-stub build
  with 0 warnings and 0 errors. The later strict-mode harness fix passed the
  focused script suite 21/21 twice.
- **Real-reference compile evidence:** the installed TIA Portal V21 reference
  worker build passed with 0 warnings and 0 errors. This is compile evidence,
  not runtime evidence.
- **Live Siemens V21 evidence:** only run
  `20260809T072541470Z-d49e4677` supports the live conclusions in this report.
  It attached through two fresh read-only worker processes and completed the
  exact guarded matrix.
- **Unobserved behavior:** the five rows above remained `not_observable`; no
  claim is made about the vendor behavior behind conditions the projects did
  not naturally provide.

## Raw artifact hashes

| File | SHA-256 |
| --- | --- |
| `cases.jsonl` | `e6ca1755b042c6bdc683990cebef540247b4b0ec535f74e891d1cf30e24fcbf3` |
| `filesystem-after.json` | `a55fe790ff40a68c82770703697dc885407c8f8f0b3cd15d8860054338ff75e4` |
| `filesystem-before.json` | `21c6266755fa97427cc53c185c5e229958df59e55f7f3d1da964cd2f9e6c3369` |
| `manifest.json` | `237c4f5fa85b5d08f2a99839e65689faa69acf1964951226d56bd1b4d0ab79e9` |
| `snapshot-after.json` | `efa1cf0903575b8abc96fd8b4a07ba919cd3825fb33789c50a0bbceec9e212b0` |
| `snapshot-before.json` | `22206f3ffb9b54b7cc467fb1b49e357bbbfd3e99e69dea2277a80747586cb867` |
| `summary.json` | `39ee9f5e338ed10d0f2c4cfff8bae87c7b05d9f68295ebe4248ef92f0e05ee7e` |

The before/after document hashes differ because capture timestamps are part of
the raw documents. The harness's normalized filesystem comparison removed only
capture metadata and confirmed identical roots, paths, sizes, timestamps, and
content hashes.

## Overall verdict and next gate

**PASS.** The separately authorized Phase 1 live read-only acceptance completed
with all evidence, repeatability, project-state, filesystem, and canary gates
satisfied. This report is the mandatory review stop. It does not authorize or
design any mutating probe; mutation work remains blocked until the user reviews
this evidence and explicitly requests the next gate.
