# Ghostagram C#/Razor Contract

Ghostagram deliberately exposes static ESM functions that accept and return JSON-shaped values. A Razor wrapper keeps one `IJSObjectReference` to `ghostagram.js`; it does not keep a JavaScript graph object per node, port, or edge.

## Module calls

| JavaScript call | C# purpose |
| --- | --- |
| `create(host, options)` | Create one isolated canvas and return its `instanceId`, protocol version, lifecycle state, and capabilities. |
| `replace(instanceId, request)` | Initial load or recovery after a revision mismatch. |
| `apply(instanceId, request)` | Atomically apply a revisioned operation batch. |
| `inspect(instanceId)` | Diagnostic snapshot only; the .NET model remains authoritative. |
| `canvasCenter(instanceId)` | Return the model-coordinate point at the center of the visible canvas viewport. |
| `hitTestClientPoint(instanceId, clientX, clientY)` | Test a browser client point against the canvas and project it into model coordinates. |
| `dispose(instanceId)` | Release listeners, pending frames, and the owned DOM subtree. |

Call `capabilities()` once after module import, then reject document descriptors that the browser engine does not support before issuing `replace`.

## Suggested C# records

```csharp
public sealed record GhostagramRequest(
    string RequestId,
    string DocumentId,
    long Revision,
    long? BaseRevision = null,
    IReadOnlyList<GhostagramOperation>? Ops = null,
    GhostagramModel? Model = null);

public sealed record GhostagramOperation(string Type, JsonElement Value, string? Id = null);

public sealed record GhostagramConnectionPolicy(
    IReadOnlyList<string>? AllowPortIds = null,
    IReadOnlyList<string>? DenyPortIds = null,
    IReadOnlyList<string>? AllowNodeIds = null,
    IReadOnlyList<string>? DenyNodeIds = null);

public sealed record GhostagramPort(
    string Id,
    string NodeId,
    string Direction = "both",
    string Scope = "*",
    int MaxConnections = -1,
    bool Enabled = true,
    GhostagramConnectionPolicy? ConnectionPolicy = null);

public sealed record GhostagramNodeDecoration(string? Icon = null);

public sealed record GhostagramFlowchartOptions(
    double Stub = 32,
    double CornerRadius = 0);

public sealed record GhostagramEvent(
    int ProtocolVersion,
    long EventId,
    string InstanceId,
    string? DocumentId,
    long RenderRevision,
    string Type,
    string Origin,
    long Timestamp,
    JsonElement Payload);
```

Use explicit JSON property names matching the JavaScript contract (`requestId`, `documentId`, `baseRevision`, and `ops`). `System.Text.Json` camel-case serialization is sufficient. A `null` `ConnectionPolicy` omits the rule; arrays in the policy map directly to Ghostagram's `allowPortIds`, `denyPortIds`, `allowNodeIds`, and `denyNodeIds` fields.

The executable API records live in `Ghostagram.Contracts`: `DiagramCommand(documentId, actorId, commandId, baseRevision, operations)`, `DiagramSnapshot`, `DiagramChange`, and `DiagramCommandResult`. `commandId` is distinct from the browser `requestId`: the former is a durable, actor-scoped idempotency key; the latter is only a bounded interop-call cache.

## Revision rule

1. The host sends `replace` with revision `N`.
2. The host sends `apply` with `baseRevision: N` and `revision: N + 1`.
3. A browser event is a proposal, not a durable write. The host validates it, updates its immutable document, then sends the corresponding `apply`.
4. On `REVISION_MISMATCH`, issue `replace` with the authoritative snapshot instead of retrying the old delta.

`revision` and `baseRevision` must be non-negative integers. Browser `apply` requires exactly `revision == baseRevision + 1`; the server, not the client, allocates the accepted next revision.

This makes repeated JavaScript interop calls safe across Blazor renders and prevents browser-side visual state from becoming a second source of truth.

## Server layout command

`POST /api/documents/{documentId}/layout` accepts a `DiagramLayoutRequest`. The built-in algorithm is `ghost-layered`; every response identifies its version and seed. `dryRun: true` returns a deterministic operation batch without persistence. A commit runs through the authoritative per-document writer and returns the same `DiagramCommandResult` shape used by direct edits.

Layout operations contain complete node or group descriptors with updated bounds, so the same batch is valid for the server reducer and browser `apply`. Supported options are direction (`right`, `left`, `down`, or `up`), origin, layer/node/component spacing, group padding/header size, bounded crossing sweeps, and graph-size limits. A revision conflict never applies a stale layout.

## Event bridge

Pass a `DotNetObjectReference` as `eventSink` with `eventMethod: "OnGhostagramEvent"`. Ghostagram invokes `invokeMethodAsync` with one envelope. Preview events are request-animation-frame coalesced; commit events are not. Shift-dragging blank canvas emits coalesced `selection.changing` preview events followed by one `selection.changed` commit event with `kind: "lasso"`, selected visible node/group/edge IDs, and a model-coordinate rectangle. Ctrl/Cmd+A emits `selection.changed` with `kind: "all"` for all visible diagram objects. A plain empty-canvas click emits `selection.changed` with `kind: "canvas"` and `ids: []`; movement beyond four screen pixels is not treated as a click.

Groups are explicit JSON records. A root group omits `parentGroupId`; a nested group supplies its parent ID. Nodes reference their immediate `groupId`. The engine rejects missing parents and cyclic nesting. `group.assignGroup` re-parents a group without rewriting its bounds or member nodes.

Nodes and groups may set `icon` to an Iconify name such as `"mdi:folder-outline"`, or `null` to clear it. Ghostagram renders the optional Iconify web component at the item's upper-right corner. It lazy-loads Iconify's documented web-component script only when needed; a host with strict CSP or local hosting can register that same component before `create`, preventing Ghostagram from adding the CDN script. Icons are presentational and are omitted from static `exportSvg`.

When a group is collapsed, an edge crossing its boundary remains visible and terminates at the midpoint of the collapsed group side nearest its peer. The router preserves that side as the proxy endpoint direction, so an edge exits or enters cleanly rather than bending across a corner. An edge whose endpoints are hidden by the same collapsed group is hidden. This behavior also applies to `exportSvg`.

Common commit events map directly to an atomic host update:

| Event | Host update |
| --- | --- |
| `selection.changed` | `selection.replace` with the proposed IDs. |
| `element.clicked` | A non-mutating host command using `{ id, kind, point, modifiers }`. It follows the corresponding selection proposal. |
| `element.contextRequested` | A non-mutating host context menu command using `{ id, kind, point, modifiers }`; Ghostagram suppresses the browser context menu. |
| `selection.deleteRequested` | One atomic host batch: remove payload `edgeIds`, then `nodeIds`, then deepest-first `groupIds`, and finish with `selection.replace` to `[]`. Group selection includes nested groups, contained nodes, and their incident paths. |
| `history.undoRequested` / `history.redoRequested` | Restore the preceding/following authoritative document snapshot with `replace` at a higher revision. Ctrl/Cmd+Z requests undo; Ctrl/Cmd+Shift+Z and Ctrl/Cmd+Y request redo. Ghostagram never owns history. |
| `node.move.commit` | `node.upsert`, including its proposed `groupId`. |
| `node.resize.commit` | `node.upsert` with its proposed `width` and `height`. Resize handles appear only while the node or group is selected. Set `node.resizable: false` to keep a node handle hidden. |
| `node.rotate.commit` | `node.upsert` with its proposed normalized `rotation` in degrees. The selected-node rotation handle can be disabled with `node.rotatable: false`; every gesture snaps to 15-degree increments. |
| `node.label.commit` / `group.label.commit` / `edge.label.commit` | `node.upsert`, `group.upsert`, or `edge.upsert` with the proposed label. Double-click a visible label or press F2 with exactly one selected item to open the transient editor. An empty edge label removes it; `labelEditable: false` suppresses editing. |
| `edge.labelPosition.commit` | `edge.upsert` with the proposed numeric `labelOffsetX` and `labelOffsetY`. Drag a visible edge label to reposition it; offsets are relative to its routed label location. |
| `nodes.move.commit` | One atomic batch of `node.upsert` operations for every proposed `{ id, x, y, groupId }` member. |
| `selection.move.commit` | One atomic batch of `group.upsert` and `node.upsert` operations for a mixed selected set; group membership is preserved. Pointer drags and keyboard movement use the same event; keyboard events include `keyboard: true`. |
| `group.move.commit` | One root `group.upsert`, including proposed `parentGroupId`; `group.upsert` for every payload `groups` descendant; plus member `node.upsert` operations. |
| `group.resize.commit` | `group.upsert` with new bounds. |
| `group.visibilityRequested` | `group.upsert` with `collapsed` set from the proposed `hidden` value. The centered eye control appears only above a selected group; the group frame remains visible while its contents are hidden so they can be shown again. |
| `edge.createRequested` | `edge.upsert`. |
| `edge.reconnectRequested` | `edge.upsert` with new source/target port IDs. |
| `edge.waypointsRequested` | `edge.upsert` with updated waypoints. |
| `edge.detachRequested` | `edge.remove`. |

The browser harness in `src/Ghostagram/demo/demo.html` demonstrates these mappings without requiring a .NET host.

## Overlay descriptors

Overlays are JSON data, never callbacks. A label overlay may specify `label`, `location` (0 through 1), `offsetX`, `offsetY`, and `fontSize`. The same descriptor is used for canvas rendering and `exportSvg`, so C# has no separate export layout policy to maintain.

An edge or named `edgeType` may specify `connectorOptions: { stub, cornerRadius }` for the `flowchart` connector. `stub` controls the directional departure/arrival distance (default `32`); `cornerRadius` rounds right-angle bends (default `0`). Both are non-negative numbers. Edge options override type options field-by-field, and the same path is used in live SVG and `exportSvg`.

Arrow, plain-arrow, and diamond markers accept `location: 0` (source) or `location: 1` (target, the default). `exportSvg` includes the required SVG marker definitions and preserves both endpoint placements.

Ports use either a simple endpoint type (`blank`, `dot`, or `rectangle`) or `{ type, size, fill, stroke, strokeWidth }`. All values are validated before an atomic model update is accepted.

An advanced JavaScript host may call `registerEndpoint(type, renderer)` before rendering, then use that registered `type` in a normal JSON endpoint descriptor. This is intentionally a JavaScript-only extension point; Razor continues to send data, never delegates.

An advanced JavaScript host may call `registerOverlay(type, renderer)` before `replace`. The renderer receives an SVG group plus `{ overlay, edge, point, sourcePoint, targetPoint }` and may use its supplied `emit(name, payload)` callback to send an `overlay.event` envelope. Razor still sends a normal `{ type, ... }` overlay descriptor. Custom renderer output is intentionally excluded from model-derived `exportSvg`; use built-in overlays when export fidelity is required.

Ports may also have a non-empty `scope`. A connection is allowed only when scopes match or either is `"*"`; the same rule applies to programmatic upserts and reconnection proposals.

For a C#-safe equivalent to a jsPlumb `beforeDrop` interceptor, a port may declare `connectionPolicy: { allowPortIds?, denyPortIds?, allowNodeIds?, denyNodeIds? }`. Each value is an array of IDs belonging to the peer port or peer node. Both endpoints must allow a new connection or reconnection. A later policy change does not invalidate existing document edges, which preserves workflow history while blocking new transitions.

Set a port's `enabled` flag to `false` to dim it and prevent new browser-created connections to or from that port. Existing document edges remain valid, so workflow state can disable future transitions without rewriting history.

Edges default to `detachable: true` and `reconnectable: true`. Set either to `false` to suppress browser detach proposals or reconnection handles; the host still owns the final edge mutation.

## Edge animation descriptor

`animation` is optional and defaults to off. Use `true` for the default data-flow treatment or `{ type: "flow", speed: 1.2, dash: "8 6", direction: "forward" }` for a C#-serializable descriptor. `speed` must be positive and `direction` is `forward` or `reverse`. The browser respects reduced-motion preferences unless `create` receives `respectReducedMotion: false`. Animation is presentational only: it does not affect routing, hit-testing, revision rules, or the static `exportSvg` result.

An edge may set `style: { stroke, strokeWidth, dash, opacity, lineCap, lineJoin, labelColor }`. `stroke` and `labelColor` are non-empty CSS color strings, `strokeWidth` is positive, `opacity` ranges from 0 to 1, `lineCap` is `butt`, `round`, or `square`, and `lineJoin` is `miter`, `round`, or `bevel`. These values are validated with the edge update and use the same representation in live rendering and static export.

## Named edge types

The authoritative diagram model may contain `edgeTypes: [{ id, connector, style, overlays, animation, detachable, reconnectable }]`. An edge then sets `type: "workflow-flow"`. The named descriptor supplies defaults, the edge overrides them, and `style` merges per property. Use revisioned `edgeType.upsert` and `edgeType.remove` operations to change types; updates repaint only edges using that type. Missing type references are rejected during `replace` or `edge.upsert`. This remains entirely document data—no callback registration or global module configuration is needed.
