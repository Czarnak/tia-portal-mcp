# network_write call timing, before the native-rejection refactor

Completed Task 0 baseline for the [Network write native-rejections plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-26-network-write-native-rejections.md). Task 7 must repeat the payloads, response budgets, setup, read and restoration rules below unchanged. No implementation ran during baseline collection.

**Status: Task 0 complete. Fifteen measured calls cover three N1–N5 rounds. Ten discarded calls cover the original warm-up and a fresh warm-up after the machine restart. Only runs 1–3 enter the medians. The disposable fixture was restored and verified.**

## Build under test

| Item | Value |
| --- | --- |
| Production source | Baseline `8090ded`; installed checkout `012d967de75b53a5747fc5bdd493a14d93c545e8` and resumed checkout `4f0aabd` have identical production/test source; their changes are documentation only |
| Installed tool version | `4.2.0-local.68.g012d967` |
| Installation | `scripts/install-local-tool.ps1 -StopRunningServer`, real Siemens V21 assemblies; package verification passed |
| Access mode | `full`; the audit records are checked individually |
| Response budgets | `--max-item-chars 1000000 --max-document-chars 1000000`, in both before and after series |
| Client | Direct JSON-RPC stdio client of the installed `tia-mcp` tool; MCP Inspector not used; tables use audit figures |
| Date | 2026-10-27, maintainer machine, TIA Portal V21; raw timestamps retain their recorded time zones |
| Project | `<disposable-copy>`, initially open and clean; restored by close without saving and reopen |
| Baseline stub build | 7 warnings, 0 errors, before timed collection; no build/test workload runs alongside the measured cases |

The checkout was installed without switching branches because its production source is identical to `8090ded`. This is source equivalence, not a claim that the installed informational version names `8090ded`. The connected MCP transport closed during installation; the direct client uses the installed public tool surface and its audit v2 records. Before resuming, the package version was rechecked and installed host/worker bytes matched the original local publication. Host SHA-256: `a35fa4c47f1812b17025a8874dcaf499e58dba5520c58e8a6ab59eeca5015a1e`; worker SHA-256: `77bdcc10ceccc6f1b86193a2abde1207b37bf70ff0c2474fd29589b6c9c00632`. The exact project file hash, clean status and complete hardware value were revalidated before mutation.

## Project size

Counts come from one complete, unpaged `network_read` / `read_hardware_config` result before changes.

| Metric | Value |
| --- | --- |
| Devices, including grouped devices | 95 |
| Root `project.Devices` count | 14 |
| Network nodes | 108 |
| Subnets | 4 |
| Canonical `read_hardware_config` document length | 964,930 characters |
| Project discovery evidence | `scope:"project", complete:true, failures:[]` |
| N3 selected subnet | `3547-1` (`PN/IE_2`), Ethernet, five connected nodes with complete selectable identities |

The default response budgets would omit this large hardware result. The raised budgets are part of the replay protocol. Another subnet has incomplete connection evidence; N3 deliberately uses the complete subnet above.

## Protocol

All calls are serialized. Cases run N1 → N2 → N3 → N4 → N5. The original discarded warm-up (`run0`) and measured rounds `run1`–`run2` ran before the maintainer requested an overnight stop. After the machine restarted, the installed baseline and fixture were revalidated, a second complete discarded warm-up (`restart-warmup`) ran, then only the missing measured round `run3` ran. Earlier measured captures were preserved. Before every case, close with `saveBeforeClose:false` and reopen the same disposable project. Before each actual timed write, perform one untimed complete `read_hardware_config`. Lifecycle calls, fixture setup and warm-up reads are excluded from the figures. The final captured case is followed by another explicit restoration and verification.

The restart warm-up had an additional client boundary after N4: the collector incorrectly expected a preview batch object, while the successful preview correctly returned `batch:null` and ten effects. The successful capture was retained; the collector restored the fixture and closed. After correction against every historical capture, a fresh client revalidated the fixture and continued only missing N5 warm-up and measured round 3. No successful timing case was repeated for this correction.

The restart separates measured round 3 from rounds 1–2. Rewarming restores the protocol's warm-up condition but does not establish identical operating-system or Portal cache conditions. These are three measured runs of the same baseline and payloads, collected across two machine sessions; they are not an uninterrupted-series result.

| Case | Measured call |
| --- | --- |
| N1 | One `configure_network_device`, setting `Address` |
| N2 | Ten items: create a subnet, add a device, six configurations of existing nodes, rename a fresh setup subnet, delete that setup subnet |
| N3 | One `delete_subnet`, on the selected subnet with five connected nodes |
| N4 | The N2 payload with `dryRun:true` |
| N5 | One `add_network_device` |

N2 requires a baseline-compatible adaptation: native subnet IDs are assigned on creation, but the baseline resolves every selector from the pre-write snapshot. An untimed setup call therefore creates `ZZ_N0_Target` and records its assigned ID. The measured create makes a separate `ZZ_N0_Created`; the connect, rename and delete target `ZZ_N0_Target`. The measured batch still has ten items. N4 uses the same setup and a fresh restoration so it does not inherit N3's deletion. Its measured preview performs no mutations; its extra lifecycle/setup calls are excluded. Task 7 must reproduce these adaptations.

One preliminary N2 warm-up request omitted `target.kind:"subnet"` and was rejected at argument validation before measured mutation. The disposable project was explicitly restored and its clean status verified. The corrected sequence retained the valid discarded N1 capture and resumed at N2. The rejected request is excluded from all figures; the measured rounds use the corrected payload throughout.

Metric source is audit v2: root `durationMs`, the sum of `items[].durationMs` (null counts as zero), and root minus item sum. **Item time includes dependent-item replanning in the baseline** (`WriteRun` starts the item timer before `RunItemAsync` calls `ReplanAsync`), so the remainder does not contain all planning cost. It contains initial planning, final verification and other call overhead. Dry-run item durations are null. Each captured audit record must match the exact returned canonical response text and be fresh relative to the response-file timestamp; stale records cannot enter the medians.

## Median table (3 measured runs, milliseconds)

| Case | Root duration | Item sum | Remainder |
| --- | --- | --- | --- |
| N1 | 113,088 | 968 | 112,120 |
| N2 | 627,273 | 516,709 | 114,207 |
| N3 | 115,959 | 6,945 | 109,932 |
| N4 | 57,230 | 0 | 57,230 |
| N5 | 113,739 | 4,383 | 109,356 |

## Per-run figures (root / item sum / remainder, ms)

| Case | Original warm-up (discarded) | Restart warm-up (discarded) | Run 1 | Run 2 | Run 3 |
| --- | --- | --- | --- | --- | --- |
| N1 | 190,060 / 1,904 / 188,156 | 150,942 / 3,228 / 147,714 | 97,876 / 674 / 97,202 | 113,088 / 968 / 112,120 | 163,836 / 1,108 / 162,728 |
| N2 | 645,312 / 536,932 / 108,380 | 1,028,974 / 855,136 / 173,838 | 622,903 / 516,709 / 106,194 | 627,273 / 513,066 / 114,207 | 960,471 / 786,536 / 173,935 |
| N3 | 111,953 / 8,657 / 103,296 | 162,677 / 8,510 / 154,167 | 114,492 / 6,945 / 107,547 | 115,959 / 6,027 / 109,932 | 168,076 / 8,193 / 159,883 |
| N4 | 92,176 / 0 / 92,176 | 84,819 / 0 / 84,819 | 57,230 / 0 / 57,230 | 56,552 / 0 / 56,552 | 80,492 / 0 / 80,492 |
| N5 | 104,963 / 4,091 / 100,872 | 166,929 / 6,626 / 160,303 | 113,739 / 4,383 / 109,356 | 109,522 / 4,053 / 105,469 | 167,572 / 5,541 / 162,031 |

Each column's median is calculated independently over measured runs 1–3. Both warm-up rounds are retained only for traceability and excluded from the medians.

## Replay payloads

The setup subnet ID is read from the successful untimed `create_subnet` result for each N2/N4 case. It is not guessed or fixed across runs.

### Untimed setup for N2 and N4

```json
{
  "operations": [
    {
      "operationId": "setup-subnet",
      "operation": "create_subnet",
      "subnet": {
        "name": "ZZ_N0_Target",
        "networkType": "Ethernet"
      }
    }
  ]
}
```

Use `batch.operations[0].result.subnetId` from its successful response as `<setup-subnet-id>` below.

### N1

```json
{
  "operations": [
    {
      "operationId": "n1-address",
      "operation": "configure_network_device",
      "target": {
        "deviceName": "S7-1500/ET200MP station_2",
        "nodeId": "E1",
        "interfacePath": [
          {
            "name": "PLC_2/SlashTest",
            "positionNumber": 1
          },
          {
            "name": "PROFINET interface_1",
            "positionNumber": 32768
          }
        ]
      },
      "changes": {
        "ipAddress": "192.168.202.10"
      }
    }
  ],
  "dryRun": false
}
```

### N2 and N4

N2 sends the following request. N4 sends the same operations with `dryRun:true`. The placeholder is replaced with that case's fresh setup ID.

```json
{
  "operations": [
    {
      "operationId": "n2-01",
      "operation": "create_subnet",
      "subnet": {
        "name": "ZZ_N0_Created",
        "networkType": "Ethernet"
      }
    },
    {
      "operationId": "n2-02",
      "operation": "add_network_device",
      "typeIdentifier": "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
      "deviceName": "ZZ_N0_CPU",
      "deviceItemName": "ZZ_N0_CPU"
    },
    {
      "operationId": "n2-03",
      "operation": "configure_network_device",
      "target": {
        "deviceName": "S7-1500/ET200MP station_2",
        "nodeId": "E1",
        "interfacePath": [
          {
            "name": "PLC_2/SlashTest",
            "positionNumber": 1
          },
          {
            "name": "PROFINET interface_1",
            "positionNumber": 32768
          }
        ]
      },
      "changes": {
        "ipAddress": "192.168.202.10"
      }
    },
    {
      "operationId": "n2-04",
      "operation": "configure_network_device",
      "target": {
        "deviceName": "S7-1500/ET200MP station_2",
        "nodeId": "E1",
        "interfacePath": [
          {
            "name": "PLC_2/SlashTest",
            "positionNumber": 1
          },
          {
            "name": "PROFINET interface_2",
            "positionNumber": 33024
          }
        ]
      },
      "changes": {
        "ipAddress": "192.168.203.10"
      }
    },
    {
      "operationId": "n2-05",
      "operation": "configure_network_device",
      "target": {
        "deviceName": "S7-1500/ET200MP station_2",
        "nodeId": "E1",
        "interfacePath": [
          {
            "name": "PLC_2/SlashTest",
            "positionNumber": 1
          },
          {
            "name": "PROFINET interface_1",
            "positionNumber": 32768
          }
        ]
      },
      "changes": {
        "subnetMask": "255.255.255.0"
      }
    },
    {
      "operationId": "n2-06",
      "operation": "configure_network_device",
      "target": {
        "deviceName": "S7-1500/ET200MP station_2",
        "nodeId": "E1",
        "interfacePath": [
          {
            "name": "PLC_2/SlashTest",
            "positionNumber": 1
          },
          {
            "name": "PROFINET interface_2",
            "positionNumber": 33024
          }
        ]
      },
      "changes": {
        "subnetMask": "255.255.255.0"
      }
    },
    {
      "operationId": "n2-07",
      "operation": "configure_network_device",
      "target": {
        "deviceName": "S7-1500/ET200MP station_2",
        "nodeId": "E1",
        "interfacePath": [
          {
            "name": "PLC_2/SlashTest",
            "positionNumber": 1
          },
          {
            "name": "PROFINET interface_1",
            "positionNumber": 32768
          }
        ]
      },
      "changes": {
        "subnet": {
          "subnetId": "<setup-subnet-id>"
        }
      }
    },
    {
      "operationId": "n2-08",
      "operation": "configure_network_device",
      "target": {
        "deviceName": "S7-1500/ET200MP station_2",
        "nodeId": "E1",
        "interfacePath": [
          {
            "name": "PLC_2/SlashTest",
            "positionNumber": 1
          },
          {
            "name": "PROFINET interface_2",
            "positionNumber": 33024
          }
        ]
      },
      "changes": {
        "pnDeviceNameAutoGeneration": false,
        "pnDeviceName": "zz-n0-cpu-x2"
      }
    },
    {
      "operationId": "n2-09",
      "operation": "update_subnet",
      "target": {
        "kind": "subnet",
        "subnetId": "<setup-subnet-id>"
      },
      "subnetChanges": {
        "name": "ZZ_N0_Renamed"
      }
    },
    {
      "operationId": "n2-10",
      "operation": "delete_subnet",
      "target": {
        "kind": "subnet",
        "subnetId": "<setup-subnet-id>"
      }
    }
  ],
  "dryRun": false
}
```

### N3

```json
{
  "operations": [
    {
      "operationId": "n3-delete",
      "operation": "delete_subnet",
      "target": {
        "kind": "subnet",
        "subnetId": "3547-1"
      }
    }
  ],
  "dryRun": false
}
```

### N5

```json
{
  "operations": [
    {
      "operationId": "n5-add",
      "operation": "add_network_device",
      "typeIdentifier": "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
      "deviceName": "ZZ_N0_CPU",
      "deviceItemName": "ZZ_N0_CPU"
    }
  ],
  "dryRun": false
}
```

## Restoration and evidence checks

- All 25 captured calls (ten discarded warm-ups and fifteen measured calls) passed audit-v2/full-mode, item-count/status, exact canonical-response/hash matching and timestamp-freshness checks. Every saved response succeeded. Freshness requires the saved response-file time to be within five seconds of the audit timestamp plus root duration.
- After N5 in measured round 3, the collector closed without saving and reopened the fixture. Final public status was `isOpen:true, isModified:false`.
- Fresh complete hardware values before the resumed warm-up and after final restoration exactly matched the original hardware value: 95 devices (14 at the project root), 108 nodes and four subnets, with the original addresses, names and relationships.
- An explicit final read of N2 item `n2-08`'s target confirmed `PnDeviceNameAutoGeneration:true` and the original generated name `plc_2/slashtest.profinet interface_2`. The original auto-generation flag was captured as `true` in that item's initial effect.
- The collector checked the `.ap21` file hash after each reopen. Its final SHA-256 was `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81`.
- The direct client was closed after fresh restoration assertions; an older restoration marker was not used as completion proof. Local evidence is retained under ignored `artifacts/network-native`: original captures, `N1`–`N5-restart-warmup`, `N1`–`N5-run3`, `resume-provenance.json`, `resume2-provenance.json`, and final `resume2-final-hardware.json`, `resume2-final-attributes.json`, `resume2-final-status.json` and `resume2-restoration.json`. The earlier `resume-final-*`/`resume-restoration.json` records prove the extra discarded-warm-up client boundary.
- Production and test source were unchanged from `8090ded` throughout timing. The prior baseline stub build had seven warnings and zero errors; no build/test workload ran alongside collection. This timing input does not establish candidate correctness or performance; those remain Tasks 6–7.

## Raw audit records (selected fields, verbatim values)

Fields match the earlier PLC timing input: call label, timestamp, tool, access mode, phase, root duration and item operation/status/duration. Project paths, bindings and full response text are not reproduced.

```jsonl
{"call":"N1-run0","timestamp":"2026-10-26T23:31:34.5265464+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":190060,"items":[{"operationId":"n1-address","operation":"configure_network_device","status":"succeeded","durationMs":1904}]}
{"call":"N2-run0","timestamp":"2026-10-26T23:41:45.0165977+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":645312,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"succeeded","durationMs":437},{"operationId":"n2-02","operation":"add_network_device","status":"succeeded","durationMs":58688},{"operationId":"n2-03","operation":"configure_network_device","status":"succeeded","durationMs":77761},{"operationId":"n2-04","operation":"configure_network_device","status":"succeeded","durationMs":66292},{"operationId":"n2-05","operation":"configure_network_device","status":"succeeded","durationMs":66023},{"operationId":"n2-06","operation":"configure_network_device","status":"succeeded","durationMs":52197},{"operationId":"n2-07","operation":"configure_network_device","status":"succeeded","durationMs":58352},{"operationId":"n2-08","operation":"configure_network_device","status":"succeeded","durationMs":53441},{"operationId":"n2-09","operation":"update_subnet","status":"succeeded","durationMs":51168},{"operationId":"n2-10","operation":"delete_subnet","status":"succeeded","durationMs":52573}]}
{"call":"N3-run0","timestamp":"2026-10-26T23:53:36.7724334+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":111953,"items":[{"operationId":"n3-delete","operation":"delete_subnet","status":"succeeded","durationMs":8657}]}
{"call":"N4-run0","timestamp":"2026-10-26T23:57:23.0035391+00:00","tool":"network_write","accessMode":"full","phase":"preview","durationMs":92176,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"skipped","durationMs":null},{"operationId":"n2-02","operation":"add_network_device","status":"skipped","durationMs":null},{"operationId":"n2-03","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-04","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-05","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-06","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-07","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-08","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-09","operation":"update_subnet","status":"skipped","durationMs":null},{"operationId":"n2-10","operation":"delete_subnet","status":"skipped","durationMs":null}]}
{"call":"N5-run0","timestamp":"2026-10-27T00:00:03.5989637+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":104963,"items":[{"operationId":"n5-add","operation":"add_network_device","status":"succeeded","durationMs":4091}]}
{"call":"N1-run1","timestamp":"2026-10-27T00:02:48.1863139+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":97876,"items":[{"operationId":"n1-address","operation":"configure_network_device","status":"succeeded","durationMs":674}]}
{"call":"N2-run1","timestamp":"2026-10-27T00:07:09.253093+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":622903,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"succeeded","durationMs":453},{"operationId":"n2-02","operation":"add_network_device","status":"succeeded","durationMs":56024},{"operationId":"n2-03","operation":"configure_network_device","status":"succeeded","durationMs":56303},{"operationId":"n2-04","operation":"configure_network_device","status":"succeeded","durationMs":53092},{"operationId":"n2-05","operation":"configure_network_device","status":"succeeded","durationMs":53397},{"operationId":"n2-06","operation":"configure_network_device","status":"succeeded","durationMs":63478},{"operationId":"n2-07","operation":"configure_network_device","status":"succeeded","durationMs":60748},{"operationId":"n2-08","operation":"configure_network_device","status":"succeeded","durationMs":59116},{"operationId":"n2-09","operation":"update_subnet","status":"succeeded","durationMs":60465},{"operationId":"n2-10","operation":"delete_subnet","status":"succeeded","durationMs":53633}]}
{"call":"N3-run1","timestamp":"2026-10-27T00:18:55.8207676+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":114492,"items":[{"operationId":"n3-delete","operation":"delete_subnet","status":"succeeded","durationMs":6945}]}
{"call":"N4-run1","timestamp":"2026-10-27T00:22:45.2164099+00:00","tool":"network_write","accessMode":"full","phase":"preview","durationMs":57230,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"skipped","durationMs":null},{"operationId":"n2-02","operation":"add_network_device","status":"skipped","durationMs":null},{"operationId":"n2-03","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-04","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-05","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-06","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-07","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-08","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-09","operation":"update_subnet","status":"skipped","durationMs":null},{"operationId":"n2-10","operation":"delete_subnet","status":"skipped","durationMs":null}]}
{"call":"N5-run1","timestamp":"2026-10-27T00:24:49.101426+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":113739,"items":[{"operationId":"n5-add","operation":"add_network_device","status":"succeeded","durationMs":4383}]}
{"call":"N1-run2","timestamp":"2026-10-27T00:27:46.8673198+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":113088,"items":[{"operationId":"n1-address","operation":"configure_network_device","status":"succeeded","durationMs":968}]}
{"call":"N2-run2","timestamp":"2026-10-27T00:32:36.1642391+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":627273,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"succeeded","durationMs":617},{"operationId":"n2-02","operation":"add_network_device","status":"succeeded","durationMs":60202},{"operationId":"n2-03","operation":"configure_network_device","status":"succeeded","durationMs":53111},{"operationId":"n2-04","operation":"configure_network_device","status":"succeeded","durationMs":60979},{"operationId":"n2-05","operation":"configure_network_device","status":"succeeded","durationMs":58595},{"operationId":"n2-06","operation":"configure_network_device","status":"succeeded","durationMs":54221},{"operationId":"n2-07","operation":"configure_network_device","status":"succeeded","durationMs":52951},{"operationId":"n2-08","operation":"configure_network_device","status":"succeeded","durationMs":55366},{"operationId":"n2-09","operation":"update_subnet","status":"succeeded","durationMs":54699},{"operationId":"n2-10","operation":"delete_subnet","status":"succeeded","durationMs":62325}]}
{"call":"N3-run2","timestamp":"2026-10-27T00:44:09.3746814+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":115959,"items":[{"operationId":"n3-delete","operation":"delete_subnet","status":"succeeded","durationMs":6027}]}
{"call":"N4-run2","timestamp":"2026-10-27T00:48:02.9969718+00:00","tool":"network_write","accessMode":"full","phase":"preview","durationMs":56552,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"skipped","durationMs":null},{"operationId":"n2-02","operation":"add_network_device","status":"skipped","durationMs":null},{"operationId":"n2-03","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-04","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-05","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-06","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-07","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-08","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-09","operation":"update_subnet","status":"skipped","durationMs":null},{"operationId":"n2-10","operation":"delete_subnet","status":"skipped","durationMs":null}]}
{"call":"N5-run2","timestamp":"2026-10-27T00:49:59.5509459+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":109522,"items":[{"operationId":"n5-add","operation":"add_network_device","status":"succeeded","durationMs":4053}]}
{"call":"N1-restart-warmup","timestamp":"2026-10-27T20:42:01.4197629+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":150942,"items":[{"operationId":"n1-address","operation":"configure_network_device","status":"succeeded","durationMs":3228}]}
{"call":"N2-restart-warmup","timestamp":"2026-10-27T20:48:47.8779667+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":1028974,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"succeeded","durationMs":571},{"operationId":"n2-02","operation":"add_network_device","status":"succeeded","durationMs":93891},{"operationId":"n2-03","operation":"configure_network_device","status":"succeeded","durationMs":82174},{"operationId":"n2-04","operation":"configure_network_device","status":"succeeded","durationMs":83291},{"operationId":"n2-05","operation":"configure_network_device","status":"succeeded","durationMs":106910},{"operationId":"n2-06","operation":"configure_network_device","status":"succeeded","durationMs":99359},{"operationId":"n2-07","operation":"configure_network_device","status":"succeeded","durationMs":93477},{"operationId":"n2-08","operation":"configure_network_device","status":"succeeded","durationMs":119606},{"operationId":"n2-09","operation":"update_subnet","status":"succeeded","durationMs":90277},{"operationId":"n2-10","operation":"delete_subnet","status":"succeeded","durationMs":85580}]}
{"call":"N3-restart-warmup","timestamp":"2026-10-27T21:07:31.0503681+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":162677,"items":[{"operationId":"n3-delete","operation":"delete_subnet","status":"succeeded","durationMs":8510}]}
{"call":"N4-restart-warmup","timestamp":"2026-10-27T21:13:06.8107305+00:00","tool":"network_write","accessMode":"full","phase":"preview","durationMs":84819,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"skipped","durationMs":null},{"operationId":"n2-02","operation":"add_network_device","status":"skipped","durationMs":null},{"operationId":"n2-03","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-04","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-05","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-06","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-07","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-08","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-09","operation":"update_subnet","status":"skipped","durationMs":null},{"operationId":"n2-10","operation":"delete_subnet","status":"skipped","durationMs":null}]}
{"call":"N5-restart-warmup","timestamp":"2026-10-27T21:19:26.3190659+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":166929,"items":[{"operationId":"n5-add","operation":"add_network_device","status":"succeeded","durationMs":6626}]}
{"call":"N1-run3","timestamp":"2026-10-27T21:23:48.3139875+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":163836,"items":[{"operationId":"n1-address","operation":"configure_network_device","status":"succeeded","durationMs":1108}]}
{"call":"N2-run3","timestamp":"2026-10-27T21:30:49.9265656+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":960471,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"succeeded","durationMs":456},{"operationId":"n2-02","operation":"add_network_device","status":"succeeded","durationMs":84276},{"operationId":"n2-03","operation":"configure_network_device","status":"succeeded","durationMs":100750},{"operationId":"n2-04","operation":"configure_network_device","status":"succeeded","durationMs":93860},{"operationId":"n2-05","operation":"configure_network_device","status":"succeeded","durationMs":83045},{"operationId":"n2-06","operation":"configure_network_device","status":"succeeded","durationMs":94456},{"operationId":"n2-07","operation":"configure_network_device","status":"succeeded","durationMs":79206},{"operationId":"n2-08","operation":"configure_network_device","status":"succeeded","durationMs":81028},{"operationId":"n2-09","operation":"update_subnet","status":"succeeded","durationMs":81820},{"operationId":"n2-10","operation":"delete_subnet","status":"succeeded","durationMs":87639}]}
{"call":"N3-run3","timestamp":"2026-10-27T21:48:22.3933434+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":168076,"items":[{"operationId":"n3-delete","operation":"delete_subnet","status":"succeeded","durationMs":8193}]}
{"call":"N4-run3","timestamp":"2026-10-27T21:53:59.7728426+00:00","tool":"network_write","accessMode":"full","phase":"preview","durationMs":80492,"items":[{"operationId":"n2-01","operation":"create_subnet","status":"skipped","durationMs":null},{"operationId":"n2-02","operation":"add_network_device","status":"skipped","durationMs":null},{"operationId":"n2-03","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-04","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-05","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-06","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-07","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-08","operation":"configure_network_device","status":"skipped","durationMs":null},{"operationId":"n2-09","operation":"update_subnet","status":"skipped","durationMs":null},{"operationId":"n2-10","operation":"delete_subnet","status":"skipped","durationMs":null}]}
{"call":"N5-run3","timestamp":"2026-10-27T21:56:51.067636+00:00","tool":"network_write","accessMode":"full","phase":"applied","durationMs":167572,"items":[{"operationId":"n5-add","operation":"add_network_device","status":"succeeded","durationMs":5541}]}
```
