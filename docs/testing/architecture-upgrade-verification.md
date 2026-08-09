# Architecture upgrade verification

The upgrade is complete only when the following major subroutines pass.

## Core and serialization

- Every built-in primitive plus enum, JSON, custom data type, and explicit null round-trips.
- Existing property-free JSON loads without mutation or automatic persistence.
- Property IDs are unique and property-bound ports reference an existing property.
- Node operations preserve property metadata and unrelated fields.

## Registry and node factory

- Duplicate set/type/property registrations fail deterministically.
- Catalog snapshots are immutable and safe under parallel reads.
- Factory output uses deterministic node/property/port definitions and stable IDs.
- Node sets project into palette groups without adding registry dependencies to `GhostPalette`.

## Compilation and adapter boundary

- DAG order is deterministic.
- `DagOnly` reports the exact cycle.
- Bounded cycles require a loop controller and termination limit.
- Public Core/execution/node-set assemblies contain no MAF types.
- Mock runs verify ordered events, cancellation, component lease disposal, and checkpoint fingerprint checks.

## Runtime and browser

- Property rows render and primitive edit proposals persist after reload.
- Input/output property ports align with their rows and connect normally.
- Straight, curved, and square connectors retain target arrows.
- Icon presets and valid advanced names render in the node's upper-right slot.
- Palette-to-canvas drag, palette-folder organization, group drag, nested-group drag, hide/show, labels, and selection remain functional.
- The final browser console contains no warning or error generated after page load.

## Commands

Run the release solution build, Core/execution verification applications, `cmd.exe /d /c npm.cmd test` from `src/Ghostagram`, JavaScript syntax checks, `git diff --check`, and the managed Server/browser workflow.

