# Ghostworx Diagram Studio — Version 2 Design

## Decision

Version 2 replaces the browser-side jsPlumb integration boundary with **Ghostplumb**: a dependency-free core, retained-mode diagram renderer that accepts versioned JSON documents and atomic deltas. The .NET domain document remains authoritative; JavaScript is a renderer and interaction-proposal source, never a second mutable diagram model.

The implementation lives in [`src/Ghostplumb`](src/Ghostplumb). The replacement baseline also contains [`src/Ghostplumb.Contracts`](src/Ghostplumb.Contracts) and [`src/Ghostplumb.Server`](src/Ghostplumb.Server): a deliberately clean API foundation that does not restore or depend on the legacy projects removed from this worktree.

## Current solution boundary

The solution already has the right high-level separation to preserve:

```text
AppHost / browser shell
        │
Editor.Client ── editor intent, undo/history, persistence orchestration
        │
Diagrams.Core ── document, validation, routing, layout, serialization
        │
Interop adapter ── browser protocol boundary
        │
Ghostplumb ── retained DOM/SVG projection and pointer proposals
```

Version 2 keeps `Diagrams.Core` as the semantic core. Ghostplumb does not own document persistence, undo history, validation policy, collaboration, or business rules.

## Architecture

```text
Authoritative C# document
        │ immutable mutation / revision N + 1
        ▼
Ghostplumb adapter
        │ replace or apply(requestId, baseRevision, revision, ops)
        ▼
Ghostplumb state indexes
 nodes · ports · edgeTypes · edges · groups
 reverse indexes: portsByNode · edgesByPort · nodesByGroup · groupsByGroup
        │ dirty-object set
        ▼
one requestAnimationFrame paint
 group DOM + node DOM + edge SVG + interaction SVG

Browser pointer event
        │ versioned proposal envelope
        ▼
Editor.Client validates and commits model mutation
        │ atomic delta at the next revision
        └───────────────────────────────────────► Ghostplumb
```

### Core patterns

| Concern | Pattern | Version 2 rule |
| --- | --- | --- |
| Domain ownership | Aggregate root / single writer | The C# diagram document is the sole durable state. |
| Browser API | Versioned command protocol | `replace` initializes or recovers; `apply` accepts one revisioned operation batch. |
| Rendering | Retained-mode projection | Maps and reverse indexes identify only nodes, groups, and edges affected by a delta; lasso hit-testing is animation-frame coalesced. |
| Interaction | Command proposal | Selection, transient inline labels on nodes, groups, and edges, draggable edge labels, pointer or keyboard movement, atomic selection deletion, connect, detach, resize, rotate, reparent, and waypoint gestures emit requests; the host commits or rejects them. Declarative port policies make connection interception serializable rather than requiring synchronous host callbacks. |
| Interop | Adapter / anti-corruption layer | The wrapper translates existing domain records to Ghostplumb JSON, keeping library details out of editor logic. JavaScript-only connector, endpoint, and overlay renderers remain outside the serializable Razor profile. |
| Recovery | Optimistic concurrency | A revision mismatch requires authoritative `replace`, never best-effort replay. |
| Lifecycle | Owned-resource boundary | One instance owns one DOM root, event abort controller, animation frames, and disposal path. |
| Operation dispatch | Command dispatcher | Named operation handlers replace a central switch; unsupported operations fail with a deterministic capability error. |
| Browser composition | Facade, controller, scheduler | The protocol facade owns handles, the interaction controller owns DOM event wiring, and the render scheduler owns request-animation-frame cadence. |
| API writes | Per-document command queue | REST, Razor, layout, and future MCP adapters submit the same typed command to one serialized writer. |
| Delivery | Transactional change log | Persist the new model, idempotency record, and ordered change before SignalR publishes its committed delta. |

## Why this prevents the Version 1 failure modes

1. **No full-canvas redraw during pointer movement.** Preview geometry is local and frame-coalesced; persistent positions are returned to the host as a commit proposal. A Blazor render cannot repeatedly rebuild the live drag surface.
2. **No split-brain graph state.** Ghostplumb cannot silently persist a connection or move. Its revisioned operation stream makes C# state, undo history, and storage the authority.
3. **Bounded update work.** Reverse indexes turn a node move into that node plus its incident edges instead of a scan/repaint of every element.
4. **Interop is data-only.** Static ESM calls, JSON envelopes, explicit capabilities, and typed descriptors avoid a graph of long-lived proxied JavaScript objects that is difficult to manage from Razor.
5. **Errors are recoverable and observable.** Each request has an ID, a revision, a structured failure, and a deterministic recovery path; unsupported descriptors fail explicitly rather than appearing to work partially.

## Model contract

Ghostplumb accepts a document-shaped model with `nodes`, `ports`, `edgeTypes`, `edges`, `groups`, `selection`, and `viewport`. The browser supports only documented descriptors and reports its support through `capabilities()`.

- A port has a direction, optional connection limit, scope, anchor, and an endpoint descriptor. Connections require matching scopes or the explicit `"*"` wildcard.
- An `edgeType` is a reusable, revisioned JSON descriptor for connector, flowchart `connectorOptions`, style, overlays, animation, and interaction defaults. An edge references source and target ports, may name an edge type, and can override that type locally with a connector profile, routing geometry, overlay descriptors, and optional waypoints.
- Groups form an acyclic hierarchy through `parentGroupId`; nodes use their immediate `groupId`.
- Nodes and groups may carry an optional Iconify name in `icon`; Ghostplumb mounts the free Iconify web component in the upper-right decoration slot without making icons part of behavior or persistence policy.
- Every mutation is an operation: `node.*`, `port.*`, `edgeType.*`, `edge.*`, `group.*`, `selection.replace`, or `viewport.*`. Selection may contain node, edge, or group IDs; browser gestures always propose the next selection to the host.

The executable protocol, C# record suggestions, and event mapping are in [`src/Ghostplumb/CONTRACT.md`](src/Ghostplumb/CONTRACT.md).

## Rendering and interaction model

Ghostplumb owns four ordered layers inside one host element:

1. group DOM for bounds, drag surfaces, and resize handles;
2. SVG edges and labels;
3. node DOM and explicit ports;
4. SVG interaction controls for reconnection and waypoints.

That layer split ensures group styling never captures node pointer input, while edge controls stay accurately aligned with computed anchors. Group collapse uses perimeter proxies for cross-boundary edges, so a collapsed workflow stays connected rather than visually losing its external dependencies. Rotation, route geometry, endpoint descriptors, and SVG export all derive from the same authoritative model state.

## C#/Razor integration shape

The future wrapper imports one ESM module and stores only the returned `instanceId`.

```csharp
var hello = await module.InvokeAsync<GhostplumbHello>("create", host, options);
await module.InvokeAsync<GhostplumbResult>("replace", hello.InstanceId, initialRequest);
await module.InvokeAsync<GhostplumbResult>("apply", hello.InstanceId, deltaRequest);
```

The wrapper should:

1. call `capabilities()` at initialization and reject unsupported document descriptors;
2. relay one event envelope to editor state;
3. validate the proposal with the C# document rules;
4. update undo/history/persistence first;
5. send the resulting atomic delta; and
6. issue `replace` on `REVISION_MISMATCH`.

The wrapper must not invoke a normal component rerender from a preview event.

Undo and redo stay entirely in the wrapper. Ghostplumb maps Ctrl/Cmd+Z to `history.undoRequested`, and Ctrl/Cmd+Shift+Z or Ctrl/Cmd+Y to `history.redoRequested`; the wrapper restores its chosen durable snapshot with a monotonic-revision `replace`. This keeps browser shortcuts, server persistence, and a future C# history service on one authoritative timeline.

## Migration path

1. Introduce a `GhostplumbAdapter` beside the existing jsPlumb adapter, mapping the same document projection.
2. Render an editor feature flag with Ghostplumb and run parity checks on representative saved diagrams.
3. Validate drag, connect, reconnection, groups, viewport, export, and undo/redo through the C# adapter—not only the standalone harness.
4. Promote Ghostplumb when the integration suite and performance thresholds pass; then remove jsPlumb-specific interop and CSS in a dedicated cleanup change.

## Phase 2: authoritative API and realtime substrate

`Ghostplumb.Contracts` defines the transport-neutral records: `DiagramCommand`, `GhostplumbOperation`, `DiagramSnapshot`, `DiagramChange`, and `DiagramCommandResult`. `Ghostplumb.Server` exposes the initial vertical slice:

1. `POST /api/documents/{documentId}/commands` submits a revisioned command.
2. `GET /api/documents/{documentId}` returns an authoritative snapshot.
3. `GET /api/documents/{documentId}/changes?afterRevision=N` supports gap recovery.
4. `/hubs/diagrams` delivers `document.changed` only after durable commit.

The local-first store writes the model, change log, and `(actorId, commandId) -> payloadHash/revision` ledger together. A replay with the same hash returns the committed revision; a reused command ID with different content is rejected. The per-document queue is intentionally single-process for this first server. Horizontal ownership, durable outbox delivery retries, retention/compaction, authentication, authorization, and a distributed idempotency store are explicit production-release work rather than hidden assumptions.

Server layout will be a deterministic strategy command against `baseRevision`, with algorithm name/version and seed recorded in its change. A dry-run returns an operation batch; commit routes that exact batch through the same command service. The future MCP server remains a thin adapter over this service: narrow read tools use snapshots/changes, while write tools use `DiagramCommand` and return the committed revision or structured conflict.

## Non-goals and release gates

Ghostplumb is not API-compatible with jsPlumb and should not be described as a drop-in replacement. It intentionally favors a small, versioned contract over legacy browser-object APIs.

Before production adoption, the adapter needs end-to-end Blazor coverage, browser automation across supported engines, accessibility review, representative large-document profiling, and an explicit decision for any intentionally unsupported jsPlumb capability. The current capability ledger is [`src/Ghostplumb/PARITY.md`](src/Ghostplumb/PARITY.md).
