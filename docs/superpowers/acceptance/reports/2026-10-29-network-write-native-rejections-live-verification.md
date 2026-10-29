# network_write native rejections and shared verification: acceptance record

Date: 2026-10-29. Verdict: **PASS for the executed installed-candidate scope on a disposable standalone project**, with the retained limits below. All 17 functional cases met their expected outcomes; the conditional three-run timing protocol and final Task 7 offline gate passed. This is not plant, online-PLC or general hardware-compatibility acceptance.

Branch: `refactor/network-write-native-rejections`. [Plan](../../plans/2026-10-26-network-write-native-rejections.md), [approved design](../../specs/2026-10-26-network-write-native-rejections-design.md), [current Network reference](../../../SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md).

## Candidates and provenance

| Stage | Installed package / source | Evidence |
| --- | --- | --- |
| Task 0 baseline | `4.2.0-local.68.g012d967`; production-equivalent to branch base `8090ded` | [Before timing input](inputs/2026-10-27-network-write-timing-before.md) |
| Initial functional observations | `4.2.0-local.76.gf1ae806` | [Frozen October 28 input](inputs/2026-10-28-network-write-native-rejections-live-cases.md); findings subsequently reviewed with the maintainer |
| L3 fix, full rerun and candidate timing | `4.2.0-local.79.g75c0568`; production source `75c0568d4d83b9430a13feb62aea52e2232c2bad` | [October 29 functional input](inputs/2026-10-29-network-write-native-rejections-live-rerun.md), [after timing input](inputs/2026-10-29-network-write-timing-after.md) |
| Final Task 7 fixtures/offline gate | `1acd599d96b55225e651c58cae4d18ae296d0db6` | FakeWorker/test changes only; production source at this gate equals the installed live-qualified candidate |
| Final-review host follow-up | Host correction after `e0302e1`; native worker and contracts unchanged from `75c0568` | Raw carried-outcome validation and corrected tool-description categories; offline evidence only, with no new installation/live run/timing |

The installed host and worker matched the local Release publish before the full rerun: host SHA256 `a8be3d6e4f2fc7fec4b280ccf9be8e21539ddae3b31f17afc19575c796dc67a9`, worker SHA256 `6eb1db5801f61e3eb83cdf80b217efaf3a12fdeb042dfd6146036ecf01234d59`. Later fixture, documentation and host follow-up changes do not represent an additional installation or live run. Live and timing observations belong to the installed `75c0568` candidate; the later host boundary changes are qualified offline separately below.

The exact already-open disposable project was explicitly bound. Portal UI language was English, confirmed by the maintainer. Functional and timing calls used `full` mode with both item and document limits raised to 1,000,000 characters. The fixture contained **95 devices, 14 root devices, 108 nodes and four subnets**; its complete hardware document was 964,930 characters. The private project path stays in local provenance.

## Resulting contract

`network_write` uses contract **2.0**; `network_read` remains **1.0**. Planning takes one complete hardware snapshot. Network mutation no longer replans after each item or performs general post-write read-back; successful items receive shared `trusted`/`superseded` verification. PLC and Network use `TrustedWriteVerifier<TItem>` through `IWriteVerificationPolicy<TItem>`, with their respective policies. HMI/VCI migration remains a separate follow-up.

Configure stops at the first native failure. A carried typed result preserves the settings applied before it; the failing setting and later settings are absent. The batch stops at that item and skips its suffix without batch rollback. Failed/skipped items have no verification entry; successful earlier items can still be verified, including conservatively marking an earlier write superseded by a later attempted write. `verification.success:true` can therefore accompany root `success:false`.

`EngineeringTargetInvocationException` during a mutation method maps to `engineering_rejected`; other Siemens `EngineeringException` types map to `worker_operation_failed`. Both can carry bounded `engineeringError`; reads never gain `engineering_rejected`, and `portal_lost` takes priority. Classification spans the mutation method, including its connection/identity checks, so the category alone does not prove a setter was reached. A wrapped configure failure can retain its applied subset even when classified as `portal_lost`; the direct fatal-after-partial-write case remains unverified.

L3 is the scoped exception to trusted completion: subnet update compares the requested name with the actual name the worker already read, before committing the transaction. An ignored rename fails `postcondition_failed`; no extra native getter, host collision check or worker RPC was added.

## Functional matrix and maintainer rulings

O1 means Openness threw. O2 means an accepted operation produced compile diagnostics. O3 means Openness accepted and the public software compile probe was silent. O3 is an observation, not proof that a value is invalid. Preflight and preview controls are labelled separately.

| Case | Outcome / class | Landed state and decision |
| --- | --- | --- |
| L1 duplicate device create | O1; `engineering_rejected` / `EngineeringTargetInvocationException` | Existing device unchanged, requested new CPU absent |
| L2 duplicate subnet create | O1; same native category/type | Subnet inventory unchanged |
| L3 colliding subnet rename | Original O3; fixed `postcondition_failed` | Name remains `PN/IE_2`; separate post-write status is unmodified before compile. Maintainer confirmed manual collision tooltip/cancel; scoped fix retained |
| L4 duplicate IP | O3 in fresh public software probe | Both addresses match after the write. Maintainer separately observed hardware compilation collision errors; no MCP collision rule added |
| L5 duplicate PROFINET name | O3 in fresh public software probe | Both names match after the write. Maintainer separately observed hardware compilation collision errors; no MCP collision rule added |
| L6 PN name with automatic naming enabled | O1; `worker_operation_failed` / `EngineeringNotSupportedException` | Empty applied subset, original settings preserved |
| L7 unsupported Ethernet `HighestAddress` | O1; same category/type | Name change rolled back with the rejected transaction |
| L8 malformed IP | O1; `engineering_rejected` / `EngineeringTargetInvocationException` | Empty applied subset, original address preserved |
| L9 incompatible subnet | O1; same native category/type | Empty applied subset, selected device unchanged |
| L10 interface without IO connector | Preflight control; `target_not_found` | Fails before Address; later create skipped, original address retained |
| L11 GSD mask update | O1; `worker_operation_failed` / `EngineeringNotSupportedException` | Address only retained; later create skipped. Maintainer explained this IO-device mask is inherited from its controller; proper rejection, no MCP fix |
| L11b literal `Invalid PN Name!` | Accepted control | Address, mask and literal name accepted; later create also succeeded. Maintainer confirmed the converted name and clean compilation. The original rejected-PN premise was invalid |
| L11c corrected partial failure | O1; `engineering_rejected` / `EngineeringTargetInvocationException` | Address and SubnetMask retained, incompatible PROFIBUS connection rejected, later create skipped; other selected properties preserved |
| L12 five-operation lifecycle path | Accepted; O2 deletion consequence | All five succeed; rename superseded by delete. Original connected subnet removed, new CPU and separate new subnet present; three missing hardware-ID-symbol software errors on the connected PLC are expected |
| L13 third item rejected | O1; native malformed-address rejection | First two creates retained/trusted, failing item typed, final two skipped |
| L13mini second item rejected | O1; native malformed-address rejection | First create retained/trusted, second fails with empty subset, third skipped |
| L14 exact L13 dry run | Preview control | Five effects, null batch/verification, complete hardware unchanged and project unmodified |

The fresh `compile_check` calls compile **PLC software**. L4/L5 hardware diagnostics are earlier maintainer observations, not newly repeated hardware compiler runs. L12's three undefined hardware-identifier symbols are the correct consequence of deleting the referenced subnet; the maintainer closed that finding without another product change. The selected original CPU and added CPU compiled cleanly in that case.

L11c supplies the genuine two-key partial-write fixture in place of the disproved PN-name rejection. Its exact native message is `Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'.\r\n\r\nAssignment to subnet failed.` The actual message contains CRLF pairs, not literal backslashes. The L13mini fixture separately preserves the captured `set_Address` message; it does not reuse the connection message.

The full functional rerun independently validated **118 canonical text/structured pairs and 19 unique fresh audit v2/full records**. All 17 case outcomes and their restoration checks passed. Exact canonical documents, landed checks, software diagnostics and hashes remain in the frozen functional input; raw wrappers and private provenance are retained locally under ignored `artifacts/network-native/task7-20261029-full1/`.

## Offline qualification and fixture reach

Task 6's earlier default suite passed 7,656 tests. The L3 fix was separately observed RED then GREEN, with its Update method at 25/25 covered lines and its full suite at 7,663 passed. Those dated gates are retained in the plan; neither substitutes for the final gate below.

The final fixtures add `LiveL11c_RejectedSubnetConnection_StopsItemWithAppliedSubset` and `LiveL13_SecondItemRejected_FirstAppliedSecondTypedThirdSkipped`. Both failed before the fake was updated: the old fixture lacked the incompatible subnet and accepted the malformed address. The new fake preserves the exact captured category/type/message/empty inner chain, while its device identities remain the test fixture's `PLC_Grouped/node-2`. These tests pin the failure projection and typed subset, not an identity-identical replay of the entire live document.

The three existing Audit/Budget/Ordering consumers were migrated to the same incompatible subnet trigger; their assertions were preserved. This removes the synthetic PN rejection instead of adding a compatibility branch. The focused suites passed **106 tests**. Independent review found no actionable issue in the exact five-file fixture/test diff.

Task 7 qualification, serialized with process-local `TEMP`/`TMP` under ignored `TestResults/task7-fixtures-20261029/temp`:

| Check | Result |
| --- | --- |
| Release solution build with reference stubs | Exit 0; zero errors, seven existing warnings; 29.54 s |
| Full default suite, `Category!=Packaging`, Release, coverage collected | **7,665 passed / zero failed / zero skipped**, exit 0; 8m13s |
| CI host/contracts line coverage | **12,530 / 13,174 = 95.11%**; 80% threshold passed |
| TRX, coverage copies, source and frozen-input checks | Counters parsed independently; coverage copies identical; production source unchanged from `75c0568`; frozen inputs unchanged |

Exact build/test commands are in the [Task 7 execution record](../../plans/2026-10-26-network-write-native-rejections.md#task-7-captured-fixtures-and-final-offline-gate--2026-10-29). Selected coverage attachment: `TestResults/task7-fixtures-20261029/coverage/1b6412e1-36e9-4a53-85a8-564c0d2de9f7/coverage.cobertura.xml`, SHA256 `83f933165665cc4aeea24440d94812a61fd3942d23682ba5181aad3593d6d7f3`. Coverage excludes the native worker under existing runsettings; the method-specific L3 evidence is separate. Packaging tests were not run because packaging inputs did not change. No expensive suite was repeated for documentation-only edits.

### Final-review host corrections (offline)

The whole-branch review found two P2 issues: carried configure outcomes were normalized by permissive envelope deserialization before strict decoding, and the advertised tool description incorrectly classified every native configuration rejection as `engineering_rejected`. The host now validates the raw optional outcome before envelope deserialization, including member names, duplicates and required members, following the existing VCI boundary pattern. Request-dependent checks remain in the Network projection. Invalid recovery evidence becomes `protocol_error` without payload echo, request replay or worker restart; absent and explicit-null conditional outcomes remain allowed.

All 12 malformed-wire regressions first failed with the old behavior. The fix passed **517 focused tests**, including 16 new raw-transport cases: 12 malformed shapes, absent/null controls and valid captured L11c/L13mini outcome controls. The description now states the exception-type distinction and `portal_lost` precedence. Only three host files and the transport test file changed; no native worker/contract or TIA mutation logic changed.

The final Release stub build passed with zero errors and seven existing warnings in 26.41 s. The renewed full Release default suite passed **7,681 tests, zero failures/skips**, exit 0, in 8m32s. CI host/contracts coverage is **12,547 / 13,191 = 95.11%**, with the 80% threshold passed. Both coverage copies have SHA256 `d9fa7e3ba8c68aceb62d936a7c24622d79f80f49989c7f1095e04a6194fbe1a2`; selected attachment is `TestResults/network-final-fix-20261029/coverage/4f56ea04-81bf-4e3c-95d7-4f1fb1c53524/coverage.cobertura.xml`. Root independently parsed the TRX counters and coverage copies.

Logs and RED/GREEN/full-suite TRX are under ignored `TestResults/network-final-fix-20261029/`. Scoped re-review resolved both P2 findings with no material residual issue. The final reviewer and root judged that further native qualification is not required for this host boundary/text correction: native worker and contracts are unchanged, malformed-wire behavior is tested directly, and captured valid outcomes pass. This follow-up is offline-qualified only; no installation, native rerun or timing of the changed host is claimed.

## Timing comparison

Audit v2 is the timing source. Each scenario has three measured runs; each root/item-sum/remainder median is computed independently. For an individual call, remainder is root minus item sum, with null preview item durations treated as zero. Subtracting column medians does not compute the remainder median.

Milliseconds, **root / item sum / remainder**:

| Scenario | Before | After | Root change |
| --- | --- | --- | ---: |
| N1 single IP-address change | 113088 / 968 / 112120 | 72668 / 681 / 71987 | −35.7% |
| N2 ten mixed writes | 627273 / 516709 / 114207 | 81174 / 11385 / 69790 | −87.1% |
| N3 connected subnet deletion | 115959 / 6945 / 109932 | 75940 / 1319 / 74267 | −34.5% |
| N4 preview of N2 | 57230 / 0 / 57230 | 70591 / 0 / 70591 | **+23.3%** |
| N5 device creation | 113739 / 4383 / 109356 | 74545 / 5638 / 68907 | −34.5% |

The largest observed root reduction is N2, 627.273 s → 81.174 s. **Preview is slower**: 57.230 s → 70.591 s. N5 item time also increases, 4,383 → 5,638 ms (+28.6%), despite a lower root. The data do not establish that every path or native operation became faster.

The exact Task 0 requests and adaptations were replayed: restore before every scenario including N4; fresh untimed target subnet for N2/N4, with only its native ID substituted; one hardware-read warm-up before actual timed writes, no additional warm-up before N4 preview; N1–N5 order. The uninterrupted candidate series contains five discarded warm-ups and 15 measured calls. Baseline collection spans two machine sessions and contains ten discarded initial/restart warm-ups plus the same 15 included calls. Both comparisons use three measured samples per scenario.

Candidate timing independently validated **20 sample requests, eight untimed setup writes, 116 canonical pairs and 28 exact fresh audit records**. No build, tests or agent analysis ran concurrently with measured writes. Item time includes work inside executors; baseline item time could include replanning and reads later removed. Remainder is not a pure planning measurement. Cache, Portal and machine-session state were not controlled between collection windows, so these are fixture observations, not universal performance or causal guarantees. Raw sample rows and exact delivery hashes are in the [after timing input](inputs/2026-10-29-network-write-timing-after.md).

## Restoration, adaptations and retained limits

At the end of each live series the project was closed without saving and reopened. Final complete hardware equals the original fixture: canonical SHA256 `9bf25234758521461a1da1db8774d209c2f04f215d1e90d6de43dd3ff8a9acb4` over 964,930 characters. All selected P/A/B/H Address/Mask/PN-name/automatic-name values and the timing-specific X2 PN values equal the original. Explicit and automatic final statuses report the exact project open with `isModified:false`. Both private clients exited 0, with zero matching clients after exit; saved AP21 SHA256 remains `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81`.

These are observations at the final live boundary. No TIA calls, fixture restoration, installation or timing were performed during the subsequent fixture/documentation closeout.

- L12 uses an initially existing connected subnet for connect/rename/delete, plus a separate measured create. A freshly assigned native subnet ID cannot be supplied to the initial planning pass before creation. The extra L13mini case supplies the specifically planned second-item failure control.
- Delete consequences use the initial snapshot. L12 lists five original nodes and omits the same-call new connection. Timing N2/N4 targets start unconnected, so their initial guard has zero nodes; baseline replanning could report one. The informational guard is not a refreshed intermediate-state inventory.
- Trusted completion does not prove every requested value landed. Only the scoped L3 ignored-rename case was fixed; mixed-property L3 rollback and broader silent-drop/native-fault locations are not live-qualified.
- The explicit wrapped engineering/disposal outcome path has offline coverage. Direct fatal failure after partial mutation, broader fault/mode/container combinations and arbitrary hardware models remain unverified here.
- No PLC download/run/stop, online execution, save or plant acceptance occurred. No remote write was made during closeout.

Task 6's production delta against `8090ded` was **423 added / 1,387 removed, net −964**. The seven-line L3 guard makes the live-candidate delta **430 added / 1,387 removed, net −957**. The final host boundary adds 25 lines, giving **455 added / 1,387 removed, net −932**; the corrected description replaces a line already changed against the baseline. FakeWorker, tests and documentation are excluded. Remaining HMI/VCI verifier migration and narrower error-flattening follow-ups stay in the [improvement log](../../../IMPROVEMENT_LOG.md).
