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
