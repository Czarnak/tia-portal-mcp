# Changelog

## Unreleased

- Add `patch_block_logic` to `plc_write` for unique text replacements in existing block XML or
  eligible external source. Patches retain the whole-export hash precondition, guarded import,
  content diff, verification and full attempted-input audit. Types remain full-document writes.
- Raise the PLC write contract from `2.0` to `2.1`; PLC reads remain `1.0`.
