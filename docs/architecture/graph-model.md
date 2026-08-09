# Graph model and dynamic properties

## Purpose

`Ghostagram.Core` defines data, not executable behavior. `DiagramDocument`, `DiagramNode`, `DiagramPort`, `DiagramEdge`, and `DiagramGroup` are serializable graph datatypes shared by Blazor, REST, MCP, storage, layout, export, and future execution adapters.

The persisted `DiagramNode` intentionally remains non-generic. A diagram can contain unrelated node types, old documents have no polymorphic discriminator, and requiring polymorphic JSON would couple files to CLR implementation classes. Strong typing belongs at the registration and handler boundary.

## Node properties

A node may carry an ordered, optional collection of `DiagramNodeProperty` records. Each property contains:

- a stable property ID;
- a data-type identifier;
- a JSON value, including an explicit JSON `null`;
- label, description, edit/display mode, requirement metadata, and optional enum choices.

Built-in authoring covers string, Boolean, 64-bit integer, decimal number, date, date/time, enum, and arbitrary JSON values. Data-type IDs are open so a developer can register a future serializer/editor without changing the document schema.

Properties are ordered because visual forms and voice/data-capture flows need deterministic presentation. Property IDs, not labels or list indexes, are identity.

## Property ports

Ports remain top-level graph entities. `DiagramPort.PropertyId` optionally associates a port with one property row, while `NodeId` remains the topology owner. This means property connections automatically use existing capacity, scope, policy, edge, removal, and collaboration operations.

The node factory generates stable port IDs once. The renderer never invents persistent ports. Legacy ports without `PropertyId` retain their existing absolute/side anchor behavior.

## Semantic graph projection

Execution consumes an immutable semantic projection rather than mutating `DiagramDocument`. The projection indexes nodes, ports, edges, incoming/outgoing transitions, and strongly connected components. Groups, viewport, selection, and styling remain authoring concerns.

Diagram cycles are valid. A free-form diagram can describe conversations, feedback, state machines, and agentic loops. Whether a cycle is executable is determined by the selected compile profile, not by the persistence model.

## Extension rule

Developers add behavior through DI-registered node definitions and handlers. They do not subclass `DiagramNode`, edit graph reducers, or implement edge scheduling. This keeps authored files portable and protects extensions from underlying graph-operation changes.

