# HMI candidate timing evidence, 2026-10-30

Installed `4.2.0-local.113.g31d1a44`; comparison baseline `4.2.0-local.79.g75c0568`. The design-named baseline was not measured.

This folder contains 20 exact canonical write responses with matching redacted audit projections, 22 complete canonical HMI reads, and 66 explicitly derived lifecycle/status projections. Canonical response text and audit response hashes are unchanged. Original capture-file and audit-collector-line hashes retain provenance; collector-line hashes are not original server audit-file-line hashes. Source response mtimes support the original freshness checks; extracted archive mtimes are not measurement evidence.

`summary.json` contains independently calculated root/item/remainder medians over runs 1-3 and baseline comparisons. Run 0 and the startup read are discarded. `workloads.json` is byte-identical to the baseline. `client.py` differs only in its expected installed version and write-contract assertion; importing it never starts a server. The final read is followed by clean status, then an independent status check after client exit. Installed binary and saved-project hashes were checked before/after collection.

Run `python self_test.py` and `python validate_archive.py` here. Both are offline. The validator verifies canonical/audit pairing, schedule, complete reads, labelled projections, independent medians and the byte/decompressed manifest hashes. `.gitattributes` preserves exact bytes. The manifest excludes itself and includes hashes of every raw input used without exporting private identities.

This evidence qualifies timing only; functional matrix and UI compile acceptance remain separate. Replay uses the baseline README protocol and environment variables with the candidate version. Never infer fixture identity from redacted projections.
