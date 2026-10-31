# Network write native rejections: full live rerun

Date: 2026-10-29. **All 17 functional cases and the final restoration gate passed.**

This input records the authorized rerun after serial maintainer review. All 17 case assertions, including L11b, corrected L11c and L13mini, passed. Per-case restoration, final whole-fixture restoration and functional-client shutdown were verified independently. This releases the maintainer's conditional timing gate; timing results are recorded separately and Task 7 is not yet complete.

The [original October 28 input](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-28-network-write-native-rejections-live-cases.md) remains unchanged. This rerun follows the approved [plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-26-network-write-native-rejections.md) and its final issue rulings.

## Provenance and baseline

| Item | Verified value |
| --- | --- |
| Production source | `75c0568d4d83b9430a13feb62aea52e2232c2bad`; later checkpoint `7a14aa9` changed documentation only |
| Installed package | `4.2.0-local.79.g75c0568` |
| MCP assembly version | `4.2.0.0`, distinct from the package version |
| Portal UI language | English, maintainer-confirmed |
| Mode and budgets | `full`; item/document limits both 1,000,000 characters |
| Saved disposable AP21 SHA256 | `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81` |
| Host SHA256 | `a8be3d6e4f2fc7fec4b280ccf9be8e21539ddae3b31f17afc19575c796dc67a9` |
| Worker SHA256 | `6eb1db5801f61e3eb83cdf80b217efaf3a12fdeb042dfd6146036ecf01234d59` |
| Preflight inventory | 95 devices; 14 root devices; 108 nodes; 4 subnets |
| Preflight hardware canonical SHA256 | `9bf25234758521461a1da1db8774d209c2f04f215d1e90d6de43dd3ff8a9acb4` |

Installed host and worker hashes equal the local Release publish. The exact authorized disposable project was explicitly bound. Preflight was open/unmodified; the root verified the full inventory against the saved original fixture and the selected node attributes. The full filesystem path remains in local provenance.

Cases are serialized safe units: actual write or preview, landed read, recorded public compile observations, then close without saving and reopen. Every finalized case below has successful close/open captures and fresh open/unmodified restoration status. The client checks the saved AP21 hash during restoration. No future case is buffered with the current case.

## Interpreting a passing case

A deliberate native rejection is a passing negative test when its category, native type, carried applied subset, ordering and landed state match expectations. Attempted failures retain `success:false`, `error:null` and MCP `isError:false`; failed items have no trusted verifier entry. Public `compile_check` compiles PLC software; a successful software compile does not establish hardware configuration validity.

The maintainer previously inspected duplicate IP and PROFINET-name writes in TIA and confirmed that hardware compilation reports those collisions. The maintainer also supplied a converted-name screenshot and clean compilation for `Invalid PN Name!`. These are prior maintainer observations, not newly captured hardware compiler diagnostics from this rerun. Fresh L12 software diagnostics after subnet deletion are expected consequences of PLC references to removed hardware-ID symbols.

## Functional outcomes and audit hashes

Each hash below covers exact UTF-8 canonical response text, not its Markdown rendering. Included writes have one matching fresh audit v2/full record, exact response text/hash and clean restoration.

| Case | Observation | Items: succeeded / failed / skipped | Verification | Canonical SHA256 | Gate |
| --- | --- | --- | --- | --- | --- |
| L1 | engineering_rejected / EngineeringTargetInvocationException | 0/1/0 | none | `b4025048111f277ba5aa7a466b6615ee4ff79faed00c073ffe3c316cda47292a` | expected outcome; restored |
| L2 | engineering_rejected / EngineeringTargetInvocationException | 0/1/0 | none | `e74f6400a48c18fb5e528dbcce887e29080ed4fea81017a87507b012f0e8bd43` | expected outcome; restored |
| L3 | postcondition_failed / no native exception | 0/1/0 | none | `c67da5304afc49e899be7aaeed08c0565b5f1de5475046aef47d6e64c1c4204d` | expected outcome; restored |
| L4 | accepted | 1/0/0 | trusted | `e32685cd2d9105e063dd1159b9e095db98350488bd5470826dba4f70fba531c6` | expected outcome; restored |
| L5 | accepted | 1/0/0 | trusted | `6d72eed5be7ee53d32b632ff83184bbb410bf94a3eb99a0fac2bf67a8d6e5d68` | expected outcome; restored |
| L6 | worker_operation_failed / EngineeringNotSupportedException | 0/1/0 | none | `d972ee22d0b10b0508c133cb4080a95bf3d4a8e096d9da397a121d8e2ee98c4f` | expected outcome; restored |
| L7 | worker_operation_failed / EngineeringNotSupportedException | 0/1/0 | none | `4fce4625180dca4b349a2e69591e314937eec996e0dd95cdbbcb7d71ebf35055` | expected outcome; restored |
| L8 | engineering_rejected / EngineeringTargetInvocationException | 0/1/0 | none | `14935903667c17fbd25d26557ae8bde6bf3a96308aaea7a8738f9af1d95d31b1` | expected outcome; restored |
| L9 | engineering_rejected / EngineeringTargetInvocationException | 0/1/0 | none | `4f1d31bd111d84e368351f7e6bc2341878856019a6a8380ae0336afcb81d2dd2` | expected outcome; restored |
| L10 | target_not_found / no native exception | 0/1/1 | none | `f01fcf7c4fa068819005c5f1f0050f2dffbb692e7b5b113e4e3f1b97c526730b` | expected outcome; restored |
| L11 | worker_operation_failed / EngineeringNotSupportedException | 0/1/1 | none | `01fca199d5e21fbe3efa7f95e4c950e99c2d86f009e4f78ff6f80d5d349954c1` | expected outcome; restored |
| L11b | accepted | 2/0/0 | trusted, trusted | `3b32dc7274e1bd8007c8ae30e5b196775c943d36d63d1d39be2b9d819adbdbef` | expected outcome; restored |
| L11c | engineering_rejected / EngineeringTargetInvocationException | 0/1/1 | none | `dc5e3a127f214fbe2664ddbb7da18036b7856d2eb2a5ea9041cd77ead9780c65` | expected outcome; restored |
| L12 | accepted | 5/0/0 | trusted, trusted, trusted, superseded, trusted | `cbf701126f2d2268dabc924caed113568e501f95ca6cdbadfed386c787faefd2` | expected outcome; restored |
| L13 | engineering_rejected / EngineeringTargetInvocationException | 2/1/2 | trusted, trusted | `8a7228ab57c44f3dffb5f51a3a5a15c123f18abc2006ca439194010c58acb158` | expected outcome; restored |
| L13mini | engineering_rejected / EngineeringTargetInvocationException | 1/1/1 | trusted | `1867f22ea91511bd955ec07273c550c83630ace6e57a86218394c81dd55019ae` | expected outcome; restored |
| L14 | preview | preview: 5 effects | none | `5f062ff5df3e234fd208a0f9dcc5fd5460a2a2a47780a34759dc15ee81256ac6` | expected outcome; restored |

## Captured case documents

Write JSON blocks below are exact canonical documents. Read/compile projections are explicitly partial views; complete canonical documents remain in local captures. No projection is represented as a complete response.

### L1: Duplicate device create

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027CreateWithItem\u0027 of type \u0027Siemens.Engineering.HW.DeviceComposition\u0027.\r\n\r\nThe name could not be set at device.\r\n\r\nValue of attribute Name not unique!","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027CreateWithItem\u0027 of type \u0027Siemens.Engineering.HW.DeviceComposition\u0027.\r\n\r\nThe name could not be set at device.\r\n\r\nValue of attribute Name not unique!"},"omission":null,"operation":"add_network_device","operationId":"l1-add","result":null,"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"add_network_device","requestedSettings":{"deviceItemName":"ZZ_L1_CPU","deviceName":"S7-1500/ET200MP station_2","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0"},"target":{"deviceItemPath":[],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0","ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"add_network_device","operationId":"l1-add","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l1-add"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "existing-device",
    "status": "succeeded",
    "deviceNames": [
      "S7-1500/ET200MP station_2"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      }
    ],
    "nodes": [
      {
        "name": "X3",
        "nodeId": "P1",
        "ipAddress": "2",
        "subnetMask": null,
        "pnDeviceName": null,
        "subnetName": "PROFIBUS_2",
        "ioSystemName": "DP-Mastersystem"
      },
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.120",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_1",
        "subnetName": null,
        "ioSystemName": null
      },
      {
        "name": "X2",
        "nodeId": "E1",
        "ipAddress": "192.168.1.1",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_2",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  }
]
```

Software compile projection from `L1-compile-existing.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Software compile projection from `L1-compile-requested-item.json` (not the full canonical response):

```json
{
  "success": false,
  "resultStatus": "failed",
  "failure": {
    "category": "target_not_found",
    "message": "No PLC software named 'ZZ_L1_CPU' was found in the project."
  },
  "totalErrorCount": null,
  "totalWarningCount": null
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L1-write.json`. Audit root duration 77910 ms is functional metadata, not a timing measurement.

### L2: Duplicate subnet create

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027Create\u0027 of type \u0027Siemens.Engineering.HW.SubnetComposition\u0027.\r\n\r\nCould not set the name.","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027Create\u0027 of type \u0027Siemens.Engineering.HW.SubnetComposition\u0027.\r\n\r\nCould not set the name."},"omission":null,"operation":"create_subnet","operationId":"l2-create","result":null,"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"PN/IE_2","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l2-create","subnetId":null,"subnetName":"PN/IE_2"}},"omission":null,"operationId":"l2-create"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "subnets",
    "status": "succeeded",
    "deviceNames": [
      "GSD device_2"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      }
    ],
    "nodes": [
      {
        "name": "IE1",
        "nodeId": "IE1",
        "ipAddress": "192.168.111.44",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "wen_m201",
        "subnetName": "PN/IE_2",
        "ioSystemName": "PROFINET IO-System"
      }
    ]
  }
]
```

Software compile projection from `L2-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L2-write.json`. Audit root duration 79562 ms is functional metadata, not a timing measurement.

### L3: Colliding subnet rename

The colliding rename now returns `postcondition_failed` before commit. The landed name remains unchanged; a separate status read before compilation is open/unmodified. The guard uses the name already read by the worker.

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"postcondition_failed","message":"update_subnet: Portal did not rename subnet \u00273547-1\u0027 to \u0027PN/IE_1\u0027; the subnet is still named \u0027PN/IE_2\u0027. Another subnet may already hold that name. No transaction was committed."},"omission":null,"operation":"update_subnet","operationId":"l3-rename","result":null,"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Name":"PN/IE_2"},"operation":"update_subnet","requestedSettings":{"Name":"PN/IE_1"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"update_subnet","operationId":"l3-rename","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l3-rename"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "subnet",
    "status": "succeeded",
    "attributes": {
      "Name": "PN/IE_2"
    }
  }
]
```

Software compile projection from `L3-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L3-write.json`. Audit root duration 95816 ms is functional metadata, not a timing measurement.

### L4: Duplicate IP address

Exact canonical write response:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"configure_network_device","operationId":"l4-ip","result":{"appliedSettings":{"Address":"192.168.111.45"},"deviceName":"GSD device_2","messages":[]},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.111.44"},"operation":"configure_network_device","requestedSettings":{"Address":"192.168.111.45"},"target":{"deviceItemPath":["WEN_M201","i550 cabinet"],"deviceName":"GSD device_2","deviceTypeIdentifier":"GSD:GSDML-V2.41-LENZE-I550CPN-20220921.XML/D","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"i550 cabinet","nodeId":"IE1","nodeName":"IE1","operation":"configure_network_device","operationId":"l4-ip","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l4-ip"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l4-ip\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l4-ip","success":true}],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "A",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.111.45"
    }
  },
  {
    "operationId": "B",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.111.45"
    }
  }
]
```

Software compile projection from `L4-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L4-write.json`. Audit root duration 72387 ms is functional metadata, not a timing measurement.

### L5: Duplicate PROFINET name

Exact canonical write response:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"configure_network_device","operationId":"l5-pn","result":{"appliedSettings":{"PnDeviceName":"mot01","PnDeviceNameAutoGeneration":"false"},"deviceName":"GSD device_2","messages":[]},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"PnDeviceName":"wen_m201","PnDeviceNameAutoGeneration":null},"operation":"configure_network_device","requestedSettings":{"PnDeviceName":"mot01","PnDeviceNameAutoGeneration":"false"},"target":{"deviceItemPath":["WEN_M201","i550 cabinet"],"deviceName":"GSD device_2","deviceTypeIdentifier":"GSD:GSDML-V2.41-LENZE-I550CPN-20220921.XML/D","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"i550 cabinet","nodeId":"IE1","nodeName":"IE1","operation":"configure_network_device","operationId":"l5-pn","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l5-pn"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l5-pn\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l5-pn","success":true}],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "A",
    "status": "succeeded",
    "attributes": {
      "PnDeviceNameAutoGeneration": false,
      "PnDeviceName": "mot01"
    }
  },
  {
    "operationId": "B",
    "status": "succeeded",
    "attributes": {
      "PnDeviceNameAutoGeneration": true,
      "PnDeviceName": "mot01"
    }
  }
]
```

Software compile projection from `L5-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L5-write.json`. Audit root duration 79701 ms is functional metadata, not a timing measurement.

### L6: PN name with automatic naming enabled

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"worker_operation_failed","engineeringError":{"inner":[],"message":"\u0027set_PnDeviceName\u0027 is not supported by type \u0027Siemens.Engineering.HW.Node\u0027.","type":"EngineeringNotSupportedException"},"message":"TIA Portal operation failed: \u0027set_PnDeviceName\u0027 is not supported by type \u0027Siemens.Engineering.HW.Node\u0027."},"omission":null,"operation":"configure_network_device","operationId":"l6-pn","result":{"appliedSettings":{},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"PnDeviceName":"plc_2/slashtest.profinet interface_2"},"operation":"configure_network_device","requestedSettings":{"PnDeviceName":"zz-native-l6-name"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l6-pn","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l6-pn"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "P",
    "status": "succeeded",
    "attributes": {
      "PnDeviceNameAutoGeneration": true,
      "PnDeviceName": "plc_2/slashtest.profinet interface_2"
    }
  }
]
```

Software compile projection from `L6-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L6-write.json`. Audit root duration 76800 ms is functional metadata, not a timing measurement.

### L7: Ethernet subnet PROFIBUS attribute

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"worker_operation_failed","engineeringError":{"inner":[],"message":"\u0027HighestAddress\u0027 is not supported by type \u0027Siemens.Engineering.HW.Subnet\u0027.","type":"EngineeringNotSupportedException"},"message":"TIA Portal operation failed: \u0027HighestAddress\u0027 is not supported by type \u0027Siemens.Engineering.HW.Subnet\u0027."},"omission":null,"operation":"update_subnet","operationId":"l7-update","result":null,"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"HighestAddress":null,"Name":"PN/IE_2"},"operation":"update_subnet","requestedSettings":{"HighestAddress":"126","Name":"ZZ_L7_ShouldRollback"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"update_subnet","operationId":"l7-update","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l7-update"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "subnet",
    "status": "succeeded",
    "attributes": {
      "Name": "PN/IE_2"
    }
  }
]
```

Software compile projection from `L7-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L7-write.json`. Audit root duration 78145 ms is functional metadata, not a timing measurement.

### L8: Malformed IP address

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027set_Address\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nnot-an-ip is not a valid IP address","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027set_Address\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nnot-an-ip is not a valid IP address"},"omission":null,"operation":"configure_network_device","operationId":"l8-address","result":{"appliedSettings":{},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.1.1"},"operation":"configure_network_device","requestedSettings":{"Address":"not-an-ip"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l8-address","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l8-address"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "P",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.1.1"
    }
  }
]
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L8-write.json`. Audit root duration 78976 ms is functional metadata, not a timing measurement.

### L9: Incompatible subnet connection

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027ConnectToSubnet\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nAssignment to subnet failed.","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027ConnectToSubnet\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nAssignment to subnet failed."},"omission":null,"operation":"configure_network_device","operationId":"l9-connect","result":{"appliedSettings":{},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Subnet":null},"operation":"configure_network_device","requestedSettings":{"Subnet":"590-3"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l9-connect","subnetId":"590-3","subnetName":"PROFIBUS_1"}},"omission":null,"operationId":"l9-connect"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "device",
    "status": "succeeded",
    "deviceNames": [
      "S7-1500/ET200MP station_2"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      }
    ],
    "nodes": [
      {
        "name": "X3",
        "nodeId": "P1",
        "ipAddress": "2",
        "subnetMask": null,
        "pnDeviceName": null,
        "subnetName": "PROFIBUS_2",
        "ioSystemName": "DP-Mastersystem"
      },
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.120",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_1",
        "subnetName": null,
        "ioSystemName": null
      },
      {
        "name": "X2",
        "nodeId": "E1",
        "ipAddress": "192.168.1.1",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_2",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  }
]
```

Software compile projection from `L9-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L9-write.json`. Audit root duration 73703 ms is functional metadata, not a timing measurement.

### L10: Interface without IO connector

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":1,"succeeded":0},"operationCount":2,"operations":[{"failure":{"category":"target_not_found","message":"Device \u0027HMI_Wash\u0027: the selected network interface exposes no IO connector, so ioSystem cannot be attached. No configuration was attempted."},"omission":null,"operation":"configure_network_device","operationId":"l10-no-connector","result":null,"skipReason":null,"status":"failed","warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]},{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l10-after","result":null,"skipReason":"earlierOperationFailed","status":"skipped","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.111.42","IoSystemNumber":null,"IoSystemSubnet":null},"operation":"configure_network_device","requestedSettings":{"Address":"192.168.111.77","IoSystemNumber":"100","IoSystemSubnet":"3547-1"},"target":{"deviceItemPath":["HMI_Wash.IE_CP_1","PROFINET Interface_GBit"],"deviceName":"HMI_Wash","deviceTypeIdentifier":null,"interfacePath":[{"name":"HMI_Wash.IE_CP_1","positionNumber":6},{"name":"PROFINET Interface_GBit","positionNumber":1}],"ioSystemName":"PROFINET IO-System","ioSystemNumber":100,"networkInterfaceName":"","nodeId":"IE1","nodeName":"X1","operation":"configure_network_device","operationId":"l10-no-connector","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l10-no-connector"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L10_Skipped","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l10-after","subnetId":null,"subnetName":"ZZ_L10_Skipped"}},"omission":null,"operationId":"l10-after"}],"error":null,"guards":[{"acknowledged":null,"id":"partial_write_no_rollback","message":"This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying.","operationId":"l10-no-connector","severity":"info"}],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "H",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.111.42"
    }
  },
  {
    "operationId": "hardware",
    "status": "succeeded",
    "deviceNames": [
      "HMI_Wash"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      }
    ],
    "nodes": [
      {
        "name": "X1",
        "nodeId": "IE1",
        "ipAddress": "192.168.111.42",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "hmi_wash",
        "subnetName": "PN/IE_2",
        "ioSystemName": null
      }
    ]
  }
]
```

Software compile projection from `L10-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L10-write.json`. Audit root duration 91514 ms is functional metadata, not a timing measurement.

### L11: GSD mask rejection after address change

A separate captured setup disabled automatic naming. The GSD mask setter rejected the update with `EngineeringNotSupportedException`; only Address was retained and the later create skipped. This is an unsupported-setter outcome, not a rejected PN-name case.

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":1,"succeeded":0},"operationCount":2,"operations":[{"failure":{"category":"worker_operation_failed","engineeringError":{"inner":[],"message":"\u0027set_SubnetMask\u0027 is not supported by type \u0027Siemens.Engineering.HW.Node\u0027.","type":"EngineeringNotSupportedException"},"message":"TIA Portal operation failed: \u0027set_SubnetMask\u0027 is not supported by type \u0027Siemens.Engineering.HW.Node\u0027."},"omission":null,"operation":"configure_network_device","operationId":"l11-partial","result":{"appliedSettings":{"Address":"192.168.111.77"},"deviceName":"GSD device_2","messages":[]},"skipReason":null,"status":"failed","warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]},{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l11-after","result":null,"skipReason":"earlierOperationFailed","status":"skipped","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.111.44","PnDeviceName":"wen_m201","SubnetMask":"255.255.255.0"},"operation":"configure_network_device","requestedSettings":{"Address":"192.168.111.77","PnDeviceName":"Invalid PN Name!","SubnetMask":"255.255.254.0"},"target":{"deviceItemPath":["WEN_M201","i550 cabinet"],"deviceName":"GSD device_2","deviceTypeIdentifier":"GSD:GSDML-V2.41-LENZE-I550CPN-20220921.XML/D","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"i550 cabinet","nodeId":"IE1","nodeName":"IE1","operation":"configure_network_device","operationId":"l11-partial","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l11-partial"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L11_Skipped","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l11-after","subnetId":null,"subnetName":"ZZ_L11_Skipped"}},"omission":null,"operationId":"l11-after"}],"error":null,"guards":[{"acknowledged":null,"id":"partial_write_no_rollback","message":"This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying.","operationId":"l11-partial","severity":"info"}],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]}
```

Exact separate setup response (untimed setup):

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"configure_network_device","operationId":"l11-setup-auto","result":{"appliedSettings":{"PnDeviceNameAutoGeneration":"false"},"deviceName":"GSD device_2","messages":[]},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"PnDeviceNameAutoGeneration":null},"operation":"configure_network_device","requestedSettings":{"PnDeviceNameAutoGeneration":"false"},"target":{"deviceItemPath":["WEN_M201","i550 cabinet"],"deviceName":"GSD device_2","deviceTypeIdentifier":"GSD:GSDML-V2.41-LENZE-I550CPN-20220921.XML/D","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"i550 cabinet","nodeId":"IE1","nodeName":"IE1","operation":"configure_network_device","operationId":"l11-setup-auto","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l11-setup-auto"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l11-setup-auto\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l11-setup-auto","success":true}],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "A",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.111.77",
      "SubnetMask": "255.255.255.0",
      "PnDeviceNameAutoGeneration": false,
      "PnDeviceName": "wen_m201"
    }
  },
  {
    "operationId": "hardware",
    "status": "succeeded",
    "deviceNames": [
      "GSD device_2"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      }
    ],
    "nodes": [
      {
        "name": "IE1",
        "nodeId": "IE1",
        "ipAddress": "192.168.111.77",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "wen_m201",
        "subnetName": "PN/IE_2",
        "ioSystemName": "PROFINET IO-System"
      }
    ]
  }
]
```

Software compile projection from `L11-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L11-write.json`. Audit root duration 72091 ms is functional metadata, not a timing measurement.

### L11b: Accepted PN input and later create

A separate captured setup disabled automatic naming. Address, mask and literal `Invalid PN Name!` were accepted; the later subnet named `ZZ_L11b_Skipped` was actually created. Its name does not describe its status. This control cannot supply a rejected-PN fixture.

Exact canonical write response:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":2},"operationCount":2,"operations":[{"failure":null,"omission":null,"operation":"configure_network_device","operationId":"l11b-partial","result":{"appliedSettings":{"Address":"192.168.203.42","PnDeviceName":"Invalid PN Name!","SubnetMask":"255.255.254.0"},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l11b-after","result":{"name":"ZZ_L11b_Skipped","subnetId":"590-5"},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.1.1","PnDeviceName":"plc_2/slashtest.profinet interface_2","SubnetMask":"255.255.255.0"},"operation":"configure_network_device","requestedSettings":{"Address":"192.168.203.42","PnDeviceName":"Invalid PN Name!","SubnetMask":"255.255.254.0"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l11b-partial","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l11b-partial"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L11b_Skipped","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l11b-after","subnetId":null,"subnetName":"ZZ_L11b_Skipped"}},"omission":null,"operationId":"l11b-after"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l11b-partial\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l11b-partial","success":true},{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l11b-after\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l11b-after","success":true}],"success":true},"warnings":[]}
```

Exact separate setup response (untimed setup):

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"configure_network_device","operationId":"l11b-setup-auto","result":{"appliedSettings":{"PnDeviceNameAutoGeneration":"false"},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"PnDeviceNameAutoGeneration":null},"operation":"configure_network_device","requestedSettings":{"PnDeviceNameAutoGeneration":"false"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l11b-setup-auto","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l11b-setup-auto"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l11b-setup-auto\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l11b-setup-auto","success":true}],"success":true},"warnings":[]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "P",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.203.42",
      "SubnetMask": "255.255.254.0",
      "PnDeviceNameAutoGeneration": false,
      "PnDeviceName": "Invalid PN Name!"
    }
  },
  {
    "operationId": "hardware",
    "status": "succeeded",
    "deviceNames": [
      "S7-1500/ET200MP station_2"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      },
      {
        "subnetId": "590-5",
        "name": "ZZ_L11b_Skipped"
      }
    ],
    "nodes": [
      {
        "name": "X3",
        "nodeId": "P1",
        "ipAddress": "2",
        "subnetMask": null,
        "pnDeviceName": null,
        "subnetName": "PROFIBUS_2",
        "ioSystemName": "DP-Mastersystem"
      },
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.120",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_1",
        "subnetName": null,
        "ioSystemName": null
      },
      {
        "name": "X2",
        "nodeId": "E1",
        "ipAddress": "192.168.203.42",
        "subnetMask": "255.255.254.0",
        "pnDeviceName": "Invalid PN Name!",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  }
]
```

Software compile projection from `L11b-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L11b-write.json`. Audit root duration 70934 ms is functional metadata, not a timing measurement.

### L11c: Corrected two-key partial-write rejection

This replaces the unavailable rejected-PN/two-key fixture. The CPU first receives writable Address/Mask settings, then attempts the previously rejected PROFINET-to-PROFIBUS connection. Native connection rejection retains exactly Address and SubnetMask; the existing subnet/PN settings remain unchanged and the later create skips. It uses the CPU and incompatible subnet already covered by L9, without new production validation.

Exact captured request:

```json
{"name":"network_write","arguments":{"operations":[{"operationId":"l11c-partial","operation":"configure_network_device","target":{"kind":"node","deviceName":"S7-1500/ET200MP station_2","nodeId":"E1","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}]},"changes":{"ipAddress":"192.168.203.42","subnetMask":"255.255.254.0","subnet":{"subnetId":"590-3"}}},{"operationId":"l11c-after","operation":"create_subnet","subnet":{"name":"ZZ_L11c_Skipped","networkType":"Ethernet"}}]}}
```

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":1,"succeeded":0},"operationCount":2,"operations":[{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027ConnectToSubnet\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nAssignment to subnet failed.","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027ConnectToSubnet\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nAssignment to subnet failed."},"omission":null,"operation":"configure_network_device","operationId":"l11c-partial","result":{"appliedSettings":{"Address":"192.168.203.42","SubnetMask":"255.255.254.0"},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"failed","warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]},{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l11c-after","result":null,"skipReason":"earlierOperationFailed","status":"skipped","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.1.1","Subnet":null,"SubnetMask":"255.255.255.0"},"operation":"configure_network_device","requestedSettings":{"Address":"192.168.203.42","Subnet":"590-3","SubnetMask":"255.255.254.0"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l11c-partial","subnetId":"590-3","subnetName":"PROFIBUS_1"}},"omission":null,"operationId":"l11c-partial"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L11c_Skipped","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l11c-after","subnetId":null,"subnetName":"ZZ_L11c_Skipped"}},"omission":null,"operationId":"l11c-after"}],"error":null,"guards":[{"acknowledged":null,"id":"partial_write_no_rollback","message":"This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying.","operationId":"l11c-partial","severity":"info"}],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "P",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.203.42",
      "SubnetMask": "255.255.254.0",
      "PnDeviceNameAutoGeneration": true,
      "PnDeviceName": "plc_2/slashtest.profinet interface_2"
    }
  },
  {
    "operationId": "hardware",
    "status": "succeeded",
    "deviceNames": [
      "S7-1500/ET200MP station_2"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      }
    ],
    "nodes": [
      {
        "name": "X3",
        "nodeId": "P1",
        "ipAddress": "2",
        "subnetMask": null,
        "pnDeviceName": null,
        "subnetName": "PROFIBUS_2",
        "ioSystemName": "DP-Mastersystem"
      },
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.120",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_1",
        "subnetName": null,
        "ioSystemName": null
      },
      {
        "name": "X2",
        "nodeId": "E1",
        "ipAddress": "192.168.203.42",
        "subnetMask": "255.255.254.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_2",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  }
]
```

Software compile projection from `L11c-compile.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L11c-write.json`. Audit root duration 84077 ms is functional metadata, not a timing measurement.

### L12: Five-operation lifecycle path

All five native operations completed. The rename is superseded by delete. The delete info guard lists five initially planned connected nodes; the same-call connected CPU is outside that initial snapshot. This retained planning limit does not imply refreshed intermediate connection inventory. The deleted subnet is absent; the separate new subnet and CPU remain in landed reads.

Exact canonical write response:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":5},"operationCount":5,"operations":[{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l12-create","result":{"name":"ZZ_L12_Created","subnetId":"590-5"},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":null,"omission":null,"operation":"add_network_device","operationId":"l12-add","result":{"deviceName":"ZZ_L12_CPU","rootItemName":"ZZ_L12_CPU","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0","warnings":[]},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":null,"omission":null,"operation":"configure_network_device","operationId":"l12-connect","result":{"appliedSettings":{"Subnet":"3547-1"},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":null,"omission":null,"operation":"update_subnet","operationId":"l12-rename","result":{"name":"ZZ_L12_Renamed","subnetId":"3547-1"},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":null,"omission":null,"operation":"delete_subnet","operationId":"l12-delete","result":{"name":"ZZ_L12_Renamed","subnetId":"3547-1"},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L12_Created","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l12-create","subnetId":null,"subnetName":"ZZ_L12_Created"}},"omission":null,"operationId":"l12-create"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"add_network_device","requestedSettings":{"deviceItemName":"ZZ_L12_CPU","deviceName":"ZZ_L12_CPU","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0"},"target":{"deviceItemPath":[],"deviceName":"ZZ_L12_CPU","deviceTypeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0","ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"add_network_device","operationId":"l12-add","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l12-add"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Subnet":null},"operation":"configure_network_device","requestedSettings":{"Subnet":"3547-1"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l12-connect","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l12-connect"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Name":"PN/IE_2"},"operation":"update_subnet","requestedSettings":{"Name":"ZZ_L12_Renamed"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"update_subnet","operationId":"l12-rename","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l12-rename"},{"effect":{"affectedNodes":[{"deviceName":"ET 200SP station_4","interfacePath":[{"name":"PLC_Wash","positionNumber":1},{"name":"PROFINET interface_1","positionNumber":32768}],"nodeId":"E1"},{"deviceName":"GSD device_2","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"GSD device_3","interfacePath":[{"name":"MOT01","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"GSD device_4","interfacePath":[{"name":"WEN_M101","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"HMI_Wash","interfacePath":[{"name":"HMI_Wash.IE_CP_1","positionNumber":6},{"name":"PROFINET Interface_GBit","positionNumber":1}],"nodeId":"IE1"}],"connectionsComplete":true,"currentSettings":{},"operation":"delete_subnet","requestedSettings":{},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"delete_subnet","operationId":"l12-delete","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l12-delete"}],"error":null,"guards":[{"acknowledged":null,"id":"network_delete_connected_subnet","message":"Deleting the selected subnet removes connections for 5 nodes; devices and nodes remain. Inspect the operation\u0027s effect for exact identities.","operationId":"l12-delete","severity":"info"}],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l12-create\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l12-create","success":true},{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l12-add\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l12-add","success":true},{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l12-connect\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l12-connect","success":true},{"actual":null,"check":"superseded","expected":null,"message":"Operation \u0027l12-rename\u0027: superseded by later operation \u0027l12-delete\u0027 on the same object; the final state is verified there.","operationId":"l12-rename","success":true},{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l12-delete\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l12-delete","success":true}],"success":true},"warnings":["Deleting the selected subnet removes connections for 5 nodes; devices and nodes remain. Inspect the operation\u0027s effect for exact identities."]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "P-device",
    "status": "succeeded",
    "deviceNames": [
      "S7-1500/ET200MP station_2"
    ],
    "subnets": [
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      },
      {
        "subnetId": "590-5",
        "name": "ZZ_L12_Created"
      }
    ],
    "nodes": [
      {
        "name": "X3",
        "nodeId": "P1",
        "ipAddress": "2",
        "subnetMask": null,
        "pnDeviceName": null,
        "subnetName": "PROFIBUS_2",
        "ioSystemName": "DP-Mastersystem"
      },
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.120",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_1",
        "subnetName": null,
        "ioSystemName": null
      },
      {
        "name": "X2",
        "nodeId": "E1",
        "ipAddress": "192.168.111.1",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_2",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  },
  {
    "operationId": "added-device",
    "status": "succeeded",
    "deviceNames": [
      "ZZ_L12_CPU"
    ],
    "subnets": [
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      },
      {
        "subnetId": "590-5",
        "name": "ZZ_L12_Created"
      }
    ],
    "nodes": [
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.1",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "zz_l12_cpu",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  }
]
```

Software compile projection from `L12-compile-0.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Software compile projection from `L12-compile-1.json` (not the full canonical response):

```json
{
  "success": false,
  "resultStatus": "failed",
  "failure": null,
  "totalErrorCount": 3,
  "totalWarningCount": 0,
  "errorMessages": [
    {
      "description": "",
      "path": "PLC_Wash",
      "severity": "Error"
    },
    {
      "description": "",
      "path": "Program blocks",
      "severity": "Error"
    },
    {
      "description": "",
      "path": "300_Communication",
      "severity": "Error"
    },
    {
      "description": "",
      "path": "CommunicationContainer_FB (FB300)",
      "severity": "Error"
    },
    {
      "description": "Tag \"WEN_M101~Head\" not defined.",
      "path": "Network 2",
      "severity": "Error"
    },
    {
      "description": "Tag \"WEN_M201~Head\" not defined.",
      "path": "Network 3",
      "severity": "Error"
    },
    {
      "description": "Tag \"MOT01~Head\" not defined.",
      "path": "Network 4",
      "severity": "Error"
    },
    {
      "description": "Compiling finished (errors: 3; warnings: 0)",
      "path": "",
      "severity": "Error"
    }
  ]
}
```

Software compile projection from `L12-compile-2.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L12-write.json`. Audit root duration 83560 ms is functional metadata, not a timing measurement.

### L13: Rejected third item after successful prefix

The first two creates remain applied; the malformed Address rejects the third item, and both later items skip. Only the successful prefix receives trusted verification. The existing CPU address and existing subnet name remain unchanged.

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":2,"succeeded":2},"operationCount":5,"operations":[{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l13-create","result":{"name":"ZZ_L13_Created","subnetId":"590-5"},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":null,"omission":null,"operation":"add_network_device","operationId":"l13-add","result":{"deviceName":"ZZ_L13_CPU","rootItemName":"ZZ_L13_CPU","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0","warnings":[]},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027set_Address\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nnot-an-ip is not a valid IP address","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027set_Address\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nnot-an-ip is not a valid IP address"},"omission":null,"operation":"configure_network_device","operationId":"l13-rejected","result":{"appliedSettings":{},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"failed","warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]},{"failure":null,"omission":null,"operation":"update_subnet","operationId":"l13-rename","result":null,"skipReason":"earlierOperationFailed","status":"skipped","warnings":[]},{"failure":null,"omission":null,"operation":"delete_subnet","operationId":"l13-delete","result":null,"skipReason":"earlierOperationFailed","status":"skipped","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L13_Created","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l13-create","subnetId":null,"subnetName":"ZZ_L13_Created"}},"omission":null,"operationId":"l13-create"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"add_network_device","requestedSettings":{"deviceItemName":"ZZ_L13_CPU","deviceName":"ZZ_L13_CPU","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0"},"target":{"deviceItemPath":[],"deviceName":"ZZ_L13_CPU","deviceTypeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0","ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"add_network_device","operationId":"l13-add","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l13-add"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.1.1"},"operation":"configure_network_device","requestedSettings":{"Address":"not-an-ip"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l13-rejected","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l13-rejected"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Name":"PN/IE_2"},"operation":"update_subnet","requestedSettings":{"Name":"ZZ_L13_Renamed"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"update_subnet","operationId":"l13-rename","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l13-rename"},{"effect":{"affectedNodes":[{"deviceName":"ET 200SP station_4","interfacePath":[{"name":"PLC_Wash","positionNumber":1},{"name":"PROFINET interface_1","positionNumber":32768}],"nodeId":"E1"},{"deviceName":"GSD device_2","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"GSD device_3","interfacePath":[{"name":"MOT01","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"GSD device_4","interfacePath":[{"name":"WEN_M101","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"HMI_Wash","interfacePath":[{"name":"HMI_Wash.IE_CP_1","positionNumber":6},{"name":"PROFINET Interface_GBit","positionNumber":1}],"nodeId":"IE1"}],"connectionsComplete":true,"currentSettings":{},"operation":"delete_subnet","requestedSettings":{},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"delete_subnet","operationId":"l13-delete","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l13-delete"}],"error":null,"guards":[{"acknowledged":null,"id":"network_delete_connected_subnet","message":"Deleting the selected subnet removes connections for 5 nodes; devices and nodes remain. Inspect the operation\u0027s effect for exact identities.","operationId":"l13-delete","severity":"info"},{"acknowledged":null,"id":"partial_write_no_rollback","message":"This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying.","operationId":"l13-rejected","severity":"info"}],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l13-create\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l13-create","success":true},{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l13-add\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l13-add","success":true}],"success":true},"warnings":["Deleting the selected subnet removes connections for 5 nodes; devices and nodes remain. Inspect the operation\u0027s effect for exact identities.","This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "P",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.1.1"
    }
  },
  {
    "operationId": "subnet",
    "status": "succeeded",
    "attributes": {
      "Name": "PN/IE_2"
    }
  },
  {
    "operationId": "created",
    "status": "succeeded",
    "deviceNames": [
      "ZZ_L13_CPU"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      },
      {
        "subnetId": "590-5",
        "name": "ZZ_L13_Created"
      }
    ],
    "nodes": [
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.1",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "zz_l13_cpu",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  }
]
```

Software compile projection from `L13-compile-created.json` (not the full canonical response):

```json
{
  "success": true,
  "resultStatus": "succeeded",
  "failure": null,
  "totalErrorCount": 0,
  "totalWarningCount": 0
}
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L13-write.json`. Audit root duration 87242 ms is functional metadata, not a timing measurement.

### L13mini: Rejected second item

The first subnet create remains applied; the second item rejects the malformed Address, and the third create skips. Only the first item receives trusted verification. This is the specifically required second-item failure control.

Exact canonical write response:

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":1,"succeeded":1},"operationCount":3,"operations":[{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l13m-first","result":{"name":"ZZ_L13mini_First","subnetId":"590-5"},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027set_Address\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nnot-an-ip is not a valid IP address","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027set_Address\u0027 of type \u0027Siemens.Engineering.HW.Node\u0027.\r\n\r\nnot-an-ip is not a valid IP address"},"omission":null,"operation":"configure_network_device","operationId":"l13m-rejected","result":{"appliedSettings":{},"deviceName":"S7-1500/ET200MP station_2","messages":[]},"skipReason":null,"status":"failed","warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]},{"failure":null,"omission":null,"operation":"create_subnet","operationId":"l13m-after","result":null,"skipReason":"earlierOperationFailed","status":"skipped","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L13mini_First","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l13m-first","subnetId":null,"subnetName":"ZZ_L13mini_First"}},"omission":null,"operationId":"l13m-first"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.1.1"},"operation":"configure_network_device","requestedSettings":{"Address":"not-an-ip"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l13m-rejected","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l13m-rejected"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L13mini_Skipped","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l13m-after","subnetId":null,"subnetName":"ZZ_L13mini_Skipped"}},"omission":null,"operationId":"l13m-after"}],"error":null,"guards":[{"acknowledged":null,"id":"partial_write_no_rollback","message":"This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying.","operationId":"l13m-rejected","severity":"info"}],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l13m-first\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l13m-first","success":true}],"success":true},"warnings":["This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "P",
    "status": "succeeded",
    "attributes": {
      "Address": "192.168.1.1"
    }
  },
  {
    "operationId": "hardware",
    "status": "succeeded",
    "deviceNames": [
      "S7-1500/ET200MP station_2"
    ],
    "subnets": [
      {
        "subnetId": "3547-1",
        "name": "PN/IE_2"
      },
      {
        "subnetId": "590-2",
        "name": "PN/IE_1"
      },
      {
        "subnetId": "590-3",
        "name": "PROFIBUS_1"
      },
      {
        "subnetId": "590-4",
        "name": "PROFIBUS_2"
      },
      {
        "subnetId": "590-5",
        "name": "ZZ_L13mini_First"
      }
    ],
    "nodes": [
      {
        "name": "X3",
        "nodeId": "P1",
        "ipAddress": "2",
        "subnetMask": null,
        "pnDeviceName": null,
        "subnetName": "PROFIBUS_2",
        "ioSystemName": "DP-Mastersystem"
      },
      {
        "name": "X1",
        "nodeId": "E1",
        "ipAddress": "192.168.0.120",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_1",
        "subnetName": null,
        "ioSystemName": null
      },
      {
        "name": "X2",
        "nodeId": "E1",
        "ipAddress": "192.168.1.1",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc_2/slashtest.profinet interface_2",
        "subnetName": null,
        "ioSystemName": null
      }
    ]
  }
]
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L13mini-write.json`. Audit root duration 85110 ms is functional metadata, not a timing measurement.

### L14: Dry-run of the L13 request

This previews the exact L13 main request: five effects, null batch/verification, and five skipped audit items with null durations. No native rejection is expected in preview. The full hardware result equals preflight, and the separate pre-restoration status is unmodified.

Exact canonical write response:

```json
{"batch":null,"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"ZZ_L13_Created","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l13-create","subnetId":null,"subnetName":"ZZ_L13_Created"}},"omission":null,"operationId":"l13-create"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"add_network_device","requestedSettings":{"deviceItemName":"ZZ_L13_CPU","deviceName":"ZZ_L13_CPU","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0"},"target":{"deviceItemPath":[],"deviceName":"ZZ_L13_CPU","deviceTypeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0","ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"add_network_device","operationId":"l13-add","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l13-add"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.1.1"},"operation":"configure_network_device","requestedSettings":{"Address":"not-an-ip"},"target":{"deviceItemPath":["PLC_2/SlashTest","PROFINET interface_2"],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"System:Device.S71500","interfacePath":[{"name":"PLC_2/SlashTest","positionNumber":1},{"name":"PROFINET interface_2","positionNumber":33024}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"PROFINET interface_2","nodeId":"E1","nodeName":"X2","operation":"configure_network_device","operationId":"l13-rejected","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l13-rejected"},{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Name":"PN/IE_2"},"operation":"update_subnet","requestedSettings":{"Name":"ZZ_L13_Renamed"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"update_subnet","operationId":"l13-rename","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l13-rename"},{"effect":{"affectedNodes":[{"deviceName":"ET 200SP station_4","interfacePath":[{"name":"PLC_Wash","positionNumber":1},{"name":"PROFINET interface_1","positionNumber":32768}],"nodeId":"E1"},{"deviceName":"GSD device_2","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"GSD device_3","interfacePath":[{"name":"MOT01","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"GSD device_4","interfacePath":[{"name":"WEN_M101","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"nodeId":"IE1"},{"deviceName":"HMI_Wash","interfacePath":[{"name":"HMI_Wash.IE_CP_1","positionNumber":6},{"name":"PROFINET Interface_GBit","positionNumber":1}],"nodeId":"IE1"}],"connectionsComplete":true,"currentSettings":{},"operation":"delete_subnet","requestedSettings":{},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"delete_subnet","operationId":"l13-delete","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l13-delete"}],"error":null,"guards":[{"acknowledged":null,"id":"network_delete_connected_subnet","message":"Deleting the selected subnet removes connections for 5 nodes; devices and nodes remain. Inspect the operation\u0027s effect for exact identities.","operationId":"l13-delete","severity":"info"}],"omission":null,"phase":"preview","success":true,"tool":"network_write","verification":null,"warnings":["Deleting the selected subnet removes connections for 5 nodes; devices and nodes remain. Inspect the operation\u0027s effect for exact identities."]}
```

Landed read projection (not the full canonical response):

```json
[
  {
    "operationId": "hardware",
    "status": "succeeded",
    "deviceCount": 95,
    "rootDeviceCount": 14,
    "nodeCount": 108,
    "subnetCount": 4,
    "exactResultEqualsPreflight": true,
    "canonicalSha256": "9bf25234758521461a1da1db8774d209c2f04f215d1e90d6de43dd3ff8a9acb4"
  }
]
```

Restoration projection: `close.success:true`, `open.success:true`, status `isOpen:true` / `isModified:false`.

Capture: `artifacts/network-native/task7-20261029-full1/L14-preview.json`. Audit root duration 78032 ms is functional metadata, not a timing measurement.

## Final restoration and limits

The final full hardware result exactly equals the original and preflight fixtures: 95 devices, 14 root devices, 108 nodes and four subnets. Its canonical text is 964,930 characters with SHA256 `9bf25234758521461a1da1db8774d209c2f04f215d1e90d6de43dd3ff8a9acb4`. All selected P/A/B/H address, mask, PROFINET name and automatic-naming values match preflight. Final status and the client's automatic final close-without-save/reopen status both confirm the exact project open with `isModified:false`. The post-exit AP21 SHA256 is the original `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81`.

The functional client exited with code 0 and no matching test Python client remained running. Independent review confirmed all 118 canonical text/structured pairs and 19 unique fresh audit v2/full records, with exact response text/hash and item statuses; maximum completion/file-time difference was 13.1 ms. Audit item totals are 14 succeeded, 12 deliberately failed and 11 skipped, including the five preview skips. These are expected test outcomes. Raw evidence and the final manifest are under ignored `artifacts/network-native/task7-20261029-full1/`, including `restoration-verified.json` and `client-final-restore-011544-status.json`.

This is bounded disposable-project qualification of the installed candidate, not a hardware compatibility claim, PLC download/online test, or evidence for unexercised fault locations. Timing is recorded separately. Task 7 fixture pinning/offline closeout and Task 8 documentation remain pending.
