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

Call `await diagram.FitAsync()`, `ReplaceAsync(...)`, `ApplyAsync(...)`, `InspectAsync()`, or `ExportSvgAsync()` from normal Razor event handlers. `CanvasCenterAsync()` returns the visible canvas center in model coordinates, while `HitTestClientPointAsync(...)` validates and projects an external pointer drop. These geometry methods keep zoom, pan, bounds, and coordinate conversion inside the canvas runtime rather than duplicating browser math in a host application.

Static Ghostagram browser assets are supplied automatically at `/Ghostagram.Blazor/ghostagram/`. A host that maps them elsewhere can set `GhostDiagramOptions.ModulePath` to the ESM entry point; `Ghostagram.Server` uses `/ghostagram/ghostagram.js`.

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

`GhostNode` also accepts `TypeId`, `TypeVersion`, and an ordered `Properties` collection. Associate a declarative port with a property row through `PropertyId`; input ports render on the left and output ports on the right.

```razor
<GhostNode Id="capture" TypeId="sample.capture" Label="Capture customer" X="100" Y="100"
           Width="240" Height="132" Properties="properties">
    <GhostPort Id="capture-name-in" Direction="target" PropertyId="customer-name" Label="Customer name" />
    <GhostPort Id="capture-name-out" Direction="source" PropertyId="customer-name" Label="Customer name" />
</GhostNode>

@code {
    private readonly IReadOnlyList<DiagramNodeProperty> properties =
    [
        new("customer-name", "customerName", Value: System.Text.Json.JsonSerializer.SerializeToElement(""),
            Mode: DiagramPropertyModes.DisplayAndEdit, Label: "Customer name", Connectable: true)
    ];
}
```

## Reusable node palette

`GhostPalette` is a dependency-free, folder-style Blazor control. The component owns accessible rendering, pointer and keyboard activation, dragging between palette groups, and canvas-drop signaling. The host supplies its groups and items, persists any changes, and decides how a dropped item becomes a diagram node.

```razor
<GhostPalette @ref="palette"
              Groups="groups"
              Items="items"
              ItemDropped="AddNodeAtDrop"
              ItemInvoked="AddNodeAtCenter"
              ItemMoved="MoveNodeType"
              GroupExpandedChanged="SetExpanded" />

<div @ref="canvasTarget">
    <GhostDiagram @ref="diagram" Document="workflow" Revision="revision" />
</div>

@code {
    private GhostPalette? palette;
    private GhostDiagram? diagram;
    private ElementReference canvasTarget;
    private readonly IReadOnlyList<GhostPaletteGroup> groups =
        [new("workflow", "Workflow", Expanded: true)];
    private readonly IReadOnlyList<GhostPaletteItem> items =
        [new("start", "workflow", "Start", "Workflow entry point", "#14b8a6")];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && palette is not null)
            await palette.AttachDropTargetAsync(canvasTarget);
    }

    private Task AddNodeAtDrop(GhostPaletteDropRequest request) => Task.CompletedTask;
    private Task AddNodeAtCenter(string itemId) => Task.CompletedTask;
    private Task MoveNodeType(GhostPaletteMoveRequest request) => Task.CompletedTask;
    private Task SetExpanded(GhostPaletteGroupToggleRequest request) => Task.CompletedTask;
}
```

Use `HeadingContent`, `HeaderActions`, `FooterContent`, and `ItemIcon` render fragments to integrate host-specific controls or an icon library without adding that dependency to `Ghostagram.Blazor`. Include the generated `Ghostagram.Blazor.bundle.scp.css` static asset in the host document head. The default palette module is served from `/Ghostagram.Blazor/ghostagram-palette.js` with a release cache key; override `ModulePath` when a host maps static assets elsewhere.
