# Ghostagram

Ghostagram is a clean-room browser diagram engine with a dependency-free core. It is intentionally designed as a small, JSON-first ESM module so a future C#/Razor `IJSObjectReference` wrapper can use it without retaining JavaScript object proxies or translating a large, browser-specific object graph. Iconify is an optional, lazily loaded presentation integration.

It is isolated from the existing application and does not import the vendored jsPlumb bundle.

The Version 2 system design is documented in [DESIGN.md](../../DESIGN.md); the executable C#/Razor boundary is in [CONTRACT.md](CONTRACT.md). The new typed server boundary is in `../Ghostagram.Contracts` and `../Ghostagram.Server`.

## Interop contract

Use the module-level API from a wrapper:

```javascript
import * as ghostagram from "./ghostagram.js";

const hello = ghostagram.create(hostElement, {
  protocolVersion: 1,
  eventSink: dotNetReference,
  eventMethod: "OnGhostagramEvent"
});

ghostagram.replace(hello.instanceId, {
  requestId: "load-1",
  documentId: "diagram-42",
  revision: 1,
  model: { nodes: [], ports: [], edges: [], groups: [], viewport: { x: 0, y: 0, zoom: 1 } }
});
```

`replace` is the recovery/initialization boundary. `apply` accepts a revisioned, atomic operation list; all commands return `{ ok, requestId, renderedRevision, stats, problem? }`.

Supported operations are `node.upsert/remove`, `port.upsert/remove`, `edgeType.upsert/remove`, `edge.upsert/remove`, `group.upsert/remove/assignNode/assignGroup`, `selection.replace`, and `viewport.set/fit/center`. Groups may use `parentGroupId` for arbitrary acyclic nesting. `capabilities()` reports both supported families and deliberately deferred features, so a wrapper can reject unsupported input rather than silently losing data.

`exportSvg(instanceId)` returns a standalone SVG snapshot derived from the authoritative graph model and current collapsed-group visibility, suitable for a C# download/export workflow.

Events use one JSON envelope:

```json
{
  "protocolVersion": 1,
  "eventId": 12,
  "instanceId": "...",
  "documentId": "diagram-42",
  "renderRevision": 3,
  "type": "node.move.commit",
  "origin": "browser",
  "timestamp": 0,
  "payload": { "nodeId": "node-1", "x": 128, "y": 64 }
}
```

Browser preview events are frame-coalesced. Commit events (move, selection, connection requests) are never coalesced. The host remains authoritative: a drag emits a proposal; it is durable only after the host sends the matching `apply` operation.

Progressive nodes remain JSON-only. A node may add `sections: [{ id, title, parentSectionId?, order, collapsible }]` and `presentation: { displayMode, expandedHeight, collapsedSectionIds }`; properties may add `sectionId` and `editor: { kind, placeholder?, minimum?, maximum?, step? }`. Editor kinds are `auto`, `text`, `multiline`, `toggle`, `number`, `range`, `date`, `dateTime`, `select`, `color`, and `json`. Simple nodes omit these fields and retain the lightweight layout. Node and section controls emit `node.presentationRequested` with the proposed `{ nodeId, height, presentation }` (and `sectionId`/`collapsed` for section actions); the host must persist the proposal with a normal node upsert.

The engine publishes `--ghostagram-grid-size`, `--ghostagram-dot-grid-size`, `--ghostagram-dot-grid-phase-x`, and `--ghostagram-dot-grid-phase-y` on its host. A presentation stylesheet can use the zoomed size and phase variables for a dot background that remains aligned with the same model grid used by drag and resize snapping.

## Practical parity surface

Ghostagram currently covers multiple canvas instances, indexed node/port/edge/group registries, source/target direction, connection limits, scopes, enabled/disabled ports, and declarative peer-port/node connection policies, nested group membership, reparenting, collapsed-group edge proxies, model-owned node and group resizing, multi-selection including Shift-drag lasso selection of nodes, groups, and visible edges plus blank-canvas deselection, atomic selection-delete proposals (including nested group content and incident paths), snapped node dragging, normalized click/context events for nodes, ports, edges, and groups, viewport zoom, four connector profiles (straight, flowchart, Bezier, state-machine), blank/dot/rectangle endpoint descriptors, label/arrow/plain-arrow/diamond descriptor handling, SVG edge rendering, and delta-based updates. A port `scope` is an exact string or `"*"`; connections require compatible scopes. A port may also use `connectionPolicy: { allowPortIds?, denyPortIds?, allowNodeIds?, denyNodeIds? }` to provide a serializable equivalent to a `beforeDrop` rule; both endpoints must allow the peer. An endpoint is either a simple type or `{ type, size, fill, stroke, strokeWidth }`; label descriptors support `location` (0–1), `offsetX`, `offsetY`, and `fontSize` and resolve against the rendered route. Marker overlays accept `location: 0` or `1` for source or target placement.

Edges default to `detachable: true` and `reconnectable: true`. Set either to `false` when the C# document policy makes that interaction unavailable.

An edge may opt into a visual data-flow treatment with `animation: true` or `animation: { type: "flow", speed: 1.2, dash: "8 6", direction: "forward" }`. It is off by default, respects `prefers-reduced-motion` unless `respectReducedMotion: false` is supplied at creation, and never changes edge geometry or model semantics. `exportSvg` intentionally produces a static snapshot.

Edge style is also a validated JSON descriptor: `style: { stroke, strokeWidth, dash, opacity, lineCap, lineJoin, labelColor }`. This keeps appearance data serializable for a future C# document model, applies it consistently to live SVG paths and labels, and carries it into static SVG export. When both `style.dash` and flow animation are specified, the animation dash pattern takes precedence only while animation is enabled.

For reusable edge policy, place a named descriptor in `model.edgeTypes`, for example `{ id: "workflow-flow", connector, style, overlays, animation, detachable, reconnectable }`, then set `edge.type: "workflow-flow"`. Per-edge `connector`, `style`, `overlays`, `animation`, `detachable`, and `reconnectable` values override the type; styles merge field-by-field. This mirrors the useful part of jsPlumb connection types without sharing mutable browser objects or callbacks with C#.

Flowchart edges and types may add `connectorOptions: { stub: 32, cornerRadius: 8 }`. `stub` controls the first and last directional run; `cornerRadius` rounds orthogonal bends. Both are non-negative, C#-serializable values, and per-edge values override an edge type field-by-field.

When a JavaScript host needs a live-only visual extension, call `registerOverlay(type, renderer)` before loading the model and use `{ type, ... }` in an edge overlay list. The renderer owns its SVG group and may emit `overlay.event` through the supplied callback. This is deliberately separate from the serializable built-in overlay profile and is omitted from `exportSvg`.

For a JavaScript host-specific endpoint, call `registerEndpoint(type, renderer)` once and use that `type` in the otherwise JSON-only endpoint descriptor. C#/Razor never passes a callback.

The capability matrix intentionally reports selector sources/targets and lists as unsupported. A JavaScript-only custom connector registry, fixed/dynamic/relative/perimeter anchors, nested groups, node rotation, and programmatic or browser-editable edge `waypoints: [{ x, y }]` are available; endpoint and overlay factories still require a versioned C#-safe descriptor policy. Unsupported descriptors are rejected rather than silently ignored.

Nodes and groups can use the upper-right decoration slot with an Iconify icon name, for example `icon: "mdi:folder-outline"`. Ghostagram lazy-loads Iconify's documented web component on the first such item; a host can pre-register the same component from a local or CSP-approved source to avoid the CDN request. Set `icon: null` to clear it. The decoration is presentational and is not included in static SVG export.

Ctrl/Cmd+Z emits `history.undoRequested`; Ctrl/Cmd+Shift+Z and Ctrl/Cmd+Y emit `history.redoRequested`. The host owns document snapshots and restores one with a higher-revision `replace`, keeping browser interaction state separate from a C#/Razor application's undo stack.

See [PARITY.md](PARITY.md) for the explicit feature-family status and release gate.
See [CONTRACT.md](CONTRACT.md) for the typed C#/Razor interop mapping and revision protocol.

## Why this architecture can be superior to jsPlumb

1. **Interop-first contract:** one correlated, versioned JSON command/event envelope is simpler and safer for C#/Razor than a broad browser-object API.
2. **Atomic deltas:** every operation batch validates before mutation and revision mismatches fail explicitly; normal edits do not require a reset/recreate render cycle.
3. **Targeted invalidation:** direct indexes find a moved node's incident edges, allowing one node plus those paths to update instead of a global repaint.
4. **True instance isolation:** every canvas owns exactly one root subtree, listener abort controller, state store, and render frame; creating one canvas never disposes another.
5. **Honest capability negotiation:** unsupported feature families are published and rejected deterministically, avoiding best-effort configuration loss.

These are architectural advantages, not a claim that this early implementation is already more mature than jsPlumb's long-established ecosystem. Practical parity must be demonstrated incrementally with the capability matrix, interaction tests, and performance benchmarks.

## Performance model

State is held in `Map` indexes for nodes, ports, edges, and groups, with reverse port/node and edge/port indexes. `apply` marks affected objects, then one `requestAnimationFrame` flush updates only dirty DOM nodes and incident SVG paths. `replace` is reserved for initialization or recovery after a revision mismatch. There are no runtime dependencies.

Run the deterministic core tests with:

```powershell
cmd.exe /d /c npm.cmd test
```

from this folder.

Run the indexed large-graph delta and lasso-selection benchmark with `cmd.exe /d /c npm.cmd run benchmark`. The standalone `demo/demo.html` is the browser harness for drag, selection, connection-request, label, zoom, and delta-render verification when a browser surface is available.

## Run the browser harness

Python is not required. From this folder, run:

```powershell
cmd.exe /d /c npm.cmd run demo
```

Then open [http://127.0.0.1:8088/](http://127.0.0.1:8088/) and leave the terminal open while testing. To use a different port, run `cmd.exe /d /c npm.cmd run demo -- --port=8090`. Stop the server with `Ctrl+C`.

The harness is a small host simulator: Review demonstrates independent left inbound and right outbound ports, Iconify decorations on the Review node and Review stage group, and two paths can coexist on the node. Dragging a node commits its new position and proposes membership for the group under its center, dragging any selected node or group moves the selected node/group set through one atomic proposal, arrow keys move the selected set by one grid unit (`Shift` moves ten), Ctrl/Cmd+A selects visible objects, `Escape` clears it, and Delete/Backspace proposes one atomic removal for selected paths, nodes, and nested group content. Ctrl/Cmd+Z and Ctrl/Cmd+Shift+Z or Ctrl/Cmd+Y ask the harness host to restore its document snapshots; Undo and Redo buttons expose the same host behavior. Double-click a visible label, or press F2 with exactly one selected node, group, or edge, to edit its label; double-clicking an unlabeled edge adds one. Enter, blur, or a click outside the editor proposes the label and Escape cancels. Clearing an edge-label input removes its label. Drag a visible edge label to reposition it on its route. Selecting a resizable node or group exposes its bottom-right resize handle; selecting a rotatable node also exposes its top-center rotation handle. Deselecting hides both. Shift-dragging blank canvas proposes lasso selection, dragging the empty Review-group frame moves that group and its members atomically, dragging its resize handle resizes it, dragging from the right-hand Start port to the unconnected Archive port previews a dashed edge and commits a new connection, selecting an edge exposes reconnect handles, double-clicking an already-labeled edge removes it, the Fit/Center buttons issue viewport operations, Rotate Start validates rotated endpoint geometry, Toggle Review group applies a collapsed group delta, Disable/Enable Start port demonstrates connection policy without changing existing edges, Allow/Restrict alternate demonstrates a declarative peer-port policy while preserving existing edges, and Pause/Resume flow toggles the opt-in data-flow animation. Its 16px dot background, host CSS variables, and `gridSize: 16` use the same model interval. This demonstrates the JSON command/event loop a Razor wrapper would own.
