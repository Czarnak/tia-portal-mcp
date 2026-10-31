# VCI Stage 0 evidence archive

This folder contains privacy-safe evidence for the baseline and temporary native probes. Private original captures remain saved locally and are not published. Actual project names and paths are omitted.

| File | Purpose |
| --- | --- |
| `evidence.jsonl.gz` | Anonymized captured documents and audit evidence. Original canonical text/structured-document equality, audit correlation and original response hashes were validated before anonymization. |
| `summary.json` | Derived case inventory and observations, including `matrixPresenceComplete`. |
| `timing-qualification.json` | Timing eligibility, restoration/protocol checks and limitations; read this before using any timing summary. |
| `import-proof.json` | Before/after hashes and value comparisons retained from the private import captures. |
| `restoration.json` | Final open/unmodified state, all 216 mappings Equal, and preservation of the base and five pre-existing file inventories. |
| `build-provenance.json` | Exact commits, versions, package/host/worker hashes, package checks and recorded test results. |
| `case-inputs.json` | Generic case inputs and selectors, without private project identity. |
| `probe.patch` | Temporary exception-note instrumentation and disabled workspace-conflict checks; not a production refactor. |
| `summarize.py`, `selfcheck_summary.py` | Generic summary logic and its offline self-check. |
| `validate_timing_protocol.py`, `selfcheck_protocol.py` | Generic timing-protocol validation and its offline self-check. |
| `build_import_proof.py`, `selfcheck_import_proof.py` | Hash/value-only import proof generator and its offline self-check. The generator takes private capture directories. |
| `checksums.json` | SHA-256 and byte lengths of the other files in this folder. |

## Interpreting the evidence

`matrixPresenceComplete` means the required case identifiers have recorded evidence. It does **not** mean every negative input was successfully constructed, a native rejection was induced, each case qualified, or a timing run met the measurement protocol. Read the individual outcomes and qualification records, including unavailable inputs and unmet preconditions.

Retained `responseHash` values identify the **original private canonical response text**. They are not hashes of the anonymized text or compressed archive. Anonymization changes text, so recomputing an original response hash from the published document is not a valid integrity check. Original correlation was checked before that transformation.

The scoped `.gitattributes` pins text artifacts to LF and keeps the compressed archive binary, so the published checksums survive checkout on other platforms. Actor names are anonymized throughout lifecycle documents and embedded canonical/audit text; ordinary state timestamps remain.

Full PLC source exports and project-status metadata/history are intentionally excluded. `import-proof.json` retains 21 before/after hash and value comparisons and 21 compile reports; it does not make the omitted source exports reconstructible. `restoration.json` retains the necessary final state/file checks. Native behavior, compilation results and recovery limitations must be assessed from the [accompanying report](../2026-10-31-vci-write-stage0.md) and case evidence.

The archive contains 2,162 artifacts plus its verification header, including 217 case-filtered original audit pairs. All 21 timing samples pass the separate protocol validator. All 16 primary probe variants are recorded, including accepted and locally blocked cases; this is not a claim that 16 native rejections occurred. The original project-status documents remain private.

## Offline checks

Run from this folder; these checks do not attach to TIA Portal:

```text
python selfcheck_summary.py
python selfcheck_protocol.py
python selfcheck_import_proof.py
python -c "import gzip,json; from collections import deque; deque((json.loads(s) for s in gzip.open('evidence.jsonl.gz','rt',encoding='utf-8')),maxlen=0); print('evidence JSONL readable')"
python -c "import json,pathlib; [json.loads(pathlib.Path(p).read_text(encoding='utf-8')) for p in ('summary.json','timing-qualification.json','import-proof.json','case-inputs.json','restoration.json','build-provenance.json')]; print('metadata JSON readable')"
python -c "import json,pathlib,hashlib; rows=json.loads(pathlib.Path('checksums.json').read_text(encoding='utf-8')); assert all(hashlib.sha256(pathlib.Path(x['file']).read_bytes()).hexdigest()==x['sha256'] and pathlib.Path(x['file']).stat().st_size==x['bytes'] for x in rows); print('published file checksums match')"
```

Successful self-checks and readable JSON confirm helper behavior and archive framing. They do not establish native acceptance, negative qualification or timing eligibility. No dedicated public validator reconstructs the omitted private original-response correlation.

## Immutable build provenance

| Build | Commit | Package version |
| --- | --- | --- |
| Baseline | `8713a476ec509360b7cc18083e76d21275fc1c92` | `4.2.0-stage0.baseline2.g8713a47` |
| Temporary probe | `7275a3fe745d3b8426947dabe8ef3d3ef8f0b3b7` | `4.2.0-stage0.probe.g7275a3fe` |

NuGet package SHA-256:

- Baseline: `55c28a8e102ec87143cee54223b58c59088cc6debc87918b9bb84142b5a76089`
- Temporary probe: `3659b4d0d261a16b4afb1cd888b00b085a28a4750fab644324f24f447187bbfa`

Installed host/worker payload hashes matched their packages; neither package contains Siemens.Engineering DLLs. [Build provenance](build-provenance.json) contains those hashes and build/test details. Package hashes identify build packages, not anonymized evidence files. The probe deliberately disables workspace-conflict protection and must not be treated as a release build; it was uninstalled after the run.
