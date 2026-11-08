# Issue 221 GlobalDB Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Create GlobalDBs through the existing PLC write in root and software-unit folders.

**Architecture:** Retain the guarded pipeline and native XML import. Change only GlobalDB XML if native probes prove a context-independent fix; otherwise pass existing owner context to generation.

**Tech Stack:** C#, net48 worker, .NET 10 xUnit, Siemens V21 Openness.

**Spec:** ../specs/2026-11-08-issue-221-globaldb-design.md

## Global Constraints

No deployment, save, push, merge, online access or PLC hardware operation.
Preserve public contracts, ImportOptions.None and compilation-after-import behavior.

## Review Focus

- Root GlobalDBs retain optimized layout.
- Unit GlobalDBs create without a ReadOnly error.
- Nested unit folders use correct XML.
- Existing names are never overwritten.
- FB/FC/OB XML remains unchanged.

## Task 1: Native-proven GlobalDB fix

- [x] Reproduce unchanged production create_block in Unit_1.
- [x] Compare native plain/omitted/read-only XML at root, unit and nested unit groups.
- [x] Write and run a failing regression for the proven XML requirement.
- [x] Apply the smallest change and run focused tests.
- [x] Build with installed Openness; exercise the built worker in the same scopes.
- [x] Run regressions, document evidence, review and commit.
