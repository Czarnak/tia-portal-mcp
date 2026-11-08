# Library delete-preview native diagnostic

Source of the net48 helper used on 2026-11-08. Built against installed TIA Portal V21 references with zero warnings/errors. Executable SHA-256: `0958444d5155e459ea9f503f027f4fb794e7e274be64e67526de89de221309ce`.

The live `inspect-global-version` call separately exercised `FindInstances` on both project PLCs, then the global version's `MasterCopiesContainingInstances` getter and enumeration. The retained JSONL is direct Openness output, not an MCP response. The PLC searches refused the cross-container scope; master-copy reads succeeded.

The generic helper also prepared and restored LT_ fixture-name changes for the guard/ambiguity cases. Those fixture captures are not retained. It never opens, saves, closes or deletes a project/library. It attaches only to the exact Portal/project selected by environment variables.

Build in this directory with `dotnet build NativeFixture.csproj -m:1`. For the read-only diagnostic, set `LT_PORTAL_PID`, `LT_PROJECT` and `LT_LIBRARY` to the authorized already-open disposable fixtures, then run `NativeFixture.exe inspect-global-version <type-guid> <version-number>` from the build output.

The JSONL's stack paths were redacted to `<repo>`/`<profile>` before compression; no native type/message/row value changed. No disposable project/library identities are present.
