import test from "node:test";
import assert from "node:assert/strict";
import { __testing, registerEndpoint, registerOverlay } from "../ghostplumb.js";

const base = {
  documentId: "doc-1",
  nodes: [{ id: "a", x: 0, y: 0, width: 80, height: 40 }, { id: "b", x: 240, y: 80, width: 80, height: 40 }],
  ports: [{ id: "a-out", nodeId: "a", direction: "source" }, { id: "b-in", nodeId: "b", direction: "target" }],
  edges: [{ id: "edge-1", sourcePortId: "a-out", targetPortId: "b-in", connector: "flowchart" }]
};

test("buildState indexes a valid graph", () => {
  const state = __testing.buildState(base);
  assert.equal(state.nodes.size, 2);
  assert.deepEqual([...state.portsByNode.get("a")], ["a-out"]);
  assert.deepEqual([...state.edgesByPort.get("a-out")], ["edge-1"]);
});

test("protocol revisions are non-negative integers and client deltas cannot skip a revision", () => {
  assert.equal(__testing.revision(0, "INVALID", "invalid"), 0);
  assert.equal(__testing.revision(42, "INVALID", "invalid"), 42);
  assert.throws(() => __testing.revision(1.5, "INVALID", "invalid"), /invalid/i);
  assert.throws(() => __testing.revision(-1, "INVALID", "invalid"), /invalid/i);
});

test("flow animation is opt-in, normalized, and validated with the edge model", () => {
  assert.equal(__testing.flowAnimationDescriptor(undefined), null);
  assert.deepEqual(__testing.flowAnimationDescriptor(true), { type: "flow", speed: 1, dash: "8 6", direction: "forward" });
  assert.deepEqual(__testing.flowAnimationDescriptor({ speed: 2, dash: "4 3", direction: "reverse" }), { type: "flow", speed: 2, dash: "4 3", direction: "reverse" });
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], animation: { speed: 0 } }] }), /speed/i);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], animation: { direction: "sideways" } }] }), /direction/i);
});

test("edge style is JSON-safe, validates deterministically, and is exported", () => {
  const style = { stroke: "#7c3aed", strokeWidth: 3, dash: "3 2", opacity: .75, lineCap: "round", lineJoin: "bevel", labelColor: "#312e81" };
  assert.deepEqual(__testing.edgeStyleDescriptor(style), style);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], style: { opacity: 2 } }] }), /opacity/i);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], style: { lineCap: "soft" } }] }), /lineCap/i);
  const svg = __testing.exportSvgDocument(__testing.buildState({ ...base, edges: [{ ...base.edges[0], style, overlays: [{ type: "label", label: "styled" }] }] }));
  assert.match(svg, /stroke="#7c3aed" stroke-width="3" stroke-dasharray="3 2" stroke-linecap="round" stroke-linejoin="bevel" opacity="0.75"/);
  assert.match(svg, /fill="#312e81"/);
});

test("named edge types provide JSON-only reusable defaults with local overrides", () => {
  const state = __testing.buildState({ ...base, edgeTypes: [{ id: "test-workflow-flow", connector: "straight", style: { stroke: "#7c3aed", strokeWidth: 3 }, animation: { speed: 2 }, overlays: [{ type: "arrow" }], detachable: false, reconnectable: false }], edges: [{ id: "edge-1", sourcePortId: "a-out", targetPortId: "b-in", type: "test-workflow-flow", style: { strokeWidth: 5 } }] });
  const resolved = __testing.resolveEdgeDescriptor(state.edges.get("edge-1"), state.edgeTypes);
  assert.equal(resolved.connector, "straight");
  assert.deepEqual(resolved.style, { stroke: "#7c3aed", strokeWidth: 5 });
  assert.equal(resolved.animation.speed, 2);
  assert.equal(resolved.overlays[0].type, "arrow");
  assert.equal(resolved.detachable, false);
  assert.match(__testing.exportSvgDocument(state), /stroke="#7c3aed" stroke-width="5"/);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], type: "missing-type" }] }), /unregistered edge type/i);
});

test("edge type updates are revision-safe deltas that invalidate their consumers", () => {
  const state = __testing.cloneState(__testing.buildState({ ...base, edgeTypes: [{ id: "typed", connector: "straight", style: { stroke: "#0f766e" } }], edges: [{ id: "edge-1", sourcePortId: "a-out", targetPortId: "b-in", type: "typed" }] }));
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "edgeType.upsert", value: { id: "typed", connector: "bezier", style: { stroke: "#7c3aed" } } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.deepEqual([...dirty.edges], ["edge-1"]);
  assert.equal(__testing.resolveEdgeDescriptor(state.edges.get("edge-1"), state.edgeTypes).connector, "bezier");
  assert.equal(__testing.serialiseState(state).edgeTypes[0].id, "typed");
  __testing.applyOperation(state, { type: "edgeType.remove", id: "typed" }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.throws(() => __testing.validateState(state), /unregistered edge type/i);
});

test("a lasso rectangle selects visible intersecting nodes, groups, and edges in model coordinates", () => {
  const state = __testing.buildState(base);
  assert.deepEqual(__testing.rectangleForPoints({ x: 90, y: 60 }, { x: 10, y: 10 }), { x: 10, y: 10, width: 80, height: 50 });
  assert.deepEqual(__testing.nodesInRectangle(state, { x: 10, y: 10, width: 80, height: 50 }), ["a"]);
  assert.deepEqual(__testing.nodesInRectangle(state, { x: 100, y: 10, width: 30, height: 30 }), []);
  assert.equal(__testing.rectanglesIntersect({ x: 0, y: 0, width: 10, height: 10 }, { x: 10, y: 10, width: 2, height: 2 }), true);
  const grouped = __testing.buildState({ ...base, groups: [{ id: "group", x: 0, y: 0, width: 160, height: 100, collapsed: false }] });
  assert.deepEqual(__testing.selectionIdsInRectangle(grouped, { x: 10, y: 10, width: 80, height: 50 }), ["a", "group", "edge-1"]);
  assert.deepEqual(__testing.edgesInRectangle(state, { x: 225, y: 50, width: 20, height: 20 }), ["edge-1"]);
  assert.equal(__testing.polylineIntersectsRectangle([{ x: 0, y: 0 }, { x: 100, y: 100 }], { x: 45, y: 45, width: 10, height: 10 }), true);
});

test("canvas deselection only commits for an actual click", () => {
  assert.equal(__testing.isClickGesture({ x: 10, y: 20 }, { clientX: 12, clientY: 22 }), true);
  assert.equal(__testing.isClickGesture({ x: 10, y: 20 }, { clientX: 16, clientY: 20 }), false);
});

test("history shortcut normalization requests host-owned undo and redo across Windows and macOS", () => {
  assert.equal(__testing.historyDirectionForKey({ ctrlKey: true, metaKey: false, altKey: false, shiftKey: false, key: "z" }), "undo");
  assert.equal(__testing.historyDirectionForKey({ ctrlKey: true, metaKey: false, altKey: false, shiftKey: true, key: "Z" }), "redo");
  assert.equal(__testing.historyDirectionForKey({ ctrlKey: false, metaKey: true, altKey: false, shiftKey: false, key: "y" }), "redo");
  assert.equal(__testing.historyDirectionForKey({ ctrlKey: true, metaKey: false, altKey: true, shiftKey: false, key: "z" }), null);
  assert.equal(__testing.historyDirectionForKey({ ctrlKey: false, metaKey: false, altKey: false, shiftKey: false, key: "z" }), null);
});

test("delete planning cascades selected groups and their incident paths atomically", () => {
  const state = __testing.buildState({
    ...base,
    nodes: [{ ...base.nodes[0], groupId: "inner" }, base.nodes[1]],
    groups: [
      { id: "outer", x: 0, y: 0, width: 200, height: 160, collapsed: false },
      { id: "inner", x: 16, y: 16, width: 120, height: 96, parentGroupId: "outer", collapsed: false }
    ]
  });
  const plan = __testing.deletionPlan(state, new Set(["outer", "b"]));
  assert.deepEqual(plan, { edgeIds: ["edge-1"], nodeIds: ["a", "b"], groupIds: ["inner", "outer"] });
  assert.deepEqual(__testing.deletionPlan(state, new Set(["edge-1"])), { edgeIds: ["edge-1"], nodeIds: [], groupIds: [] });
  assert.deepEqual(__testing.selectableIds(state), ["a", "b", "outer", "inner", "edge-1"]);
  const deleted = __testing.cloneState(state), dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  for (const id of plan.edgeIds) __testing.applyOperation(deleted, { type: "edge.remove", id }, dirty, { minZoom: .2, maxZoom: 3 });
  for (const id of plan.nodeIds) __testing.applyOperation(deleted, { type: "node.remove", id }, dirty, { minZoom: .2, maxZoom: 3 });
  for (const id of plan.groupIds) __testing.applyOperation(deleted, { type: "group.remove", id }, dirty, { minZoom: .2, maxZoom: 3 });
  __testing.applyOperation(deleted, { type: "selection.replace", value: { ids: [] } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.doesNotThrow(() => __testing.validateState(deleted));
  assert.equal(deleted.nodes.size, 0); assert.equal(deleted.groups.size, 0); assert.equal(deleted.edges.size, 0);
});

test("Iconify names decorate nodes and groups while legacy lock state is ignored", () => {
  const model = {
    ...base,
    nodes: [{ ...base.nodes[0], groupId: "inner", icon: "mdi:clipboard-check-outline", locked: true }, { ...base.nodes[1], groupId: "outer" }],
    groups: [
      { id: "outer", x: 0, y: 0, width: 200, height: 160, collapsed: false, icon: "mdi:folder-outline", locked: true },
      { id: "inner", x: 16, y: 16, width: 120, height: 96, parentGroupId: "outer", collapsed: false }
    ]
  };
  const state = __testing.buildState(model);
  assert.equal(state.nodes.get("a").icon, "mdi:clipboard-check-outline");
  assert.equal(state.groups.get("outer").icon, "mdi:folder-outline");
  assert.equal(Object.hasOwn(state.nodes.get("a"), "locked"), false);
  assert.equal(Object.hasOwn(state.groups.get("outer"), "locked"), false);
  assert.equal(__testing.iconifyIconName(null), null);
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], icon: "not-an-icon" }, base.nodes[1]] }), /Iconify/i);
});

test("a delta updates only the affected node and incident edge", () => {
  const state = __testing.cloneState(__testing.buildState(base));
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "node.upsert", value: { ...base.nodes[0], x: 64 } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.equal(state.nodes.get("a").x, 64);
  assert.deepEqual([...dirty.nodes], ["a"]);
  assert.deepEqual([...dirty.edges], ["edge-1"]);
});

test("a drag preview reroutes an incident edge from the preview position", () => {
  const state = __testing.buildState(base);
  const point = __testing.previewPointForPort(state, new Map([["a", { ...base.nodes[0], x: 64, y: 48 }]]), state.ports.get("a-out"), state.nodes.get("b"));
  assert.deepEqual(point, { x: 144, y: 68 });
});

test("connection previews convert browser coordinates through the current viewport", () => {
  const point = __testing.viewportPoint({ clientX: 250, clientY: 130 }, { left: 50, top: 30 }, { x: 100, y: 40, zoom: 2 });
  assert.deepEqual(point, { x: 200, y: 90 });
});

test("drag previews and commits use the same grid-snapped position", () => {
  const position = __testing.dragPosition({ x: 100, y: 100, nodeX: 64, nodeY: 48 }, { clientX: 109, clientY: 117 }, 1, 16);
  assert.deepEqual(position, { x: 80, y: 64 });
});

test("dragging a selected node preserves the selected set's relative positions", () => {
  const nodes = [{ id: "a", x: 64, y: 48 }, { id: "b", x: 160, y: 96 }];
  const positions = __testing.multiDragPositions(nodes, "a", { x: 100, y: 100, nodeX: 64, nodeY: 48 }, { clientX: 125, clientY: 117 }, 1, 16);
  assert.deepEqual(positions, [{ id: "a", x: 96, y: 64 }, { id: "b", x: 192, y: 112 }]);
  assert.deepEqual(__testing.translatePositions([{ id: "group", x: 32, y: 16 }], 32, 16, 16), [{ id: "group", x: 64, y: 32 }]);
});

test("keyboard movement preserves group membership and moves nested selected content once", () => {
  const state = __testing.buildState({
    ...base,
    nodes: [{ ...base.nodes[0], x: 32, y: 40, groupId: "inner" }, base.nodes[1]],
    groups: [
      { id: "outer", x: 0, y: 8, width: 200, height: 160, collapsed: false },
      { id: "inner", x: 16, y: 24, width: 120, height: 96, parentGroupId: "outer", collapsed: false }
    ]
  });
  const positions = __testing.selectedMovePositions(state, new Set(["outer", "inner", "b"]), 16, 8, 8);
  assert.deepEqual(positions.groups, [{ id: "outer", x: 16, y: 16 }, { id: "inner", x: 32, y: 32 }]);
  assert.deepEqual(positions.nodes, [{ id: "b", x: 256, y: 88, groupId: null }, { id: "a", x: 48, y: 48, groupId: "inner" }]);
});

test("fit centers all content and derives a bounded zoom from canvas dimensions", () => {
  const state = __testing.buildState(base);
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "viewport.fit", value: { padding: 40 } }, dirty, { minZoom: .2, maxZoom: 3, viewportSize: { width: 640, height: 360 } });
  assert.equal(state.viewport.zoom, 1.75);
  assert.ok(Math.abs(state.viewport.x + 22.857142857) < .001);
  assert.ok(Math.abs(state.viewport.y + 42.857142857) < .001);
  assert.equal(dirty.viewport, true);
});

test("center uses the visible canvas center rather than placing a node at the origin", () => {
  const state = __testing.buildState(base);
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "viewport.center", value: { ids: ["b"] } }, dirty, { minZoom: .2, maxZoom: 3, viewportSize: { width: 640, height: 360 } });
  assert.deepEqual(state.viewport, { x: -40, y: -80, zoom: 1 });
});

test("invalid references fail atomically before a model is accepted", () => {
  assert.throws(() => __testing.buildState({ ...base, edges: [{ id: "bad", sourcePortId: "missing", targetPortId: "b-in" }] }), /missing/i);
});

test("port direction and capacity rules reject invalid connections", () => {
  const model = structuredClone(base);
  model.ports[0].maxConnections = 1;
  model.edges.push({ id: "edge-2", sourcePortId: "a-out", targetPortId: "b-in" });
  assert.throws(() => __testing.buildState(model), /limit/i);
});

test("disabled ports reject new interactive connections without invalidating existing edges", () => {
  const model = structuredClone(base);
  model.ports[1].enabled = false;
  const state = __testing.buildState(model);
  assert.equal(__testing.canConnect(state, state.ports.get("a-out"), state.ports.get("b-in")), false);
  assert.equal(state.edges.size, 1);
  assert.throws(() => __testing.buildState({ ...base, ports: [{ ...base.ports[0], enabled: "yes" }, base.ports[1] ] }), /enabled/i);
});

test("connection scopes reject incompatible creation, reconnection, and port mutation", () => {
  const model = structuredClone(base);
  model.ports[0].scope = "review";
  model.ports[1].scope = "review";
  const state = __testing.buildState(model), archiveTarget = { ...state.ports.get("b-in"), scope: "archive" };
  assert.equal(__testing.scopesCompatible(state.ports.get("a-out"), archiveTarget), false);
  assert.equal(__testing.canConnect(state, state.ports.get("a-out"), archiveTarget), false);
  assert.equal(__testing.canReconnect(state, state.edges.get("edge-1"), state.ports.get("a-out"), archiveTarget), false);
  assert.throws(() => __testing.buildState({ ...model, ports: [model.ports[0], archiveTarget] }), /scope/i);
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "port.upsert", value: archiveTarget }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.throws(() => __testing.validateState(state), /scope/i);
});

test("declarative port policies reject only new connections and reconnections", () => {
  const model = structuredClone(base);
  model.edges = [];
  model.nodes.push({ id: "c", x: 480, y: 80, width: 80, height: 40 });
  model.ports.push({ id: "c-in", nodeId: "c", direction: "target" });
  model.ports[0].connectionPolicy = { allowNodeIds: ["b"], denyPortIds: ["blocked"] };
  const state = __testing.buildState(model), source = state.ports.get("a-out"), approved = state.ports.get("b-in"), rejected = state.ports.get("c-in");
  assert.equal(__testing.connectionPolicyAllows(source, approved), true);
  assert.equal(__testing.canConnect(state, source, approved), true);
  assert.equal(__testing.canConnect(state, source, rejected), false);
  assert.throws(() => __testing.buildState({ ...model, edges: [{ id: "rejected", sourcePortId: "a-out", targetPortId: "c-in" }] }), /reject/i);
  assert.throws(() => __testing.buildState({ ...model, ports: [{ ...model.ports[0], connectionPolicy: { predicate: "custom" } }, ...model.ports.slice(1)] }), /unsupported/i);
  const existing = __testing.buildState(base), dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(existing, { type: "port.upsert", value: { ...existing.ports.get("a-out"), connectionPolicy: { denyPortIds: ["b-in"] } } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.doesNotThrow(() => __testing.validateState(existing));
  assert.equal(__testing.canReconnect(existing, existing.edges.get("edge-1"), existing.ports.get("a-out"), existing.ports.get("b-in")), false);
  assert.doesNotThrow(() => __testing.applyOperation(existing, { type: "edge.upsert", value: { ...existing.edges.get("edge-1"), style: { stroke: "#0f766e" } } }, dirty, { minZoom: .2, maxZoom: 3 }));
});

test("endpoint descriptors are C#-safe data with validated geometry and colors", () => {
  const model = structuredClone(base);
  model.ports[0].endpoint = { type: "rectangle", size: 16, fill: "#fef3c7", stroke: "#92400e", strokeWidth: 2 };
  const state = __testing.buildState(model);
  assert.deepEqual(__testing.endpointDescriptor(state.ports.get("a-out")), model.ports[0].endpoint);
  assert.match(__testing.sideStyle("right", 16, 2), /right:-10px/);
  assert.throws(() => __testing.buildState({ ...base, ports: [{ ...base.ports[0], endpoint: { type: "dot", size: 0 } }, base.ports[1] ] }), /size/i);
});

test("a JavaScript endpoint renderer extends a JSON descriptor without changing the C# contract", () => {
  registerEndpoint("ring", element => { element.dataset.rendered = "ring"; });
  const model = structuredClone(base);
  model.ports[0].endpoint = { type: "ring", size: 18, stroke: "#0f766e" };
  assert.equal(__testing.endpointDescriptor(__testing.buildState(model).ports.get("a-out")).type, "ring");
  assert.throws(() => __testing.buildState({ ...base, ports: [{ ...base.ports[0], endpoint: "unknown-endpoint" }, base.ports[1] ] }), /unsupported/i);
});

test("a JavaScript overlay renderer extends a JSON descriptor without changing the C# contract", () => {
  registerOverlay("status-dot", () => {});
  const model = structuredClone(base);
  model.edges[0].overlays = [{ type: "status-dot", location: .7, state: "ready" }];
  assert.equal(__testing.buildState(model).edges.get("edge-1").overlays[0].type, "status-dot");
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], overlays: [{ type: "unknown-overlay" }] }] }), /unsupported/i);
});

test("routing supports the four interop-safe connector profiles", () => {
  const a = { x: 0, y: 0 }, b = { x: 100, y: 80 };
  for (const connector of ["straight", "flowchart", "bezier", "state-machine"]) {
    assert.match(__testing.route({ connector }, a, b), /^M /);
  }
});

test("flowchart connector options merge through edge types, preserve stubs, and round right-angle paths", () => {
  const state = __testing.buildState({ ...base, edgeTypes: [{ id: "rounded", connector: "flowchart", connectorOptions: { stub: 48, cornerRadius: 10 } }], edges: [{ ...base.edges[0], type: "rounded", connectorOptions: { cornerRadius: 6 } }] });
  const edge = __testing.resolveEdgeDescriptor(state.edges.get("edge-1"), state.edgeTypes);
  assert.deepEqual(edge.connectorOptions, { stub: 48, cornerRadius: 6 });
  const points = __testing.edgeRoutePoints({ connector: "flowchart", connectorOptions: { stub: 16 } }, { x: 0, y: 0 }, { x: 100, y: 80 }, { sourceSide: "right", targetSide: "right" });
  assert.equal(points.at(-2).x, 116);
  assert.match(__testing.route({ connector: "flowchart", connectorOptions: { cornerRadius: 8 } }, { x: 0, y: 0 }, { x: 100, y: 80 }, { sourceSide: "right", targetSide: "right" }), / Q /);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], connectorOptions: { stub: -1 } }] }), /stub/i);
});

test("flowchart routing leaves a bottom port vertically before travelling to a left target", () => {
  const source = { x: 0, y: 0 }, target = { x: 100, y: 80 }, geometry = { sourceSide: "bottom", targetSide: "left" };
  assert.deepEqual(__testing.edgeRoutePoints({ connector: "flowchart" }, source, target, geometry), [source, { x: 0, y: 80 }, target]);
  assert.equal(__testing.route({ connector: "flowchart" }, source, target, geometry), "M 0 0 L 0 80 L 100 80");
});

test("flowchart routing honors the exit and entry direction of every static port side", () => {
  const source = { x: 0, y: 0 }, target = { x: 120, y: 80 }, vectors = { left: { x: -1, y: 0 }, right: { x: 1, y: 0 }, top: { x: 0, y: -1 }, bottom: { x: 0, y: 1 } };
  for (const [sourceSide, sourceVector] of Object.entries(vectors)) for (const [targetSide, targetVector] of Object.entries(vectors)) {
    const points = __testing.edgeRoutePoints({ connector: "flowchart" }, source, target, { sourceSide, targetSide }), first = points[1], previous = points.at(-2);
    assert.ok((first.x - source.x) * sourceVector.x + (first.y - source.y) * sourceVector.y > 0, `${sourceSide} exits outward`);
    assert.ok((target.x - previous.x) * targetVector.x + (target.y - previous.y) * targetVector.y < 0, `${targetSide} enters inward`);
  }
});

test("programmatic waypoints produce a deterministic edge path", () => {
  const path = __testing.route({ connector: "flowchart", waypoints: [{ x: 40, y: 20 }, { x: 60, y: 80 }] }, { x: 0, y: 0 }, { x: 100, y: 100 });
  assert.equal(path, "M 0 0 L 40 20 L 60 80 L 100 100");
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], waypoints: [{ x: "bad", y: 1 }] }] }), /waypoints/i);
});

test("edge interaction policies default to enabled and validate explicit host rules", () => {
  const defaults = __testing.buildState(base).edges.get("edge-1");
  assert.equal(defaults.detachable, true);
  assert.equal(defaults.reconnectable, true);
  const restricted = __testing.buildState({ ...base, edges: [{ ...base.edges[0], detachable: false, reconnectable: false }] }).edges.get("edge-1");
  assert.equal(restricted.detachable, false);
  assert.equal(restricted.reconnectable, false);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], detachable: "no" }] }), /boolean/i);
});

test("overlay descriptors select distinct marker resources", () => {
  const markerIds = { arrow: "filled", "plain-arrow": "open", diamond: "diamond" };
  assert.equal(__testing.markerFor([{ type: "arrow" }], markerIds), "url(#filled)");
  assert.equal(__testing.markerFor(["plain-arrow"], markerIds), "url(#open)");
  assert.equal(__testing.markerFor([{ type: "diamond" }], markerIds), "url(#diamond)");
  assert.equal(__testing.markerFor([{ type: "plain-arrow", location: 0 }], markerIds, "start"), "url(#open)");
  assert.equal(__testing.markerFor([{ type: "plain-arrow", location: 0 }], markerIds, "end"), "");
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], overlays: [{ type: "arrow", location: .5 }] }] }), /marker/i);
});

test("label overlays use deterministic routed locations and reject invalid descriptor values", () => {
  const placement = __testing.edgeLabelPlacement({ connector: "flowchart", overlays: [{ type: "label", label: "quarter", location: .25, offsetY: 0 }] }, { x: 0, y: 0 }, { x: 100, y: 100 });
  assert.deepEqual(placement, { text: "quarter", x: 50, y: 0, fontSize: 12 });
  assert.equal(__testing.edgeLabelText({ overlays: [{ type: "label", label: "quarter" }] }), "quarter");
  assert.equal(__testing.edgeLabelPlacement({ connector: "flowchart", label: "" }, { x: 0, y: 0 }, { x: 100, y: 100 }), null);
  assert.deepEqual(__testing.edgeLabelPlacement({ connector: "flowchart", label: "moved", labelOffsetX: 8, labelOffsetY: -4 }, { x: 0, y: 0 }, { x: 100, y: 100 }), { text: "moved", x: 58, y: 46, fontSize: 12 });
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], overlays: [{ type: "label", label: "bad", location: 2 }] }] }), /location/i);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], labelOffsetX: "bad" }] }), /label offsets/i);
});

test("a rejected operation batch leaves the authoritative snapshot unchanged", () => {
  const original = __testing.buildState(base);
  const candidate = __testing.cloneState(original);
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(candidate, { type: "node.upsert", value: { ...base.nodes[0], x: 320 } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.throws(() => __testing.applyOperation(candidate, { type: "edge.upsert", value: { id: "bad", sourcePortId: "missing", targetPortId: "b-in" } }, dirty, { minZoom: .2, maxZoom: 3 }), /missing/i);
  assert.equal(original.nodes.get("a").x, 0);
  assert.equal(original.edges.size, 1);
});

test("selection is a validated host operation rather than an unbounded browser value", () => {
  const state = __testing.buildState(base), dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "selection.replace", value: { ids: ["a", "edge-1"] } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.deepEqual([...state.selection], ["a", "edge-1"]);
  assert.equal(dirty.selection, true);
  assert.throws(() => __testing.buildState({ ...base, selection: ["missing"] }), /selection/i);
});

test("unsupported features fail explicitly instead of being silently ignored", () => {
  const state = __testing.cloneState(__testing.buildState(base));
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  assert.throws(() => __testing.applyOperation(state, { type: "connector.custom" }, dirty, { minZoom: .2, maxZoom: 3 }), /not supported/i);
});

test("group collapse invalidates only its members and incident edges", () => {
  const model = structuredClone(base);
  model.nodes[0].groupId = "group-a";
  model.groups = [{ id: "group-a", x: 0, y: 0, width: 160, height: 100, collapsed: false }];
  const state = __testing.buildState(model);
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "group.upsert", value: { ...model.groups[0], collapsed: true } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.deepEqual([...dirty.groups], ["group-a"]);
  assert.deepEqual([...dirty.nodes], ["a"]);
  assert.deepEqual([...dirty.edges], ["edge-1"]);
  assert.equal(dirty.all, false);
});

test("flat group membership is indexed, reparentable, and cleared when a group is removed", () => {
  const model = { ...structuredClone(base), groups: [{ id: "group-a", x: 0, y: 0, width: 180, height: 120, collapsed: false }] };
  const state = __testing.buildState(model);
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "group.assignNode", value: { nodeId: "a", groupId: "group-a" } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.equal(state.nodes.get("a").groupId, "group-a");
  assert.deepEqual([...state.nodesByGroup.get("group-a")], ["a"]);
  assert.deepEqual([...dirty.edges], ["edge-1"]);
  __testing.applyOperation(state, { type: "group.remove", id: "group-a" }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.equal(state.nodes.get("a").groupId, null);
  assert.equal(state.nodesByGroup.has("group-a"), false);
});

test("nested groups support out-of-order model input, reparenting, and cycle rejection", () => {
  const model = {
    ...structuredClone(base),
    groups: [
      { id: "inner", parentGroupId: "outer", x: 40, y: 40, width: 120, height: 80, collapsed: false },
      { id: "outer", x: 0, y: 0, width: 240, height: 180, collapsed: false }
    ]
  };
  model.nodes[0].groupId = "inner";
  const state = __testing.buildState(model), dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  assert.deepEqual([...state.groupsByGroup.get("outer")], ["inner"]);
  assert.deepEqual(__testing.groupsForRender(state).map(group => group.id), ["outer", "inner"]);
  __testing.applyOperation(state, { type: "group.assignGroup", value: { groupId: "inner", parentGroupId: null } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.equal(state.groups.get("inner").parentGroupId, null);
  assert.throws(() => __testing.buildState({ ...model, groups: [{ ...model.groups[0], parentGroupId: "outer" }, { ...model.groups[1], parentGroupId: "inner" }] }), /cycle/i);
});

test("collapsing a parent group invalidates nested groups, members, and their edges", () => {
  const model = {
    ...structuredClone(base),
    groups: [
      { id: "outer", x: 0, y: 0, width: 240, height: 180, collapsed: false },
      { id: "inner", parentGroupId: "outer", x: 40, y: 40, width: 120, height: 80, collapsed: false }
    ]
  };
  model.nodes[0].groupId = "inner";
  const state = __testing.buildState(model), dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "group.upsert", value: { ...model.groups[0], collapsed: true } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.deepEqual(new Set(dirty.groups), new Set(["outer", "inner"]));
  assert.deepEqual([...dirty.nodes], ["a"]);
  assert.deepEqual([...dirty.edges], ["edge-1"]);
});

test("collapsed groups proxy external edges to their visible perimeter and hide internal edges", () => {
  const model = {
    ...structuredClone(base),
    groups: [{ id: "collapsed", x: 0, y: 0, width: 160, height: 100, collapsed: true }]
  };
  model.nodes[0].groupId = "collapsed";
  const state = __testing.buildState(model), external = __testing.edgeGeometry(state, state.edges.get("edge-1"));
  assert.equal(external.hidden, false);
  assert.equal(external.proxied, true);
  assert.equal(external.sourceProxy.id, "collapsed");
  assert.deepEqual(external.sourcePoint, { x: 160, y: 50 });
  assert.equal(external.sourceSide, "right");
  const internalModel = structuredClone(model);
  internalModel.nodes[1].groupId = "collapsed";
  assert.equal(__testing.edgeGeometry(__testing.buildState(internalModel), internalModel.edges[0]).hidden, true);
});

test("collapsed group proxy targets use their nearest side midpoint and directional entry", () => {
  const model = {
    ...structuredClone(base),
    groups: [{ id: "collapsed", x: 200, y: 0, width: 160, height: 100, collapsed: true }],
    nodes: [base.nodes[0], { ...base.nodes[1], groupId: "collapsed" }]
  };
  const geometry = __testing.edgeGeometry(__testing.buildState(model), model.edges[0]);
  assert.equal(geometry.sourceSide, "right");
  assert.deepEqual(geometry.targetPoint, { x: 200, y: 50 });
  assert.equal(geometry.targetSide, "left");
  const points = __testing.edgeRoutePoints({ connector: "flowchart" }, geometry.sourcePoint, geometry.targetPoint, geometry);
  assert.ok(points.at(-2).x < geometry.targetPoint.x, "route approaches the collapsed group from its left side");
});

test("dragging a nested group outside its parent proposes disassociation without allowing descendant cycles", () => {
  const model = {
    ...structuredClone(base),
    groups: [
      { id: "outer", x: 0, y: 0, width: 240, height: 180, collapsed: false },
      { id: "inner", parentGroupId: "outer", x: 40, y: 40, width: 120, height: 80, collapsed: false },
      { id: "grandchild", parentGroupId: "inner", x: 60, y: 60, width: 60, height: 40, collapsed: false }
    ]
  };
  const state = __testing.buildState(model);
  assert.equal(__testing.groupForGroupPosition(state, "inner", { x: 60, y: 60 })?.id, "outer");
  assert.equal(__testing.groupForGroupPosition(state, "inner", { x: 300, y: 300 }), null);
});

test("a node drop targets the group containing its center and excludes collapsed groups", () => {
  const node = { id: "node", width: 100, height: 60 };
  const groups = [{ id: "open", x: 400, y: 200, width: 240, height: 160, collapsed: false }, { id: "inner", x: 440, y: 240, width: 120, height: 100, collapsed: false }, { id: "collapsed", x: 0, y: 0, width: 300, height: 300, collapsed: true }];
  assert.equal(__testing.groupForNodePosition(groups, node, { x: 460, y: 200 })?.id, "open");
  assert.equal(__testing.groupForNodePosition(groups, node, { x: 450, y: 250 })?.id, "inner");
  assert.equal(__testing.groupForNodePosition(groups, node, { x: 20, y: 20 }), null);
});

test("group and node resize honor minimum dimensions, zoom, and resizable policy", () => {
  const grown = __testing.resizeDimensions({ x: 100, y: 100, width: 240, height: 160 }, { clientX: 180, clientY: 140 }, 2, 1, 80, 64);
  assert.deepEqual(grown, { width: 280, height: 180 });
  const clamped = __testing.resizeDimensions({ x: 100, y: 100, width: 240, height: 160 }, { clientX: -500, clientY: -500 }, 1, 1, 80, 64);
  assert.deepEqual(clamped, { width: 80, height: 64 });
  assert.equal(__testing.buildState({ ...base, nodes: [{ ...base.nodes[0], resizable: false }, base.nodes[1]] }).nodes.get("a").resizable, false);
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], resizable: "yes" }, base.nodes[1]] }), /resizable/i);
});

test("reconnecting an existing edge does not consume its own port capacity", () => {
  const model = structuredClone(base);
  model.ports[0].maxConnections = 1;
  model.ports[1].maxConnections = 1;
  const state = __testing.buildState(model), edge = state.edges.get("edge-1");
  assert.equal(__testing.canReconnect(state, edge, state.ports.get("a-out"), state.ports.get("b-in")), true);
});

test("reconnect handles stay exactly on endpoint anchors", () => {
  assert.deepEqual(__testing.reconnectHandlePoint({ x: 0, y: 0 }, { x: 100, y: 0 }, "source"), { x: 0, y: 0 });
  assert.deepEqual(__testing.reconnectHandlePoint({ x: 0, y: 0 }, { x: 100, y: 0 }, "target"), { x: 100, y: 0 });
});

test("perimeter anchors intersect the node boundary in the peer direction", () => {
  const node = { x: 0, y: 0, width: 100, height: 50 }, peer = { x: 200, y: 100, width: 100, height: 50 };
  assert.deepEqual(__testing.anchorPoint(node, "perimeter", peer), { x: 100, y: 50 });
  assert.deepEqual(__testing.anchorPoint(node, { type: "perimeter" }, peer), { x: 100, y: 50 });
});

test("relative anchors place distinct ports on a node's top, left, and bottom edges", () => {
  const node = { x: 100, y: 200, width: 120, height: 60 };
  assert.deepEqual(__testing.anchorPoint(node, [.5, 0]), { x: 160, y: 200 });
  assert.deepEqual(__testing.anchorPoint(node, [0, .75]), { x: 100, y: 245 });
  assert.deepEqual(__testing.anchorPoint(node, [.5, 1]), { x: 160, y: 260 });
  assert.equal(__testing.portAnchorStyle([.5, 0]), "left:50%;top:0%;transform:translate(-50%,-50%);");
  assert.equal(__testing.portAnchorStyle([0, .75]), "left:0%;top:75%;transform:translate(-50%,-50%);");
  assert.equal(__testing.portAnchorStyle([.5, 1]), "left:50%;top:100%;transform:translate(-50%,-50%);");
});

test("rotated nodes rotate their endpoint geometry with the DOM node", () => {
  const node = { x: 0, y: 0, width: 100, height: 50, rotation: 90 };
  const point = __testing.anchorPoint(node, "right");
  assert.ok(Math.abs(point.x - 50) < .000001);
  assert.ok(Math.abs(point.y - 75) < .000001);
  assert.equal(__testing.rotationForPoint(node, { x: 50, y: -75 }), 0);
  assert.equal(__testing.rotationForPoint(node, { x: 150, y: 25 }, 15), 90);
  assert.equal(__testing.rotationForPoint(node, { x: 60, y: 0 }, 15), 15);
  assert.equal(__testing.buildState({ ...base, nodes: [{ ...base.nodes[0], rotatable: false }, base.nodes[1]] }).nodes.get("a").rotatable, false);
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], rotation: "bad" }, base.nodes[1] ] }), /rotation/i);
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], rotatable: "yes" }, base.nodes[1] ] }), /rotatable/i);
});

test("node and group label edit policies are JSON-safe", () => {
  const state = __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], labelEditable: false }, base.nodes[1]], groups: [{ id: "g", x: 0, y: 0, width: 100, height: 80, labelEditable: false }], edges: [{ ...base.edges[0], labelEditable: false }] });
  assert.equal(state.nodes.get("a").labelEditable, false);
  assert.equal(state.groups.get("g").labelEditable, false);
  assert.equal(state.edges.get("edge-1").labelEditable, false);
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], labelEditable: "yes" }, base.nodes[1]] }), /labelEditable/i);
  assert.throws(() => __testing.buildState({ ...base, groups: [{ id: "g", x: 0, y: 0, width: 100, height: 80, labelEditable: "yes" }] }), /labelEditable/i);
  assert.throws(() => __testing.buildState({ ...base, edges: [{ ...base.edges[0], labelEditable: "yes" }] }), /labelEditable/i);
});

test("SVG export is standalone and escapes model text", () => {
  const state = __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], label: "A < B" }, base.nodes[1] ], edges: [{ ...base.edges[0], animation: true, overlays: [{ type: "arrow" }, { type: "plain-arrow", location: 0 }] }] });
  const svg = __testing.exportSvgDocument(state);
  assert.match(svg, /^<svg /);
  assert.match(svg, /A &lt; B/);
  assert.match(svg, /<path /);
  assert.match(svg, /marker-start="url\(#ghostplumb-export-plain-arrow\)"/);
  assert.match(svg, /marker-end="url\(#ghostplumb-export-arrow\)"/);
  assert.doesNotMatch(svg, /animation|stroke-dashoffset/);
});

test("SVG export follows ancestor collapse visibility", () => {
  const model = {
    ...structuredClone(base),
    groups: [
      { id: "outer", x: 0, y: 0, width: 240, height: 180, collapsed: true },
      { id: "inner", parentGroupId: "outer", x: 40, y: 40, width: 120, height: 80, collapsed: false }
    ]
  };
  model.nodes[0] = { ...model.nodes[0], label: "hidden child", groupId: "inner" };
  const svg = __testing.exportSvgDocument(__testing.buildState(model));
  assert.match(svg, /<rect x="0" y="0"/);
  assert.doesNotMatch(svg, /hidden child|marker-end=/);
  assert.match(svg, /d="M 240 90/);
});

test("SVG export includes visible group labels and omits labels hidden by ancestor collapse", () => {
  const model = { ...structuredClone(base), groups: [{ id: "visible", label: "Visible group", x: 0, y: 0, width: 200, height: 120, collapsed: false }, { id: "hidden", label: "Hidden child", parentGroupId: "visible", x: 20, y: 20, width: 100, height: 60, collapsed: false }] };
  const svg = __testing.exportSvgDocument(__testing.buildState(model));
  assert.match(svg, /Visible group/);
  assert.match(svg, /Hidden child/);
  model.groups[0].collapsed = true;
  const collapsed = __testing.exportSvgDocument(__testing.buildState(model));
  assert.match(collapsed, /Visible group/);
  assert.doesNotMatch(collapsed, /Hidden child/);
});
