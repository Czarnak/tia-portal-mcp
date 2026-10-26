# Final collision closeout — 2026-10-26

Authorization: user explicitly authorized live verification on the currently open disposable project in this chat. Sole open standalone target verified and explicitly bound; PID 21332. Initial isModified:false. Project, device and fixture names are redacted below. PLC_A and PLC_B are two PLCs in that same disposable project. The existing-folder fixture was present on PLC_B; no folder was created.

Provenance: installed tool 4.2.0-local.48.g7445c5f, full access mode, plc_write contract 2.0. `git diff --quiet 7445c5f 20f512f -- TiaMcpServer.OpennessWorker` exited 0. The final fix e7803a4 does not change worker mutation code. These probes qualify the named native create paths and their installed result handling, not the host verifier-message/cache changes added in e7803a4 (those have fresh offline tests).

| Case | Setup and attempted create | Native outcome |
| --- | --- | --- |
| C1 | PLC_A tag Name exists in table A; create tag Name in table B | engineering_rejected / EngineeringTargetInvocationException / PlcTagComposition.Create |
| C2 | PLC_A tag Name exists in table A; create user constant Name in table B | engineering_rejected / EngineeringTargetInvocationException / PlcUserConstantComposition.Create |
| C3 | PLC_A FC Block exists in temporary block group; create tag Block in table B | engineering_rejected / EngineeringTargetInvocationException / PlcTagComposition.Create |
| C4 | PLC_B table FolderTable exists at root; create same name in pre-existing child folder | engineering_rejected / EngineeringTargetInvocationException / PlcTagTableComposition.Create |

All four writes returned phase:applied, success:false, error:null; each attempted item failed with engineeringError.type:EngineeringTargetInvocationException and the native message intact. None silently created/renamed an object. No extra create readback or blanket host collision guard was added.

After-probe reads: source table A still contained exactly the original test tag (Bool, %M500.0, external flags true); table B remained empty; root FolderTable remained empty; the same name in the child folder was target_not_found as expected; the temporary FC remained readable (XML hash xml:sha256:a07c07ec8af4e7e08687387ddb52e94278328dd2d627db88ef6696be5202844e, 3181 characters). No before/after FC hash comparison was captured; do not claim hash preservation for this temporary FC.

Cleanup preview proved the deletion scope: three temporary tables (one test tag total, zero constants) and one temporary block group (one FC, zero subgroups). Actual cleanup succeeded for all four items. Final full PLC_A tag-table inventory, PLC_B root inventory and PLC_B child-folder inventory were byte-equivalent as decoded JSON to the respective preflight inventories, including all original members/properties. Fresh depth-4 project tree was equal by typed path/details (158 nodes), with zero remaining test-prefix objects. Project remained open on the same target, isModified:true (unsaved edit history remains), with project lastModified unchanged from preflight. No save, close, download or PLC control was called. Restoration confirms these observed scopes, not every byte of in-memory project state.

Limits: four exact create directions on one disposable project and installed candidate. Reverse directions, every object-kind combination, other CPU models/versions, and arbitrary folder layouts were not tested. PLC_A had no tag-table folders, so C4 used the existing folder on PLC_B in the same authorized project. General create behavior remains governed by native TIA rules; do not turn these four results into an exhaustive namespace guarantee. Existing singleton-OB guard remains because native rejection was not reached in L6. Existing live L7/hash/timing evidence is unchanged; no performance measurements were repeated.

Exact sanitized requests and canonical documents follow. A failed read of the absent cross-folder target is an expected postcondition, not a failed collision probe.

```json
[
  {
    "case": "C1-cross-table-tag",
    "input": {
      "operations": [
        {
          "operationId": "cross-table-tag",
          "operation": "create_tag",
          "plcName": "PLC_A",
          "folderPath": "/",
          "tableName": "ZZ_CDX_NR_1026_B",
          "name": "ZZ_CDX_NR_1026_Name",
          "dataType": "Bool",
          "logicalAddress": "%M500.1"
        }
      ]
    },
    "document": {
      "batch": {
        "counts": {
          "failed": 1,
          "omitted": 0,
          "skipped": 0,
          "succeeded": 0
        },
        "operationCount": 1,
        "operations": [
          {
            "failure": {
              "category": "engineering_rejected",
              "engineeringError": {
                "inner": [],
                "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
                "type": "EngineeringTargetInvocationException"
              },
              "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists."
            },
            "omission": null,
            "operation": "create_tag",
            "operationId": "cross-table-tag",
            "result": null,
            "skipReason": null,
            "status": "failed",
            "warnings": []
          }
        ],
        "truncation": null
      },
      "contractVersion": "2.0",
      "effects": [
        {
          "effect": {
            "changes": [
              {
                "current": null,
                "field": "name",
                "requested": "ZZ_CDX_NR_1026_Name"
              },
              {
                "current": null,
                "field": "dataType",
                "requested": "Bool"
              },
              {
                "current": null,
                "field": "logicalAddress",
                "requested": "%M500.1"
              }
            ],
            "contentDiff": null,
            "dependsOn": null,
            "operation": "create_tag",
            "placement": "/ZZ_CDX_NR_1026_B",
            "removes": null,
            "target": {
              "blockPath": null,
              "deviceName": "<device-a>",
              "folderPath": "/",
              "kind": "Tag",
              "name": "ZZ_CDX_NR_1026_Name",
              "plcName": "PLC_A",
              "tableName": "ZZ_CDX_NR_1026_B",
              "typePath": null
            }
          },
          "omission": null,
          "operationId": "cross-table-tag"
        }
      ],
      "error": null,
      "guards": [],
      "omission": null,
      "phase": "applied",
      "success": false,
      "tool": "plc_write",
      "verification": {
        "omission": null,
        "operations": [],
        "success": true
      },
      "warnings": []
    }
  },
  {
    "case": "C2-constant-vs-tag",
    "input": {
      "operations": [
        {
          "operationId": "constant-vs-tag",
          "operation": "create_user_constant",
          "plcName": "PLC_A",
          "folderPath": "/",
          "tableName": "ZZ_CDX_NR_1026_B",
          "name": "ZZ_CDX_NR_1026_Name",
          "dataType": "Int",
          "value": "17"
        }
      ]
    },
    "document": {
      "batch": {
        "counts": {
          "failed": 1,
          "omitted": 0,
          "skipped": 0,
          "succeeded": 0
        },
        "operationCount": 1,
        "operations": [
          {
            "failure": {
              "category": "engineering_rejected",
              "engineeringError": {
                "inner": [],
                "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcUserConstantComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
                "type": "EngineeringTargetInvocationException"
              },
              "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcUserConstantComposition'.\r\n\r\nAn attempt was made to create an object that already exists."
            },
            "omission": null,
            "operation": "create_user_constant",
            "operationId": "constant-vs-tag",
            "result": null,
            "skipReason": null,
            "status": "failed",
            "warnings": []
          }
        ],
        "truncation": null
      },
      "contractVersion": "2.0",
      "effects": [
        {
          "effect": {
            "changes": [
              {
                "current": null,
                "field": "name",
                "requested": "ZZ_CDX_NR_1026_Name"
              },
              {
                "current": null,
                "field": "dataType",
                "requested": "Int"
              },
              {
                "current": null,
                "field": "value",
                "requested": "17"
              }
            ],
            "contentDiff": null,
            "dependsOn": null,
            "operation": "create_user_constant",
            "placement": "/ZZ_CDX_NR_1026_B",
            "removes": null,
            "target": {
              "blockPath": null,
              "deviceName": "<device-a>",
              "folderPath": "/",
              "kind": "UserConstant",
              "name": "ZZ_CDX_NR_1026_Name",
              "plcName": "PLC_A",
              "tableName": "ZZ_CDX_NR_1026_B",
              "typePath": null
            }
          },
          "omission": null,
          "operationId": "constant-vs-tag"
        }
      ],
      "error": null,
      "guards": [],
      "omission": null,
      "phase": "applied",
      "success": false,
      "tool": "plc_write",
      "verification": {
        "omission": null,
        "operations": [],
        "success": true
      },
      "warnings": []
    }
  },
  {
    "case": "C3-tag-vs-block",
    "input": {
      "operations": [
        {
          "operationId": "tag-vs-block",
          "operation": "create_tag",
          "plcName": "PLC_A",
          "folderPath": "/",
          "tableName": "ZZ_CDX_NR_1026_B",
          "name": "ZZ_CDX_NR_1026_Block",
          "dataType": "Bool",
          "logicalAddress": "%M500.2"
        }
      ]
    },
    "document": {
      "batch": {
        "counts": {
          "failed": 1,
          "omitted": 0,
          "skipped": 0,
          "succeeded": 0
        },
        "operationCount": 1,
        "operations": [
          {
            "failure": {
              "category": "engineering_rejected",
              "engineeringError": {
                "inner": [],
                "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
                "type": "EngineeringTargetInvocationException"
              },
              "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists."
            },
            "omission": null,
            "operation": "create_tag",
            "operationId": "tag-vs-block",
            "result": null,
            "skipReason": null,
            "status": "failed",
            "warnings": []
          }
        ],
        "truncation": null
      },
      "contractVersion": "2.0",
      "effects": [
        {
          "effect": {
            "changes": [
              {
                "current": null,
                "field": "name",
                "requested": "ZZ_CDX_NR_1026_Block"
              },
              {
                "current": null,
                "field": "dataType",
                "requested": "Bool"
              },
              {
                "current": null,
                "field": "logicalAddress",
                "requested": "%M500.2"
              }
            ],
            "contentDiff": null,
            "dependsOn": null,
            "operation": "create_tag",
            "placement": "/ZZ_CDX_NR_1026_B",
            "removes": null,
            "target": {
              "blockPath": null,
              "deviceName": "<device-a>",
              "folderPath": "/",
              "kind": "Tag",
              "name": "ZZ_CDX_NR_1026_Block",
              "plcName": "PLC_A",
              "tableName": "ZZ_CDX_NR_1026_B",
              "typePath": null
            }
          },
          "omission": null,
          "operationId": "tag-vs-block"
        }
      ],
      "error": null,
      "guards": [],
      "omission": null,
      "phase": "applied",
      "success": false,
      "tool": "plc_write",
      "verification": {
        "omission": null,
        "operations": [],
        "success": true
      },
      "warnings": []
    }
  },
  {
    "case": "C4-cross-folder-table",
    "input": {
      "operations": [
        {
          "operationId": "cross-folder-table",
          "operation": "create_tag_table",
          "plcName": "PLC_B",
          "folderPath": "/existing-folder",
          "tableName": "ZZ_CDX_NR_1026_FolderTable"
        }
      ]
    },
    "document": {
      "batch": {
        "counts": {
          "failed": 1,
          "omitted": 0,
          "skipped": 0,
          "succeeded": 0
        },
        "operationCount": 1,
        "operations": [
          {
            "failure": {
              "category": "engineering_rejected",
              "engineeringError": {
                "inner": [],
                "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagTableComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
                "type": "EngineeringTargetInvocationException"
              },
              "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagTableComposition'.\r\n\r\nAn attempt was made to create an object that already exists."
            },
            "omission": null,
            "operation": "create_tag_table",
            "operationId": "cross-folder-table",
            "result": null,
            "skipReason": null,
            "status": "failed",
            "warnings": []
          }
        ],
        "truncation": null
      },
      "contractVersion": "2.0",
      "effects": [
        {
          "effect": {
            "changes": [
              {
                "current": null,
                "field": "tableName",
                "requested": "ZZ_CDX_NR_1026_FolderTable"
              }
            ],
            "contentDiff": null,
            "dependsOn": null,
            "operation": "create_tag_table",
            "placement": "/existing-folder",
            "removes": null,
            "target": {
              "blockPath": null,
              "deviceName": "<device-b>",
              "folderPath": "/existing-folder",
              "kind": "TagTable",
              "name": null,
              "plcName": "PLC_B",
              "tableName": "ZZ_CDX_NR_1026_FolderTable",
              "typePath": null
            }
          },
          "omission": null,
          "operationId": "cross-folder-table"
        }
      ],
      "error": null,
      "guards": [],
      "omission": null,
      "phase": "applied",
      "success": false,
      "tool": "plc_write",
      "verification": {
        "omission": null,
        "operations": [],
        "success": true
      },
      "warnings": []
    }
  }
]
```
