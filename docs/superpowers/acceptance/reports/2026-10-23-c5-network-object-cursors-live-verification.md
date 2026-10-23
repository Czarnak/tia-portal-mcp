# C-5 network-object cursors scoped live verification

Date: 2026-10-23. Verdict: **PASS for the executed installed-candidate, read-only scope**.
Binding changes, worker faults and the other cases listed below remain offline-only evidence.

## Candidate and authorization

- Qualified source: `db2947ba3aac2f07b33510a6d877dded3f990282` on `fix/c5-sealed-network-object-cursors`. The installed package was built at `5647477ac8e83ec1cf92dbab08981116b9a57048`; its delta from the qualified source contains documentation only.
- Installed version: `4.1.2-local.110.g5647477`, built with real V21 references. All 56 packaged runtime files matched the installed files by SHA-256. The running worker's executable path belonged to that installed version. The host command line selected `full` access mode.
- In response to the pending exact-target authorization request, the maintainer confirmed installation and identified an already-open disposable standalone AP21. Only that exact target was bound. Public records call it **Fixture A** and omit its name, path, selectors and raw cursors.
- The [approved C-5 plan](../../plans/2026-10-23-c5-sealed-network-object-cursors.md) limits this smoke to binding and read-only cursor checks. Every inventory request used `objectKinds:["deviceItem"]` and explicit `pageSize:1`, independent of C-9's planned defaults.

## Executed checks

| Check | Observed result |
| --- | --- |
| Initial binding and status | Binding changed from unbound to verified on Fixture A. Status reported the authorized path, `isOpen:true` and `isModified:false`. |
| First page and continuation | Both succeeded with one item each, the same total of 1,087 entries, distinct first/second items, no omission and a further continuation. The public cursor was 1,171 characters with purpose `network-object-page`, containing the host binding and complete worker identity around the retained 247-character internal continuation. |
| Tamper rejection and item isolation | Changing an interior character of the public token returned per-item `invalid_cursor` with null result. The original valid continuation immediately afterward succeeded in the same batch and returned the same second item. Counts were one failed, one succeeded, zero omitted/skipped; root `success:false`, `error:null` and MCP `isError:false` retained the typed batch contract. |
| Same-owner re-verification | Binding the same exact owner with `forceRebind:true` returned transition `unchanged`, verified binding and `isModified:false`. Decoded before/after public envelopes retained the exact host binding tuple, revision 1, worker session identity and process issuer. The original continuation remained usable and returned the same second item. This is preservation of the epoch, not creation of a different epoch. |
| Fresh sequence | A fresh first page and its continuation succeeded, matched the original first/second items and retained the same total. |
| Canonical responses and warnings | All nine retained response documents had text exactly equal to JSON serialization of their `structuredContent`. Seven Network operations produced six successes and the one intentional rejection, with zero omissions/skips. Initial binding included the generic Openness access-dialog advisory; subsequent calls had no warnings. The advisory does not establish whether a dialog appeared. |
| Final preservation | Final status reported the same path, `isOpen:true`, `isModified:false`, unchanged last-modified time and size. The AP21 SHA-256 was unchanged. Portal, host and worker retained their PIDs and creation times; no restart was observed. |

The smoke used two binding calls, two status calls and five Network read calls. No project write,
open/create/save/close, compile, online PLC operation, target switch, Portal termination or restart
was invoked. No restoration was required; the fixture remained open and unmodified.

The de-identified response receipts, package/file hashes, process observations and comparison
results are retained locally in ignored `TestResults/c5-live-20261023/verification-manifest.json`.
Real target identities and raw cursors are not stored in the report or manifest.

## Evidence limits

- No different binding epoch or target switch was induced. Same-owner re-verification deliberately retained its epoch.
- No worker restart, identity fault, Portal/transport loss, foreign-process cursor, issuance overflow, malformed worker payload or concurrent lease race was injected. Their regression evidence remains offline.
- Native snapshot/filter/range fault categories were not induced. This run continued unchanged filters and an unchanged snapshot; it does not add fault-injection coverage.
- Coverage is one standalone root in `full` mode, one object-kind filter and two-page sequences. Other access modes, local sessions, a full traversal and performance qualification were not exercised.
- C-9's size-100 defaults, rebased combined qualification and final paging matrix remain separate. C-5 must merge before C-9.

The [offline execution record](../../plans/2026-10-23-c5-sealed-network-object-cursors.md#offline-execution-record-2026-10-23)
retains the final 7,531 passing tests, zero failures/skips and 94.99% line coverage. That unchanged
suite was not repeated for this documentation-only follow-up. No push, PR creation or merge was
performed by this live gate.
