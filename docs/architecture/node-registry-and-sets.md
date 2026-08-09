# Node registry and node sets

## DI is authoritative

The node catalog is assembled from dependency injection at application startup. Registered node sets are validated and frozen into immutable lookup tables. Reads are therefore lock-free and safe across concurrent Blazor circuits and future graph runs.

Registration fails fast for duplicate node-set IDs, duplicate node type/version identities, duplicate property IDs, invalid property defaults, and invalid property-port references.

## Definitions versus instances

A node definition describes reusable authoring metadata:

- stable type ID and version;
- node-set ownership and palette presentation;
- default size, icon, style, properties, and ports;
- optional handler/adapter metadata.

A node instance is ordinary `DiagramNode` data. The node factory materializes a definition into one node plus its ports and initial property values. This is the only component that needs to understand definition defaults, so consumers do not hand-author graph operation batches.

## Generic extension boundary

Persisted nodes are type-erased, while definitions and handlers may be generic over a developer-owned property/state type. The registry records the type-erased descriptor and resolver needed by execution. Normal node authors work with their typed handler and registration; the engine owns activation, routing, retries, cancellation, and concurrency.

Handlers should be scoped or transient. Singleton handlers require an explicit thread-safety decision because multiple graph runs may activate the same node type concurrently.

## Node sets

A node set is a coherent group of types designed to work together. Its descriptor supplies the palette group name, description, icon, ordering, and node definitions. The reusable `GhostPalette` receives a projection of these descriptors; it does not depend on the registry or execution engine.

The built-in `MAF Orchestration` set contains composable primitives:

- Agent Component
- Route
- Parallel Split
- Join
- Handoff
- Human Input
- Loop Guard
- Output

Sequential work uses ordinary edges. Concurrent orchestration uses split and join. Agentic loops use a route and bounded loop guard. This avoids persisting a provider-specific orchestration class as a monolithic node.

## Future loading

Startup DI registration is the supported boundary in this phase. Future plugin loading can build a new immutable catalog snapshot, but it must not mutate a catalog currently used by an execution run.

