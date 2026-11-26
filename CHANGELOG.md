# Changelog

## Unreleased

- Reduce escaping in canonical tool-result JSON for XML/SCL punctuation and ordinary BMP text.
  Decoded values and public contract versions are unchanged; more values can fit the existing
  character budgets. Audit response hashes change with the exact delivered JSON spelling;
  PLC export content hashes are unchanged. SDK/client transport may escape structured data again.
- Add `patch_block_logic` to `plc_write` for unique text replacements in existing block XML or
  eligible external source. Patches retain the whole-export hash precondition, guarded import,
  content diff, verification and full attempted-input audit. Types remain full-document writes.
- Share compilation for calls containing two or more source-format block updates or patches:
  compile committed imports once per PLC selector after the mutation loop, with informational
  `compilationMode` and `sharedCompilations` results. Thanks to doerk77 for the contribution in #238.
- Raise the PLC write contract from `2.0` to `2.1`, including patches and shared compilation
  response members; PLC reads remain `1.0`.
