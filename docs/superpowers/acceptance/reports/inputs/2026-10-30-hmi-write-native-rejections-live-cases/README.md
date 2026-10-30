# HMI candidate live-evidence bundle

This bundle accompanies the [case report](../2026-10-30-hmi-write-native-rejections-live-cases.md).
It preserves 35 installed-candidate cases: all 31 P variants plus L1–L4, captured with
`4.2.0-local.112.g96a28b7`. Identity is controller-supplied; the exporter does not inspect
the installed binaries. The source, binary hashes, fixture, settings and pending checks
are recorded in the case report.

## Files and representation

| File | Purpose |
| --- | --- |
| `trace.jsonl.gz` | 238 request/response captures with labels, timestamps, elapsed capture time and source-line hashes |
| `cases.jsonl.gz` | 35 per-case captures: write document, independent reads, public validation, restoration and a minimized native-checkpoint state projection |
| `summary.json` | Derived outcome/verification/validation inventory and explicit evidence limitations; not an independent runtime observation |
| `manifest.json` | Stored and decompressed hashes plus 39 original-input role/size/hash records |
| `.gitattributes` | `* -text` preserves evidence bytes across Windows checkouts |

The trace contains **155 original canonical text/structured response pairs** and **83
explicitly labelled privacy-redacted projections**. Original HMI responses retain their
canonical text, structured document, MCP `isError`, and canonical-text SHA-256. Do not
re-serialize a document and call it the original text. Project/lifecycle responses are
minimized projections with `<disposable-copy>` in place of private project identity;
their projection is not a byte-for-byte canonical original. The same distinction is
recorded by `responseRepresentation` and `writeRepresentation`.

Audit records are redacted projections. Their original response text/hash pair is retained
and checked where recorded by `auditCanonicalPairMatches`; the original audit-line bytes
are not exported. Native-checkpoint projections retain the relevant table/tag/alarm state,
not the complete raw native capture. The bundle contains no saved project.

The manifest's `rawInputs` bind roles (`hostProbe`, `trace`, `checkpointSummary`,
`checkpointTrace`, and each `case:<id>`) to the original input bytes before export.
`rawInputLineSha256` binds a trace entry to its original source line;
`rawInputSha256` binds a case record to its source file. These hashes identify local raw
sources without publishing their filenames or private paths. `canonicalResponseSha256`
hashes the UTF-8 canonical response text. The manifest's file hashes instead cover the
committed compressed/stored bytes, and `decompressedSha256` covers the complete uncompressed
JSONL bytes, including line endings. These hashes have different subjects and are not
interchangeable. The manifest has no self-hash.

## Frozen integrity values

| File / representation | Bytes | SHA-256 |
| --- | ---: | --- |
| `trace.jsonl.gz` | 420,909 | `e5859fbf4b10859b6e56f5624e886ae8235713b93c9966b02b45145137693c6d` |
| Decompressed trace | 8,219,577 | `8a8f8db3e44ec47799737965360c4389ae0ed69ae020bab2f7ef633c50cfdc8f` |
| `cases.jsonl.gz` | 62,171 | `52b2d0cda76bbef7984f1e1d4a2a9b8c3e40bbda987de59444780472dfc822c4` |
| Decompressed cases | 1,348,840 | `9c8524be6c9d4b76fe908c44ab0659caedb8447d34d865e5e4b77a7a9d624323` |
| `summary.json` | 124,004 | `891a563fa7d6ce08d6335ff19077521fd3f47e14d1f668e1666c57d913850944` |

## Offline validation recipe

Run this standard-library Python check from the repository root. It reads only the bundle,
verifies stored/decompressed bytes, checks case coverage and canonical text/document pairs,
and checks case-to-trace linkage. It does not access TIA Portal or repeat live acceptance.

```powershell
@'
import gzip, hashlib, json, pathlib, sys

root = pathlib.Path(sys.argv[1])
sha = lambda value: hashlib.sha256(value).hexdigest()
manifest = json.loads((root / 'manifest.json').read_bytes())
decoded = {}
for name, expected in manifest['files'].items():
    data = (root / name).read_bytes()
    assert len(data) == expected['bytes'] and sha(data) == expected['sha256'], name
    if name.endswith('.gz'):
        data = gzip.decompress(data)
        assert len(data) == expected['decompressedBytes'], name
        assert sha(data) == expected['decompressedSha256'], name
        decoded[name] = [json.loads(line) for line in data.splitlines()]

trace, cases = decoded['trace.jsonl.gz'], decoded['cases.jsonl.gz']
expected_p = '''P1 P2-cross-group P2-case P3 P4 P5-cross-table P5-case P6 P7
P8 P8-case P9 P10 P11-discrete P11-analog P12-discrete P12-analog P12-table
P13 P14 P15-update P15-create P16-create P16-update P17 P18 P19
P20-missing-tag P20-missing-member P21 P22'''.split()
assert len(trace) == 238 and len(cases) == 35
assert {c['case'] for c in cases} == set(expected_p + ['L1', 'L2', 'L3', 'L4'])
originals = [r for r in trace if r['responseRepresentation'] ==
             'original canonical text and structured document']
assert len(originals) == 155
for row in originals:
    response = row['response']
    text = response['content'][0]['text']
    assert json.loads(text) == response['structuredContent'], row['label']
    assert sha(text.encode('utf-8')) == row['canonicalResponseSha256'], row['label']
for case in cases:
    link = case['canonicalTrace']
    matches = [r for r in trace if r['label'] == link['label']]
    assert link['matchCount'] == 1 and len(matches) == 1, case['case']
    assert matches[0]['canonicalResponseSha256'] == link['canonicalResponseSha256']
    assert matches[0]['rawInputLineSha256'] == link['rawInputLineSha256']
summary = json.loads((root / 'summary.json').read_bytes())
assert summary['caseCount'] == 35 and summary['traceCaptureCount'] == 238
assert all(c['auditFound'] and c['restoration']['inventoryEqual']
           for c in summary['cases'])
print('Verified: stored/decompressed hashes, 35 cases, 238 captures, 155 canonical pairs.')
'@ | python - 'docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-live-cases'
```

For an original raw source still held locally, compare its bytes/size with the matching
`manifest.rawInputs` role before using it. To inspect a particular case without a live
rerun, decompress `cases.jsonl.gz`, select its `case` identifier, then follow
`canonicalTrace.label` into `trace.jsonl.gz`. Compare the original canonical string there;
use the source hashes to trace provenance, rather than rebuilding strings from summary data.
Live reproduction follows design §6.7 and Task 6 with the recorded installed candidate,
settings, baseline and close-without-saving/reopen protocol; this offline recipe does not
authorize a new live run.

## Open evidence

The generated summary deliberately does not assign final O classifications or infer absence
of O3 from success. The case report adds the independent state/diagnostic review. A separate
[validation supplement](../2026-10-30-hmi-write-native-rejections-validation-supplement.json)
records all 15 child-tag validations succeeding with zero findings on the unchanged restored
baseline after the matrix; it is outside this frozen manifest and is not a table validator.
Candidate UI compiles, direct UI-language observation, L5 and timing-after remain pending.
The later R1
source qualification and planned focused `.113` checks do not change the fact that every
captured matrix case used `.112`. Do not edit the frozen data or manifest to imply otherwise.
