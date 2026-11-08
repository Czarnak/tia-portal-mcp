# ADR-0018: Read-only is the default access mode

**Status:** Accepted
**Decided:** 2026-11-08   **Recorded:** 2026-11-08
**Area:** write-safety
**Current home:** [ARCHITECTURE: Host startup and access modes](../ARCHITECTURE.md#2-host-startup-and-access-modes); [Installation: Access modes](../guides/installation.md#access-modes)

## Context

A manually configured MCP client could previously obtain write permissions simply by omitting
the access mode. The installer already chose read-only. The host, directly launched worker,
and worker client need the same least-permission fallback, with enough explanation for users
whose configurations previously relied on implicit write access.

## Decision

Missing access-mode configuration resolves to read-only at every product entry point.
Explicit configuration retains precedence over the default. Startup and diagnostics report
the source, and default startup explains how to opt into write access.

Keeping implicit read-write was rejected because omission should not grant write access.
Requiring an explicit mode was rejected because an omitted mode must still start successfully.

## Consequences

- Manual configurations that relied on the old default must explicitly select a writable mode.
- Installed configurations retain their explicit mode. Mode permissions and lifecycle
  confirmation behavior remain unchanged.
- New entry points must preserve the read-only fallback; source reporting must distinguish
  explicit configuration from omission.

## Evidence

- Original design and implementation checklist: commit `8ea73d9b` (read-only default spec).
