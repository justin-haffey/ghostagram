# Ghostagram practical browser-UI parity matrix

This is a clean-room comparison against `vendor/jsplumb-community/dist/browser-ui/js/jsplumb.browser-ui.es.js`. It is a capability target, not a promise of source or API compatibility. Ghostagram deliberately has a smaller, versioned API for future C#/Razor interop.

| Browser diagramming family | Ghostagram status | Notes |
| --- | --- | --- |
| Multiple instances, lifecycle, managed elements | Implemented | Each instance owns one root, state maps, listener controller, and frame scheduler. |
| Nodes, explicit ports, connection limits, scopes, connect/detach data model | Implemented | Port direction, maximum connections, and scope compatibility validate atomically; edge removal is supported by a delta. |
| Programmatic updates | Implemented | `replace` and atomic revisioned `apply` operations. |
| Incremental paint and batching | Implemented | Dirty node and incident-edge sets flush once per animation frame; `replace` is recovery/initialization only. |
| Connector profiles | Implemented | Straight, orthogonal flowchart with serializable stub and rounded-corner geometry, Bezier, and state-machine path profiles. |
| Endpoint descriptors | Implemented | Blank, dot, and rectangle descriptors support C#-safe size, fill, stroke, and stroke-width data; blank intentionally renders no handle. |
| Styling and overlays | Partial | Validated JSON stroke/dash/opacity/cap/join/label style, model-hosted inline node/group/edge label editing, optional Iconify item decorations, draggable routed edge-label placement, source/target filled-arrow, open-arrow, diamond markers, and opt-in reduced-motion-aware flow animation are rendered; the SVG export remains intentionally static. Additional marker families remain. |
| Node drag, multi-selection, grid snapping | Implemented | Click and Shift-drag lasso selection of nodes, groups, and visible connection paths; model-owned node resize handles; and browser move proposals. The host commits all durable selection, position, and dimension changes through `apply`. |
| Connection drag/reconnection/detachment interaction | Implemented | Source-to-target creation, selected-edge endpoint reconnection, and policy-controlled detachment show live previews and emit requests; the host performs all edge mutation. |
| Groups | Implemented | Nested acyclic membership, reparenting-by-drop, group/descendant dragging, resize handles, bounds, ancestor-aware collapse, and perimeter proxies for cross-boundary edges are indexed and supported. |
| Zoom, pan, fit, center | Implemented | Pointer panning, wheel zoom, and canvas-size-aware fit/center are covered by browser testing. |
| Anchors | Implemented | Fixed side, deterministic `auto`/`continuous`, relative, and rectangle-perimeter anchors are supported. |
| Eventing and interception | Implemented | One correlated versioned event envelope supports .NET callbacks. Normalized node/port/edge/group click and context events avoid DOM-specific handlers; port `connectionPolicy` supplies serializable allow/deny rules for peer port/node IDs, replacing synchronous browser callbacks for new connections and reconnections. |
| Custom connector/endpoint/overlay factories | Implemented | JavaScript-only connector, endpoint, and SVG-overlay registries are available; named JSON-only edge types provide C#/Razor-safe reusable connector/style/overlay/animation defaults. Live custom overlays are deliberately omitted from model-derived SVG export. |
| Selector sources/targets, lists, rotation, editable waypoints | Partial | Node rotation is model-owned and supports a selected-node live rotation handle snapped to 15-degree increments, plus programmatic rotation and selected-edge browser waypoint editing; selector/list remain explicitly unsupported. |
| Model-derived SVG export | Implemented | `exportSvg` produces a standalone, escaped SVG from authoritative model state and collapsed-group visibility without inspecting or serializing live DOM. |
| Test support, browser parity and benchmark suite | In progress | Core Node contract tests and the deterministic 1,000-node/2,000-edge delta benchmark exist. Full browser automation and production-parity certification remain required. |

## Release gate

Ghostagram must not be called feature-complete or a drop-in jsPlumb replacement until every `Partial` family has either reached `Implemented` with browser tests or has been explicitly removed from the integration compatibility profile. The C#/Razor wrapper must call `capabilities()` during initialization and reject unsupported document descriptors before rendering.
