# Issue #224 PR 3: block-logic patch live verification

2026-11-26. **Verdict: scoped PASS.** Source/XML preview and apply, hash refusals, exact attempted
input audit and fixture cleanup passed on the installed candidate and disposable V21 fixture below.
Measured compact argument size fell by 93.5160% for XML and 30.2326% for source in this fixture.
The project remains open, modified and unsaved.

## Candidate and target

- Source branch: `feat/issue-224-patch-block-logic`; installed candidate identifies commit
  `3409c2d0` through package version `4.4.0-local.21.g3409c2d0`.
- Installed host SHA-256:
  `A281D054857D46680FA92FCEC3EAFFF72FD604D5D4A6DE93E0F1B8F95F1B14B3`.
- Installed net48 worker SHA-256:
  `B660CD5A9560A80C758E42544EB7F987778C283178FF5556362B4B6354E97236`.
- Installed contracts SHA-256:
  `791625288459B0936459CB79070074CF65CF171733727247621A9925A7A0BF96`.
- TIA Portal V21, bound Portal PID `7224`, authorized disposable standalone project
  `<DisposableProjects>/MixOfEverything/MixOfEverything.ap21`. Initial public status reported
  `isModified:false`. Personal paths and access-bearing identifiers are excluded from this report.
- Observed Portal file product version: `2100.0.121.1`.
- Isolated fixture: SCL FB `PLC_1/Blocks/Codex_I224_PR3_20261126/PatchProbe`, created for this run.
  It is addressed through the installed full-mode MCP tool, PLC write contract `2.1`, audit v2.

## Authorization and method

The user authorized this live acceptance through MCP only, including removal of the isolated
fixture afterward. All engineering reads and mutations in this run use the product's registered
MCP tools. The operator approved Portal's native Openness access dialog. Native engineering calls
remain inside the product worker, and the evidence comes from registered tool responses and their
matching audits.

Scope is source/XML patch preview and apply, stale-hash refusal, diff and full resolved-input
audit evidence, fresh verification hash, measured request size and fixture cleanup. Compilation
scope is recorded from each import outcome and is reported independently of import success.
Saving, closing, downloading and PLC control are outside the authorized acceptance actions.

## Acceptance matrix

An expected refusal is a passing acceptance case only after its response and resulting state are
checked. All cases below were checked through saved MCP evidence.

| Case | Observed evidence | Status |
| --- | --- | --- |
| Source read and preview | Hash-bearing source read and one-added/one-removed-line patch diff; subsequent source read unchanged. Apply is assessed separately. | Passed |
| Source apply | `#ProbeValue` changed from `17` to `23`; typed committed success, temporary source removed, exact 189-character expected readback and fresh hash equal to verification. Reported PLC compile succeeded with zero errors/warnings. | Passed |
| Stale-hash refusal | Actual XML call used the stale original whole-export hash and an absent anchor; it failed `state_changed` and a fresh read was unchanged. | Passed |
| XML read and preview | LF multi-line anchor matched CRLF XML; bounded one-added/one-removed-line diff. Subsequent read was byte-for-byte and hash-identical. | Passed |
| XML apply | Typed committed import succeeded, block-scope compile reported zero errors/warnings, and fresh whole-export hash equalled verification. Companion and source text remained unchanged. | Passed |
| Whole-export hash scope | A preview using the current XML-part-only hash failed `state_changed`; the precondition requires the whole served export. | Passed |
| XML audit input | Original LF edits retained; exact XML-only normalized attempted request retained without edits. Preview had no resolved input. Canonical response text, stored hash and parsed document matched. | Passed |
| Source stale-hash refusal | Actual source call combined the stale original hash and an absent anchor; `state_changed`, MCP `isError:true`, and fresh read unchanged. | Passed |
| Source audit input | Exact 189-character normalized source retained. Independent checks matched all 24 receipts and 12 write audits, including response text, hashes and parsed structure. Preview, stale rejection and ordinary full update omitted resolved input. | Passed |
| Explicit block compile | `compile_check` returned Success, zero errors/warnings and no block compiled because all were up to date. | Passed |
| Request-size comparison | Compact UTF-8 argument objects: XML 459 bytes versus equivalent XML-only full replacement 7,079; source 360 versus 516. Method and limits below. | Passed |
| Cleanup | Preview listed only the owned FB and zero contained groups; actual group deletion succeeded. Fresh probe read returned `target_not_found`; root inventory matched the initial Main-only inventory and Main bytes/hash were unchanged. Project remained open and unsaved. | Passed |

## Verified setup and XML results

An ordinary source `update_block_logic` prepared the isolated SCL FB before the patch cases. It
returned a successful committed import with `temporarySourceState:removed`. Its reported compile
scope was PLC `PLC_1`, with overall success, zero errors and zero warnings. A fresh source read
matched verification hash `source:sha256:1f62cf199f9550ffa436fe542edb7c56833f467bc9a0ea4dc6ee80d3acac4a99`.
This setup result is distinct from the source-patch apply below.

The XML patch changed `MemoryReserve` from `100` to `96` using an LF multi-line anchor spanning
`MemoryLayout` and `MemoryReserve` in the CRLF export. The original whole-export hash was
`xml:sha256:8c867174d17f0b346ded7cc1804528d6564082e2c3f72fb45440724a3302067e`.
The preview did not change the block. Apply retained typed commitment evidence and a successful
block compile with zero errors and zero warnings. A fresh whole XML export matched verification
hash `xml:sha256:464fef57d1b8e97e0ff9de4a57efd6e5e9132acfaf1c8639df0f7c5e124283c7`.

The normalized attempted content was exactly the XML document: 6,324 characters, including
166 CRLF pairs and one lone LF at EOF. The `.s7dcl` companion was unchanged byte-for-byte, and a
source read was unchanged. An actual call using the stale original whole-export hash plus an
anchor absent from the document failed `state_changed`, establishing hash checking before anchor
resolution for that case; a fresh read confirmed no change. A separate preview with the current
XML-part hash also failed `state_changed`.

Sixteen assertions over the live XML receipts passed. Independent audit inspection
matched the original LF public edits and exact full XML-only normalized `update_block_logic`
request, with explicit `format:xml` and no `edits`, while the preview omitted `resolvedOperation`.
The retained canonical response text, its response hash and the parsed document were consistent.

## Verified source and audit results

The source patch changed `#ProbeValue` from `17` to `23`. Its preview carried a bounded
one-added/one-removed-line diff, and the following read retained the original source bytes and hash.
Apply returned typed committed success with `temporarySourceState:removed`; the exact expected
native readback was 189 characters. Its fresh hash
`source:sha256:acd29dac09ff7fe5e384a2744b77fcc22224c5099183bb4289c8a35e4f40a90d`
equalled verification. The import outcome reported a successful PLC-scope compile with zero errors
and zero warnings. A separate block `compile_check` returned Success with zero errors and warnings,
reporting that no block was compiled because all were up to date; this does not establish an
additional recompilation of the changed code.

An actual source patch using the old hash and an absent anchor failed `state_changed` with
MCP `isError:true`; a fresh read was unchanged. All 13 source assertions over the private receipts
passed. Independent audit checks matched all 24 receipts and 12 write audits, including exact
response text, hashes, parsed document structure and the full 189-character source candidate.
The source apply canonical response SHA-256 was
`6ee4adb51050fcdeca9b147649e3baece6c7b48a46a015e86c6deb0bac3ace9d`.
Previews, stale-hash rejections and the ordinary full-replacement setup update had no
`resolvedOperation` member.

Final independent checks confirmed text/document/structured-content equality for all 24 receipts,
and exact response text and response hashes for all 12 selected write audits. Records retained
audit v2, PLC write contract `2.1` and full mode, with confirmation `none` for previews/errors and
`policy` for actual calls. Verified binding ID/revision, worker session/generation, project and
Portal PID remained consistent across the run. No findings remained from this cross-check.

## Measured argument size

The comparison serializes the argument objects as compact UTF-8 JSON with literal Unicode
(`ensure_ascii=False`, comma/colon separators). For each full-replacement comparator it preserves
the patch request's metadata, including format presence, substitutes the exact audited resolved
content for `edits`, and changes the operation name to `update_block_logic`. The XML comparator
uses the XML-only document accepted by the existing importer.

| Measured request | Patch bytes | Equivalent full-replacement bytes | Bytes saved | Reduction |
| --- | ---: | ---: | ---: | ---: |
| XML | 459 | 7,079 | 6,620 | 93.5160% |
| Source | 360 | 516 | 156 | 30.2326% |

These are argument-object measurements for this fixture. They do not measure wire framing,
tokens, model effort, request latency or results for larger blocks, and do not remove the need for
a complete hash-bearing read.

## Cleanup and final state

The group-deletion preview listed exactly the owned `PatchProbe` FB, zero contained groups and
the informational `plc_deletes_group_contents` guard. Actual deletion of the isolated group
succeeded. A fresh XML read of the probe returned `target_not_found`. The root Program blocks
inventory again contained only the original `Main`, matching the initial inventory. Its served
XML-bundle bytes and hash were unchanged:
`xml:sha256:f94beec10c771b559e64f79abd7b12edf69a23658339e38343fde14d9f85ef8a`.

All eight cleanup assertions passed. Across XML, source and cleanup, all 37 functional assertions
over the saved MCP receipts passed (16 XML, 13 source and 8 cleanup). Final public status retained
the open `MixOfEverything` project with `isModified:true`; it was left unsaved. Removing the test
objects does not establish that the project returned to a clean state. No save, close, download or
PLC-control operation was called.

The cleanup preview canonical response SHA-256 was
`edb405373da65a7b5d18490ea441a6fd9dcd3c75aef2eaaca48b40d7061480af`;
the applied deletion response SHA-256 was
`23cdde4b252aaa2e67931a351ba9fac2ae9ad9e651936a17a1d4a7f126c15e7a`.

## Offline qualification

Before this live run, the implementation completed 8,954 non-packaging tests and both the
reference-stub and installed-V21-assembly solution builds. Those checks are offline evidence;
they do not establish live patch, compiler or cleanup outcomes.

## Evidence retention and limits

The private evidence contains 24 MCP receipt files, 12 selected write-audit rows and a run summary.
Raw MCP receipts and audit material remain outside the repository. This report retains only
verified summaries and relevant binary/response provenance; raw traces, sealed cursors and
personal profile paths are excluded from durable evidence.

The live verdict is limited to the installed candidate and disposable V21 fixture above.
It cannot establish other CPU families, Portal versions, local-session behavior, native fault
interleavings, concurrent editing, persistence, download or hardware acceptance. Request-size
measurements describe the actual serialized arguments measured in this run; they do not measure
model effort or prove a general latency improvement. Working copies and encoder changes are not
part of this candidate.
