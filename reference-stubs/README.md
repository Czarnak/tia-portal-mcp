# Source-owned Openness compile references

These net48 projects declare only the current worker and PR1 compile probe's required V21 surface. Their generated reference assemblies are compile-only: do not load them at runtime or include them in packages. Placeholder members throw NotSupportedException; they supply no TIA implementation or live evidence.

Assembly identity is Siemens.Engineering.Base / Siemens.Engineering.Step7, version 21.0.0.0, public-key token 29bfe5fdf4ba5d3b. Siemens.Engineering.PublicKey.snk contains the public-only strong-name key extracted from installed V21 assembly metadata. It establishes assembly identity only and cannot sign Siemens implementation code. No private signing key is present.

Release builds are deterministic, path-mapped, delay-signed, and contain no PDB. Source hash provenance uses AssemblyMetadata StubOrigin=tia-portal-mcp and StubSourceHash. Direct developer builds default to unverified; the drift verifier supplies the canonical source hash.

Build the dedicated solution with `dotnet build TiaMcpServer.ReferenceStubs.sln -m:1 --configuration Release`. Compile references are emitted under each project's `obj/Release/net48/ref/`; the regular bin assemblies are placeholders and must never be runtime substitutes. The compile-only probe has no entry point and references only Base and Step7. Worker/probe compilation against generated references uses an explicit TiaOpennessReferenceDir override; installed V21 compilation is a separate check.
