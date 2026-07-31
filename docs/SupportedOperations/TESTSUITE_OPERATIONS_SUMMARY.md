# TIA Portal TestSuite Operations

## Support status

The current MCP contract does not include TestSuite-specific operations.

`compile_check` returns compiler results for a PLC or selected block scope. It is a generic compile operation; it does not create or execute Application Tests, Style Guide checks, or System Tests.

## Current limits

The following TestSuite areas are outside the current surface:

- `TestSuiteService` discovery.
- Application test sets, groups, cases, and `TestCaseExecutor` execution.
- Style Guide system groups, rule sets, rule-set file updates, and `RuleSetExecutor` execution.
- System Test groups, cases, OPC UA validation, and `SystemTestCaseExecutor` execution.
- TestSuite result messages, execution states, and result interpretation.

For generic compile behavior, see [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md) and [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md).
