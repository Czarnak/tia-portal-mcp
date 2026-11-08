# GlobalDB creation in software units design

The existing generator writes a mutable MemoryLayout element for every GlobalDB.
Reproduce issue #221 in the authorized open disposable V21 project, then compare
plain, omitted and read-only XML through native PlcBlockComposition.Import.
Choose the smallest candidate proved to preserve optimized DB creation both at
the PLC root and in a software unit, including a nested folder.

Scope: GlobalDB creation and regressions only. Preserve public contracts,
ImportOptions.None, FB/FC/OB XML, and compilation-after-import behavior.
No deployment, save, push, merge, online access or PLC hardware operation.

Native probes assert the exact Portal PID and disposable project before mutation.
Delete only uniquely named probe-owned objects; keep the project open and unsaved.
Record live evidence and limits in the existing acceptance report structure.
