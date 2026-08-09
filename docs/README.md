# Ghostagram architecture upgrade

This documentation describes the extensibility and execution boundary added to Ghostagram without changing the renderer's authoritative-state rule: the C# document remains durable data and JavaScript remains a retained visual projection plus interaction-proposal source.

## Architecture

- [Graph model and dynamic properties](architecture/graph-model.md)
- [Node registry and node sets](architecture/node-registry-and-sets.md)
- [Execution engine boundary](architecture/execution-engine.md)
- [Microsoft Agent Framework adapter](architecture/maf-adapter.md)
- [Serialization compatibility](architecture/serialization-compatibility.md)
- [UI and runtime rendering](architecture/ui-runtime.md)
- [Current solution architecture assessment and roadmap](architecture/solution-architecture-assessment.md)

## Decisions

- [ADR-001: heterogeneous persistence and typed extension boundary](adr/ADR-001-typed-extension-boundary.md)
- [ADR-002: controlled-cycle execution compilation](adr/ADR-002-controlled-cycle-compilation.md)
- [ADR-003: provider-neutral MAF adapter](adr/ADR-003-provider-neutral-maf-adapter.md)

## Verification

- [Architecture upgrade verification matrix](testing/architecture-upgrade-verification.md)
