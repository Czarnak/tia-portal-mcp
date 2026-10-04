# Network repair live verification — 2026-10-05

Result: **PARTIAL PASS; full live acceptance remains pending.** Qualified duplicate-E1 identity, sparse recovery and guarded execution work on this fixture. Public node listing and connected-subnet consequence capture still reject valid owners. Moving an already-connected node also fails.

Frozen code: 2c9a3f94ea85af82ad393b7924151329e930dd02. Tree: 16d277dfde7ce49bc2646b65ea637019269ab2e2. Historical failed evidence remains unchanged.

## Authorization, target and deployment

The human installed the Network branch, opened a disposable project and explicitly authorized any tools against it. Explicit binding verified Portal PID 15124 and this exact project:

C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\_mcp_test\SimpleProject_copy\SimpleProject_copy.ap21

Initial modified flag was false; final flag is true after reversible unsaved test writes. No save, close, compile, download, PLC action, implicit open or automatic rollback/replay was performed. Live calls were serial; no restore/build/test/pack ran during this phase.

Installed package: 3.0.1-local.249.g2c9a3f9. Nuspec repository commit matches the frozen head. Package SHA256: 89421DC92E6F3DB698B72F15F29D62B804463899AA85F8345FA4D118F1A4A035.

All 81 recorded source files match the offline source hashes. Installed host/Contracts and all 15 worker files exactly match the current Release build: 17 files, zero mismatches. The direct worker ran from the installed package. The user's version-stamped package differs from the recorded offline 1.0.0 package. This is same-source and installed/current-Release correspondence, **not byte identity with the earlier offline package**. See live-provenance.json.

Recorded offline qualification remains 5,201 passed, zero failed/skipped; 93.8895% overall line coverage; Debug/Release stub and actual V21 reference builds passed. Those suites were not repeated.

## Results

| Check | Observed result | Exact capture |
| --- | --- | --- |
| Ordinary full hardware | Honest omission: 90,772 characters exceeds the 60,000 item limit | 003 |
| Hardware pages | 5 pages preserve 7 devices and 3 subnets; no ordinary project discovery evidence | 004, 006, 008, 011, 013 |
| Qualified X1/X2 | Both E1 nodes have distinct returned paths: PROFINET interface_1 position 32768, interface_2 position 33024, under PLC_DP position 1 | 008 |
| Inspect round-trip | Both returned selectors inspect their own node | 010 |
| Bare E1 | Refused as ambiguous before mutation | 012 |
| Qualified previews | Both pass despite unrelated optional metadata diagnostics | 009 |
| X1 actual change | Only X1 becomes 192.168.12.241; X2 stays unchanged | 014, 015 |
| Sparse X2 partial outcome | Address applies as 192.168.13.242; unsupported PN-name setting is skipped; only Address is verified | 016, 017 |
| Ordered stop | First operation partially fails; later subnet creation is skipped and proved absent | 019, 020 |
| Both-E1 restoration | Both original addresses restored in one call; four final checks preserve distinct owners | 021, 022 |
| Subnet create | Positive actual creation B9DF-3; root device count unchanged at 5 | 023 |
| Subnet rename | Overlong request rejected by Siemens's 24-character limit; fresh inspection proves unchanged; distinct valid rename succeeds | 026, 027, 028 |
| Empty subnet delete | Preview and actual deletion pass; created subnet is absent | 033, 034, 035 |
| Connected deletion | Incomplete owner evidence causes unknown-consequence block; no mutation | 018, 032 |
| Existing-node subnet move | Preview accepts; actual reports already connected, no applied Subnet, second item skipped; original connections remain | 029, 030, 031 |
| Device addition | Catalog-backed preview passes; actual addition remains unverified because no public device deletion exists within the plan's no-save/no-close restoration scope | 036 |
| Read-only | 5 advertised tools, no network_write; forced call rejected by MCP dispatch | modes-evidence.json |
| Read-write | 15 tools, exact binding, same-address actual configure passes with form capability advertised and zero elicitation | modes-evidence-read-write.json |
| Full | Direct connector writes audit as full with confirmation none/not_requested | audit comparison |
| Canonical/audit | 39 direct text documents equal structuredContent, plus read-write probe; 18 direct entered Network calls plus one read-write probe each have one exact-text, correct-SHA256 audit; max direct document 118,450 chars | audit comparison |
| Complete fixture restoration | All 7 complete device records and all 3 complete original subnet records exactly match fresh reads; root count 5 | 037 |
| Final read after mode probes | Both E1 values and original subnets still match | 039 |

The 7 all-scope devices and 5 root devices are distinct counts. Paged aggregation is a restoration baseline only; it never authorizes writes or synthesizes complete project evidence.

## Integration findings

1. **Node listing and connected-node evidence lose owner identity.** All 11 listed nodes are unselectable with “Interface owner is not unique.” Existing subnet connection evidence is incomplete; actual connected deletion returns guard_blocked/network_state_unverifiable. The same fixture supplies valid hardware selectors that inspect and configure, establishing a live integration gap.

   Source hypothesis: NetworkObjectIndexReader.cs:248 demands ReferenceEquals between re-resolved owner and source item. HardwareConfigReader.cs:535 and NetworkConnectionEvidenceCapture.cs:28 also require reference identity for captured device/node objects. This is a plausible Siemens/remoting proxy-identity issue, not proven runtime root cause. A repair must preserve authority and ambiguity checks, including the unknown-consequence guard.

2. **Subnet reassignment is connect-only.** NetworkDeviceConfigurator invokes ConnectToSubnet without disconnecting an already-connected node. The outcome truthfully reports Subnet as skipped, with no applied value; fresh hardware proves unchanged connections. A design decision is needed for explicit disconnect/reconnect semantics or earlier refusal. No guessed detach or replay was attempted.

PN-name skipping and ordered failure reporting are expected behavior on this hardware. The overlong rename failure is retained as native validation evidence; it is not erased by the later separate valid request.

## Restoration, limitations and evidence recovery

All seven complete device records and all three complete original subnet records match final fresh reads exactly. The temporary subnet and must-skip subnet are absent. Original IPs, masks, PN names, subnet/IO relationships and qualified selectors are restored. The project remains open and unsaved, with modified=true after the test writes.

Actual device addition, positive connected deletion, successful movement of connected nodes, repeated optional-constraint permutations, oversized live recovery inputs, persistence, hardware execution and representative large-project performance remain unverified. Full acceptance and maintained final documentation remain pending integration findings. No product fix occurred during live verification.

Sandboxed standalone full/read-write probes could not discover the Portal; failed captures and logs are preserved. Scoped native execution of the same read-write probe succeeded. This is not classified as a product binding defect.

An initial hash analyzer collided with a PowerShell alias, failed and produced an empty analysis file. That failed artifact is retained as canonical-audit-comparison-failed-analyzer.json. The corrected fail-fast analyzer requires all 39 captures and the 19 audited calls; it passes with zero document/text/hash mismatches. The failed analysis is not pass evidence.

## Coordinator rulings

- Freeze code during live testing; bounded follow-up handles findings. Cost: full acceptance remains pending.
- Verify the version-stamped package against the same source and current Release files. Cost: no offline-to-live byte-identity claim.
- Use pages only as restoration evidence. Cost: extra filtered reads; write admission remains ordinary full-project preparation.
- Leave actual device addition unverified where no authorized public inverse exists within the plan. Cost: one positive write capability remains unqualified.
- Treat reference/proxy identity as a hypothesis. Cost: diagnosis still needs executable negative controls and fresh runtime proof.
- Inspect after failed writes before distinct corrections/restoration. Cost: more calls; no automatic replay.
- Preserve sandbox/analyzer failures. Cost: additional evidence files.
- Preserve unknown-consequence blocking. Cost: connected deletion stays unavailable until owner evidence is repaired.

Independent review completed: one P2 owner-evidence availability gap, no P1 unsafe mutation or false success in the reviewed writes. The reviewer independently confirmed exact restoration, sparse outcomes, ordered stopping, canonical/audit comparisons and provenance limits. Full live acceptance remains incomplete. Ignored captures, hashes, offline artifacts and the SDD ledger remain preserved.

Publication instruction: the human explicitly requested pushing the full branch and opening a PR regardless of the review outcome, with a fresh review on a fresh checkout. The known P2 gap and unverified cases travel with that handoff; publication does not imply acceptance or merge approval.


## Evidence storage and handoff

Raw numeric captures, mode probes, manifests and the independent review remain under the ignored primary-workspace directory TestResults/network-repair-live-20261005-2c9a3f9. Offline reports and the chronological ruling ledger remain under the Network worktree's .superpowers/sdd/2026-10-04-network-discovery-and-interface-node-identity-repair. Raw captures and build outputs are not committed.

The plan's broader maintained-documentation Task 9 is not claimed complete. The human's latest instruction is to publish this branch now regardless of the live review outcome and perform fresh review on a fresh checkout. This report and factual status/index updates accompany that publication; no additional code fix or new acceptance claim is introduced.
