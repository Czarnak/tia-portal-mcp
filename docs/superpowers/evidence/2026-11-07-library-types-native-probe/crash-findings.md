# Crash-risk findings (2026-11-07)

Each case ran alone on fresh copies (`lt-p13b`, `lt-p14a`, `lt-p14b`), Portal 24360. **No case killed Portal**;
no exception, no Openness confirmation, no UI dialog; the originals were reopened after each run.

| ID | Act | Result | Design consequence |
| --- | --- | --- | --- |
| P13b | `Release(NoDeps, null, null, null)` on the new in-work version | Committed as the prefilled `0.0.1`, default; PLC_2 instance relinked to it. | A missing number means "use the prefilled one" (last release + 1, or `0.0.1`); null author and comment are accepted. |
| P14a | `Release` the first version as `1.0.0` | Committed `1.0.0`, default, instance linked. | A first release may skip `0.0.1`. |
| P14b | `Release` the first version as `0.1.0` | Committed `0.1.0`, default, instance linked. | As P14a. |

PR 2's `0.0.0` crash (T7Z) remains the only known fatal version value; the tool keeps rejecting `0.0.0` before
any native call. Out-of-range or lower-than-existing numbers stay unprobed.
