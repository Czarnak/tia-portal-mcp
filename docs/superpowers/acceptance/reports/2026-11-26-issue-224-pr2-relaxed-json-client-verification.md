# Issue #224 PR 2: partial relaxed-JSON client verification

Date: 2026-11-26. **Result: PARTIAL; actual-client acceptance remains open.**

Baseline and candidate both completed native XML and source reads through a private read-only
stdio client. Their exported content bytes and content hashes matched exactly; their complete
decoded canonical documents also matched. Original canonical text decreased by 26.0814% for XML
and 1.54052% for source. Raw MCP response-frame bytes decreased by 0.363636% and 0.0282805%
respectively, illustrating a different transport boundary from the canonical text.

The candidate also returned the exact same original text through the owner's actual Codex client.
That connection was in full mode, although only read operations were invoked. The baseline was
not observed in Codex, and no representative BMP case or model-request capture was obtained.
The escaping follow-up remains open; the successful native comparison does not complete the
required actual-client acceptance.

## Provenance and scope

| Item | Observed provenance |
| --- | --- |
| Candidate | Commit `e7a6b80a`; installed package `4.4.0-local.27.ge7a6b80a` |
| Candidate host DLL SHA-256 | `8a62407972df9c60693922d7a85ce6813d65a680632dc90a6271ec5ef6223584` |
| Baseline | Archive of `b3dbe11bed9cfe92facbc4f9ca180b23e73ccd8f`; Release build using installed TIA Portal V21 references |
| Baseline host DLL SHA-256 | `bbbe60e27ad29c15bbadf54de75bfe3c3b5d57c43871ea520a6ebbf0dd3a3349` |
| Runtime and dependencies | .NET runtime `10.0.15`; SDK `10.0.404`; MCP SDK `2.4.0`; System.Text.Json `11.0.0` |
| Actual client | Codex desktop package `26.1120.6357.0`, bundled engine `0.171.0-alpha.13.2` |
| Other CLI | PATH CLI `0.172.0` was not the actual client used for this observation |
| Actual-client access mode | Full; calls were limited to binding, status, project-tree browsing and PLC reads |
| Actual-client budget settings | No budget CLI or per-server environment overrides were configured; effective values were not independently observable here |
| Isolated probe arguments | `--access-mode read-only --max-item-chars 60000 --max-document-chars 180000` |
| Probe capture | Private standard-library stdio client; separate XML/source calls, 120-second response deadline and no automatic replay; both final native retries completed |

The actual-client configuration inspection did not inspect the process's inherited environment.
Successful non-omitted responses therefore do not establish the effective budget settings.

The fixture was an already-open disposable standalone project, initially reported as unmodified.
Both builds used the same existing block,
`KFIPC1/Blocks/Functions/Common/Real_To_ASCII`, with operation IDs `encoder-xml` and
`encoder-source`. The project path, raw MCP responses, process details, logs, captured PLC content
and build manifests remain private; none is retained in this report.

## Successful native comparison and actual-client observation

Each native XML export contained 15,488 characters; each source export contained 867.
Both were ASCII. XML exercised attributes and escaped punctuation; source exercised quoted text
and operators. Three additional source discoveries also contained only ASCII, so they do not
qualify the representative BMP requirement.

The baseline and candidate native retries both verified binding to the exact requested project
and Portal PID before reads. Each used the explicit read-only mode and 60,000/180,000-character
budget arguments above. Their successful read results had no omissions. Original text was
preserved separately from the structured object after one JSON-RPC decode; parsed text equalled
the structured object for every measured response.

Independent inspection of the stored native receipts confirmed identical request IDs and shapes,
exactly matching exported-content bytes and hashes, and deeply equal complete response documents.
The candidate native text also matched the original text captured from the actual Codex client
byte for byte. That actual-client observation remains candidate-only and full-mode configured.

| Format | Exported-content characters, each build | Matching native content hash, baseline and candidate |
| --- | ---: | --- |
| XML | 15,488 | `xml:sha256:dcc87d35f5da48a34d65f030a8ea6f7630ecd3d67daa5c94f70b7849d7009da6` |
| Source | 867 | `source:sha256:e97878909efd3292bf3ac596050002c42f6103890f0887423b2bbf95b3838297` |

## Original canonical-text measurements

These are the original text-block values from completed **native baseline and candidate reads**,
not reconstructed documents or reserialized MCP envelopes. Character and UTF-8 byte counts
coincide for these ASCII responses.

| Format | Native baseline original text: characters / UTF-8 bytes | Native candidate original text: characters / UTF-8 bytes | Reduction | Reduction percentage |
| --- | ---: | ---: | ---: | ---: |
| XML | 23,465 / 23,465 | 17,345 / 17,345 | 6,120 characters and bytes | 26.0814% |
| Source | 1,493 / 1,493 | 1,470 / 1,470 | 23 characters and bytes | 1.54052% |

The earlier pure serialization oracle remains corroborating evidence: the exact baseline
`CanonicalJson` implementation, applied to captured candidate JSON without loading Siemens
assemblies, produced text identical to the later native baseline text. The oracle alone preserved
content and hashes by construction; the successful native reads now independently establish their
equality for these two exports.

## Raw MCP response-frame measurements

The following counts come from the matching JSON-RPC response frames in captured raw stdout,
including each line terminator. They include the text block, independently serialized structured
object and MCP/JSON-RPC wrapper. They are not counts of files produced by reserializing a response.

| Format | Native baseline raw frame bytes | Native candidate raw frame bytes | Reduction in bytes | Reduction percentage |
| --- | ---: | ---: | ---: | ---: |
| XML | 49,500 | 49,320 | 180 | 0.363636% |
| Source | 3,536 | 3,535 | 1 | 0.0282805% |

Raw MCP transport frames are not model requests. Neither these counts nor the canonical-text
reductions establish model input size, tokens or latency, and neither predicts a fixed reduction
for other blocks.

## Bounded attempts, capture qualification and final state

The first isolated baseline bind attempt in the sandbox returned `target_not_found`, reporting
that no running TIA Portal matched the requested PID. A subsequent host-side baseline attachment
exceeded the 120-second probe deadline; its own probe processes were stopped. A separate candidate
probe also remained in binding and was stopped. After the user accepted prompts and authorized
one further bounded attempt per build, both native retries completed successfully. The causes of
the earlier attachment outcomes remain unconfirmed; the later prompted successes do not establish
retrospectively what blocked the earlier attempts.

Both server processes exited with code 0 after stdin was closed. The baseline capture harness's
final console print raised `UnicodeEncodeError` under CP1250 after all receipts and its successful
summary had been written and the server had exited. Baseline acquisition is verified from those
stored receipts, not from a successful harness process exit. Only the final console JSON rendering
was changed to ASCII-safe output before the candidate run; baseline reads were not replayed.

No project write, compile or lifecycle operation was invoked for this measurement.
Before and after both successful native runs, status reported the same project path, open and
unmodified. A separate final status read through Codex also succeeded and reported the same
project open and unmodified.

## Remaining acceptance

The actual model-request representation was unavailable through the bundled client's help and
available app tools. It remains unobserved whether the model receives the original text,
a serialization of structured content, or both. Model-facing representation counts, token cost
and model latency are **unmeasured**.

Full actual-client acceptance still requires the baseline/candidate comparison in the owner's
target client under the required read-only configuration and a representative BMP case. The
model-facing representation remains unobserved. The current evidence establishes the two-format
native read-only comparison, unchanged exports and hashes, candidate-only Codex delivery and the
separate text/frame measurements above. The escaping follow-up remains open.
