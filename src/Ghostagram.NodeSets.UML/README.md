# Ghostagram.NodeSets.UML

`Ghostagram.NodeSets.UML` supplies the reusable `UML` palette catalog. The current version 2 includes Class, Abstract Class, Interface, Enumeration, Data Type, and Object. Each compact descriptor exposes four unbound relationship ports anchored to the top, right, bottom, and left sides; node properties never create connection points. Version 1 descriptors remain registered so saved diagrams with the original two-port schema stay resolvable, while the palette presents only the latest version. Each descriptor has stable type, property, and relationship-port identifiers plus a distinct style and Iconify icon. The package references only `Ghostagram.Execution`; these nodes are authoring-only, register no execution handlers, and have no Server or Blazor dependency.

Every UML relationship port uses the `uml.relationship` scope. This keeps structural connections intentional while still allowing UML nodes to interoperate with generic ports whose scope is the Ghostagram wildcard `*`.

Use the existing edge marker controls to express standard structural relationships:

| Relationship | Marker placement |
| --- | --- |
| Association | No marker, or `plain-arrow` at the navigable end |
| Generalization | `triangle-open` at the superclass end |
| Realization | `triangle-open` at the interface end; use a dashed edge style |
| Aggregation | `diamond-open` at the whole end |
| Composition | `diamond` at the composite/whole end |
| Domain cardinality | `erd-one`, `erd-zero-one`, `erd-one-many`, or `erd-zero-many` at either end |

This basic set models structural diagrams. It does not yet provide UML semantic validation, dedicated compartment rendering, automatic relationship inference, sequence/activity/state nodes, or code generation. Those capabilities can be added as new descriptor versions or separate node sets without coupling them to the laboratory UI.
