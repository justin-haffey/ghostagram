# Ghostworx Diagram Studio — Version 2 Design

## Decision

Version 2 replaces the browser-side jsPlumb integration boundary with **Ghostagram**: a dependency-free core, retained-mode diagram renderer that accepts versioned JSON documents and atomic deltas. The .NET domain document remains authoritative; JavaScript is a renderer and interaction-proposal source, never a second mutable diagram model.

The implementation lives in [`src/Ghostagram`](src/Ghostagram). The replacement baseline also contains [`src/Ghostagram.Contracts`](src/Ghostagram.Contracts) and [`src/Ghostagram.Server`](src/Ghostagram.Server): a deliberately clean API foundation that does not restore or depend on the legacy projects removed from this worktree.

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
Ghostagram ── retained DOM/SVG projection and pointer proposals
```

Version 2 keeps `Diagrams.Core` as the semantic core. Ghostagram does not own document persistence, undo history, validation policy, collaboration, or business rules.

## Architecture

```text
Authoritative C# document
        │ immutable mutation / revision N + 1
        ▼
Ghostagram adapter
        │ replace or apply(requestId, baseRevision, revision, ops)
        ▼
Ghostagram state indexes
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
        └───────────────────────────────────────► Ghostagram
```

### Core patterns

| Concern | Pattern | Version 2 rule |
| --- | --- | --- |
| Domain ownership | Aggregate root / single writer | The C# diagram document is the sole durable state. |
| Browser API | Versioned command protocol | `replace` initializes or recovers; `apply` accepts one revisioned operation batch. |
| Rendering | Retained-mode projection | Maps and reverse indexes identify only nodes, groups, and edges affected by a delta; lasso hit-testing is animation-frame coalesced. |
| Interaction | Command proposal | Selection, transient inline labels on nodes, groups, and edges, draggable edge labels, pointer or keyboard movement, atomic selection deletion, connect, detach, resize, rotate, reparent, and waypoint gestures emit requests; the host commits or rejects them. Declarative port policies make connection interception serializable rather than requiring synchronous host callbacks. |
| Interop | Adapter / anti-corruption layer | The wrapper translates existing domain records to Ghostagram JSON, keeping library details out of editor logic. JavaScript-only connector, endpoint, and overlay renderers remain outside the serializable Razor profile. |
| Recovery | Optimistic concurrency | A revision mismatch requires authoritative `replace`, never best-effort replay. |
| Lifecycle | Owned-resource boundary | One instance owns one DOM root, event abort controller, animation frames, and disposal path. |
| Operation dispatch | Command dispatcher | Named operation handlers replace a central switch; unsupported operations fail with a deterministic capability error. |
| Browser composition | Facade, controller, scheduler | The protocol facade owns handles, the interaction controller owns DOM event wiring, and the render scheduler owns request-animation-frame cadence. |
| API writes | Per-document command queue | REST, Razor, layout, and future MCP adapters submit the same typed command to one serialized writer. |
| Delivery | Transactional change log | Persist the new model, idempotency record, and ordered change before SignalR publishes its committed delta. |

## Why this prevents the Version 1 failure modes

1. **No full-canvas redraw during pointer movement.** Preview geometry is local and frame-coalesced; persistent positions are returned to the host as a commit proposal. A Blazor render cannot repeatedly rebuild the live drag surface.
2. **No split-brain graph state.** Ghostagram cannot silently persist a connection or move. Its revisioned operation stream makes C# state, undo history, and storage the authority.
3. **Bounded update work.** Reverse indexes turn a node move into that node plus its incident edges instead of a scan/repaint of every element.
4. **Interop is data-only.** Static ESM calls, JSON envelopes, explicit capabilities, and typed descriptors avoid a graph of long-lived proxied JavaScript objects that is difficult to manage from Razor.
5. **Errors are recoverable and observable.** Each request has an ID, a revision, a structured failure, and a deterministic recovery path; unsupported descriptors fail explicitly rather than appearing to work partially.

## Model contract

Ghostagram accepts a document-shaped model with `nodes`, `ports`, `edgeTypes`, `edges`, `groups`, `selection`, and `viewport`. The browser supports only documented descriptors and reports its support through `capabilities()`.

- A port has a direction, optional connection limit, scope, anchor, and an endpoint descriptor. Connections require matching scopes or the explicit `"*"` wildcard.
- An `edgeType` is a reusable, revisioned JSON descriptor for connector, flowchart `connectorOptions`, style, overlays, animation, and interaction defaults. An edge references source and target ports, may name an edge type, and can override that type locally with a connector profile, routing geometry, overlay descriptors, and optional waypoints.
- Groups form an acyclic hierarchy through `parentGroupId`; nodes use their immediate `groupId`.
- Nodes and groups may carry an optional Iconify name in `icon`; Ghostagram mounts the free Iconify web component in the upper-right decoration slot without making icons part of behavior or persistence policy.
- Every mutation is an operation: `node.*`, `port.*`, `edgeType.*`, `edge.*`, `group.*`, `selection.replace`, or `viewport.*`. Selection may contain node, edge, or group IDs; browser gestures always propose the next selection to the host.

The executable protocol, C# record suggestions, and event mapping are in [`src/Ghostagram/CONTRACT.md`](src/Ghostagram/CONTRACT.md).

## Rendering and interaction model

Ghostagram owns four ordered layers inside one host element:

1. group DOM for bounds, drag surfaces, and resize handles;
2. SVG edge paths;
3. node DOM and explicit ports;
4. foreground SVG edge labels and interaction controls for reconnection and waypoints.

That layer split ensures group styling never captures node pointer input, while draggable edge labels remain readable above node bodies and edge controls stay accurately aligned with computed anchors. Browser static SVG export preserves the same label-over-node paint order. Group collapse uses perimeter proxies for cross-boundary edges, so a collapsed workflow stays connected rather than visually losing its external dependencies. Rotation, route geometry, endpoint descriptors, and SVG export all derive from the same authoritative model state.

## C#/Razor integration shape

The future wrapper imports one ESM module and stores only the returned `instanceId`.

```csharp
var hello = await module.InvokeAsync<GhostagramHello>("create", host, options);
await module.InvokeAsync<GhostagramResult>("replace", hello.InstanceId, initialRequest);
await module.InvokeAsync<GhostagramResult>("apply", hello.InstanceId, deltaRequest);
```

The wrapper should:

1. call `capabilities()` at initialization and reject unsupported document descriptors;
2. relay one event envelope to editor state;
3. validate the proposal with the C# document rules;
4. update undo/history/persistence first;
5. send the resulting atomic delta; and
6. issue `replace` on `REVISION_MISMATCH`.

The wrapper must not invoke a normal component rerender from a preview event.

Undo and redo stay entirely in the wrapper. Ghostagram maps Ctrl/Cmd+Z to `history.undoRequested`, and Ctrl/Cmd+Shift+Z or Ctrl/Cmd+Y to `history.redoRequested`; the wrapper restores its chosen durable snapshot with a monotonic-revision `replace`. This keeps browser shortcuts, server persistence, and a future C# history service on one authoritative timeline.

## Migration path

1. Introduce a `GhostagramAdapter` beside the existing jsPlumb adapter, mapping the same document projection.
2. Render an editor feature flag with Ghostagram and run parity checks on representative saved diagrams.
3. Validate drag, connect, reconnection, groups, viewport, export, and undo/redo through the C# adapter—not only the standalone harness.
4. Promote Ghostagram when the integration suite and performance thresholds pass; then remove jsPlumb-specific interop and CSS in a dedicated cleanup change.

## Phase 2: authoritative API and realtime substrate

`Ghostagram.Contracts` defines the transport-neutral records: `DiagramCommand`, `GhostagramOperation`, `DiagramSnapshot`, `DiagramChange`, and `DiagramCommandResult`. `Ghostagram.Server` exposes the initial vertical slice:

1. `POST /api/documents/{documentId}/commands` submits a revisioned command.
2. `GET /api/documents/{documentId}` returns an authoritative snapshot.
3. `GET /api/documents/{documentId}/changes?afterRevision=N` supports gap recovery.
4. `/hubs/diagrams` delivers `document.changed` only after durable commit.

The local-first store writes the model, change log, and `(actorId, commandId) -> payloadHash/revision` ledger together. A replay with the same hash returns the committed revision; a reused command ID with different content is rejected. The per-document queue is intentionally single-process for this first server. Horizontal ownership, durable outbox delivery retries, retention/compaction, authentication, authorization, and a distributed idempotency store are explicit production-release work rather than hidden assumptions.

Server layout is a deterministic Strategy/Application Command against `baseRevision`. `ghost-layered` version 1.0.0 uses a compound Sugiyama-style pipeline:

1. recursively project nested groups into layout containers;
2. split each container into weakly connected components;
3. condense strongly connected components so cycles remain deterministic;
4. assign weighted ranks to preserve directed flow;
5. run stable, seeded barycentric sweeps while retaining only crossing counts that do not regress;
6. compact variable-size nodes into non-overlapping layers; and
7. pack disconnected components before expanding group-local coordinates back into absolute document positions.

The crossing estimator uses weighted inversion counting rather than pairwise edge comparisons. The implementation is linear or near-linear in the sparse-graph stages, with a bounded `crossingSweeps` option. Model position and the explicit seed provide deterministic tie-breaking, while algorithm version, seed, direction, spacing, limits, and group padding are recorded in change metadata.

`POST /api/documents/{documentId}/layout` supports both preview and commit. A dry-run returns the exact `node.upsert` and `group.upsert` operation batch without persistence. Commit regenerates inside the per-document writer, validates the operations with the normal reducer, persists them atomically, and publishes the resulting change through SignalR. Layout request idempotency is durable and separate from browser request IDs.

The strategy interface permits future ELK or MSAGL adapters, but the default has no native process or external runtime dependency. Its design draws on [ELK Layered](https://eclipse.dev/elk/reference/algorithms/org-eclipse-elk-layered.html), [Graphviz dot](https://graphviz.org/docs/layouts/dot/), [Brandes-Kopf coordinate assignment](https://boriskoepf.de/papers/gd01a.pdf), and the [linear-space Sugiyama implementation](https://jgaa.info/index.php/jgaa/article/download/paper111/2849/2656). The future MCP server remains a thin adapter over this service: narrow read tools use snapshots/changes, while write tools use the same layout request and return preview operations, committed revision, or a structured conflict.

## Phase 3: C#, Blazor, and collaborative MCP

`Ghostagram.Core` is the typed C# authoring surface. Immutable node, port, edge, group, edge-type, viewport, and style records serialize to the existing browser/server vocabulary. `DiagramBuilder` provides fluent construction, while `DiagramOperations` produces transport operations without hand-authored JSON.

`Ghostagram.Blazor` is a Razor Class Library and anti-corruption layer over the ESM runtime. It supports:

1. controlled mode through an authoritative `DiagramDocument`;
2. declarative composition with `GhostGroup`, `GhostNode`, `GhostPort`, `GhostEdge`, and `GhostEdgeType` child controls;
3. imperative replace, apply, fit, inspect, and SVG export methods; and
4. browser proposal events that the host may validate and commit without making JavaScript durable state.

The RCL owns and disposes its JavaScript module and callback references. Browser visual proposals do not become authoritative until a C# or server command accepts them.

`Ghostagram.Server` exposes the official C# MCP SDK over streamable HTTP at `/mcp`. MCP is a thin adapter over `DiagramSessionService`, `DiagramCommandService`, `DiagramLayoutService`, and `IDiagramExporter`; it does not duplicate reducers, layout policy, persistence, or realtime publication. The MVP tools are:

1. `open_session`;
2. `create_diagram`;
3. `get_diagram`;
4. `apply_operations`;
5. `layout_diagram`;
6. `export_svg`; and
7. `close_session`.

A session is an ephemeral collaboration handle over a durable revisioned document. Each participant keeps a stable actor ID, reads the authoritative revision before mutation, supplies a unique command ID, and recovers conflicts by reading changes or the latest snapshot. A committed MCP edit uses the same store commit and `document.changed` SignalR publication as REST and browser-originated edits, so users and multiple agents share one timeline. Closing a session never deletes the diagram.

The Ghostagram Codex plugin mirrors the seven tools with focused skills. Each mutating skill follows observe, plan, commit, and verify; retries reuse an idempotency key only for byte-identical uncertain outcomes, while revision conflicts trigger refresh and re-planning.

## Non-goals and release gates

Ghostagram is not API-compatible with jsPlumb and should not be described as a drop-in replacement. It intentionally favors a small, versioned contract over legacy browser-object APIs.

Before production adoption, the adapter needs end-to-end Blazor coverage, browser automation across supported engines, accessibility review, representative large-document profiling, and an explicit decision for any intentionally unsupported jsPlumb capability. The current capability ledger is [`src/Ghostagram/PARITY.md`](src/Ghostagram/PARITY.md).
