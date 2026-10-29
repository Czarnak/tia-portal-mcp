# Interactive HMI compile probe

This is the original checkpoint probe with a single-case `--leave-open` option for the eight accepted variants. It preserves the original native calls, before/after snapshots, exact disposable-project/PID/HMI scope and 900-second watchdog. The original archived probe is unchanged.

Build with `dotnet build NativeHmiProbe.csproj -m:1`, then run `bin\Debug\net48\NativeHmiProbe.exe --self-test` without attaching. The verified interactive executable SHA-256 is `9793714C9F084BD49CD634127AA75C79CF356F1BED4DAC8C76E082015C5A2C28`; build and offline self-test passed.

To reproduce one case, use the original scope/output arguments with `--case P10 --leave-open` instead of `--all`. Allowed selections are P10, P12-discrete, P12-analog, P12-table, P13, P19, P20-missing-tag and P20-missing-member. Multi-case or other selections are refused before attachment. The existing Portal must have a UI.

Leave-open exits after the independent after-snapshot, without saving, closing or reopening the project. The maintainer inspects and compiles that state. Later cases may retain earlier results; diagnostics must be attributed by object name. Normal attachment disposal disconnects this helper from the GUI Portal.

The attached metadata's `save` string predates this option and still describes default restoration; the case/complete records explicitly identify `restorationSkipped:true` and `projectLeftOpen:true`. Raw case `uiCompile` fields precede the later maintainer results recorded in the report. A complete record or exit zero alone does not prove acceptance: inspect `nativeMutationReturned`, exceptions and the after-snapshot.

See the [checkpoint report](../../2026-10-29-hmi-write-checkpoint-probes.md) for results and UI evidence.
