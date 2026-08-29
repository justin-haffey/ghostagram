# Solution Architecture

This directory is the durable local architecture library.

- `SOLUTION-ARCHITECTURE.md` describes the Solution boundary and external contracts.
- `decisions/` contains Solution ADRs.
- `packages/[PACKAGE_NAME]/PACKAGE-ARCHITECTURE.md` describes a Package.
- `packages/[PACKAGE_NAME]/modules/[MODULE_NAME]/MODULE-ARCHITECTURE.md` describes a Module nested under its owning Package.
- `views/systems/` contains runtime and operational system views. A System is a view, not an architecture tier.

Architecture changes begin as `Target`, become `Implemented` only with implementation evidence, and become `Current` only after validation and reconciliation. `$swe-architect` is the sole architecture-template authority; do not maintain competing repository-local template copies.
