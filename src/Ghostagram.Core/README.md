# Ghostagram.Core

`Ghostagram.Core` is the typed, JSON-compatible diagram model used by Blazor, REST, and MCP adapters.

```csharp
var workflow = DiagramBuilder.Create("onboarding")
    .Node("start", "Start", 80, 80)
    .Port("start-out", "start", "right", direction: "source")
    .Node("review", "Review", 360, 80)
    .Port("review-in", "review", "left", direction: "target")
    .Edge("handoff", "start-out", "review-in", "handoff")
    .Build();
```

Use `DiagramOperations.Upsert(...)` and `DiagramOperations.Remove...(...)` to build revisioned batches for either Ghostagram browser interop or the server command API.

Nodes are heterogeneous persisted data. `TypeId`/`TypeVersion` identify an optional registered definition, while `DiagramNodeProperty` stores ordered typed values as JSON—including explicit `null` and custom type identifiers. A top-level `DiagramPort` may set `PropertyId` to bind graph topology to a rendered property row without putting executable behavior in the model.

Typed behavior, DI registration, compilation, run state, and orchestration adapters live in `Ghostagram.Execution`; see the root `docs/architecture` decisions for the compatibility boundary.
