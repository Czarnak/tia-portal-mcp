# Independent block import and compilation results

The user requires a completed block import to remain successful when its subsequent compilation reports errors. Compilation still runs after import and its result remains visible separately.

Use the existing blockImportOutcome.compileStage and compileReport fields for update_block_logic in XML and source formats. Keep compilation scopes, final read/content verification, diagnostics sanitization, and unsuccessful/uncertain import handling. Compiler errors alone must not fail a completed, verified import or stop later batch items. Unavailable compile evidence is independent where final verification succeeds; fatal context loss must remain a failure.

Approach: remove compilation success from the import-success gate, preserving independent compile evidence. Narrowing compilation would hide diagnostics and would not satisfy the requested separation. Another response envelope would duplicate the existing contract.

Scope excludes PLC type imports and block creation, whose separate contracts are outside the issue's update_block_logic complaint.
