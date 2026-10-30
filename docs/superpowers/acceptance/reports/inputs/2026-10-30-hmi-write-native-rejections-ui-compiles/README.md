# HMI UI compile evidence under accepted scope R3

The maintainer accepted "Reduce to those four (Recommended)". This bundle verifies two completed prior standalone compiles (P10, P12-discrete), the remaining three completed standalone cases (L1, L2, L4), and their baseline restorations. L5 is separate and is not required here. Six P-case GUI repeats are explicitly waived/reused; waived does not mean passed.

P12-analog's native write/read/Validate capture preceded a performed:false cancellation. No new GUI compile is claimed for that hold. The stopped prior queue has no fabricated completion record; its subsequent explicit manual restoration is checked against canonical lifecycle/status/full-inventory evidence. The fresh reduced queue must have its own complete.json and matching pinned before/after artifacts.

Canonical safe HMI responses remain unchanged. Project responses are metadata-minimal labelled projections. Trace labels and references are scoped by queue=prior or remaining. Compiler counts/text/language are maintainer observations only: null stays unknown and zero is not synthesized into compiler success. Language observations retain their source queue; no per-case language is inferred.

The manifest records all raw input hashes and compressed/decompressed output hashes. The accepted scope JSON is referenced by its exact source hash. Both source snapshots must remain unchanged through publication. Raw project identities are omitted. .gitattributes preserves output bytes.
