# PR4 existing local sessions — installed-public-tool live definition

Protocol originally planned October 7; recorded after the October 8 confirmed live run.
This is a historical acceptance definition, not a claim that every case passed. The
[report](reports/2026-10-08-multiuser-pr4-live-verification.md) distinguishes original
expectations, observed outcomes and the maintainer's scoped acceptance decision.

## Identity, fixtures and execution boundary

Use exact existing `.als21` files only for deliberate writable `open_project`. Adoption,
basic status and startup assertions use the observed typed `LocalSession.Project.Path`
`.amc21` engineering path, optionally the exact binding Portal PID. Cold adoption has no
worker ownership or ALS provenance. An ALS input is opener provenance only after verified
success; no inventory directory/filename or fixture mode supplies a reverse session join.

Tracked documents use synthetic A (operator-created Exclusive), B (operator-created
Multiuser), H (headless) and E (dedicated endpoint) roles. Exact private paths, endpoint keys
and operator identity stay in ignored local evidence. Creation mode/endpoint mapping and
product `sessionMode`/remote identity are separate evidence. The original online/offline and
read-write/full matrix preferred separate copies; the actual run reused A/B under explicit
operator-controlled project-only closure, retained each UI Portal, and preserved the other
fixture. H was not prepared or executed.

Freeze a clean implementation candidate after serial offline qualification, real-V21 API
compilation and the applicable review/ruling. Verify the actual installed version, package,
host DLL and adjacent worker hashes, launch mode/startup selector and observed worker path.
An ancestor-locator fallback or different installed build cannot qualify the candidate.
Calls go through the locally installed public MCP tool, serialized on the shared TIA state.
No dedicated PR4 harness, direct-worker substitute, global agent installation or automatic
session cleanup is part of the protocol.

## Original cases and pass criteria

The [implementation plan](../plans/2026-10-07-multiuser-pr4-open-existing-sessions.md#live-cases-requests-and-pass-criteria)
retains the detailed original requests. Every stated mode/state row needs its own evidence.

| Case | Original expectation |
| --- | --- |
| L01 | Closed online Multiuser: preview and actual open in read-write/full, sole supported Portal; exact owner/result/verification, preview preservation, one read-write MCP prompt, full policy, one audit per entered call. |
| L02 | Closed offline Multiuser in both writable modes: **noninteractive** local opening despite independently proved remote loss, local status available, no fabricated remote state; vendor dialogs recorded separately. |
| L03 | L01 for an independently operator-proved Exclusive fixture. Product mode is not inferred from names. |
| L04 | L02 for Exclusive, including the noninteractive expectation in both writable modes. |
| L05 | Existing online/offline owners adopted/status-read in all modes without opener/audit; cold provenance null/ownership false; ALS adoption denied. Read-only has six tools and no opener. |
| L06 | CLI (`--project S`, `--project=S`) and environment AMC assertions, online/offline/closed targets, precedence and duplicate-CLI failures; no implicit opening. |
| L07 | Omitted/null/explicit bind action and repeated same-owner AMC verification preserve generation/binding/ownership; no-owner returns not-found. |
| L08 | Multiple owners/Portals, exact AMC/PID and mismatches, missing/ALS/directory assertions, same-path ambiguity; deterministic selection and preservation on pre-detach refusal. |
| L09 | Read-write decline/cancel/missing confirm/no form capability deny before opener and preserve binding. A human declined response is audited `user/declined`; previews/pre-prompt guard denials use `none/not_requested`. Other refusal variants were unexecuted and retain the shared pipeline contract. |
| L10 | Standalone/local and cross-Portal force switching preserve both owners and modified state; detach relinquishes worker ownership. |
| L11 | Borrowed/UI local source and cold ALS provenance: coexistence/duplicate/source preservation must be proved, otherwise block before opener. |
| L12 | Worker-owned local source, clean/modified: different open blocks; unsupported standalone/local-save/terminal calls do not mutate; known same-ALS reuse performs no second opener and follows confirmation/audit rules. |
| L13 | Standalone cursor remains valid on same-owner refresh and rejects revision changes/switch-back; local content remains unsupported. |
| L14 | Original standalone lifecycle/metadata/ownership/prompt/audit regression matrix in both writable modes. |
| L15 | Safe headless/last-client detach and shutdown preservation for clean/modified/unreadable local owners; never kill a sole owner to manufacture the test. |
| L16 | Dedicated endpoint online/offline/online observations with local status/binding preserved; typed remote loss only when proved, endpoint history scoped to actual attachment/configuration, no unsupported active-session join. |
| L17 | Prompt-time source/destination drift replans and denies changed consequences before mutation; no replay. |
| L18 | External same-path owner replacement and deliberate worker loss invalidate identity; explicit recovery adopts existing owner without replay or restored ownership. |
| L19 | Representative local content/compile/write negatives and all six PR3 inspections, strict canonical/budget contracts and preservation. |

The historical L09 blanket `confirmation:none` after human refusal conflicts with the existing
shared audit contract. The corrected criterion above describes that contract; no production
fix or conversion of an unexecuted refusal variant to PASS is implied. The original L16
reconnection-transition expectation also needs same-history evidence; a restored positive read
with `previousState:null`/`transition:false` does not demonstrate such a transition.

## Public sequence and evidence

1. Discover Portal owners without adoption; capture exact before-state and candidate mapping.
2. Adopt the authorized AMC/PID and read local basic status. Inspect a specific endpoint only
   using independently authorized exact selectors; local adoption does not require inventory.
3. Let the operator perform authorized project-only closure or endpoint stop/start. Rediscover
   and capture invalidation/retained empty Portal and preservation of the other UI fixture.
4. Preview an exact ALS open with `dryRun:true` and force only where required. Inspect unchanged
   state. Make one authorized actual call; record MCP elicitation and Siemens UI separately.
5. Read exact AMC status and fresh inventory after the call. Correlate each entered lifecycle
   call to exactly one audit record and exact delivered text/SHA256; then verify restoration.

These are **redacted synthetic request examples**, not literal observed fixture paths:

```json
{"action":"list_portals"}
{"action":"bind","projectPath":"C:\\Disposable\\A\\Engineering\\A.amc21","portalProcessId":1234,"forceRebind":true}
{"projectPath":"C:\\Disposable\\A\\A.als21","forceRebind":true,"dryRun":true}
{"projectPath":"C:\\Disposable\\A\\A.als21","forceRebind":true,"dryRun":false}
{"projectPath":"C:\\Disposable\\A\\Engineering\\A.amc21"}
```

The first two are `bind_project`, the next two `open_project`, the last `get_project_status`.
No public confirm/token/acknowledge argument is added; force cannot override preservation guards.
Raw private requests and responses are retained in ignored evidence, with canonical text and
structured equality, `isError`, worker/Portal/generation, binding ID/revision, conditional context,
independent UI/modified state, human prompt counts, audit deltas/hash and restoration state.
The audit sink is the existing per-user JSONL directory; no log-path override is available.

## Verdicts and the October 8 scope ruling

PASS requires evidence for the stated criterion; an observed violation is FAIL. An unexecuted
or unavailable row is BLOCKED/unexecuted, never PASS. A partial row names only the subcases
that passed. Offline/reference/FakeWorker checks and older PR2/PR3 runs cannot fill missing
PR4 runtime rows. Unknown mutation, timeout or unresolved UI calls for inspection, not replay.

On October 8 the maintainer expressly excluded read-only/general lifecycle baseline retesting
and answered **“Accept scoped results; document limits”** after being told that offline opening
needed Siemens dialog dismissal and full-offline/headless/race setups remained unexecuted.
This permits documentation of accepted delivered behavior without declaring the original
aggregate L01–L19 gate PASS. Noninteractive FAIL and unexecuted rows remain in the report.
Restoration is operator-controlled and separately verified; no save/commit/generic session
close, automatic cleanup or terminal action is inferred from final open/clean UI status.
