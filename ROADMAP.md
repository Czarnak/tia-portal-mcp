# Roadmap

> Directional roadmap, not a release schedule. Priorities may change as Siemens TIA Portal V21 capabilities, licensing, and real-project validation are confirmed.

- **Maintain a reliable and safe foundation** — keep lifecycle operations, diagnostics, error handling, packaging, documentation, and live TIA Portal V21 acceptance evidence dependable.
- **Complete live Openness validation** — resolve remaining hardware-gated API questions and add guarded transaction, exclusive-access, event, and diagnostic capabilities where V21 support is verified.
- **Evolve PLC source exchange** — introduce practical source formats for UDTs, data blocks, and SCL, then assess graphical LAD/FBD editing through real round trips; XML remains the compatibility fallback.
- **Broaden engineering data exchange** — add generic SimaticML, project graphics, CAx AutomationML, and project-text import/export workflows.
- **Expand hardware and network engineering** — support topology, communication connections, and selected advanced device and module configuration workflows.
- **Extend HMI engineering support** — Unified reads (contract 1.1) and nine controlled content writes (contract 2.0) are implemented, including multilingual text and typed native rejection outcomes. The [native-rejection qualification](docs/superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md) is complete within the recorded R1/R2/R3 scope; extend supported import/export and object coverage next. The 35-case matrix used installed .112; focused R1, UI/local-session checks and timing-after used .113, rather than a full matrix rerun.
- **Enable reusable engineering assets** — add global-library, library-type, and master-copy workflows with explicit version and access controls.
- **Support collaborative engineering** — add Multiuser/VCI and project-server visibility, session lifecycle, locking, and commit workflows.
- **Strengthen controlled online and security operations** — maintain guarded PLC online workflows and add least-privilege user, role, function-right, and UMAC capabilities.
- **Evaluate safety and automated testing extensions** — investigate Safety and TestSuite automation only where prerequisites, isolation, approval, and auditability can be established.

All project-changing capabilities will retain explicit target validation, a preview of consequences, guards against known-dangerous changes, auditability, and proportionate automated plus live TIA Portal verification. Approving an individual write is the MCP client's decision; the server makes the consequence visible and, where the client supports it, asks the user directly (see the [write-safety redesign](docs/superpowers/specs/2026-09-29-write-safety-redesign-design.md)).
