# Disposable HMI checkpoint probe

This standalone .NET Framework 4.8 executable probes the native Siemens V21 HMI calls for P1–P22 (31 variants). It does not alter or install the MCP server. It is scoped to Portal PID 35872, the exact disposable MixOfEverything AP21 path, and HMI_RT_1 on HMI_Wash.

Build:

    dotnet build NativeHmiProbe.csproj -m:1

Offline verification (does not attach):

    bin\Debug\net48\NativeHmiProbe.exe --self-test
    bin\Debug\net48\NativeHmiProbe.exe --list

The root agent alone runs the live command:

    bin\Debug\net48\NativeHmiProbe.exe --all --confirm-disposable --pid 35872 --project "C:\Users\LCZ\Desktop\RnD\TIA-Portal\DisposableProjects\MixOfEverything\MixOfEverything.ap21" --timeout-seconds 900 --output "C:\Users\LCZ\.codex\visualizations\2026\10\29\01a187d0-883d-7d82-b84c-24468920f5cd\native-hmi-probe\native-results.jsonl"

Use --case P1 or --case P2 instead of --all to select one numbered case (including all its variants), or an exact variant such as --case P2-case.

Each attachment and each complete case request has a 900-second watchdog. A timeout emits a flushed record and terminates only the probe process with exit code 124. The executable never accepts permission dialogs or retries requests. Results and progress are appended and flushed to --output in addition to stdout. For the recorded run, the root agent approved the ordinary Openness dialog outside this executable; see the [checkpoint report](../../2026-10-29-hmi-write-checkpoint-probes.md).

Every case captures state before the call, independent state/Validate after the call, and state after closing without saving and reopening the saved fixture baseline. A restoration mismatch stops the run. Native exception types/messages/inner chains and the failing stage are preserved. No transaction or ExclusiveAccess is added because neither exists in the current HMI writers; the purpose is to observe their exact native mutation behavior, including partial writes and cleanup.

P4/P8/P9 record whether native Create returned an object that equals a pre-existing object. If it returns, valid setters are followed by the worker-equivalent missing-culture refusal. The exact native composition Find/object.Equals/Delete cleanup is then observed; this can delete an original object if Create returned it, which is why execution is restricted to the explicitly disposable project and each case restores from disk.

P21's missing group and P22's missing culture are intentionally worker-like refusals, not Siemens rejections. Tables do not support native Validate. Per-case manual UI compilation is pending the maintainer's return; do not classify accepted/Validate-clean requests as final O3 decisions without that check.

No Save() is called. Confirmation events after attachment are recorded and answered No solely to support closing this disposable project without saving; Openness access/security dialogs are never handled.
