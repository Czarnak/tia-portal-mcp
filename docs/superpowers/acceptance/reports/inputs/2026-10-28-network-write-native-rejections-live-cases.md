# network_write native-rejections live cases — partial checkpoint, 2026-10-28

**Paused by the maintainer; Task 7 is incomplete.** The maintainer stopped at the L3 silent-rename finding and wants to be present in TIA Portal for future investigation. No ruling to keep or change the behavior was made. TIA Portal was closed by the maintainer; it was not reopened after the stop.

Candidate: branch `refactor/network-write-native-rejections`, source `f1ae806954a65a44f7638226a42cd9c66de70a8a`, installed package `4.2.0-local.76.gf1ae806`. The host and worker matched the local Release publish by SHA-256. Portal UI language: English, confirmed by the maintainer. Full mode; item/document budgets both 1,000,000, matching the [Task 0 baseline](2026-10-27-network-write-timing-before.md).

Initial full hardware equaled the original fixture (95 devices, 14 root devices, 108 nodes, four subnets); the project was open and unmodified. Raw MCP responses, requests, exact matching audit-v2 records and provenance remain in the local ignored directory `artifacts/network-native/task7-20261028/`. The captured canonical documents below are retained directly; a full live report and replay fixtures have not been completed.

`compile_check` compiles PLC software. Its silence does not establish hardware compiler acceptance. Every attempted failure must be inspected before retrying; trusted means the native call completed without rejection, not that requested values were read back.

## L1 — duplicate device name: O1

Portal rejected the create with `engineering_rejected`, `EngineeringTargetInvocationException` and the native message. A filtered hardware read found the existing device, with the original subnets. The existing PLC software compiled with zero errors/warnings; the requested new CPU software was absent. Close without save, reopen and clean status succeeded.

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027CreateWithItem\u0027 of type \u0027Siemens.Engineering.HW.DeviceComposition\u0027.\r\n\r\nThe name could not be set at device.\r\n\r\nValue of attribute Name not unique!","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027CreateWithItem\u0027 of type \u0027Siemens.Engineering.HW.DeviceComposition\u0027.\r\n\r\nThe name could not be set at device.\r\n\r\nValue of attribute Name not unique!"},"omission":null,"operation":"add_network_device","operationId":"l1-add","result":null,"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"add_network_device","requestedSettings":{"deviceItemName":"ZZ_L1_CPU","deviceName":"S7-1500/ET200MP station_2","typeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0"},"target":{"deviceItemPath":[],"deviceName":"S7-1500/ET200MP station_2","deviceTypeIdentifier":"OrderNumber:6ES7 510-1DJ01-0AB0/V2.0","ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"add_network_device","operationId":"l1-add","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l1-add"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

## L2 — duplicate subnet name: O1

Portal rejected the create with `engineering_rejected` and the native message. The landed hardware read retained the four original subnet identities/names. Software compilation had zero errors/warnings. Close without save, reopen and clean status succeeded.

```json
{"batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operationCount":1,"operations":[{"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"message":"Error when calling method \u0027Create\u0027 of type \u0027Siemens.Engineering.HW.SubnetComposition\u0027.\r\n\r\nCould not set the name.","type":"EngineeringTargetInvocationException"},"message":"TIA Portal rejected the operation: Error when calling method \u0027Create\u0027 of type \u0027Siemens.Engineering.HW.SubnetComposition\u0027.\r\n\r\nCould not set the name."},"omission":null,"operation":"create_subnet","operationId":"l2-create","result":null,"skipReason":null,"status":"failed","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{},"operation":"create_subnet","requestedSettings":{"Name":"PN/IE_2","TypeIdentifier":"System:Subnet.Ethernet"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"create_subnet","operationId":"l2-create","subnetId":null,"subnetName":"PN/IE_2"}},"omission":null,"operationId":"l2-create"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":false,"tool":"network_write","verification":{"omission":null,"operations":[],"success":true},"warnings":[]}
```

## L3 — colliding subnet rename: O3 silent drop; decision deferred

The request renamed Ethernet subnet `3547-1` from `PN/IE_2` to the existing name `PN/IE_1`. The call succeeded and verification was `trusted`, but both the mutation result and the independent landed read retained `PN/IE_2`. PLC software compilation reported zero errors/warnings. This is evidence of an ignored rename; it is not evidence that the requested name landed.

The maintainer chose to stop and return later while watching TIA itself. No `postcondition_failed` read-back, collision precheck or other product change was authorized or implemented. Close without save, reopen and clean status succeeded.

Request:

```json
{"operations":[{"operationId":"l3-rename","operation":"update_subnet","target":{"kind":"subnet","subnetId":"3547-1"},"subnetChanges":{"name":"PN/IE_1"}}]}
```

Canonical write:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"update_subnet","operationId":"l3-rename","result":{"name":"PN/IE_2","subnetId":"3547-1"},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Name":"PN/IE_2"},"operation":"update_subnet","requestedSettings":{"Name":"PN/IE_1"},"target":{"deviceItemPath":[],"deviceName":null,"deviceTypeIdentifier":null,"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":null,"nodeId":null,"nodeName":null,"operation":"update_subnet","operationId":"l3-rename","subnetId":"3547-1","subnetName":"PN/IE_2"}},"omission":null,"operationId":"l3-rename"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l3-rename\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l3-rename","success":true}],"success":true},"warnings":[]}
```

Canonical landed check:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"inspect_network_object","operationId":"subnet","result":{"attributes":[{"access":"readWrite","availability":"available","diagnostic":null,"name":"Name","source":"modeledAndDynamic","supportedTypes":["System.String"],"value":{"kind":"string","typeName":"System.String","value":"PN/IE_2"}}],"evidence":{"address":null,"connectionIsValid":null,"deviceItemPath":[],"interfaceName":null,"interfaceOperatingMode":null,"interfaceType":null,"ioControllerName":null,"ioSystemName":null,"localEndpointName":null,"localSubnetName":null,"name":null,"networkType":"Ethernet","nodeName":null,"nodeType":null,"partnerEndpointName":null,"partnerSubnetName":null,"positionNumber":null,"subnetName":"PN/IE_2","typeIdentifier":"System:Subnet.Ethernet"},"messages":[],"target":{"connectionIndex":null,"connectionType":null,"deviceName":null,"interfaceName":null,"interfaceOperatingMode":null,"interfaceType":null,"ioSystemIndex":null,"ioSystemName":null,"itemPath":null,"kind":"subnet","localConnectionId":null,"localConnectionName":null,"nodeId":null,"nodeIndex":null,"number":null,"subnetId":"3547-1"}},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"1.0","error":null,"success":true,"tool":"network_read","warnings":[]}
```

Canonical software compile diagnostics:

```json
{"contractVersion":"1.0","error":null,"result":{"failure":null,"omission":null,"status":"succeeded","value":{"blockPath":null,"overallState":"Success","plcs":[{"deviceName":"ET 200SP station_4","diagnosticNotes":[],"errorCount":0,"messages":[{"description":"","path":"PLC_Wash","severity":"Information"},{"description":"Compile safety program \u0027Safety Administration\u0027.","path":"Safety","severity":"Information"},{"description":"","path":"Program blocks","severity":"Information"},{"description":"","path":"999_MISC","severity":"Information"},{"description":"","path":"LSNTP","severity":"Information"},{"description":"","path":"LSNTP_Server (FB1000)","severity":"Information"},{"description":"The project setting (Project \u003E Properties \u003E Protection) for simulation support cannot be applied to this block, because it is not editable.","path":"","severity":"Information"},{"description":"","path":"LSNTP_Server (FB1000)","severity":"Information"},{"description":"The project setting (Project \u003E Properties \u003E Protection) for simulation support cannot be applied to this block, because it is not editable.","path":"","severity":"Information"},{"description":"Consistency check for safety program \u0027Safety Administration\u0027.","path":"Consistency check","severity":"Information"},{"description":"The safety program is already consistent and for this reason is not recompiled.","path":"","severity":"Information"},{"description":"Compiling finished (errors: 0; warnings: 0)","path":"","severity":"Information"}],"plcName":"PLC_Wash","state":"Success","warningCount":0}],"scope":"plc","totalErrorCount":0,"totalWarningCount":0}},"success":true,"tool":"compile_check","warnings":[]}
```

## L4 — duplicate IP address: accepted and landed; software O3

Both inspected nodes reported `192.168.111.45` after the write. The public software compile reported zero errors/warnings; hardware compilation was not observed. This differs from L3: the duplicate IP landed. L4 had already been dispatched while the L3 ruling was pending. Its close-without-save, reopen and clean-status checks succeeded.

Canonical write:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"configure_network_device","operationId":"l4-ip","result":{"appliedSettings":{"Address":"192.168.111.45"},"deviceName":"GSD device_2","messages":[]},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"2.0","effects":[{"effect":{"affectedNodes":[],"connectionsComplete":true,"currentSettings":{"Address":"192.168.111.44"},"operation":"configure_network_device","requestedSettings":{"Address":"192.168.111.45"},"target":{"deviceItemPath":["WEN_M201","i550 cabinet"],"deviceName":"GSD device_2","deviceTypeIdentifier":"GSD:GSDML-V2.41-LENZE-I550CPN-20220921.XML/D","interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"ioSystemName":null,"ioSystemNumber":null,"networkInterfaceName":"i550 cabinet","nodeId":"IE1","nodeName":"IE1","operation":"configure_network_device","operationId":"l4-ip","subnetId":null,"subnetName":null}},"omission":null,"operationId":"l4-ip"}],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"network_write","verification":{"omission":null,"operations":[{"actual":null,"check":"trusted","expected":null,"message":"Operation \u0027l4-ip\u0027: the Openness call completed without rejection; nothing was read back.","operationId":"l4-ip","success":true}],"success":true},"warnings":[]}
```

Canonical landed check:

```json
{"batch":{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":2},"operationCount":2,"operations":[{"failure":null,"omission":null,"operation":"inspect_network_object","operationId":"A","result":{"attributes":[{"access":"readWrite","availability":"available","diagnostic":null,"name":"Address","source":"dynamic","supportedTypes":["System.String"],"value":{"kind":"string","typeName":"System.String","value":"192.168.111.45"}}],"evidence":{"address":null,"connectionIsValid":null,"deviceItemPath":["WEN_M201","i550 cabinet"],"interfaceName":"i550 cabinet","interfaceOperatingMode":"IoDevice","interfaceType":"Ethernet","ioControllerName":null,"ioSystemName":null,"localEndpointName":null,"localSubnetName":null,"name":null,"networkType":null,"nodeName":"IE1","nodeType":"Ethernet","partnerEndpointName":null,"partnerSubnetName":null,"positionNumber":null,"subnetName":null,"typeIdentifier":null},"messages":[],"target":{"connectionIndex":null,"connectionType":null,"deviceName":"GSD device_2","interfaceName":"i550 cabinet","interfaceOperatingMode":null,"interfacePath":[{"name":"WEN_M201","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"interfaceType":null,"ioSystemIndex":null,"ioSystemName":null,"itemPath":null,"kind":"node","localConnectionId":null,"localConnectionName":null,"nodeId":"IE1","nodeIndex":null,"number":null,"subnetId":null}},"skipReason":null,"status":"succeeded","warnings":[]},{"failure":null,"omission":null,"operation":"inspect_network_object","operationId":"B","result":{"attributes":[{"access":"readWrite","availability":"available","diagnostic":null,"name":"Address","source":"dynamic","supportedTypes":["System.String"],"value":{"kind":"string","typeName":"System.String","value":"192.168.111.45"}}],"evidence":{"address":null,"connectionIsValid":null,"deviceItemPath":["MOT01","i550 cabinet"],"interfaceName":"i550 cabinet","interfaceOperatingMode":"IoDevice","interfaceType":"Ethernet","ioControllerName":null,"ioSystemName":null,"localEndpointName":null,"localSubnetName":null,"name":null,"networkType":null,"nodeName":"IE1","nodeType":"Ethernet","partnerEndpointName":null,"partnerSubnetName":null,"positionNumber":null,"subnetName":null,"typeIdentifier":null},"messages":[],"target":{"connectionIndex":null,"connectionType":null,"deviceName":"GSD device_3","interfaceName":"i550 cabinet","interfaceOperatingMode":null,"interfacePath":[{"name":"MOT01","positionNumber":0},{"name":"i550 cabinet","positionNumber":1}],"interfaceType":null,"ioSystemIndex":null,"ioSystemName":null,"itemPath":null,"kind":"node","localConnectionId":null,"localConnectionName":null,"nodeId":"IE1","nodeIndex":null,"number":null,"subnetId":null}},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null},"contractVersion":"1.0","error":null,"success":true,"tool":"network_read","warnings":[]}
```

Canonical software compile diagnostics:

```json
{"contractVersion":"1.0","error":null,"result":{"failure":null,"omission":null,"status":"succeeded","value":{"blockPath":null,"overallState":"Success","plcs":[{"deviceName":"ET 200SP station_4","diagnosticNotes":[],"errorCount":0,"messages":[{"description":"","path":"PLC_Wash","severity":"Information"},{"description":"Compile safety program \u0027Safety Administration\u0027.","path":"Safety","severity":"Information"},{"description":"","path":"Program blocks","severity":"Information"},{"description":"","path":"999_MISC","severity":"Information"},{"description":"","path":"LSNTP","severity":"Information"},{"description":"","path":"LSNTP_Server (FB1000)","severity":"Information"},{"description":"The project setting (Project \u003E Properties \u003E Protection) for simulation support cannot be applied to this block, because it is not editable.","path":"","severity":"Information"},{"description":"","path":"LSNTP_Server (FB1000)","severity":"Information"},{"description":"The project setting (Project \u003E Properties \u003E Protection) for simulation support cannot be applied to this block, because it is not editable.","path":"","severity":"Information"},{"description":"Consistency check for safety program \u0027Safety Administration\u0027.","path":"Consistency check","severity":"Information"},{"description":"The safety program is already consistent and for this reason is not recompiled.","path":"","severity":"Information"},{"description":"Compiling finished (errors: 0; warnings: 0)","path":"","severity":"Information"}],"plcName":"PLC_Wash","state":"Success","warningCount":0}],"scope":"plc","totalErrorCount":0,"totalWarningCount":0}},"success":true,"tool":"compile_check","warnings":[]}
```

## L5 — interrupted, no acceptance outcome

L5 had begun when the maintainer closed Portal. It returned `binding_conflict` because complete worker/Portal identity was unavailable. Subsequent landed/compile/cleanup calls were denied on the invalidated binding. This does not prove whether a setter was reached. The client exited; no retry or replay was performed. L5 must be investigated or rerun in a future authorized session.

```json
{"batch":null,"contractVersion":"2.0","effects":[],"error":{"category":"binding_conflict","message":"The worker did not return a complete workerSessionId/sessionGeneration/portalProcessId identity."},"guards":[],"omission":null,"phase":"error","success":false,"tool":"network_write","verification":null,"warnings":[]}
```

## Stop and remaining work

- L1–L4 each have complete write, landed, software-compile and clean restore records.
- L5 is interrupted; L6–L14, the additional L13 variant, all after-timing captures, pinned offline fixtures and the Task 7 default-suite run remain pending.
- Zero candidate timing captures were collected. Existing baseline timing was preserved.
- No product, FakeWorker or test source changed; the installed candidate remains the one above.
- After stop, the original AP21 SHA-256 was independently verified as `564a644a3116fe60cba9265e40e5a797fd68b57b0c954dff7e3059c84d702a81`. No Portal process or owned Task 7 client remained.
- Final full hardware/PN-attribute equality was not reread after Portal closure. The saved file is unchanged; no new live calls were made to obtain a stronger final runtime claim.
- On resume, verify candidate provenance and fixture state again, have the maintainer present, and advance one case at a time with a stop boundary before the next case. Do not rerun already accepted captures without a reason or treat L5 as acceptance evidence.
