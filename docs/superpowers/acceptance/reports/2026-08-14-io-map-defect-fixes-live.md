# Acceptance Test Report — Structured I/O Map defect fixes (live TIA Portal V21)

**Target:** `feat/io-map-extraction` (PR https://github.com/allanmarum/tia-portal-mcp/pull/1)
**Date:** 2026-08-14
**Runtime:** Real TIA Portal V21, live project `C:\Users\allan\OneDrive\Documents\Automation\Project20\Project20.ap21`
**Harness:** `scripts/live-test-network-io-map.ps1` (read-only, separately authorized, never run by automated tests)
**Boundary:** Read-only. No write tool, no project save, no compile, no commissioning action was performed.

## Purpose

The PR adds structured read-only PLC I/O map extraction to `read_hardware_config`. Live validation
against Project20.ap21 exposed two genuine defects, which this delivery fixes:

1. `includeIoDetails=true` failed with `protocol_error` because TIA V21 reports `StartAddress = -1`
   for `Diagnosis`-type addresses on the PROFINET interface and Port device items, and the host
   payload contract rejects negative start addresses.
2. Channel `ChannelAddress`/`ChannelWidth` dynamic attributes are reported by V21 as `Int64`/
   `UInt64`, while the worker coercion accepted only exact `int`/`uint`, so every channel degraded
   and never produced `logicalAddress` or `tagMatches`.

## Fixes

- **Worker** (`HardwareConfigReader.cs`): new `ReadOptionalNonNegativeInt` normalizes negative
  `StartAddress`/`Length` to null with a non-fatal `messages` entry (`...the reported value was negative.`).
  The strict host contract (`NetworkPayloadContract.ValidateIoDetails`) is unchanged.
- **Worker** (`DynamicNumericAttribute.cs`, new): pure, Siemens-free `CoerceInt32`/`CoerceUInt32`
  accept `int`/`long`/`uint`/`ulong` with range guards (negative or out-of-range → null); wired into
  `ReadDynamicIntAttribute`/`ReadDynamicUIntAttribute`. The DTO stays `int? ChannelAddressBits` /
  `uint? ChannelWidthBits`.
- **Tests:** unit tests for the coercions; FakeWorker fixture models a normalized Diagnosis address;
  payload-contract test proves the host accepts null `startAddress`/`length` while the
  negative-rejection case is preserved; source-assertion test pins the worker behavior.

## Verification

- `dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"` → 0 warnings, 0 errors.
- `dotnet test TiaMcpServer.Tests` → 2187 passed, 0 failed, 0 skipped.
- Live harness:
  `pwsh -File scripts/live-test-network-io-map.ps1 -ProjectPath <Project20.ap21> -IncludeIoDetails -IncludeTagMatches -PlcName PLC_1`
  → **succeeded**, read 1 device, 12 channels. Raw evidence:
  `docs/superpowers/acceptance/evidence/2026-08-14-io-map-defect-fixes-live.json`.

## Live results observed

| Criterion | Observed |
|---|---|
| `includeIoDetails=true` succeeds (no `protocol_error`) | PASS — full read returned; Diagnosis addresses normalized |
| Diagnosis address handling | `ioType: "Diagnosis"`, `startAddress: null`, `length: 0`, degradation message `...address start address: the reported value was negative.` on PROFINET interface and Port_1 |
| Channel address/width on live V21 | PASS — `ChannelAddress`/`ChannelWidth` accepted as 64-bit integers |
| AI 2_1 | channelAddressBits 512 / channelWidthBits 16 → `logicalAddress: "%IW64"` |
| DI 6/DQ 4_1 inputs | bits 0–5, width 1 → `%I0.0`…`%I0.5` |
| DI 6/DQ 4_1 outputs | `%Q0.0`…`%Q0.3` |
| `includeTagMatches=true` | PASS — `Tag_1`→`%I0.0`, `Tag_2`→`%I0.1`, `Tag_3`→`%Q0.0` matched from "Default tag table" on controller `S7-1200 station_1` |
| HSC/Pulse controller association | PASS — `controllerNames` populated for HSC_1..6 and Pulse_1..4 |
| Default read unchanged | PASS — `ioDetails` absent unless `includeIoDetails` is set (source contract unchanged; no safety-token hash impact) |

## Notes and limits

- `length` on the live Diagnosis address is reported as `0`, not negative; the worker's defensive
  negative guard for `Length` is still exercised by tests and applies when V21 reports `-1`.
- `Tag_2` matches `%I0.1`; channels without a corresponding tag report `tagMatches: []`, which is
  the documented conservative-matching contract.
- This live run covers one S7-1200 station. It does not certify other controllers, commissioning,
  or physical-hardware behavior.

## Overall verdict

**PASS.** Both live defects are resolved, regression tests pass, and the live harness output is
recorded as evidence in this commit.
