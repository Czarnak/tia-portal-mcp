# PR4 existing local sessions — scoped maintainer live acceptance

**Accepted within scope on 2026-10-08; original noninteractive offline opening failed in both
tested read-write fixtures.** Online retained-Portal recovery passed in full/read-write.
Offline previews passed; actual opens completed only after operator-dismissed Siemens dialogs.
The maintainer answered **“Accept scoped results; document limits”** and authorized documentation.
This is a scope ruling, not an aggregate all-L01–L19 PASS or proof that unexecuted cases ran.

The maintainer also expressly excluded baseline retesting: “No read-only. Lifecycle itself was
tested before. Keep focus on current scope only.” Prior lifecycle/PR3 acceptance remains historical
evidence and does not supply PR4-specific runtime evidence. The
[definition](../2026-10-07-multiuser-pr4-live-definition.md) and
[plan](../../plans/2026-10-07-multiuser-pr4-open-existing-sessions.md) retain original expectations.

## Frozen candidate, offline qualification and installed provenance

| Evidence | Recorded value |
| --- | --- |
| Tested implementation | `feature/multiuser-pr4`, `3772edad0ce9e90390bc36deb71dbd8140511424`; clean, ahead two at freeze |
| Version | `3.0.1-local.406.g3772eda` |
| Package | `TiaMcpServer.3.0.1-local.406.g3772eda.nupkg`, 2,836,028 bytes |
| Package SHA256 | `67CF11516D441B513C24C4920C93A025AEDBD952BD0226B4D52CA26AFD6454A9` |
| Installed host DLL SHA256 | `4F67F65C78AC1A98E6375AB0F0D1B5F9E7E75B0755462C071F179B1367BFCFE5` |
| Installed adjacent worker EXE SHA256 | `B32D91E8FF3FD20F1BB307979EFB6042F9D642B080353BF86340189466EF7757` |
| Full offline suite | 5,508 passed, zero failed/skipped; root line coverage 94.09%, above 80% |
| Cumulative changed instrumented host/contracts lines | 563/583 covered (96.57%); worker coverage **unmeasured** |
| Static/package gates | Serial stub and installed-V21 solution/API compilation, source-reference drift and package layout/exclusion passed |

Frozen qualification is retained in ignored `task-5-offline-3772eda-report.md`; the recovery
implementation is `e4d73a8`, followed by the test-only discovery expectation repair `3772eda`.
The installed-reference/drift checks retained from `e4d73a8` cover identical production/reference
source. The failed earlier full run and older candidate live evidence are retained as history,
not final qualification. Four pre-existing linked-source CS8602 warnings and the existing Step7
CS0108 warning remain. Coverage excludes actual net48 worker execution; doubles/API compilation
are distinct from installed-public-tool live evidence.

The user installed the matching package and confirmed the global launch command in full, then
read-write, without a startup project selector. Recorded workers matched the exact installed
executable (full PID 34408, read-write PID 74324). Installed host DLL and adjacent worker hashes
matched final package bytes; the actually loaded host module hash was **not** directly inspected.
TIA Portal V21 was used. Compile references reported Base/Step7 `2100.0.121.1` and WinCCUnified
`2100.200.5.1`; these are API file versions, not separately captured TIA/Project Server runtime
build versions. No agent installation or .NET/package rerun followed this documentation step.
This report qualifies the tested implementation, not a subsequent documentation-only commit.

## Fixtures and product evidence

| Synthetic role | Independent operator fixture knowledge | Retained UI Portal |
| --- | --- | --- |
| A | Disposable Exclusive local session | PID 48288 |
| B | Disposable Multiuser local session | PID 45848 |
| E | Dedicated endpoint mapped by the operator to A/B, stopped and restored explicitly | Exact private selectors retained only in ignored evidence |
| H | Headless/retaining-client setup | Not prepared/executed |

All tracked fixture/path descriptions are redacted roles, not literal observed names. Each
actual open was verified against the exact private ALS input, typed AMC engineering owner and
original Portal PID; the other UI fixture remained open. The initial full-mode B Portal 55660
was accidentally shut down by the operator during setup. Attachment loss invalidated binding;
fresh adoption in B's replacement Portal 45848 recovered. This setup interruption is not a
deliberate qualified worker-loss case.

Product status reported `containerKind:"localSession"`, `sessionMode:"unknown"`,
`remoteIdentity:null` and active connection `unknown`. Fixture creation modes and endpoint
mapping did not populate a reverse ALS/server-inventory join. Cold adoption had no worker
ownership/ALS provenance; successful opening reported `openedByWorker:true`, exact AMC path
and known exact ALS provenance. Distinct AMC paths establish observed local owners only,
not durable remote session identity for sessions associated with the same server project.

## Observed openings, preservation and prompt evidence

For both fixtures/modes, the operator closed only the target project and retained its exact UI
Portal. Discovery invalidated the old binding. Forced previews from the retained empty Portal
passed with no guards/source-close, left the host unbound and target closed, and preserved the
other UI fixture. Actual opens returned `applied` with successful typed result/verification,
correct AMC/ALS, worker ownership and original PID. These are **two-Portal retained-empty
recovery tests**, not the complete sole-Portal initial-opener matrix.

Full mode additionally passed continuously verified same-ALS preview/actual reuse without a
second opener; binding revision 6/generation 8 remained stable. Different-ALS opening was
blocked by `local_session_requires_terminal_operation`. Generic close/save previews and compile
returned `unsupported_capability`. Ordinary status asserting B's different AMC while A was
bound returned `binding_conflict` and preserved A. Cross-Portal adoption preserved B and
relinquished its worker ownership. Full actual audits used `policy/not_requested`; previews
and guard denials used `none/not_requested`. A separate final operator Siemens-dialog/UI
observation was not answered, so no absence of Siemens UI in full is claimed.

In read-write, the cold UI-owned B source preview exposed
`local_session_source_preservation_unproved`; actual W04 blocked before confirmation/mutation.
W08 B and W18 A online recovery each had **exactly one human-observed MCP prompt accepted**,
and the operator confirmed no additional Siemens dialog and both original UI projects open.
W11 same-ALS actual reuse had exactly one human-observed prompt declined: canonical
`access_denied`, `phase:"blocked"`, null result/verification and unchanged healthy B ownership.
The audit correctly records `user/declined`; the historical blanket L09 `none` expectation is
stale and did not require a production change.

## Offline result and restoration

W22 established an explicit positive `connected` inventory baseline for E. The operator stopped
that dedicated server while both UI projects remained open. W24 returned generic
`worker_operation_failed`: connectivity/authentication remained unproven, not typed
`unavailable`. Local W25 status and W26/W27 B adoption/status continued successfully.

| Fixture | Preview | Actual/post-open result | Original noninteractive expectation |
| --- | --- | --- | --- |
| B / Multiuser | W29 PASS; W30 B closed in PID 45848, A preserved in PID 48288 | W31 UI-assisted applied/success; W32/W33 exact identity, ownership, healthy unmodified status and original PIDs | **FAIL** — Siemens dialog required dismissal |
| A / Exclusive | W36 PASS; W37 A closed in PID 48288, B preserved in PID 45848 | W38 UI-assisted applied/success; W39/W40 exact identity, ownership, healthy unmodified status and original PIDs | **FAIL** — Siemens dialog required dismissal |

For B the operator observed an MCP prompt and supplied Siemens dialog text: “The connection
to the server cannot be established”. B was closed before dismissal and opened after it.
Exact human MCP prompt count/acceptance was not separately confirmed; `user/confirmed` audit
is protocol evidence. For A the operator separately confirmed exactly one MCP prompt accepted,
a Siemens dialog dismissed, server still stopped and both projects open. A's exact vendor
dialog text was not supplied. UI-assisted success does not override the noninteractive FAIL
or establish that all offline opening is unsupported. Full-mode offline remains unexecuted.

The user restored the server. W41 read the **same exact recorded endpoint** as W22 and returned
`connected`; `previousState:null` and `transition:false` mean no reconnection-transition claim.
Active remote identity/connection remained null/unknown throughout. W42 showed A open,
unmodified and worker-owned; W43 showed both UI projects open at original PIDs 48288/45848.
Restoration was recorded verified with no pending action. No save, commit, session terminal
action, generic session close, automatic cleanup or mutation replay occurred.

## Canonical responses, audits and retained artifacts

Private raw artifacts remain ignored under `TestResults/pr4-live/3772eda/2026-10-08-full-recovery/`.
They contain exact requests/responses; tracked documentation uses synthetic roles.

| Phase | Final public-call artifact | Calls/canonical matches | Final audit correlation | Entered lifecycle records |
| --- | --- | ---: | --- | ---: |
| Full | `public-calls-03-full-mode-complete.json` | 30/30 | `audit-correlation-02-full-mode-final.json` | 10, baseline 8 → 18 |
| Read-write online | `read-write-calls-06-final.json` | 20/20 | `read-write-audit-02-final.json` | 7, baseline 18 → 25 |
| Read-write offline/restoration | `offline-calls-09-restored-final.json` | 23/23 | `offline-audit-01-final.json` | 4, baseline 25 → 29 |
| Final candidate total | 30 full + 43 read-write | **73/73** | Exact delivered text/SHA256, unique match and no interleaving | **21** new; 29 total including 8 historical |

The offline final call JSON does not itself embed the later audit artifact; the two were
correlated separately. Actual-open audits preserve entry binding snapshots, including unbound
recovery state; later status/inventory proves opened identity. Read-write previews/guard denial
use `none/not_requested`, accepted actuals `user/confirmed`, human decline `user/declined`.
Client acceptance/audit does not prove human dialog observation beyond the explicitly recorded
operator statements. Working summaries are derived checkpoints; final call/correlation files
are the primary retained evidence. Earlier snapshots and failed runs remain historical.

## Original case disposition

PARTIAL records scoped subcases, never an aggregate case PASS. BLOCKED here means unexecuted
in this final-candidate run; the scope ruling permits documentation without silently waiving
the facts or claiming the original aggregate gate passed.

| Case | Final disposition |
| --- | --- |
| L01 | PARTIAL: B retained-empty recovery preview/open PASS in full/read-write; sole-Portal/complete original setup not qualified. |
| L02 | Read-write preview PASS; noninteractive actual **FAIL**, UI-assisted actual PASS; full offline BLOCKED/unexecuted. |
| L03 | PARTIAL: A retained-empty recovery preview/open PASS in full/read-write; sole-Portal/complete original setup not qualified. |
| L04 | Read-write preview PASS; noninteractive actual **FAIL**, UI-assisted actual PASS; full offline BLOCKED/unexecuted. |
| L05 | PARTIAL: exact AMC adoption/status in writable modes, including offline read-write; read-only/all original negatives unexecuted. Read-only baseline retest excluded. |
| L06 | BLOCKED/unexecuted startup-selector matrix. |
| L07 | PARTIAL: same-owner status/binding and full same-ALS reuse stable; all omitted/null/no-owner variants not executed. |
| L08 | PARTIAL: exact two-Portal selection and differing-path binding conflict observed; duplicate/same-path ambiguity and all selector negatives unexecuted. |
| L09 | PARTIAL: human read-write decline PASS with preserved owner; cancel/missing-confirm/no-form variants unexecuted. |
| L10 | PARTIAL: A/B cross-Portal adoption preserved both and relinquished ownership; standalone/modified variants unexecuted. |
| L11 | PARTIAL: cold-source preview/actual guard PASS; supported borrowed-source coexistence opener matrix unexecuted. |
| L12 | PARTIAL: clean owned source different-ALS block, same-ALS reuse and close/save-preview/compile rejection PASS; modified/all-operation variants unexecuted. |
| L13 | BLOCKED/unexecuted retaining standalone cursor matrix. |
| L14 | Not rerun; general standalone lifecycle baseline expressly excluded. |
| L15 | BLOCKED/unexecuted headless/last-client matrix. |
| L16 | PARTIAL: endpoint stop/restore, generic failure and local preservation PASS; no typed unavailable or reconnect transition observed. |
| L17 | BLOCKED/unexecuted prompt-time source/destination drift. |
| L18 | PARTIAL: operator project-only closure invalidated and explicit recovery passed; same-path replacement/deliberate worker-loss injection unexecuted. Accidental setup shutdown is not that test. |
| L19 | PARTIAL: compile/save/close-preview negatives and relevant discovery/endpoint reads observed; full content/write negatives/all six inventory matrix not repeated. |

## Delivered boundary and remaining work

The maintainer accepted these scoped results after the offline dialogs and unexecuted limits
were disclosed. Delivered support is exact AMC adoption/basic status/startup assertions and
explicit ALS opening with owner-aware preservation; accepted runtime evidence covers the
recorded retained-Portal online recovery and UI-assisted read-write offline behavior.
Noninteractive offline failure, full Siemens-UI observation gap, full-offline/headless/race,
startup/ambiguity/cursor/worker-loss and other unexecuted rows remain limitations. This report
does not establish PLC/plant acceptance, autonomous offline opening, all clients, or future
session capabilities. Issue #65 and reliable distinction of sessions bound to the same server
project remain open; local content/compile/save/markings/terminal/server mutation are successors.
No independent review was added for the post-live documentation; the recorded recovery review
waiver and prior qualification are retained. No upstream/HMI, push/PR/merge or remote work occurred.
The final recovery independent review was expressly waived by the maintainer: “No independent
review. Apply patch and if tests pass, then we proceed directly to live-testing.” Final amended
source had implementer/controller self-inspection and offline qualification; earlier reviews
remain historical, not a fresh independent review of `3772eda`.
