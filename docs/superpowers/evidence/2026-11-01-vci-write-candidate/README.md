# VCI write candidate execution record

Candidate commit: `4057b44a062d6199ecf6861e174d730b1d948c0e`, installed package `4.2.0-local.160.g4057b44a`.

The unchanged Stage 0 capture client and 13 original timing macro files produced 21 qualified measured samples, seven qualified warm-ups, one excluded startup call and 103 exact audit pairs. Original Stage 0 validators required no adaptation. Root, item-sum and remainder medians were recomputed independently.

All 20 final contract/lifecycle variants passed, represented by the 16 C1–C10/L1–L6 report rows. Two malformed L-case setup attempts were preserved separately and corrected without changing the candidate server: unsupported source format for a LAD FC; `DB` instead of the observed `GlobalDB` selector. The latter's temporary workspace was explicitly discarded and removed before retry. Analyzer regressions cover valid empty later pages, native create metadata and the shared partial-write info guard.

Final preservation: six workspace inventories unchanged; 216 unchanged base mappings all `Equal`; 1,249 file names/sizes/hashes unchanged; project open/unmodified and saved hash matching the protected Stage 0 revision. Client captures, audit traces, PLC source reads and restoration dumps remain in ignored private scratch and are not published here.

The durable report is [VCI write native rejections live verification](../../acceptance/reports/2026-11-01-vci-write-native-rejections-live-verification.md). No raw Openness spike evidence was added in this continuation.
