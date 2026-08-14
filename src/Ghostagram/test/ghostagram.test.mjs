import test from "node:test";
import assert from "node:assert/strict";
import { __testing, registerEndpoint, registerOverlay } from "../ghostagram.js";

const base = {
  documentId: "doc-1",
  nodes: [{ id: "a", x: 0, y: 0, width: 80, height: 40 }, { id: "b", x: 240, y: 80, width: 80, height: 40 }],
  ports: [{ id: "a-out", nodeId: "a", direction: "source" }, { id: "b-in", nodeId: "b", direction: "target" }],
  edges: [{ id: "edge-1", sourcePortId: "a-out", targetPortId: "b-in", connector: "flowchart" }]
};

test("Blazor interop preserves committed event order without preview backlog", async () => {
  let releaseFirst;
  const firstDelivery = new Promise(resolve => { releaseFirst = resolve; });
  const delivered = [];
  const queue = new __testing.InteropEventQueue(async event => {
    delivered.push(event.eventId);
    if (event.eventId === 1) await firstDelivery;
  });

  queue.enqueue({ eventId: 1, type: "selection.changed" });
  queue.enqueue({ eventId: 2, type: "element.clicked" });
  queue.enqueue({ eventId: 3, type: "node.move.preview" }, true);
  queue.enqueue({ eventId: 4, type: "node.move.preview" }, true);
  queue.enqueue({ eventId: 5, type: "node.move.commit" });

  assert.deepEqual(delivered, [1]);
  releaseFirst();
  await queue.whenIdle();
  assert.deepEqual(delivered, [1, 2, 5]);
});

test("buildState indexes a valid graph", () => {
  const state = __testing.buildState(base);
  assert.equal(state.nodes.size, 2);
  assert.deepEqual([...state.portsByNode.get("a")], ["a-out"]);
  assert.deepEqual([...state.edgesByPort.get("a-out")], ["edge-1"]);
});

test("nullable optional .NET fields behave as omitted descriptors", () => {
  const state = __testing.buildState({
    ...base,
    ports: base.ports.map(port => ({ ...port, endpoint: null, connectionPolicy: null })),
    edges: [{
      ...base.edges[0],
      type: null,
      connector: null,
      overlays: [
        { type: "arrow", label: null, location: null, offsetX: null, offsetY: null, fontSize: null },
        { type: "label", label: "C# label", location: null, offsetX: null, offsetY: null, fontSize: null }
      ],
      style: null,
      label: null,
      labelOffsetX: null,
      labelOffsetY: null
    }]
  });
  const edge = __testing.resolveEdgeDescriptor(state.edges.get("edge-1"), state.edgeTypes);
  assert.equal(edge.connector, "flowchart");
  assert.equal(edge.overlays.length, 2);
  assert.deepEqual(edge.style, {});
});

test("dynamic node properties retain explicit null and provide stable property-port anchors", () => {
  const state = __testing.buildState({
    ...base,
    nodes: [{ ...base.nodes[0], height: 120, properties: [
      { id: "customer", name: "customer", type: "string", value: null, mode: "edit", label: "Customer" },
      { id: "priority", name: "priority", type: "enum", value: "high", options: ["low", "high"], mode: "display" }
    ] }, base.nodes[1]],
    ports: [{ id: "customer-in", nodeId: "a", direction: "target", propertyId: "customer" }, { id: "customer-out", nodeId: "a", direction: "source", propertyId: "customer" }, { id: "priority-out", nodeId: "a", direction: "source", propertyId: "priority" }, base.ports[1]],
    edges: [{ id: "edge-1", sourcePortId: "priority-out", targetPortId: "b-in", connector: "straight" }]
  });
  assert.equal(state.nodes.get("a").properties[0].value, null);
  assert.deepEqual(__testing.serialiseState(state).nodes[0].properties[0].value, null);
  const customerInputAnchor = { type: "property", side: "left", offsetY: 40 };
  const customerOutputAnchor = { type: "property", side: "right", offsetY: 40 };
  assert.deepEqual(__testing.resolvePortAnchor(state, state.ports.get("customer-in")), customerInputAnchor);
  assert.deepEqual(__testing.resolvePortAnchor(state, state.ports.get("customer-out")), customerOutputAnchor);
  assert.deepEqual(__testing.resolvePortAnchor(state, state.ports.get("priority-out")), { type: "property", side: "right", offsetY: 61 });
  assert.equal(__testing.anchorPoint(state.nodes.get("a"), customerInputAnchor).y, __testing.anchorPoint(state.nodes.get("a"), customerOutputAnchor).y);
  assert.match(__testing.portAnchorStyle(customerInputAnchor, 10, 2), /left:-7px;top:40px/);
  const geometry = __testing.edgeGeometry(state, state.edges.get("edge-1"));
  assert.deepEqual(geometry.sourcePoint, { x: 80, y: 61 });
  assert.equal(geometry.sourceSide, "right");
  const hidden = __testing.buildState({
    ...base,
    nodes: [{ ...base.nodes[0], height: 120, properties: [
      { id: "internal", name: "Internal", type: "string", mode: "hidden", value: "secret" },
      { id: "visible", name: "Visible", type: "string", value: "shown", connectable: true }
    ] }, base.nodes[1]],
    ports: [{ id: "visible-both", nodeId: "a", direction: "both", propertyId: "visible" }, base.ports[1]],
    edges: []
  });
  assert.deepEqual(__testing.resolvePortAnchor(hidden, hidden.ports.get("visible-both")), { type: "property", side: "right", offsetY: 40 });
});

test("property ports use field sockets while ordinary node connectors remain circular", () => {
  assert.deepEqual(__testing.portVisualDescriptor({ id: "value", propertyId: "value" }, { type: "dot" }), {
    kind: "property",
    borderRadius: "3px"
  });
  assert.deepEqual(__testing.portVisualDescriptor({ id: "flow", propertyId: null }, { type: "dot" }), {
    kind: "node",
    borderRadius: "50%"
  });
});

test("simple nodes remain lightweight and omit progressive state", () => {
  const state = __testing.buildState(base), node = state.nodes.get("a"), layout = __testing.nodeLayoutProjection(node);
  assert.equal(Object.hasOwn(node, "sections"), false);
  assert.equal(Object.hasOwn(node, "presentation"), false);
  assert.equal(layout.progressive, false);
  assert.equal(layout.rows.length, 0);
  assert.equal(layout.minimumHeight, 30);
});

test("nested progressive sections project deterministic rows and collapsed port proxies", () => {
  const state = __testing.buildState({
    ...base,
    nodes: [{ ...base.nodes[0], width: 300, height: 180, sections: [
      { id: "details", title: "Details", order: 0 },
      { id: "advanced", title: "Advanced", parentSectionId: "details", order: 1 }
    ], presentation: { displayMode: "expanded", expandedHeight: 180, collapsedSectionIds: ["advanced"] }, properties: [
      { id: "summary", name: "Summary", type: "string", mode: "displayAndEdit", sectionId: "details", editor: { kind: "multiline", placeholder: "Describe it" } },
      { id: "threshold", name: "Threshold", type: "decimal", mode: "displayAndEdit", sectionId: "advanced", editor: { kind: "range", minimum: 0, maximum: 10, step: .5 } }
    ] }, base.nodes[1]],
    ports: [{ id: "threshold-out", nodeId: "a", direction: "source", propertyId: "threshold" }, base.ports[1]],
    edges: [{ id: "edge-1", sourcePortId: "threshold-out", targetPortId: "b-in", connector: "straight" }]
  });
  const node = state.nodes.get("a"), layout = __testing.nodeLayoutProjection(node);
  assert.equal(layout.progressive, true);
  assert.deepEqual(layout.rootSections.map(section => section.section.id), ["details"]);
  assert.deepEqual(layout.rows.map(row => row.property.id), ["summary"]);
  assert.equal(layout.rootSections[0].children[0].collapsed, true);
  assert.deepEqual(__testing.resolvePortAnchor(state, state.ports.get("threshold-out")), { type: "property", side: "right", offsetY: 113, proxied: true, proxy: "advanced" });
  assert.deepEqual(__testing.edgeGeometry(state, state.edges.get("edge-1")).sourcePoint, { x: 300, y: 113 });

  const collapsed = __testing.buildState({ ...__testing.serialiseState(state), nodes: __testing.serialiseState(state).nodes.map(candidate => candidate.id === "a" ? { ...candidate, height: 30, presentation: { ...candidate.presentation, displayMode: "collapsed" } } : candidate) });
  assert.deepEqual(__testing.resolvePortAnchor(collapsed, collapsed.ports.get("threshold-out")), { type: "property", side: "right", offsetY: 15, proxied: true, proxy: "node" });
});

test("progressive contracts validate editor kinds, section references, cycles, and depth", () => {
  assert.equal(__testing.normalisePropertyEditor({ kind: "color" }, "accent").kind, "color");
  assert.equal(__testing.propertyEditorKind({ type: "string", editor: { kind: "color" } }), "color");
  assert.throws(() => __testing.normalisePropertyEditor({ kind: "dial" }, "amount"), /unsupported editor kind/i);
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], properties: [{ id: "x", sectionId: "missing" }] }, base.nodes[1]] }), /missing section/i);
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], sections: [{ id: "one", parentSectionId: "two" }, { id: "two", parentSectionId: "one" }] }, base.nodes[1]] }), /cycle/i);
  const deep = Array.from({ length: 10 }, (_, index) => ({ id: `s${index}`, parentSectionId: index ? `s${index - 1}` : null }));
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], sections: deep }, base.nodes[1]] }), /8 levels/i);
});

test("presentation controls produce host-authoritative node and section proposals", () => {
  const node = { id: "progressive", x: 0, y: 0, width: 240, height: 140, sections: [{ id: "details", title: "Details", parentSectionId: null, order: 0, collapsible: true }], properties: [{ id: "name", name: "Name", type: "string", mode: "display", order: 0, options: null, sectionId: "details" }], presentation: { displayMode: "expanded", expandedHeight: 140, collapsedSectionIds: [] } };
  const compact = __testing.nextNodePresentationRequest(node);
  assert.equal(compact.payload.presentation.displayMode, "compact");
  assert.equal(compact.payload.nodeId, "progressive");
  assert.ok(compact.payload.height < node.height);
  const collapsed = __testing.nextNodePresentationRequest({ ...node, height: compact.payload.height, presentation: compact.payload.presentation });
  assert.equal(collapsed.payload.presentation.displayMode, "collapsed");
  assert.equal(collapsed.payload.height, 32);
  assert.equal(compact.payload.height % 16, 0);
  assert.equal(collapsed.payload.presentation.expandedHeight % 16, 0);
  const section = __testing.nextSectionPresentationRequest(node, "details");
  assert.deepEqual(section.payload.presentation.collapsedSectionIds, ["details"]);
  assert.equal(section.payload.sectionId, "details");
  assert.equal(section.payload.collapsed, true);

  const compactNode = { ...node, height: compact.payload.height, presentation: compact.payload.presentation };
  const compactCollapsedSection = __testing.nextSectionPresentationRequest(compactNode, "details");
  const collapsedCompactMinimum = __testing.nodeLayoutProjection({ ...compactNode, presentation: compactCollapsedSection.payload.presentation }).minimumHeight;
  assert.equal(compactCollapsedSection.payload.height, Math.ceil(collapsedCompactMinimum / 16) * 16);
  const compactExpandedSection = __testing.nextSectionPresentationRequest({ ...compactNode, height: compactCollapsedSection.payload.height, presentation: compactCollapsedSection.payload.presentation }, "details");
  const expandedCompactMinimum = __testing.nodeLayoutProjection({ ...compactNode, presentation: compactExpandedSection.payload.presentation }).minimumHeight;
  assert.equal(compactExpandedSection.payload.height, Math.ceil(expandedCompactMinimum / 16) * 16);
  assert.ok(compactExpandedSection.payload.height < compactExpandedSection.payload.presentation.expandedHeight);
});

test("fully collapsed nodes hide property ports while collapsed sections proxy them", () => {
  const model = (displayMode, collapsedSectionIds) => ({
    ...base,
    nodes: [{ ...base.nodes[0], width: 240, height: displayMode === "collapsed" ? 30 : 120, sections: [{ id: "details", title: "Details" }], presentation: { displayMode, expandedHeight: 120, collapsedSectionIds }, properties: [
      { id: "disabledOut", name: "Disabled out", sectionId: "details" },
      { id: "enabledOut", name: "Enabled out", sectionId: "details" },
      { id: "firstIn", name: "First in", sectionId: "details" },
      { id: "secondIn", name: "Second in", sectionId: "details" }
    ] }, base.nodes[1]],
    ports: [
      { id: "disabled-out", nodeId: "a", direction: "source", propertyId: "disabledOut", label: "Disabled out", enabled: false, order: 0 },
      { id: "enabled-out", nodeId: "a", direction: "source", propertyId: "enabledOut", label: "Enabled out", order: 1 },
      { id: "first-in", nodeId: "a", direction: "target", propertyId: "firstIn", label: "First in", order: 2 },
      { id: "second-in", nodeId: "a", direction: "target", propertyId: "secondIn", label: "Second in", order: 3 },
      { id: "node-in", nodeId: "a", direction: "target", label: "Node in", order: 4 },
      { id: "node-out", nodeId: "a", direction: "source", label: "Node out", order: 5 }
    ],
    edges: []
  });
  const collapsed = __testing.buildState(model("collapsed", [])), collapsedPlan = __testing.portRenderPlan(collapsed, "a");
  assert.deepEqual(collapsedPlan.map(entry => entry.port.id).sort(), ["node-in", "node-out"]);
  assert.ok(collapsedPlan.every(entry => !entry.port.propertyId));
  assert.equal(__testing.resolvePortAnchor(collapsed, collapsed.ports.get("node-in")), "left");
  assert.equal(__testing.resolvePortAnchor(collapsed, collapsed.ports.get("node-out")), "right");

  const sectionCollapsed = __testing.buildState(model("expanded", ["details"])), sectionPlan = __testing.portRenderPlan(sectionCollapsed, "a");
  assert.equal(sectionPlan.length, 4);
  const source = sectionPlan.find(entry => entry.anchor.proxy === "details" && entry.anchor.side === "right"), target = sectionPlan.find(entry => entry.anchor.proxy === "details" && entry.anchor.side === "left");
  assert.equal(source.port.id, "enabled-out");
  assert.deepEqual(source.ports.map(port => port.id), ["disabled-out", "enabled-out"]);
  assert.deepEqual(target.ports.map(port => port.id), ["first-in", "second-in"]);
  assert.equal(__testing.proxyPortDescriptor(source.ports).enabled, true);
  assert.match(__testing.proxyPortDescriptor(source.ports).label, /Collapsed connections \(2\).*Disabled out.*Enabled out/);
  assert.deepEqual(__testing.resolvePortAnchor(sectionCollapsed, sectionCollapsed.ports.get("disabled-out")), __testing.resolvePortAnchor(sectionCollapsed, sectionCollapsed.ports.get("enabled-out")));
  const expanded = __testing.buildState(model("expanded", []));
  assert.equal(__testing.portRenderPlan(expanded, "a").length, 6);
});

test("ordered side ports on complex nodes retain pixel positions when node height changes", () => {
  const model = height => ({
    ...base,
    nodes: [{ ...base.nodes[0], height, properties: [{ id: "condition", type: "string", mode: "edit", value: "ready" }] }, base.nodes[1]],
    ports: [
      { id: "a-in", nodeId: "a", direction: "target", order: 0 },
      { id: "a-true", nodeId: "a", direction: "source", order: 1 },
      { id: "a-false", nodeId: "a", direction: "source", order: 2 },
      base.ports[1]
    ],
    edges: []
  });
  const before = __testing.buildState(model(136)), after = __testing.buildState(model(260));
  const expected = [
    { id: "a-in", anchor: { type: "ordered", side: "left", offsetY: 40 } },
    { id: "a-true", anchor: { type: "ordered", side: "right", offsetY: 61 } },
    { id: "a-false", anchor: { type: "ordered", side: "right", offsetY: 82 } }
  ];
  for (const item of expected) {
    assert.deepEqual(__testing.resolvePortAnchor(before, before.ports.get(item.id)), item.anchor);
    assert.deepEqual(__testing.resolvePortAnchor(after, after.ports.get(item.id)), item.anchor);
  }
  assert.equal(__testing.anchorPoint(before.nodes.get("a"), expected[1].anchor).y, __testing.anchorPoint(after.nodes.get("a"), expected[1].anchor).y);
  assert.equal(__testing.nodeContentMinimumHeight(before, before.nodes.get("a")), 90);
});

test("interactive property controls do not trigger node selection or dragging", () => {
  const interactive = { closest: selector => selector.includes("select") ? {} : null };
  const passive = { closest: () => null };
  assert.equal(__testing.isInteractiveNodeTarget(interactive), true);
  assert.equal(__testing.isInteractiveNodeTarget(passive), false);
  assert.equal(__testing.isInteractiveNodeTarget(null), false);
});

test("property editors retain a neutral control palette instead of inheriting node text color", () => {
  const style = __testing.propertyEditorStyle();
  assert.match(style, /color:#0f172a/);
  assert.match(style, /border:1px solid #94a3b8/);
  assert.match(style, /background:rgba\(255,255,255,\.92\)/);
  assert.doesNotMatch(style, /color:inherit|currentColor/);
});

test("node labels fill the header so inherited text alignment is visible", () => {
  const style = __testing.nodeLabelStyle();
  assert.match(style, /flex:1 1 0/);
  assert.match(style, /min-width:0/);
});

test("dynamic property validation permits extension types and rejects invalid references", () => {
  const custom = __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], properties: [{ id: "attachment", type: "sample/file", value: "a.txt" }] }, base.nodes[1]] });
  assert.equal(custom.nodes.get("a").properties[0].type, "sample/file");
  assert.throws(() => __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], properties: [{ id: "bad", type: "" }] }, base.nodes[1]] }), /type identifier/i);
  assert.throws(() => __testing.buildState({ ...base, ports: [{ ...base.ports[0], propertyId: "missing" }, base.ports[1]] }), /missing node property/i);
  assert.equal(__testing.propertyDisplayValue({ type: "boolean", value: false }), "No");
  assert.equal(__testing.propertyDisplayValue({ type: "json", value: { a: 1 } }), '{"a":1}');
  assert.match(__testing.dateTimeInputValue("2026-08-08T12:00:00Z"), /^2026-08-08T\d{2}:00$/);
  assert.throws(() => __testing.propertyInputValue({ value: "{" }, { type: "json", value: null }), /valid JSON/i);
});

test("property commit signatures deduplicate change blur and Enter while retaining null", () => {
  const initial = __testing.propertyValueSignature(null);
  const first = __testing.propertyCommitDecision(initial, "Ada");
  assert.equal(first.commit, true);
  assert.equal(__testing.propertyCommitDecision(first.signature, "Ada").commit, false);
  assert.equal(__testing.propertyCommitDecision(first.signature, null).commit, true);
  assert.equal(__testing.propertyCommitDecision(__testing.propertyValueSignature({ approved: false }), { approved: false }).commit, false);
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

test("an explicit false delta stops an already animated edge", () => {
  const state = __testing.buildState({ ...base, edges: [{ ...base.edges[0], animation: true }] });
  const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
  __testing.applyOperation(state, { type: "edge.upsert", value: { ...state.edges.get("edge-1"), animation: false } }, dirty, { minZoom: .2, maxZoom: 3 });
  assert.equal(state.edges.get("edge-1").animation, false);
  assert.equal(__testing.flowAnimationDescriptor(state.edges.get("edge-1").animation), null);
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
  const edge = state.edges.get("edge-1"), geometry = __testing.edgeGeometry(state, edge), route = __testing.edgeRoutePoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, __testing.buildRoutingContext(state)), probe = __testing.pointAlongPolyline(route, .5);
  assert.deepEqual(__testing.edgesInRectangle(state, { x: probe.x - 2, y: probe.y - 2, width: 4, height: 4 }), ["edge-1"]);
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

test("canvas geometry owns center projection and bounded external hit testing", () => {
  const bounds = { left: 10, top: 20, right: 410, bottom: 320, width: 400, height: 300 };
  const viewport = { x: 100, y: 50, zoom: 2 };
  assert.deepEqual(__testing.canvasCenterPoint(bounds, viewport), { x: 200, y: 125 });
  assert.deepEqual(__testing.canvasHitDescriptor({ clientX: 210, clientY: 170 }, bounds, viewport), { inside: true, x: 200, y: 125 });
  assert.deepEqual(__testing.canvasHitDescriptor({ clientX: 9, clientY: 170 }, bounds, viewport), { inside: false, x: 99.5, y: 125 });
});

test("drag previews and commits use the same grid-snapped position", () => {
  const position = __testing.dragPosition({ x: 100, y: 100, nodeX: 64, nodeY: 48 }, { clientX: 109, clientY: 117 }, 1, 16);
  assert.deepEqual(position, { x: 80, y: 64 });
});

test("dot-grid CSS projection and drag/resize math share the same model interval", () => {
  const grid = __testing.gridCssProjection({ x: 5, y: 3, zoom: 2 }, 16);
  assert.deepEqual(grid, { modelSize: 16, screenSize: 32, phaseX: 6, phaseY: 10 });
  assert.equal((grid.phaseX + grid.screenSize / 2) % grid.screenSize, 22);
  assert.equal((grid.phaseY + grid.screenSize / 2) % grid.screenSize, 26);
  assert.equal(__testing.snap(25, 16), 32);
  assert.deepEqual(__testing.resizeDimensions({ x: 100, y: 100, width: 80, height: 40 }, { clientX: 109, clientY: 107 }, 1, 16, 32, 32), { width: 96, height: 48 });
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

test("group drag commits retain direct membership for every moved node", () => {
  const payload = __testing.groupMovePayload(
    "outer",
    null,
    { x: 64, y: 32 },
    32,
    16,
    [{ id: "inner", x: 96, y: 64 }],
    [{ id: "inside", x: 120, y: 80, groupId: "inner" }, { id: "outside", x: 360, y: 80, groupId: null }]
  );

  assert.deepEqual(payload.groups, [{ id: "inner", x: 128, y: 80 }]);
  assert.deepEqual(payload.nodes, [
    { id: "inside", x: 152, y: 96, groupId: "inner" },
    { id: "outside", x: 392, y: 96, groupId: null }
  ]);
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

test("routing supports the five interop-safe connector profiles", () => {
  const a = { x: 0, y: 0 }, b = { x: 100, y: 80 };
  for (const connector of ["straight", "flowchart", "bezier", "curved", "state-machine"]) {
    assert.match(__testing.route({ connector }, a, b), /^M /);
  }
});

test("tall backward Bezier curves keep their horizontal port tangents longer", () => {
  const source = { x: 256, y: 240 }, target = { x: 112, y: 605 };
  assert.equal(__testing.bezierControlDistance(source, target), 102.2);
  assert.equal(__testing.bezierPath(source, target), "M 256 240 C 358.2 240, 9.8 605, 112 605");
  assert.equal(__testing.bezierControlDistance({ x: 0, y: 0 }, { x: 100, y: 80 }), 48);
});

test("Curved connectors render a deterministic arch rather than a Bezier tangent curve", () => {
  assert.equal(__testing.curvedPath({ x: 0, y: 0 }, { x: 100, y: 0 }), "M 0 0 Q 50 -32 100 0");
  assert.equal(__testing.curvedPath({ x: 0, y: 0 }, { x: 0, y: 100 }), "M 0 0 Q -32 50 0 100");
  assert.match(__testing.route({ connector: "curved" }, { x: 0, y: 0 }, { x: 100, y: 0 }), / Q /);
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

test("Square routing chooses a direct orthogonal path through group containers instead of routing around them", () => {
  const state = __testing.buildState({
    documentId: "group-transparent-routing",
    groups: [
      { id: "surface", x: 0, y: 0, width: 360, height: 300, collapsed: false },
      { id: "core", x: 392, y: 0, width: 160, height: 300, collapsed: false }
    ],
    nodes: [
      { id: "source", groupId: "surface", x: 24, y: 180, width: 128, height: 48 },
      { id: "blocker", groupId: "surface", x: 214, y: 70, width: 130, height: 70 },
      { id: "target", groupId: "core", x: 408, y: 88, width: 128, height: 48 }
    ],
    ports: [
      { id: "source-out", nodeId: "source", direction: "source", anchor: "right" },
      { id: "target-in", nodeId: "target", direction: "target", anchor: "left" }
    ],
    edges: [{ id: "direct", sourcePortId: "source-out", targetPortId: "target-in", connector: "flowchart" }]
  });
  const edge = state.edges.get("direct"), geometry = __testing.edgeGeometry(state, edge);
  const points = __testing.edgeRoutePoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, __testing.buildRoutingContext(state));
  assert.deepEqual(points, [
    { x: 152, y: 204 }, { x: 376, y: 204 }, { x: 376, y: 112 }, { x: 408, y: 112 }
  ]);
  assert.ok(Math.max(...points.map(point => point.y)) < 300, "a group boundary must not push the route below the group");
});

test("obstacle-aware flowchart routing sends a backward loop through the clearest exterior corridor", () => {
  const outer = { id: "quality", x: 108, y: 245, width: 1064, height: 493, collapsed: false };
  const model = {
    documentId: "routing-loop",
    groups: [outer, { id: "review-loop", parentGroupId: "quality", x: 171, y: 366, width: 390, height: 309, collapsed: false }],
    nodes: [
      { id: "review", groupId: "review-loop", x: 236, y: 486, width: 262, height: 124 },
      { id: "approve", groupId: "quality", x: 875, y: 342, width: 265, height: 123 },
      { id: "retry", x: 1356, y: 598, width: 256, height: 97 }
    ],
    ports: [
      { id: "review-in", nodeId: "review", direction: "target", anchor: "left" },
      { id: "review-out", nodeId: "review", direction: "source", anchor: "right" },
      { id: "retry-in", nodeId: "retry", direction: "target", anchor: "left" },
      { id: "retry-out", nodeId: "retry", direction: "source", anchor: "right" }
    ],
    edges: [
      { id: "forward", sourcePortId: "review-out", targetPortId: "retry-in", connector: "flowchart" },
      { id: "return", sourcePortId: "retry-out", targetPortId: "review-in", connector: "flowchart" }
    ]
  };
  const state = __testing.buildState(model), context = __testing.buildRoutingContext(state), edge = state.edges.get("return"), geometry = __testing.edgeGeometry(state, edge);
  const first = __testing.edgeRoutePoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, context);
  const second = __testing.edgeRoutePoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, __testing.buildRoutingContext(state));
  assert.deepEqual(first, second, "routing is independent of render order and prior calls");
  assert.ok(Math.max(...first.map(point => point.y)) < outer.y + outer.height, "group boundaries must not push the return loop outside its containing workflow region");
  assert.ok(first[1].x > first[0].x, "the source still exits its right port outward");
  assert.ok(first.at(-2).x < first.at(-1).x, "the target is still approached from its left side");
});

test("a self-loop clears its own node instead of crossing through it", () => {
  const node = { id: "loop", x: 100, y: 100, width: 100, height: 100 };
  const state = __testing.buildState({
    documentId: "self-loop",
    nodes: [node],
    ports: [
      { id: "loop-out", nodeId: "loop", direction: "source", anchor: [1, .3] },
      { id: "loop-in", nodeId: "loop", direction: "target", anchor: [0, .7] }
    ],
    edges: [{ id: "loop-edge", sourcePortId: "loop-out", targetPortId: "loop-in", connector: "flowchart" }]
  });
  const edge = state.edges.get("loop-edge"), geometry = __testing.edgeGeometry(state, edge), points = __testing.edgeRoutePoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, __testing.buildRoutingContext(state));
  for (let index = 1; index < points.length - 2; index++) assert.equal(__testing.polylineIntersectsRectangle([points[index], points[index + 1]], node), false, "internal loop segments remain outside the node");
});

test("routing context remains bounded for an interactive-size graph", () => {
  const nodes = [], ports = [], edges = [];
  for (let index = 0; index < 250; index++) {
    const id = `node-${index}`, column = index % 25, row = Math.floor(index / 25);
    nodes.push({ id, x: column * 180, y: row * 110, width: 120, height: 64 });
    ports.push({ id: `${id}-in`, nodeId: id, direction: "target", anchor: "left" }, { id: `${id}-out`, nodeId: id, direction: "source", anchor: "right" });
    if (index) edges.push({ id: `edge-${index}`, sourcePortId: `node-${index - 1}-out`, targetPortId: `${id}-in`, connector: "flowchart" });
  }
  const state = __testing.buildState({ documentId: "routing-scale", nodes, ports, edges }), started = performance.now(), context = __testing.buildRoutingContext(state);
  for (const edge of state.edges.values()) { const geometry = __testing.edgeGeometry(state, edge); __testing.edgeRoutePoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, context); }
  const elapsed = performance.now() - started;
  assert.ok(elapsed < 500, `250-node routing pass should stay interactive; observed ${elapsed.toFixed(1)}ms`);
  const previewStarted = performance.now(), moving = state.nodes.get("node-125"), incidentEdges = [state.edges.get("edge-125"), state.edges.get("edge-126")].filter(Boolean);
  for (let frame = 0; frame < 60; frame++) {
    const previewNodes = new Map([[moving.id, { ...moving, x: moving.x + frame }]]), previewContext = __testing.buildRoutingContext(state, previewNodes);
    for (const edge of incidentEdges) { const geometry = __testing.edgeGeometry(state, edge, previewNodes); __testing.edgeRoutePoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, previewContext); }
  }
  const previewElapsed = performance.now() - previewStarted;
  assert.ok(previewElapsed < 1000, `60 drag-preview frames should stay below a 16.7ms average; observed ${(previewElapsed / 60).toFixed(1)}ms/frame`);
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
  const markerIds = { arrow: "filled", "plain-arrow": "open", "triangle-open": "generalization", diamond: "diamond", "diamond-open": "aggregation", "erd-one": "one", "erd-zero-one": "zero-one", "erd-one-many": "one-many", "erd-zero-many": "zero-many" };
  assert.equal(__testing.markerFor([{ type: "arrow" }], markerIds), "url(#filled)");
  assert.equal(__testing.markerFor(["plain-arrow"], markerIds), "url(#open)");
  assert.equal(__testing.markerFor([{ type: "diamond" }], markerIds), "url(#diamond)");
  assert.equal(__testing.markerFor([{ type: "triangle-open" }], markerIds), "url(#generalization)");
  assert.equal(__testing.markerFor([{ type: "diamond-open", location: 0 }], markerIds, "start"), "url(#aggregation)");
  assert.equal(__testing.markerFor([{ type: "erd-zero-many" }], markerIds), "url(#zero-many)");
  assert.equal(__testing.markerFor([{ type: "plain-arrow", location: 0 }], markerIds, "start"), "url(#open)");
  assert.equal(__testing.markerFor([{ type: "plain-arrow", location: 0 }], markerIds, "end"), "");
  assert.equal(__testing.markerDescriptor("triangle-open").shapes[0].attributes.fill, "white");
  assert.equal(__testing.markerDescriptor("erd-zero-many").shapes[0].tag, "circle");
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

test("selected group visibility control advertises the next intuitive action", () => {
  const visible = __testing.groupVisibilityDescriptor({ id: "group-a", label: "Review", collapsed: false }, true);
  assert.equal(visible.hidden, false);
  assert.equal(visible.contentsHidden, false);
  assert.equal(visible.expanded, true);
  assert.equal(visible.icon, "mdi:eye-off-outline");
  assert.equal(visible.label, "Hide contents of Review");

  const collapsed = __testing.groupVisibilityDescriptor({ id: "group-a", label: "Review", collapsed: true }, true);
  assert.equal(collapsed.hidden, false);
  assert.equal(collapsed.contentsHidden, true);
  assert.equal(collapsed.expanded, false);
  assert.equal(collapsed.icon, "mdi:eye-outline");
  assert.equal(collapsed.label, "Show contents of Review");

  assert.equal(__testing.groupVisibilityDescriptor({ id: "group-a", collapsed: false }, false).hidden, true);
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

test("contained group selections retain reparent-aware dragging at arbitrary nesting depth", () => {
  const model = {
    ...structuredClone(base),
    groups: [
      { id: "outer", x: 0, y: 0, width: 360, height: 280, collapsed: false },
      { id: "middle", parentGroupId: "outer", x: 24, y: 32, width: 240, height: 184, collapsed: false },
      { id: "inner", parentGroupId: "middle", x: 48, y: 64, width: 144, height: 104, collapsed: false },
      { id: "sibling", parentGroupId: "outer", x: 272, y: 40, width: 72, height: 96, collapsed: false }
    ],
    nodes: [{ ...base.nodes[0], groupId: "inner" }, { ...base.nodes[1], groupId: "sibling" }]
  };
  const state = __testing.buildState(model);
  assert.equal(__testing.selectionRequiresMultiGroupDrag(state, "middle", new Set(["middle"])), false);
  assert.equal(__testing.selectionRequiresMultiGroupDrag(state, "middle", new Set(["middle", "inner", "a"])), false);
  assert.equal(__testing.selectionRequiresMultiGroupDrag(state, "outer", new Set(["outer", "middle", "inner", "sibling", "a", "b"])), false);
  assert.equal(__testing.selectionRequiresMultiGroupDrag(state, "middle", new Set(["middle", "sibling"])), true);
});

test("ctrl-drag selects group subtree duplication even when other items are selected", () => {
  const state = __testing.buildState({
    ...structuredClone(base),
    groups: [
      { id: "outer", x: 0, y: 0, width: 320, height: 240, collapsed: false },
      { id: "inner", parentGroupId: "outer", x: 32, y: 48, width: 160, height: 112, collapsed: false },
      { id: "sibling", x: 400, y: 0, width: 160, height: 112, collapsed: false }
    ]
  });
  const selection = new Set(["outer", "sibling"]);
  assert.equal(__testing.groupDragMode(state, "outer", selection, false), "selection");
  assert.equal(__testing.groupDragMode(state, "outer", selection, true), "duplicate");
  assert.deepEqual(
    __testing.groupDuplicatePayload("outer", "sibling", { x: 192, y: 160 }),
    { groupId: "outer", parentGroupId: "sibling", x: 192, y: 160 }
  );
});

test("overlapping group interactions cycle through layers and preserve a selected group behind another", () => {
  const state = __testing.buildState({
    ...structuredClone(base),
    groups: [
      { id: "outer", x: 0, y: 0, width: 300, height: 240, collapsed: false },
      { id: "left", parentGroupId: "outer", x: 32, y: 32, width: 180, height: 150, collapsed: false },
      { id: "right", parentGroupId: "outer", x: 80, y: 64, width: 180, height: 150, collapsed: false }
    ]
  });
  const point = { x: 120, y: 100 };
  assert.deepEqual(__testing.groupsAtPosition(state, point).map(group => group.id), ["right", "left", "outer"]);
  assert.equal(__testing.groupInteractionTarget(state, point, "right", new Set(), "cycle")?.id, "right");
  assert.equal(__testing.groupInteractionTarget(state, point, "right", new Set(["right"]), "cycle")?.id, "left");
  assert.equal(__testing.groupInteractionTarget(state, point, "right", new Set(["left"]), "cycle")?.id, "outer");
  assert.equal(__testing.groupInteractionTarget(state, point, "right", new Set(["left"]), "drag")?.id, "left");
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
  assert.equal(__testing.groupForGroupPosition(state, "inner", { x: 300, y: 300 }, { x: 20, y: 20 })?.id, "outer");
  assert.equal(__testing.groupForGroupPosition(state, "outer", { x: 40, y: 40 }, { x: 80, y: 80 }), null, "descendants cannot become drop targets");
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
  const gridClamped = __testing.resizeDimensions({ x: 100, y: 100, width: 80, height: 64 }, { clientX: -500, clientY: -500 }, 1, 16, 50, 35);
  assert.deepEqual(gridClamped, { width: 64, height: 48 });
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

test("label commits normalize whitespace without creating invisible labels", () => {
  assert.equal(__testing.editableLabelValue("  Updated label  "), "Updated label");
  assert.equal(__testing.editableLabelValue("\t \n"), null);
  assert.equal(__testing.editableLabelValue(null), null);
  assert.equal(__testing.editableEdgeLabelValue("  Updated edge  "), "Updated edge");
  assert.equal(__testing.editableEdgeLabelValue("\t \n"), "");
  assert.equal(__testing.edgeLabelText({ label: "", overlays: [{ type: "label", label: "inherited" }] }), "");
  assert.equal(__testing.edgeLabelPlacement({ connector: "flowchart", label: "", overlays: [{ type: "label", label: "inherited" }] }, { x: 0, y: 0 }, { x: 100, y: 100 }), null);
});

test("SVG export is standalone and escapes model text", () => {
  const state = __testing.buildState({ ...base, nodes: [{ ...base.nodes[0], label: "A < B", icon: "mdi:robot-outline", style: { color: "#123456" }, properties: [{ id: "prompt", name: "Prompt", value: "Hello", mode: "display" }] }, base.nodes[1] ], edges: [{ ...base.edges[0], animation: true, overlays: [{ type: "erd-zero-many" }, { type: "triangle-open", location: 0 }] }] });
  const svg = __testing.exportSvgDocument(state);
  assert.match(svg, /^<svg /);
  assert.match(svg, /A &lt; B/);
  assert.match(svg, />Prompt<\/text>/);
  assert.match(svg, />Hello<\/text>/);
  assert.match(svg, /fill="#123456"/);
  assert.match(svg, /<path /);
  assert.match(svg, /marker-start="url\(#ghostagram-export-triangle-open\)"/);
  assert.match(svg, /marker-end="url\(#ghostagram-export-erd-zero-many\)"/);
  assert.match(svg, /id="ghostagram-export-erd-zero-many"[^>]*>[\s\S]*?<circle/);
  assert.doesNotMatch(svg, /animation|stroke-dashoffset/);
});

test("SVG export can crop to the current viewport bounds", () => {
  const state = __testing.buildState({ ...base, viewport: { x: 40, y: 80, zoom: 2 } });
  assert.deepEqual(__testing.viewportExportBounds(state, { width: 800, height: 600 }), { x: 40, y: 80, width: 400, height: 300 });
  const svg = __testing.exportSvgDocument(state, { bounds: { x: 40, y: 80, width: 400, height: 300 } });
  assert.match(svg, /viewBox="40 80 400 300"/);
  assert.throws(() => __testing.exportSvgBounds([], { bounds: { x: 0, y: 0, width: 0, height: 1 } }), /Export bounds/);
});

test("SVG export top-aligns simple node titles", () => {
  const svg = __testing.exportSvgDocument(__testing.buildState({
    ...base,
    nodes: [{ ...base.nodes[0], x: 12, y: 30, width: 160, height: 120, label: "Simple title" }, base.nodes[1]]
  }));
  assert.match(svg, /<text x="20" y="50"[^>]*>Simple title<\/text>/);
  assert.doesNotMatch(svg, /<text x="20" y="95"[^>]*>Simple title<\/text>/);
});

test("SVG export uses progressive layout and honors node and section collapse", () => {
  const progressive = {
    ...base,
    nodes: [{ ...base.nodes[0], width: 240, height: 120, label: "Progressive", sections: [{ id: "details", title: "Details" }], presentation: { displayMode: "expanded", expandedHeight: 120, collapsedSectionIds: ["details"] }, properties: [{ id: "secret", name: "Secret", label: "Secret", value: "hidden-value", mode: "display", sectionId: "details" }] }, base.nodes[1]]
  };
  const sectionCollapsedSvg = __testing.exportSvgDocument(__testing.buildState(progressive));
  assert.match(sectionCollapsedSvg, /Details/);
  assert.doesNotMatch(sectionCollapsedSvg, /hidden-value/);
  const nodeCollapsedSvg = __testing.exportSvgDocument(__testing.buildState({ ...progressive, nodes: progressive.nodes.map(node => node.id === "a" ? { ...node, height: 30, presentation: { ...node.presentation, displayMode: "collapsed" } } : node) }));
  assert.match(nodeCollapsedSvg, /Progressive/);
  assert.doesNotMatch(nodeCollapsedSvg, /Details|hidden-value/);
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
