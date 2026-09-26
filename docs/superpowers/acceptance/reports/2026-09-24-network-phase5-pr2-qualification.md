# Network Phase 5 PR 2 — TIA Portal V21 qualification

**Verdict: bounded live qualification completed on 2026-09-26.** The frozen
worker-only candidate proved one exact PROFINET IO-system fixture (PN-B) and
one DP master-system fixture (DP-B), their separate controller interface owner
and master PLC hardware compile target, baseline compiles, and the field
verdicts in the [qualified contract](../../specs/2026-09-24-network-phase5-qualified-contract.md).
This was a diagnostic worker route, not a public `network_write` acceptance run.
`update_io_system` is not shipped.

## Candidate and evidence boundary

| Item | Observed identity or limit |
| --- | --- |
| Frozen executable candidate | Branch `network-phase5-pr2-qualification`; commit `03bf3871a9b7cff29276df2db3f9deec5de07b84`; tree `c3bb29852f01ac859c0d604a2d1dc260cc8cf827` |
| Ignored candidate manifest | `artifacts/live-network-phase5-pr2/fixture-b-manifest-03bf387.json`; SHA-256 `16063cf4ea4ea76ab8004a64c5f7a5f857b4e4b8a5ad2fa84a2e989b5d2746b8` |
| Built host / copied net48 worker | SHA-256 `f616a090e87df369e62d72edeb080e67f10804490117d0ea56fb11e47f8ae562` / `3132ebf4e40a68ce9a6df26d0271fe3dd3b9cbd53ac7bc7a8217fee61ae45e12` |
| Guarded private harness | SHA-256 `d48dc0b6a7f748ed6d07c150f791143cc7a6151c46d3d078d48e109387af070c` |
| Fixture | One user-authorized disposable V21 copy, with PN-B and DP-B aliases. Exact project path, selectors, requested values, session identity, and raw transport records remain ignored locally. |
| Route | Public `network_read` Inventory/Preview plus guarded worker-only `probe_io_system_qualification` Compile/Apply. The worker probe is classified as a project mutation and has no public MCP registration. |

**Current-HEAD gate remains pending.** Post-live commit `7a51365` changed
host and test project references and CI after candidate `03bf387` was frozen.
This report establishes live behavior only for the recorded candidate; it
does not qualify the later branch HEAD for merge. Re-pin the executable
candidate and resolve the changed-scope gate under the delivery plan before
claiming current-HEAD live acceptance or PR 2 merge readiness.

The earlier attached-MCP read and earlier candidate probes were reconnaissance,
not substitutes for these frozen-candidate results. The ignored manifest and
records bind the executable hashes, exact project/session, target, and evidence
chain. Raw hashes below identify the records inspected without exposing their
private content. The private harness is a prototype: it does not independently
compare every segment's `positionNumber`/`typeIdentifier` tuple in an item
path. The worker does perform the exact owner/compile-target resolution and
identity continuity checks. Later public implementation and acceptance must
retain those checks.

## Fixture metadata and compile scope

Public inspection and worker raw metadata agreed on the following observed
field types and access. Both selectors had `kind: ioSystem`, nonblank
`subnetId`, and `number`; private evidence retains the exact values.

| Fixture | Field observations | Separate hardware proof |
| --- | --- | --- |
| PN-B | `Name`: modeled/dynamic, `System.String` / `string`, available read-write; `Number`: modeled/dynamic, `System.Int32` / `integer`, available read-write; `MultipleUseIoSystem` and `UseIoSystemNameAsDeviceNameExtension`: dynamic, `System.Boolean` / `boolean`, available read-write; `MaxNumberIWlanLinksPerSegment`: `unknownAttribute` | One exact controller interface `DeviceItem` owner. Its deepest qualifying strict ancestor was a different `DeviceItem`, directly hosting the sole `PlcSoftware` in the containing `Device` and exposing `ICompilable`. |
| DP-B | `Name`: modeled/dynamic, `System.String` / `string`, available read-write; `Number`: modeled/dynamic, `System.Int32` / `integer`, available read-write; the three named PN dynamic candidates: `unknownAttribute` | The same proof structure held for DP-B's own distinct owner and strict ancestor compiler target. |

Each baseline hardware compile targeted the proven master PLC `DeviceItem`, not
the interface owner, containing `Device`, or existing PLC-software
`compile_check` path. Both returned `Success`, zero errors, and zero warnings.
The baseline records report direct PLC host, unique software, compile service,
resolved item identity, and post-read continuity as verified. A subsequent
successful edit re-proved the owner and compiler before its compile.

## One-field live matrix

An authorized Preview preceded each one-field Apply. The harness captured all
five candidate field observations and directly associated PN device names
before and after, used the returned post-write selector for identity-changing
restoration, and compiled only after transaction commit and post-read proof.
`Success` below refers to the worker's hardware compile state; "full harness"
distinguishes its final status check.

| Fixture and field | Forward and restoration observation | Verdict |
| --- | --- | --- |
| PN-B `Name` | Forward edit committed, post-read matched, hardware compile `Success` (0 errors, 0 warnings), full harness success. Reverse worker edit committed, restored the original name and five fields, and compiled `Success` (0/0). The reverse harness's final `success=false` was solely a transient `project.size` status drift. A fresh close without save and guarded reopen established a clean baseline. | Include modeled field; retain the harness limitation. |
| PN-B `Number` | Forward and reverse both passed the full harness, returned the new selector then restored the original selector, and compiled `Success` (0/0). The PN name and other observed fields remained unchanged. | Include modeled field. |
| DP-B `Name` | Forward committed, post-read matched, and passed the full harness. Reverse worker edit committed, restored the original name and fields, and compiled; its final harness `success=false` was solely transient `project.size` status drift. Both edit compiles had `Warning` with zero errors and one unchanged fixture warning concerning CPU display without password protection. Clean reopen corroborated restoration. | Include modeled field; retain warning and harness limits. |
| DP-B `Number` | Forward and reverse passed the full harness, returned and restored the selector, and compiled with `Warning`, zero errors and the same one unrelated fixture warning. | Include modeled field. |
| PN-B `UseIoSystemNameAsDeviceNameExtension` | Forward and reverse boolean toggles passed the full harness and compiled `Success` (0/0). The directly linked PN device name changed on the forward toggle and returned on reversal; the other field observations stayed stable. | Include dynamic field with linked-name safety and restoration checks. |
| PN-B `MultipleUseIoSystem` | Boolean toggle committed, but the directly linked PN name became unavailable on post-read. The worker cleared current owner/compiler proof and stopped before hardware compile with `postCommitFailure`; `mutationCommitted=true`. The copy was discarded without save. | Exclude: safe postcondition and restoration unproved. |
| PN-B `MaxNumberIWlanLinksPerSegment` | `unknownAttribute`; no setter or compile case attempted. | Exclude. |

The DP warning was observed on both sides of the DP edits and restores, with
zero compiler errors. The record does not attribute it to the IO-system fields.
No deliberately invalid value or live failed hardware compile was induced.
The `MultipleUseIoSystem` post-commit **verification** failure does not prove
the later public failed-compile response path.

## Raw record hashes and final state

| Worker-only record | Forward SHA-256 | Reverse SHA-256 |
| --- | --- | --- |
| PN-B baseline hardware compile | `51c4a7d4f1c5910b74f3acbc30490eb67149ad8485fdde7b901fbb4a607e4353` | — |
| DP-B baseline hardware compile | `83ae966e5300c45280fed7d1ca94f1e43a7b3ab94c5ed6f04e8f263c6539bcfc` | — |
| PN-B `Name` | `61f8511de597adf2c5485c0b13d632675522f5b0121864ec3c5e0579308b5435` | `725c77306a2d103bd8b875dde6b8c0a4f589a851b4d1efd950c0142c794032ef` |
| PN-B `Number` | `911fb7bb2340de6539c47c5cbe026b47a8b2149b92f13c64caf80df66ec9ea5a` | `355e7466967a47be1a748d43233e6da98dfbba9c1ef48ef34a960033f83b4fb0` |
| DP-B `Name` | `724a3e8bfa93a0b6e530e2117d2374eeaf5ab8b6a6c73c1965ee115224939d6b` | `7a8753f762db144e8daff57c3087038b785ac1430955a1b1cdca81a8f0b7c52f` |
| DP-B `Number` | `efc96386d6de75e5c6a37ddcc173f4cd26aded90c8c52f6862c427dad24a237a` | `539d8a12d401f977dffdfc959c4ea4d494d6d634e6554961d3170c55f8e6dafe` |
| PN-B `UseIoSystemNameAsDeviceNameExtension` | `bbaf46fdd5e459ea83a97da803240c4309f3f02b21000bb05503dc68bf7af229` | `29fca0e0c55ad68f4f04040d46cb59fb9a6aa629322a76bb1e7c5f62b38146e3` |
| PN-B `MultipleUseIoSystem` | `7e3810fa861786f7f6fabd5c62d0bc4143114d39065c2c494fc451b03b60770d` | Discarded without save |

After the final close without save and reopen, separate guarded Inventories
for PN-B (`36e35a0072cd9f32088a91ac70827383af71449541094cc8a5b4566b3222fb34`)
and DP-B (`26cfae6b15319e00b25e34894198d93268be71b365e51528c47c0b624f00532f`)
both succeeded with `isModified=false`. They matched the initial five-field
observations, directly associated PN names, owner, and compiler target. This
is clean reopened-fixture evidence, not a claim that the temporary edits were
saved. The local public lifecycle audit for that day contained five closes
and five opens from discard/reopen cycles, with no save or download entry;
worker-only probe calls are not written to that public audit. The probe and
reset procedure requested no project save, download, commissioning, or PLC
mode change. No persistence, plant, or installed-package acceptance is claimed.

## Offline verification after the live run

One full test-suite run with coverage reported 3,370 passed and one failed:
`DirectPack_WithSdk10_IncludesCanonicalWorkerPayload(noBuild: True)`.
Its Cobertura line rate was 0.9409, above the required 0.80. After the
post-live project-reference change, a fresh restore and serial stub and
installed-V21 solution builds each passed with zero errors and seven xUnit
analyzer warnings. The single failed packaging test then passed in a focused
coverage run. The cause of its earlier failure was not established, and the
full suite was not repeated on the later HEAD. Commit `f057f4c` corrected the
CI coverage command's xUnit argument separator; test discovery validated the
corrected invocation. These offline checks do not extend the frozen live
candidate's evidence to the later HEAD.

PR 3 and later public slices require their own strict TDD, public preview/apply
and audit acceptance, stale-state rejection, bounded known-committed failure
contract, and scope-specific live run from the then-current merged `main`.
