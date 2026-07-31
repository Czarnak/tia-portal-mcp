# TIA Portal TestSuite Operations

## Scope

This area covers Application Tests, Style Guide rule sets, System Tests, and TestSuite result/execution state handling.

## Exposed operations

No TestSuite-specific public MCP operation was found.

`compile_check` returns compiler results for a PLC or selected block scope. It is not a TestSuite operation and does not create or execute application, style-guide, or system tests.

## Not exposed

- `TestSuiteService` discovery.
- Application test sets, groups, cases, and `TestCaseExecutor` execution.
- Style Guide system groups, rule sets, rule-set file updates, and `RuleSetExecutor` execution.
- System Test groups, cases, OPC UA validation, and `SystemTestCaseExecutor` execution.
- TestSuite result messages, execution states, and result interpretation.

## Static evidence

No TestSuite operation appears in the batch catalog or worker dispatch. The generic compile capability and its limits are described in [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md) and [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md).
