# Ghostagram.Blazor

The component owns one browser canvas; its `Document` parameter stays a typed C# value. Browser gestures arrive as proposals through `EventReceived`, allowing the app or MCP server to validate and commit an authoritative operation batch.

```razor
@using Ghostagram.Blazor
@using Ghostagram.Core

<GhostDiagram @ref="diagram" Document="workflow" Revision="revision"
              Options="new(Height: \"70vh\")" EventReceived="OnDiagramEvent" />

@code {
    private GhostDiagram? diagram;
    private long revision;
    private readonly DiagramDocument workflow = DiagramBuilder.Create("onboarding")
        .Node("start", "Start", 80, 80).Port("start-out", "start", "right", direction: "source")
        .Node("review", "Review", 360, 80).Port("review-in", "review", "left", direction: "target")
        .Edge("handoff", "start-out", "review-in", "handoff").Build();

    private Task OnDiagramEvent(GhostagramEvent proposal) => Task.CompletedTask;
}
```

Call `await diagram.FitAsync()`, `ReplaceAsync(...)`, `ApplyAsync(...)`, `InspectAsync()`, or `ExportSvgAsync()` from normal Razor event handlers. Static Ghostagram browser assets are supplied automatically at `/_content/Ghostagram.Blazor/ghostagram/`.

## Declarative composition

Use exactly one mode: either the controlled `Document` shown above, or a declarative `DocumentId` with child controls. Groups establish group membership for nested nodes; nodes establish ownership for nested ports.

```razor
<GhostDiagram DocumentId="onboarding" Options="new(Height: \"70vh\")">
    <GhostGroup Id="stage" Label="Review stage" X="40" Y="40" Width="520" Height="260">
        <GhostNode Id="start" Label="Start" X="80" Y="90">
            <GhostPort Id="start-out" Anchor="right" Direction="source" />
        </GhostNode>
        <GhostNode Id="review" Label="Review" X="320" Y="90">
            <GhostPort Id="review-in" Anchor="left" Direction="target" />
        </GhostNode>
    </GhostGroup>
    <GhostEdge Id="handoff" SourcePortId="start-out" TargetPortId="review-in" Label="handoff" />
</GhostDiagram>
```
