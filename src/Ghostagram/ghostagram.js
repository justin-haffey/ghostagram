/**
 * Ghostagram is a clean-room, dependency-free browser diagram engine.
 * Its module functions accept JSON-shaped values so they can be invoked from
 * a future .NET IJSObjectReference without proxying JavaScript objects.
 */
import { InteractionController } from "./runtime/interaction-controller.js";
import { OperationDispatcher } from "./runtime/operation-dispatcher.js";
import { ProtocolFacade } from "./runtime/protocol-facade.js?v=20260808.3";
import { RenderScheduler } from "./runtime/render-scheduler.js";

export const PROTOCOL_VERSION = 1;
const connectorRegistry = new Map();
const endpointRegistry = new Map();
const overlayRegistry = new Map();
const SVG_NS = "http://www.w3.org/2000/svg";
const ICONIFY_ICON_SCRIPT = "https://code.iconify.design/iconify-icon/3.0.0/iconify-icon.min.js";
const NODE_PROPERTY_TOP = 30;
const NODE_PROPERTY_HEIGHT = 20;
const NODE_PROPERTY_GAP = 1;
const NODE_SECTION_HEADER_HEIGHT = 22;
const NODE_COMPACT_PROPERTY_HEIGHT = 18;
const NODE_BODY_BOTTOM_PADDING = 8;
const NODE_PROPERTY_EDITOR_COLOR = "#0f172a";
const NODE_PROPERTY_EDITOR_BORDER = "#94a3b8";
const NODE_INTERACTIVE_SELECTOR = "button,input,select,textarea,[contenteditable='true'],[data-ghostagram-interactive]";
const ROUTE_OBSTACLE_PADDING = 12;
const ROUTE_CORRIDOR_CLEARANCE = 24;
const ROUTE_LANE_SPACING = 8;
const ROUTE_SPATIAL_CELL = 128;
const markerTypes = Object.freeze(["arrow", "plain-arrow", "triangle-open", "diamond", "diamond-open", "erd-one", "erd-zero-one", "erd-one-many", "erd-zero-many"]);
let iconifyScriptRequested = false;
const supported = Object.freeze({
  protocolVersion: PROTOCOL_VERSION,
  connectors: ["straight", "flowchart", "bezier", "state-machine"],
  endpoints: ["blank", "dot", "rectangle"],
  overlays: ["label", ...markerTypes],
  features: {
    batchedDeltas: true, groups: "nested-membership-resizable", incrementalRendering: true,
    multiInstance: true, multiSelection: true, portConnectionLimits: true, connectionScopes: true, directNodeRotation: true,
    razorInterop: true, viewport: true, canvasGeometry: true,
    customFactories: true, dynamicAnchors: true, editableWaypoints: true, labelOverlayPlacement: true,
    nestedGroups: true, perimeterAnchors: true, rotation: true, flowAnimation: true, selectionLasso: true, edgeTypes: true, iconifyIcons: true,
    progressiveNodes: true, nestedNodeSections: true, richPropertyEditors: true, selectorSources: false,
    imageExport: true, clipboardImage: true
  }
});

/** A static module entry point intended for C#/Razor. */
const protocolFacade = new ProtocolFacade(
  createGhostagram,
  capabilities,
  instanceId => new GhostagramError("INSTANCE_NOT_FOUND", `Ghostagram instance '${instanceId}' does not exist.`)
);
export function create(host, options = {}) { return protocolFacade.create(host, options); }
export function hello(instanceId) { return protocolFacade.hello(instanceId); }
export function replace(instanceId, request) { return protocolFacade.replace(instanceId, request); }
export function apply(instanceId, request) { return protocolFacade.apply(instanceId, request); }
export function inspect(instanceId) { return protocolFacade.inspect(instanceId); }
/** Converts a browser client point into the current document coordinate system. */
export function clientToCanvas(instanceId, clientX, clientY) { return protocolFacade.clientToCanvas(instanceId, clientX, clientY); }
/** Returns the visible canvas center in document coordinates. */
export function canvasCenter(instanceId) { return protocolFacade.canvasCenter(instanceId); }
/** Projects a browser point and reports whether it is inside the live canvas. */
export function hitTestClientPoint(instanceId, clientX, clientY) { return protocolFacade.hitTestClientPoint(instanceId, clientX, clientY); }
export function exportSvg(instanceId, options = {}) { return protocolFacade.exportSvg(instanceId, options); }
/** Exports the complete visible diagram as a PNG data URL. */
export function exportPng(instanceId, options = {}) { return protocolFacade.exportPng(instanceId, options); }
/** Copies the currently visible canvas region as a PNG image. */
export function copyViewportPng(instanceId) { return protocolFacade.copyViewportPng(instanceId); }
export function dispose(instanceId) { return protocolFacade.dispose(instanceId); }
export function capabilities() { return structuredClone(supported); }
/** Register a JavaScript-only connector router. C#/Razor stays descriptor-only. */
export function registerConnector(type, router) {
  if (!type || typeof type !== "string" || typeof router !== "function") throw new GhostagramError("INVALID_MODEL", "registerConnector requires a string type and router function.");
  connectorRegistry.set(type, router);
}
/** Register a JavaScript-only endpoint renderer; C#/Razor still sends a JSON descriptor. */
export function registerEndpoint(type, renderer) {
  if (!type || typeof type !== "string" || typeof renderer !== "function") throw new GhostagramError("INVALID_MODEL", "registerEndpoint requires a string type and renderer function.");
  endpointRegistry.set(type, renderer);
}
/** Register a JavaScript-only SVG overlay renderer; C#/Razor continues to send only JSON descriptors. */
export function registerOverlay(type, renderer) {
  if (!type || typeof type !== "string" || supported.overlays.includes(type) || typeof renderer !== "function") throw new GhostagramError("INVALID_MODEL", "registerOverlay requires a new string type and renderer function.");
  overlayRegistry.set(type, renderer);
}
/** Register a reusable, JSON-only edge type before sending documents that reference it. */

export function createGhostagram(host, options = {}) {
  if (!(host instanceof HTMLElement)) throw new GhostagramError("INVALID_HOST", "Ghostagram requires an HTMLElement host.");
  if ((options.protocolVersion ?? PROTOCOL_VERSION) !== PROTOCOL_VERSION) {
    throw new GhostagramError("VERSION_UNSUPPORTED", `Protocol ${options.protocolVersion} is not supported.`);
  }
  return new GhostagramEngine(host, options);
}

export class GhostagramError extends Error {
  constructor(code, message, details) { super(message); this.name = "GhostagramError"; this.code = code; this.details = details; }
}

class GhostagramEngine {
  constructor(host, options) {
    this.instanceId = crypto.randomUUID();
    this.host = host;
    this.options = { gridSize: 16, minZoom: .2, maxZoom: 3, ...options };
    this.state = emptyState();
    this.lifecycle = "ready";
    this.completed = new Map();
    this.pending = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
    this.renderer = new RenderScheduler(() => this.flush());
    this.eventId = 0;
    this.interopDelivery = new InteropEventQueue(event => this.options.eventSink.invokeMethodAsync(this.options.eventMethod ?? "OnGhostagramEvent", event));
    this.abort = new AbortController();
    this.previewNodes = new Map();
    this.previewGroups = new Map();
    this.previewWaypoints = new Map();
    this.previewLabelOffsets = new Map();
    this.previewSelection = null;
    this.labelEditor = null;
    this.routingContext = null;
    this.dom = buildRoot(host);
    this.interactions = new InteractionController(this);
    this.interactions.bind();
    this.emit("ready", {}, "engine");
  }

  hello() {
    return { ok: true, instanceId: this.instanceId, protocolVersion: PROTOCOL_VERSION, capabilities: capabilities(), lifecycle: this.lifecycle };
  }

  replace(request) {
    return this.execute(request, () => {
      requireObject(request?.model, "INVALID_MODEL", "replace requires a model.");
      const next = buildState(request.model);
      this.state = next;
      this.previewNodes.clear();
      this.previewGroups.clear();
      this.previewWaypoints.clear();
      this.previewSelection = null;
      this.state.documentId = request.documentId ?? next.documentId ?? null;
      this.state.revision = revision(request.revision, "INVALID_MODEL", "replace requires a non-negative integer revision.");
      this.pending.all = true;
      this.pending.viewport = this.pending.selection = true;
      this.schedule();
    }, { replaces: true });
  }

  apply(request) {
    return this.execute(request, () => {
      if (this.lifecycle === "desynced") throw new GhostagramError("NOT_READY", "A replace is required after renderer desynchronization.");
      if (request.documentId && this.state.documentId && request.documentId !== this.state.documentId) throw new GhostagramError("REVISION_MISMATCH", "The request targets another document.");
      const baseRevision = revision(request.baseRevision, "INVALID_MODEL", "apply requires a non-negative integer baseRevision.");
      if (baseRevision !== this.state.revision) {
        this.lifecycle = "desynced";
        throw new GhostagramError("REVISION_MISMATCH", `Expected revision ${this.state.revision}, received ${request.baseRevision}.`);
      }
      if (!Array.isArray(request.ops)) throw new GhostagramError("INVALID_MODEL", "apply requires an ops array.");
      const nextRevision = revision(request.revision, "INVALID_MODEL", "apply requires a non-negative integer revision.");
      if (nextRevision !== baseRevision + 1) throw new GhostagramError("REVISION_MISMATCH", `Expected next revision ${baseRevision + 1}, received ${request.revision}.`);
      const next = cloneState(this.state);
      const dirty = newDirty();
      const operationOptions = { ...this.options, viewportSize: this.viewportSize() };
      for (const op of request.ops) applyOperation(next, op, dirty, operationOptions);
      validateState(next);
      next.revision = nextRevision;
      this.state = next;
      this.clearCommittedPreviews(request.ops);
      mergeDirty(this.pending, dirty);
      this.schedule();
    });
  }

  execute(request, action, flags = {}) {
    if (this.lifecycle === "disposed") return failed(request, "DISPOSED", "The Ghostagram instance is disposed.", this.state.revision);
    const requestId = request?.requestId;
    if (!requestId || typeof requestId !== "string") return failed(request, "INVALID_MODEL", "A string requestId is required.", this.state.revision);
    const cached = this.completed.get(requestId);
    if (cached) return cached;
    try {
      action();
      if (flags.replaces) this.lifecycle = "ready";
      const result = ok(requestId, this.state.revision, this.stats());
      this.remember(requestId, result);
      return result;
    } catch (error) {
      const problem = asProblem(error);
      const result = failed(request, problem.code, problem.message, this.state.revision, problem.details);
      this.remember(requestId, result);
      this.emit("error", { requestId, problem }, "engine");
      return result;
    }
  }

  inspect() {
    return { ok: this.lifecycle !== "disposed", instanceId: this.instanceId, lifecycle: this.lifecycle, documentId: this.state.documentId, revision: this.state.revision, stats: this.stats(), model: serialiseState(this.state) };
  }
  clientToCanvas(clientX, clientY) {
    if (!Number.isFinite(clientX) || !Number.isFinite(clientY)) throw new GhostagramError("INVALID_MODEL", "Client coordinates must be finite numbers.");
    return viewportPoint({ clientX, clientY }, this.dom.root.getBoundingClientRect(), this.state.viewport);
  }
  canvasCenter() { return canvasCenterPoint(this.dom.root.getBoundingClientRect(), this.state.viewport); }
  hitTestClientPoint(clientX, clientY) {
    if (!Number.isFinite(clientX) || !Number.isFinite(clientY)) throw new GhostagramError("INVALID_MODEL", "Client coordinates must be finite numbers.");
    return canvasHitDescriptor({ clientX, clientY }, this.dom.root.getBoundingClientRect(), this.state.viewport);
  }
  exportSvg(options = {}) { return exportSvgDocument(this.state, options); }
  async exportPng(options = {}) { return (await exportPngArtifact(exportSvgArtifact(this.state, options))).dataUrl; }
  async copyViewportPng() {
    if (!navigator.clipboard?.write || typeof ClipboardItem === "undefined")
      throw new GhostagramError("CLIPBOARD_UNAVAILABLE", "This browser cannot copy PNG images to the clipboard.");
    const artifact = exportSvgArtifact(this.state, { bounds: viewportExportBounds(this.state, this.viewportSize()) });
    const image = await exportPngArtifact(artifact, Math.min(2, Math.max(1, window.devicePixelRatio || 1)));
    await navigator.clipboard.write([new ClipboardItem({ "image/png": image.blob })]);
    return { width: image.width, height: image.height };
  }
  stats() { return { nodes: this.state.nodes.size, ports: this.state.ports.size, edges: this.state.edges.size, groups: this.state.groups.size, selected: this.state.selection.size, scheduled: this.renderer.scheduled }; }
  remember(id, value) { this.completed.set(id, value); if (this.completed.size > 256) this.completed.delete(this.completed.keys().next().value); }
  clearCommittedPreviews(ops) {
    for (const op of ops) {
      if (op?.type === "node.upsert" || op?.type === "node.remove") this.previewNodes.delete(op.value?.id ?? op.id);
      if (op?.type === "group.upsert" || op?.type === "group.remove") this.previewGroups.delete(op.value?.id ?? op.id);
      if (op?.type === "edge.upsert" || op?.type === "edge.remove") this.previewWaypoints.delete(op.value?.id ?? op.id);
      if (op?.type === "selection.replace") this.previewSelection = null;
    }
  }

  schedule() { this.renderer.request(); }
  flush() {
    try {
      if (!this.routingContext || this.pending.all || this.pending.nodes.size || this.pending.groups.size || this.pending.edges.size) this.routingContext = buildRoutingContext(this.state, this.previewNodes, this.previewGroups);
      if (this.pending.all) this.renderAll(); else this.renderDirty();
      this.pending = newDirty();
    } catch (error) {
      this.lifecycle = "desynced";
      this.emit("renderer.desynced", { problem: asProblem(error) }, "engine");
    }
  }
  renderAll() {
    // A host-driven replace can arrive while an input has focus. Temporarily
    // detach it without treating that DOM removal as a user blur/commit.
    const editor = this.suspendLabelEditor();
    this.dom.nodes.replaceChildren(); this.dom.groups.replaceChildren(); this.dom.edges.replaceChildren(); this.dom.overlay.replaceChildren();
    this.dom.nodeById.clear(); this.dom.groupById.clear(); this.dom.pathById.clear(); this.dom.labelById.clear(); this.dom.customOverlayByKey.clear(); this.dom.edgeHandlesById.clear(); this.dom.waypointHandlesById.clear(); this.dom.previewPath = null; this.previewLabelOffsets.clear();
    for (const group of groupsForRender(this.state)) this.renderGroup(group);
    for (const node of this.state.nodes.values()) this.renderNode(node);
    for (const edge of this.state.edges.values()) this.renderEdge(edge);
    this.restoreLabelEditor(editor);
    this.renderViewport(); this.renderSelection();
  }
  renderDirty() {
    for (const id of this.pending.groups) { const group = this.state.groups.get(id); if (group) this.renderGroup(group); else this.dom.groupById.get(id)?.remove(); }
    for (const id of this.pending.nodes) { const node = this.state.nodes.get(id); if (node) this.renderNode(node); else this.dom.nodeById.get(id)?.remove(); }
    for (const id of this.pending.edges) {
      const edge = this.state.edges.get(id);
      if (edge) this.renderEdge(edge);
      else {
        if (this.labelEditor?.kind === "edge" && this.labelEditor.id === id) this.finishLabelEdit(false);
        this.dom.pathById.get(id)?.remove();
        this.dom.labelById.get(id)?.remove();
        for (const handle of [this.dom.edgeHandlesById.get(id)?.source, this.dom.edgeHandlesById.get(id)?.target]) handle?.remove();
        this.dom.pathById.delete(id);
        this.dom.labelById.delete(id);
        this.previewLabelOffsets.delete(id);
        this.removeCustomOverlays(id);
        this.dom.edgeHandlesById.delete(id);
        for (const handle of this.dom.waypointHandlesById.get(id) ?? []) handle.remove();
        this.dom.waypointHandlesById.delete(id);
      }
    }
    if (this.pending.viewport) this.renderViewport();
    if (this.pending.selection || this.pending.nodes.size || this.pending.groups.size || this.pending.edges.size) this.renderSelection();
  }
  renderGroup(group) {
    group = this.previewGroups.get(group.id) ?? group;
    let el = this.dom.groupById.get(group.id);
    if (!el) {
      el = document.createElement("div"); el.className = "ghostagram-group"; el.dataset.groupId = group.id;
      const label = document.createElement("span"); label.className = "ghostagram-group-label"; label.style.cssText = "position:absolute;left:6px;top:4px;pointer-events:none;user-select:none;color:#334155;font-size:12px;font-weight:600;line-height:1.2;";
      const icon = document.createElement("iconify-icon"); icon.className = "ghostagram-item-icon"; icon.style.cssText = "position:absolute;right:5px;top:4px;display:inline-block;width:18px;height:18px;color:#334155;pointer-events:none;z-index:3;";
      const visibility = document.createElement("button"); visibility.type = "button"; visibility.className = "ghostagram-group-visibility"; visibility.hidden = true;
      const visibilityIcon = document.createElementNS(SVG_NS, "svg"); visibilityIcon.classList.add("ghostagram-group-visibility-icon"); visibilityIcon.setAttribute("viewBox", "0 0 24 24"); visibilityIcon.setAttribute("aria-hidden", "true");
      const eye = document.createElementNS(SVG_NS, "path"); eye.setAttribute("d", "M2.06 12.35a1 1 0 0 1 0-.7 10.75 10.75 0 0 1 19.88 0 1 1 0 0 1 0 .7 10.75 10.75 0 0 1-19.88 0Z");
      const pupil = document.createElementNS(SVG_NS, "circle"); pupil.setAttribute("cx", "12"); pupil.setAttribute("cy", "12"); pupil.setAttribute("r", "3");
      const slash = document.createElementNS(SVG_NS, "line"); slash.classList.add("ghostagram-group-visibility-slash"); slash.setAttribute("x1", "4"); slash.setAttribute("y1", "4"); slash.setAttribute("x2", "20"); slash.setAttribute("y2", "20");
      visibilityIcon.append(eye, pupil, slash); visibility.append(visibilityIcon);
      visibility.addEventListener("pointerdown", event => event.stopPropagation(), { signal: this.abort.signal });
      visibility.addEventListener("dblclick", event => event.stopPropagation(), { signal: this.abort.signal });
      visibility.addEventListener("click", event => { event.preventDefault(); event.stopPropagation(); const current = this.state.groups.get(group.id); if (!current) return; const hidden = !current.collapsed; this.emit("group.visibilityRequested", { groupId: group.id, hidden, collapsed: hidden }, "browser"); }, { signal: this.abort.signal });
      const handle = document.createElement("button"); handle.type = "button"; handle.className = "ghostagram-group-resize"; handle.setAttribute("aria-label", `Resize ${group.label ?? group.id}`); handle.title = "Resize group"; handle.style.cssText = "position:absolute;right:-7px;bottom:-7px;width:14px;height:14px;padding:0;border:1px solid #0f766e;background:#fff;cursor:nwse-resize;z-index:2;";
      handle.addEventListener("pointerdown", event => { event.stopPropagation(); this.startGroupResize(group.id, event); }, { signal: this.abort.signal });
      el.addEventListener("click", event => this.selectFromElement(group.id, event, "group"), { signal: this.abort.signal });
      el.addEventListener("dblclick", event => { if (!event.target.closest?.("button,input")) this.startLabelEdit("group", group.id); }, { signal: this.abort.signal });
      el.addEventListener("contextmenu", event => this.requestContext(group.id, event, "group"), { signal: this.abort.signal });
      el.addEventListener("pointerdown", event => { if (!event.shiftKey && event.detail < 2) this.startGroupDrag(group.id, event); }, { signal: this.abort.signal }); el.append(label, icon, visibility, handle); this.dom.groups.append(el); this.dom.groupById.set(group.id, el);
    }
    const selected = (this.previewSelection ?? this.state.selection).has(group.id);
    setBox(el, group); el.style.display = isGroupHiddenByCollapsedAncestor(this.state, group) ? "none" : "block"; el.style.removeProperty("z-index"); el.dataset.collapsed = String(group.collapsed); el.title = ""; el.querySelector(".ghostagram-group-label").textContent = group.label ?? group.id; renderIconifyIcon(el.querySelector(".ghostagram-item-icon"), group.icon); el.querySelector(".ghostagram-group-resize").hidden = !selected; updateGroupVisibilityControl(el, group, selected); applyStyle(el, group.style, { border: group.collapsed ? "1px solid #64748b" : "1px dashed #64748b", background: group.collapsed ? "rgba(148,163,184,.14)" : "rgba(148,163,184,.08)", cursor: "move" });
  }
  renderNode(node) {
    let el = this.dom.nodeById.get(node.id);
    if (!el) {
      el = document.createElement("div"); el.className = "ghostagram-node"; el.tabIndex = 0; el.dataset.nodeId = node.id;
      const label = document.createElement("span"); label.className = "ghostagram-node-label";
      const icon = document.createElement("iconify-icon"); icon.className = "ghostagram-item-icon"; icon.style.cssText = "position:absolute;right:4px;top:4px;display:inline-block;width:18px;height:18px;color:currentColor;pointer-events:none;z-index:3;";
      const rotate = document.createElement("button"); rotate.type = "button"; rotate.className = "ghostagram-node-rotate"; rotate.textContent = "↻"; rotate.setAttribute("aria-label", `Rotate ${node.label ?? node.id} in 15 degree increments`); rotate.title = "Rotate node in 15° increments"; rotate.style.cssText = "position:absolute;left:50%;top:-28px;width:18px;height:18px;padding:0;border:1px solid #0f766e;border-radius:50%;background:#fff;color:#0f766e;font-size:14px;line-height:14px;transform:translateX(-50%);cursor:grab;z-index:2;";
      rotate.addEventListener("pointerdown", event => { event.stopPropagation(); this.startNodeRotate(node.id, event); }, { signal: this.abort.signal });
      const handle = document.createElement("button"); handle.type = "button"; handle.className = "ghostagram-node-resize"; handle.setAttribute("aria-label", `Resize ${node.label ?? node.id}`); handle.title = "Resize node"; handle.style.cssText = "position:absolute;right:-7px;bottom:-7px;width:14px;height:14px;padding:0;border:1px solid #0f766e;background:#fff;cursor:nwse-resize;z-index:2;";
      handle.addEventListener("pointerdown", event => { event.stopPropagation(); this.startNodeResize(node.id, event); }, { signal: this.abort.signal }); el.append(label, icon, rotate, handle);
      el.addEventListener("click", event => { if (!isInteractiveNodeTarget(event.target)) this.selectFromElement(node.id, event); }, { signal: this.abort.signal });
      el.addEventListener("dblclick", event => { if (!isInteractiveNodeTarget(event.target)) this.startLabelEdit("node", node.id); }, { signal: this.abort.signal });
      el.addEventListener("contextmenu", event => { if (!isInteractiveNodeTarget(event.target)) this.requestContext(node.id, event); }, { signal: this.abort.signal });
      el.addEventListener("pointerdown", event => { if (!isInteractiveNodeTarget(event.target) && event.detail < 2) this.startDrag(node.id, event); }, { signal: this.abort.signal });
      this.dom.nodes.append(el); this.dom.nodeById.set(node.id, el);
    }
    setBox(el, node);
    el.style.display = isNodeHiddenByCollapsedGroup(this.state, node) ? "none" : "block";
    const layout = nodeLayoutProjection(node);
    el.dataset.displayMode = layout.displayMode;
    el.title = ""; el.querySelector(".ghostagram-node-label").textContent = node.label ?? node.id; renderIconifyIcon(el.querySelector(".ghostagram-item-icon"), node.icon);
    applyStyle(el, node.style, { background: "#f8fafc", border: "1px solid #334155", borderRadius: "6px", color: "#0f172a", padding: "6px", boxSizing: "border-box", cursor: "grab", pointerEvents: "auto" });
    el.style.transform = node.rotation ? `rotate(${node.rotation}deg)` : "";
    const label = el.querySelector(".ghostagram-node-label"), icon = el.querySelector(".ghostagram-item-icon"), rotate = el.querySelector(".ghostagram-node-rotate"), handle = el.querySelector(".ghostagram-node-resize"), activeEditor = this.labelEditor?.kind === "node" && this.labelEditor.id === node.id ? this.suspendLabelEditor() : null;
    const header = document.createElement("header"); header.className = "ghostagram-node-header"; header.style.cssText = `position:absolute;left:6px;right:6px;top:0;height:${NODE_PROPERTY_TOP}px;display:flex;align-items:center;gap:5px;box-sizing:border-box;z-index:2;`;
    label.style.cssText = "display:block;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;line-height:1.2;";
    icon.style.right = layout.progressive ? "28px" : "4px";
    header.append(label, icon);
    if (layout.progressive) {
      const toggle = document.createElement("button"), next = nextNodePresentationRequest(node, this.options.gridSize);
      toggle.type = "button"; toggle.className = "ghostagram-node-presentation"; toggle.dataset.ghostagramInteractive = "true"; toggle.textContent = layout.displayMode === "expanded" ? "−" : layout.displayMode === "compact" ? "▾" : "▸";
      toggle.setAttribute("aria-label", `${next.label} ${node.label ?? node.id}`); toggle.setAttribute("aria-expanded", String(layout.displayMode !== "collapsed")); toggle.title = `${next.label} node`;
      toggle.style.cssText = "position:absolute;right:0;top:5px;width:20px;height:20px;padding:0;border:1px solid currentColor;border-radius:4px;background:rgba(255,255,255,.88);color:inherit;font:600 14px/18px system-ui;cursor:pointer;";
      toggle.addEventListener("pointerdown", event => event.stopPropagation()); toggle.addEventListener("click", event => { event.preventDefault(); event.stopPropagation(); this.emit("node.presentationRequested", next.payload, "browser"); }); header.append(toggle);
    }
    const body = document.createElement("div"); body.className = "ghostagram-node-body"; body.hidden = layout.displayMode === "collapsed"; body.style.cssText = "position:absolute;inset:0;z-index:1;";
    this.renderNodeProperties(body, node, layout);
    rotate.hidden = true; handle.hidden = true; el.replaceChildren(header, body, rotate, handle);
    this.restoreLabelEditor(activeEditor);
    for (const entry of portRenderPlan(this.state, node.id)) this.renderPort(el, entry.port, entry.ports);
  }
  renderNodeProperties(body, node, layout) {
    const renderRow = (container, rowLayout, originY = 0) => {
      const property = rowLayout.property, row = document.createElement("label"); row.className = "ghostagram-node-property"; row.dataset.propertyId = property.id;
      row.style.cssText = `position:absolute;left:${6 + Math.min(rowLayout.depth, 4) * 8}px;right:6px;top:${rowLayout.y - originY}px;display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.2fr);align-items:center;gap:5px;height:${rowLayout.height}px;font-size:11px;line-height:18px;box-sizing:border-box;`;
      const name = document.createElement("span"); name.className = "ghostagram-node-property-label"; name.textContent = property.label ?? property.name ?? property.id; name.style.cssText = "overflow:hidden;text-overflow:ellipsis;white-space:nowrap;opacity:.75;";
      row.append(name);
      if (!rowLayout.compact && (property.mode === "edit" || property.mode === "displayAndEdit") && (editablePropertyTypes.has(property.type) || property.editor?.kind && property.editor.kind !== "auto")) {
        const input = propertyInput(property); input.dataset.propertyId = property.id; input.dataset.ghostagramInteractive = "true"; input.setAttribute("aria-label", `${node.label ?? node.id}: ${property.label ?? property.id}`);
        input.style.cssText = propertyEditorStyle(rowLayout.height);
        let lastSignature = propertyValueSignature(property.value);
        const commit = () => {
          try {
            const value = propertyInputValue(input, property);
            const decision = propertyCommitDecision(lastSignature, value);
            input.setCustomValidity("");
            if (!decision.commit) return;
            lastSignature = decision.signature;
            this.emit("node.property.commit", { nodeId: node.id, propertyId: property.id, value }, "browser");
          } catch (error) {
            input.setCustomValidity(error?.message ?? "Invalid value.");
            input.reportValidity();
          }
        };
        input.addEventListener("pointerdown", event => event.stopPropagation(), { signal: this.abort.signal });
        input.addEventListener("click", event => event.stopPropagation(), { signal: this.abort.signal });
        input.addEventListener("dblclick", event => event.stopPropagation(), { signal: this.abort.signal });
        input.addEventListener("keydown", event => {
          event.stopPropagation();
          if (event.key === "Enter" && (input.tagName !== "TEXTAREA" || event.ctrlKey || event.metaKey)) { event.preventDefault(); commit(); input.blur(); }
        }, { signal: this.abort.signal });
        input.addEventListener("change", commit, { signal: this.abort.signal });
        input.addEventListener("blur", commit, { signal: this.abort.signal });
        row.append(input);
      } else {
        const value = document.createElement("output"); value.className = "ghostagram-node-property-value"; value.textContent = propertyDisplayValue(property); value.style.cssText = "min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;text-align:right;font-variant-numeric:tabular-nums;"; row.append(value);
      }
      container.append(row);
    };
    for (const row of layout.unsectionedRows) renderRow(body, row);
    const renderSection = (container, sectionLayout, originY = 0) => {
      const section = document.createElement("section"); section.className = "ghostagram-node-section"; section.dataset.sectionId = sectionLayout.section.id; section.dataset.collapsed = String(sectionLayout.collapsed);
      section.style.cssText = `position:absolute;left:${6 + Math.min(sectionLayout.depth, 4) * 8}px;right:6px;top:${sectionLayout.y - originY}px;height:${sectionLayout.height}px;box-sizing:border-box;`;
      const heading = document.createElement(sectionLayout.section.collapsible ? "button" : "div"); heading.className = "ghostagram-node-section-heading"; heading.textContent = `${sectionLayout.section.collapsible ? sectionLayout.collapsed ? "▸ " : "▾ " : ""}${sectionLayout.section.title}`;
      heading.style.cssText = `position:absolute;left:0;right:0;top:0;height:${NODE_SECTION_HEADER_HEIGHT}px;padding:1px 5px;border:0;border-bottom:1px solid rgba(100,116,139,.35);background:rgba(148,163,184,.10);color:inherit;text-align:left;font:600 11px/18px system-ui;box-sizing:border-box;`;
      if (sectionLayout.section.collapsible) {
        const request = nextSectionPresentationRequest(node, sectionLayout.section.id, this.options.gridSize);
        heading.type = "button"; heading.dataset.ghostagramInteractive = "true"; heading.setAttribute("aria-expanded", String(!sectionLayout.collapsed)); heading.setAttribute("aria-label", `${request.label} ${sectionLayout.section.title}`); heading.title = request.label;
        heading.addEventListener("pointerdown", event => event.stopPropagation()); heading.addEventListener("click", event => { event.preventDefault(); event.stopPropagation(); this.emit("node.presentationRequested", request.payload, "browser"); });
      }
      section.append(heading);
      for (const row of sectionLayout.rows) renderRow(section, row, sectionLayout.y);
      for (const child of sectionLayout.children) renderSection(section, child, sectionLayout.y);
      container.append(section);
    };
    for (const section of layout.rootSections) renderSection(body, section);
  }
  renderPort(nodeEl, port, proxyPorts = [port]) {
    const endpoint = endpointDescriptor(port);
    if (!port || endpoint.type === "blank") return;
    const descriptor = proxyPortDescriptor(proxyPorts);
    const visual = portVisualDescriptor(port, endpoint);
    const el = document.createElement("button"); el.type = "button"; el.className = `ghostagram-port ghostagram-${visual.kind}-port`; el.dataset.portId = port.id; el.dataset.portKind = visual.kind; el.dataset.proxyPortIds = descriptor.portIds.join(","); el.setAttribute("aria-label", descriptor.label); el.title = descriptor.label;
    const anchor = resolvePortAnchor(this.state, port), size = endpoint.size ?? 12, strokeWidth = endpoint.strokeWidth ?? 1;
    el.disabled = !descriptor.enabled;
    el.style.cssText = `position:absolute;width:${size}px;height:${size}px;border:${strokeWidth}px solid ${endpoint.stroke ?? "#0f766e"};border-radius:${visual.borderRadius};background:${endpoint.fill ?? "#fff"};color:${endpoint.stroke ?? "#0f766e"};padding:0;opacity:${descriptor.enabled ? 1 : .45};cursor:${descriptor.enabled ? "crosshair" : "not-allowed"};${portAnchorStyle(anchor, size, strokeWidth)}`;
    endpointRegistry.get(endpoint.type)?.(el, endpoint, port);
    el.addEventListener("pointerdown", event => this.startConnection(port, event), { signal: this.abort.signal });
    el.addEventListener("contextmenu", event => this.requestContext(port.id, event, "port"), { signal: this.abort.signal }); nodeEl.append(el);
  }
  renderEdge(edge) {
    edge = resolveEdgeDescriptor(edge, this.state.edgeTypes);
    const source = this.state.ports.get(edge.sourcePortId), target = this.state.ports.get(edge.targetPortId);
    if (!source || !target) return;
    const previewLabelOffset = this.previewLabelOffsets.get(edge.id), visualEdge = { ...edge, ...(this.previewWaypoints.has(edge.id) ? { waypoints: this.previewWaypoints.get(edge.id) } : {}), ...(previewLabelOffset ?? {}) };
    let path = this.dom.pathById.get(edge.id);
    if (!path) {
      path = document.createElementNS(SVG_NS, "path"); path.classList.add("ghostagram-edge"); path.dataset.edgeId = edge.id; path.style.pointerEvents = "stroke";
      path.addEventListener("click", event => this.selectFromElement(edge.id, event, "edge"), { signal: this.abort.signal });
      path.addEventListener("contextmenu", event => this.requestContext(edge.id, event, "edge"), { signal: this.abort.signal });
      path.addEventListener("dblclick", () => { const current = this.state.edges.get(edge.id); if (!current) return; const resolved = resolveEdgeDescriptor(current, this.state.edgeTypes); if (!edgeLabelText(resolved) && current.labelEditable !== false) { this.startLabelEdit("edge", edge.id); return; } if (resolved.detachable !== false) this.emit("edge.detachRequested", { edgeId: edge.id }, "browser"); }, { signal: this.abort.signal });
      this.dom.edges.append(path); this.dom.pathById.set(edge.id, path);
    }
    const geometry = edgeGeometry(this.state, visualEdge, this.previewNodes);
    const { sourcePoint, targetPoint } = geometry;
    const routePoints = edgeRoutePoints(visualEdge, sourcePoint, targetPoint, geometry, this.routingContext);
    path.setAttribute("d", route(visualEdge, sourcePoint, targetPoint, geometry, routePoints));
    const edgeStyle = edgeStyleDescriptor(edge.style);
    path.setAttribute("fill", "none"); path.setAttribute("stroke", edgeStyle.stroke); path.setAttribute("stroke-width", String(edgeStyle.strokeWidth));
    setOptionalSvgAttribute(path, "stroke-dasharray", edgeStyle.dash); setOptionalSvgAttribute(path, "stroke-linecap", edgeStyle.lineCap); setOptionalSvgAttribute(path, "stroke-linejoin", edgeStyle.lineJoin); setOptionalSvgAttribute(path, "opacity", edgeStyle.opacity); this.applyFlowAnimation(path, edge.animation);
    path.setAttribute("marker-start", markerFor(edge.overlays, this.dom.markerIds, "start"));
    path.setAttribute("marker-end", markerFor(edge.overlays, this.dom.markerIds, "end"));
    path.style.display = geometry.hidden ? "none" : "";
    const labelPlacement = edgeLabelPlacement(edge, sourcePoint, targetPoint, geometry, routePoints);
    let label = this.dom.labelById.get(edge.id);
    if (labelPlacement) {
      if (!label) {
        label = document.createElementNS(SVG_NS, "text"); label.classList.add("ghostagram-edge-label"); label.dataset.edgeId = edge.id; label.style.pointerEvents = "all"; label.style.cursor = "text";
        label.addEventListener("pointerdown", event => this.startEdgeLabelDrag(edge.id, event), { signal: this.abort.signal });
        label.addEventListener("click", event => this.selectFromElement(edge.id, event, "edge"), { signal: this.abort.signal });
        label.addEventListener("dblclick", event => { event.stopPropagation(); this.startLabelEdit("edge", edge.id); }, { signal: this.abort.signal });
        this.dom.edges.append(label); this.dom.labelById.set(edge.id, label);
      }
      label.textContent = labelPlacement.text; label.style.display = geometry.hidden ? "none" : ""; label.setAttribute("x", String(labelPlacement.x)); label.setAttribute("y", String(labelPlacement.y)); label.setAttribute("fill", edgeStyle.labelColor); label.setAttribute("font-size", String(labelPlacement.fontSize));
    } else if (label) { label.remove(); this.dom.labelById.delete(edge.id); }
    if (this.labelEditor?.kind === "edge" && this.labelEditor.id === edge.id) {
      if (label) label.style.visibility = "hidden";
      this.positionEdgeLabelEditor(this.labelEditor, labelPlacement ?? this.edgeLabelPoint(edge, sourcePoint, targetPoint, geometry, routePoints));
    }
    this.renderCustomOverlays(edge, sourcePoint, targetPoint, geometry.hidden, geometry, routePoints);
    this.renderReconnectHandles(edge, sourcePoint, targetPoint, geometry.hidden || geometry.proxied || edge.reconnectable === false);
    this.renderWaypointHandles(edge, visualEdge.waypoints, geometry.hidden || geometry.proxied);
  }
  renderCustomOverlays(edge, sourcePoint, targetPoint, hidden, geometry, routePoints) {
    const active = new Set();
    for (const [index, overlay] of edge.overlays.entries()) {
      const type = overlay?.type ?? overlay, renderer = overlayRegistry.get(type);
      if (!renderer) continue;
      const key = `${edge.id}:${index}`; active.add(key);
      let element = this.dom.customOverlayByKey.get(key);
      if (!element) { element = document.createElementNS(SVG_NS, "g"); element.classList.add("ghostagram-custom-overlay"); element.dataset.edgeId = edge.id; element.dataset.overlayIndex = String(index); this.dom.edges.append(element); this.dom.customOverlayByKey.set(key, element); }
      const location = typeof overlay === "object" ? overlay.location ?? .5 : .5, point = pointAlongPolyline(routePoints ?? edgeRoutePoints(edge, sourcePoint, targetPoint, geometry, this.routingContext), location);
      element.setAttribute("transform", `translate(${point.x} ${point.y})`); element.style.display = hidden ? "none" : "";
      renderer({ element, overlay, edge, point, sourcePoint, targetPoint, emit: (name, payload = {}) => this.emit("overlay.event", { edgeId: edge.id, overlayIndex: index, type, name, payload }, "browser") });
    }
    this.removeCustomOverlays(edge.id, active);
  }
  removeCustomOverlays(edgeId, except = new Set()) { for (const [key, element] of this.dom.customOverlayByKey) if (key.startsWith(`${edgeId}:`) && !except.has(key)) { element.remove(); this.dom.customOverlayByKey.delete(key); } }
  renderReconnectHandles(edge, sourcePoint, targetPoint, hidden) {
    let handles = this.dom.edgeHandlesById.get(edge.id);
    if (!handles) {
      handles = {};
      for (const end of ["source", "target"]) {
        const handle = document.createElementNS(SVG_NS, "circle"); handle.classList.add("ghostagram-edge-reconnect"); handle.dataset.edgeId = edge.id; handle.dataset.end = end; handle.setAttribute("r", "5"); handle.setAttribute("fill", "#fff"); handle.setAttribute("stroke", "#0f766e"); handle.setAttribute("stroke-width", "2"); handle.style.pointerEvents = "all"; handle.style.cursor = "crosshair";
        handle.addEventListener("pointerdown", event => { event.stopPropagation(); this.startReconnect(edge.id, end, event); }, { signal: this.abort.signal }); this.dom.overlay.append(handle); handles[end] = handle;
      }
      this.dom.edgeHandlesById.set(edge.id, handles);
    }
    const sourceHandle = reconnectHandlePoint(sourcePoint, targetPoint, "source"), targetHandle = reconnectHandlePoint(sourcePoint, targetPoint, "target");
    handles.source.setAttribute("cx", String(sourceHandle.x)); handles.source.setAttribute("cy", String(sourceHandle.y)); handles.target.setAttribute("cx", String(targetHandle.x)); handles.target.setAttribute("cy", String(targetHandle.y)); handles.hidden = hidden;
  }
  renderWaypointHandles(edge, waypoints, hidden) {
    const prior = this.dom.waypointHandlesById.get(edge.id) ?? [];
    while (prior.length > waypoints.length) prior.pop().remove();
    while (prior.length < waypoints.length) {
      const index = prior.length, handle = document.createElementNS(SVG_NS, "rect");
      handle.classList.add("ghostagram-waypoint"); handle.setAttribute("width", "10"); handle.setAttribute("height", "10"); handle.setAttribute("fill", "#fff"); handle.setAttribute("stroke", "#0f766e"); handle.setAttribute("stroke-width", "2"); handle.style.pointerEvents = "all"; handle.style.cursor = "move";
      handle.addEventListener("pointerdown", event => { event.stopPropagation(); this.startWaypointDrag(edge.id, index, event); }, { signal: this.abort.signal }); this.dom.overlay.append(handle); prior.push(handle);
    }
    for (let index = 0; index < waypoints.length; index++) { prior[index].setAttribute("x", String(waypoints[index].x - 5)); prior[index].setAttribute("y", String(waypoints[index].y - 5)); }
    prior.hidden = hidden; this.dom.waypointHandlesById.set(edge.id, prior);
  }
  applyFlowAnimation(path, animation) {
    const flow = flowAnimationDescriptor(animation);
    const reducedMotion = this.options.respectReducedMotion !== false && typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (!flow || reducedMotion) {
      path.style.removeProperty("stroke-dasharray"); path.style.removeProperty("stroke-dashoffset"); path.style.removeProperty("animation"); path.style.removeProperty("animation-direction"); return;
    }
    path.style.strokeDasharray = flow.dash; path.style.strokeDashoffset = "0"; path.style.animation = `${this.dom.flowAnimationName} ${1 / flow.speed}s linear infinite`; path.style.animationDirection = flow.direction === "reverse" ? "reverse" : "normal";
  }
  pointForPort(port, peerNode) { return previewPointForPort(this.state, this.previewNodes, port, peerNode); }
  viewportSize() {
    const bounds = this.dom.root.getBoundingClientRect();
    return { width: Math.max(1, this.dom.root.clientWidth || bounds.width), height: Math.max(1, this.dom.root.clientHeight || bounds.height) };
  }
  canvasPoint(pointer) { return viewportPoint(pointer, this.dom.root.getBoundingClientRect(), this.state.viewport); }
  renderPathPreview(edge, sourcePoint, targetPoint) {
    let path = this.dom.previewPath;
    if (!path) {
      path = document.createElementNS(SVG_NS, "path"); path.classList.add("ghostagram-connection-preview"); path.style.pointerEvents = "none";
      this.dom.edges.append(path); this.dom.previewPath = path;
    }
    path.setAttribute("d", route(resolveEdgeDescriptor(edge, this.state.edgeTypes), sourcePoint, targetPoint));
    path.setAttribute("fill", "none"); path.setAttribute("stroke", "#0f766e"); path.setAttribute("stroke-width", "2"); path.setAttribute("stroke-dasharray", "6 4"); path.setAttribute("opacity", ".75");
  }
  renderConnectionPreview(port, targetPoint) { this.renderPathPreview({ connector: "straight" }, this.pointForPort(port), targetPoint); }
  setConnectionTarget(portId) {
    if (this.connectionPreviewTarget === portId) return;
    if (this.connectionPreviewTarget) this.portElement(this.connectionPreviewTarget)?.style.removeProperty("box-shadow");
    this.connectionPreviewTarget = portId ?? null;
    if (portId) this.portElement(portId)?.style.setProperty("box-shadow", "0 0 0 3px rgba(13, 148, 136, .25)");
  }
  portElement(portId) { return [...this.dom.root.querySelectorAll("[data-port-id]")].find(element => element.dataset.portId === portId); }
  clearConnectionPreview() { this.dom.previewPath?.remove(); this.dom.previewPath = null; this.setConnectionTarget(null); this.connectionSource = null; }
  setGroupDropTarget(groupId) {
    if (this.groupDropTarget === groupId) return;
    if (this.groupDropTarget) this.dom.groupById.get(this.groupDropTarget)?.style.removeProperty("outline");
    this.groupDropTarget = groupId ?? null;
    if (groupId) this.dom.groupById.get(groupId)?.style.setProperty("outline", "3px solid rgba(13, 148, 136, .3)");
  }
  groupAtPosition(node, position, excludedGroupIds = new Set()) {
    return groupForNodePosition([...this.state.groups.values()].filter(group => !excludedGroupIds.has(group.id) && !isGroupHiddenByCollapsedAncestor(this.state, group)), node, position);
  }
  renderViewport() {
    const v = this.state.viewport; this.dom.stage.style.transform = `translate(${-v.x * v.zoom}px, ${-v.y * v.zoom}px) scale(${v.zoom})`;
    const grid = gridCssProjection(v, this.options.gridSize);
    this.host.style.setProperty("--ghostagram-grid-size", `${grid.modelSize}px`);
    this.host.style.setProperty("--ghostagram-dot-grid-size", `${grid.screenSize}px`);
    this.host.style.setProperty("--ghostagram-dot-grid-phase-x", `${grid.phaseX}px`);
    this.host.style.setProperty("--ghostagram-dot-grid-phase-y", `${grid.phaseY}px`);
  }
  renderSelection() {
    const selection = this.previewSelection ?? this.state.selection;
    for (const [id, el] of this.dom.nodeById) {
      const selected = selection.has(id), node = this.state.nodes.get(id);
      el.classList.toggle("ghostagram-selected", selected);
      el.querySelector(".ghostagram-node-rotate").hidden = !selected || node?.rotatable === false;
      el.querySelector(".ghostagram-node-resize").hidden = !selected || node?.resizable === false;
    }
    for (const [id, el] of this.dom.groupById) {
      const selected = selection.has(id), group = this.state.groups.get(id);
      el.classList.toggle("ghostagram-selected", selected);
      el.querySelector(".ghostagram-group-resize").hidden = !selected;
      updateGroupVisibilityControl(el, group, selected);
    }
    for (const [id, el] of this.dom.pathById) el.classList.toggle("ghostagram-selected", selection.has(id));
    for (const [id, handles] of this.dom.edgeHandlesById) for (const handle of [handles.source, handles.target]) handle.style.display = selection.has(id) && !handles.hidden ? "" : "none";
    for (const [id, handles] of this.dom.waypointHandlesById) for (const handle of handles) handle.style.display = selection.has(id) && !handles.hidden ? "" : "none";
  }
  edgeLabelPoint(edge, sourcePoint, targetPoint, geometry, routePoints) { return pointAlongPolyline(routePoints ?? edgeRoutePoints(edge, sourcePoint, targetPoint, geometry, this.routingContext), .5); }
  positionEdgeLabelEditor(editor, point) { editor.input.style.left = `${point.x}px`; editor.input.style.top = `${point.y}px`; }
  startLabelEdit(kind, id) {
    const items = kind === "node" ? this.state.nodes : kind === "group" ? this.state.groups : this.state.edges, item = items.get(id), el = kind === "node" ? this.dom.nodeById.get(id) : kind === "group" ? this.dom.groupById.get(id) : this.dom.editors;
    if (!item || !el || item.labelEditable === false) return false;
    if (this.labelEditor?.kind === kind && this.labelEditor.id === id) return true;
    this.finishLabelEdit(false);
    const label = kind === "edge" ? this.dom.labelById.get(id) : el.querySelector(kind === "node" ? ".ghostagram-node-label" : ".ghostagram-group-label"), input = document.createElement("input"), initialValue = item.label ?? (kind === "edge" ? edgeLabelText(item) ?? "" : item.id);
    input.type = "text"; input.value = initialValue; input.className = "ghostagram-label-editor"; input.setAttribute("aria-label", `Edit ${kind} label`);
    input.style.cssText = kind === "node" ? "position:absolute;inset:4px;width:calc(100% - 8px);height:calc(100% - 8px);box-sizing:border-box;border:1px solid #0f766e;border-radius:3px;padding:2px 4px;font:inherit;color:inherit;background:#fff;z-index:3;" : kind === "group" ? "position:absolute;left:4px;top:2px;width:calc(100% - 8px);height:20px;box-sizing:border-box;border:1px solid #0f766e;border-radius:3px;padding:1px 3px;font:inherit;color:inherit;background:#fff;z-index:3;" : "position:absolute;width:128px;height:24px;box-sizing:border-box;border:1px solid #0f766e;border-radius:3px;padding:2px 4px;font:14px system-ui,sans-serif;color:#0f172a;background:#fff;transform:translate(-50%,-50%);z-index:5;pointer-events:auto;";
    const stop = event => event.stopPropagation();
    input.addEventListener("pointerdown", stop, { signal: this.abort.signal });
    input.addEventListener("click", stop, { signal: this.abort.signal });
    input.addEventListener("dblclick", stop, { signal: this.abort.signal });
    input.addEventListener("keydown", event => { stop(event); if (event.key === "Enter") { event.preventDefault(); this.finishLabelEdit(true); } else if (event.key === "Escape") { event.preventDefault(); this.finishLabelEdit(false); } }, { signal: this.abort.signal });
    input.addEventListener("blur", () => this.finishLabelEdit(true), { signal: this.abort.signal });
    if (label) label.style.visibility = "hidden"; el.append(input); this.labelEditor = { kind, id, initialValue, input, label };
    if (kind === "edge") {
      const edge = this.state.edges.get(id), geometry = edge && edgeGeometry(this.state, edge, this.previewNodes);
      if (edge && geometry) this.positionEdgeLabelEditor(this.labelEditor, label ? { x: Number(label.getAttribute("x")), y: Number(label.getAttribute("y")) } : this.edgeLabelPoint(edge, geometry.sourcePoint, geometry.targetPoint, geometry));
    }
    this.focusLabelEditor(this.labelEditor); return true;
  }
  focusLabelEditor(editor) {
    requestAnimationFrame(() => {
      if (this.labelEditor !== editor || !editor.input.isConnected) return;
      editor.input.focus({ preventScroll: true });
      editor.input.select();
      editor.input.setSelectionRange(0, editor.input.value.length, "forward");
    });
  }
  finishLabelEdit(commit) {
    const editor = this.labelEditor;
    if (!editor) return;
    this.labelEditor = null; editor.input.remove(); editor.label?.style.removeProperty("visibility");
    const normalise = editor.kind === "edge" ? editableEdgeLabelValue : editableLabelValue;
    const label = normalise(editor.input.value), initialLabel = normalise(editor.initialValue);
    if (commit && label !== initialLabel) this.emit(`${editor.kind}.label.commit`, { [`${editor.kind}Id`]: editor.id, label }, "browser");
  }
  suspendLabelEditor() {
    const editor = this.labelEditor;
    if (!editor) return null;
    this.labelEditor = null;
    return editor;
  }
  restoreLabelEditor(editor) {
    if (!editor) return;
    const item = editor.kind === "node" ? this.state.nodes.get(editor.id) : editor.kind === "group" ? this.state.groups.get(editor.id) : this.state.edges.get(editor.id);
    const el = editor.kind === "node" ? this.dom.nodeById.get(editor.id) : editor.kind === "group" ? this.dom.groupById.get(editor.id) : this.dom.editors;
    if (!item || !el || item.labelEditable === false) return;
    editor.label = editor.kind === "edge" ? this.dom.labelById.get(editor.id) : el.querySelector(editor.kind === "node" ? ".ghostagram-node-label" : ".ghostagram-group-label");
    this.labelEditor = editor;
    if (editor.label) editor.label.style.visibility = "hidden";
    el.append(editor.input);
    if (editor.kind === "edge") {
      const edge = this.state.edges.get(editor.id), geometry = edge && edgeGeometry(this.state, edge, this.previewNodes);
      if (edge && geometry) this.positionEdgeLabelEditor(editor, editor.label ? { x: Number(editor.label.getAttribute("x")), y: Number(editor.label.getAttribute("y")) } : this.edgeLabelPoint(edge, geometry.sourcePoint, geometry.targetPoint, geometry));
    }
    this.focusLabelEditor(editor);
  }

  selectableIds() { return selectableIds(this.state); }
  startSelectionLasso(event) {
    event.preventDefault();
    const start = this.canvasPoint(event), box = this.dom.selectionBox; let frame = 0, latestPointer = event;
    box.hidden = false;
    const update = () => {
      frame = 0;
      const rect = rectangleForPoints(start, this.canvasPoint(latestPointer));
      setBox(box, rect);
      const ids = selectionIdsInRectangle(this.state, rect, this.routingContext);
      this.previewSelection = new Set(ids); this.pending.selection = true; this.schedule();
      this.emit("selection.changing", { kind: "lasso", ids, rect }, "browser", true);
    };
    const move = pointer => { latestPointer = pointer; if (!frame) frame = requestAnimationFrame(update); };
    const up = pointer => {
      window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); if (frame) cancelAnimationFrame(frame); box.hidden = true;
      const rect = rectangleForPoints(start, this.canvasPoint(pointer)), ids = selectionIdsInRectangle(this.state, rect, this.routingContext);
      this.previewSelection = new Set(ids); this.pending.selection = true; this.schedule();
      this.emit("selection.changed", { kind: "lasso", ids, rect }, "browser");
    };
    update(); window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startCanvasDeselection(event) {
    event.preventDefault();
    const start = { x: event.clientX, y: event.clientY };
    const up = pointer => {
      window.removeEventListener("pointerup", up); window.removeEventListener("pointercancel", cancel);
      if (!isClickGesture(start, pointer)) return;
      this.previewSelection = new Set(); this.pending.selection = true; this.schedule();
      this.emit("selection.changed", { kind: "canvas", ids: [] }, "browser");
    };
    const cancel = () => { window.removeEventListener("pointerup", up); };
    window.addEventListener("pointerup", up, { once: true }); window.addEventListener("pointercancel", cancel, { once: true });
  }
  suppressSelectionClickForDrag(start, end) { if (isClickGesture(start, end)) return; const suppress = event => { event.preventDefault(); event.stopImmediatePropagation(); }; this.dom.root.addEventListener("click", suppress, { capture: true, once: true }); window.setTimeout(() => this.dom.root.removeEventListener("click", suppress, true), 0); }
  interactionPayload(id, event, kind) { return { id, kind, point: this.canvasPoint(event), modifiers: { shift: event.shiftKey, ctrl: event.ctrlKey, alt: event.altKey, meta: event.metaKey } }; }
  requestContext(id, event, kind) { event.preventDefault(); event.stopPropagation(); this.dom.root.focus({ preventScroll: true }); this.emit("element.contextRequested", this.interactionPayload(id, event, kind), "browser"); }
  selectFromElement(id, event, kind = "node") { this.dom.root.focus({ preventScroll: true }); const selection = new Set(this.previewSelection ?? this.state.selection); if (!event.shiftKey) selection.clear(); selection.has(id) && event.shiftKey ? selection.delete(id) : selection.add(id); this.previewSelection = selection; this.pending.selection = true; this.schedule(); this.emit("selection.changed", { kind, ids: [...selection] }, "browser"); this.emit("element.clicked", this.interactionPayload(id, event, kind), "browser"); }
  requestSelectionDeletion() { const selection = this.previewSelection ?? this.state.selection, plan = deletionPlan(this.state, selection); if (!plan.edgeIds.length && !plan.nodeIds.length && !plan.groupIds.length) return false; this.emit("selection.deleteRequested", { selection: [...selection], ...plan }, "browser"); return true; }
  moveSelectionByKeyboard(dx, dy) { const positions = selectedMovePositions(this.state, this.previewSelection ?? this.state.selection, dx, dy, this.options.gridSize); if (!positions.groups.length && !positions.nodes.length) return false; if (positions.groups.length) this.emit("selection.move.commit", { keyboard: true, ...positions }, "browser"); else if (positions.nodes.length === 1) this.emit("node.move.commit", { keyboard: true, nodeId: positions.nodes[0].id, ...positions.nodes[0] }, "browser"); else this.emit("nodes.move.commit", { keyboard: true, nodes: positions.nodes }, "browser"); return true; }
  startDrag(nodeId, event) {
    const node = this.state.nodes.get(nodeId);
    if (!node || event.button !== 0) return;
    this.dom.root.focus({ preventScroll: true });
    const selected = this.previewSelection ?? this.state.selection;
    const selectedGroupRoots = [...selected].map(id => this.state.groups.get(id)).filter(group => group && ![...selected].some(selectedId => selectedId !== group.id && isGroupDescendant(this.state, selectedId, group.id)));
    const selectedGroupIds = [...new Set(selectedGroupRoots.flatMap(group => [group.id, ...descendantGroupIds(this.state, group.id)]))];
    const selectedNodeIds = new Set(selected.has(node.id) ? [...selected].map(id => this.state.nodes.get(id)).filter(Boolean).map(candidate => candidate.id) : [node.id]);
    for (const groupId of selectedGroupIds) for (const memberId of descendantNodeIds(this.state, groupId)) selectedNodeIds.add(memberId);
    const dragged = [...selectedNodeIds].map(id => this.previewNodes.get(id) ?? this.state.nodes.get(id)).filter(Boolean), draggedGroups = selectedGroupIds.map(id => this.previewGroups.get(id) ?? this.state.groups.get(id)).filter(Boolean);
    const initial = dragged.find(candidate => candidate.id === node.id) ?? node, start = { x: event.clientX, y: event.clientY, nodeX: initial.x, nodeY: initial.y };
    const positionsAt = pointer => {
      const nodePositions = multiDragPositions(dragged, node.id, start, pointer, this.state.viewport.zoom, this.options.gridSize), primary = nodePositions.find(position => position.id === node.id), dx = primary.x - initial.x, dy = primary.y - initial.y;
      return { nodes: nodePositions.map(position => ({ ...position, groupId: draggedGroups.length ? this.state.nodes.get(position.id).groupId ?? null : this.groupAtPosition(this.state.nodes.get(position.id), position)?.id ?? null })), groups: translatePositions(draggedGroups, dx, dy, this.options.gridSize) };
    };
    const applyPreview = positions => {
      const edgeIds = new Set();
      for (const position of positions.groups) { const original = this.state.groups.get(position.id); this.previewGroups.set(position.id, { ...original, ...position }); this.renderGroup(original); }
      for (const position of positions.nodes) { const original = this.state.nodes.get(position.id), preview = { ...original, ...position }; this.previewNodes.set(position.id, preview); const el = this.dom.nodeById.get(position.id); if (el) { el.style.left = `${preview.x}px`; el.style.top = `${preview.y}px`; } for (const edgeId of incident(this.state, position.id)) edgeIds.add(edgeId); }
      for (const edgeId of edgeIds) { const edge = this.state.edges.get(edgeId); if (edge) this.renderEdge(edge); }
    };
    event.preventDefault();
    const move = e => { const positions = positionsAt(e), primary = positions.nodes.find(position => position.id === node.id); applyPreview(positions); this.setGroupDropTarget(draggedGroups.length ? null : primary?.groupId); const type = positions.groups.length ? "selection.move.preview" : positions.nodes.length === 1 ? "node.move.preview" : "nodes.move.preview"; this.emit(type, positions.groups.length ? { nodeId: node.id, ...positions } : positions.nodes.length === 1 ? { nodeId: node.id, ...primary } : { nodeId: node.id, nodes: positions.nodes }, "browser", true); };
    const up = e => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); this.suppressSelectionClickForDrag(start, e); this.setGroupDropTarget(null); if (isClickGesture(start, e)) return; const positions = positionsAt(e), primary = positions.nodes.find(position => position.id === node.id); if (positions.groups.length) this.emit("selection.move.commit", { nodeId: node.id, ...positions }, "browser"); else if (positions.nodes.length === 1) this.emit("node.move.commit", { nodeId: node.id, ...primary }, "browser"); else this.emit("nodes.move.commit", { nodeId: node.id, nodes: positions.nodes }, "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startGroupDrag(groupId, event) {
    const group = this.state.groups.get(groupId);
    if (!group || event.button !== 0) return;
    this.dom.root.focus({ preventScroll: true });
    const selection = this.previewSelection ?? this.state.selection;
    if (selection.has(groupId)) { this.startSelectedGroupDrag(groupId, event, selection); return; }
    event.preventDefault(); const initial = this.previewGroups.get(groupId) ?? group, childGroups = descendantGroupIds(this.state, groupId).map(id => this.previewGroups.get(id) ?? this.state.groups.get(id)).filter(Boolean), members = descendantNodeIds(this.state, groupId).map(nodeId => this.previewNodes.get(nodeId) ?? this.state.nodes.get(nodeId)).filter(Boolean), start = { x: event.clientX, y: event.clientY, nodeX: initial.x, nodeY: initial.y };
    const move = e => {
      const position = dragPosition(start, e, this.state.viewport.zoom, this.options.gridSize), dx = position.x - initial.x, dy = position.y - initial.y, targetGroup = groupForGroupPosition(this.state, groupId, position);
      this.previewGroups.set(groupId, { ...group, ...initial, ...position }); this.renderGroup(group);
      for (const child of childGroups) { const preview = { ...child, x: child.x + dx, y: child.y + dy }; this.previewGroups.set(child.id, preview); this.renderGroup(child); }
      for (const member of members) { const preview = { ...member, x: member.x + dx, y: member.y + dy }; this.previewNodes.set(member.id, preview); const el = this.dom.nodeById.get(member.id); if (el) { el.style.left = `${preview.x}px`; el.style.top = `${preview.y}px`; } for (const edgeId of incident(this.state, member.id)) { const edge = this.state.edges.get(edgeId); if (edge) this.renderEdge(edge); } }
      this.setGroupDropTarget(targetGroup?.id); this.emit("group.move.preview", { groupId, parentGroupId: targetGroup?.id ?? null, ...position, dx, dy }, "browser", true);
    };
    const up = e => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); this.suppressSelectionClickForDrag(start, e); this.setGroupDropTarget(null); if (isClickGesture(start, e)) return; const position = dragPosition(start, e, this.state.viewport.zoom, this.options.gridSize), dx = position.x - initial.x, dy = position.y - initial.y, targetGroup = groupForGroupPosition(this.state, groupId, position); this.emit("group.move.commit", groupMovePayload(groupId, targetGroup?.id ?? null, position, dx, dy, childGroups, members), "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startSelectedGroupDrag(groupId, event, selection) {
    const selectedGroupRoots = [...selection].map(id => this.state.groups.get(id)).filter(group => group && ![...selection].some(selectedId => selectedId !== group.id && isGroupDescendant(this.state, selectedId, group.id)));
    const selectedGroupIds = [...new Set(selectedGroupRoots.flatMap(group => [group.id, ...descendantGroupIds(this.state, group.id)]))];
    const selectedNodeIds = new Set([...selection].map(id => this.state.nodes.get(id)).filter(Boolean).map(node => node.id));
    for (const selectedGroupId of selectedGroupIds) for (const nodeId of descendantNodeIds(this.state, selectedGroupId)) selectedNodeIds.add(nodeId);
    const groups = selectedGroupIds.map(id => this.previewGroups.get(id) ?? this.state.groups.get(id)).filter(Boolean), nodes = [...selectedNodeIds].map(id => this.previewNodes.get(id) ?? this.state.nodes.get(id)).filter(Boolean), anchor = groups.find(group => group.id === groupId);
    if (!anchor) return;
    const start = { x: event.clientX, y: event.clientY, nodeX: anchor.x, nodeY: anchor.y };
    const positionsAt = pointer => {
      const position = dragPosition(start, pointer, this.state.viewport.zoom, this.options.gridSize), dx = position.x - anchor.x, dy = position.y - anchor.y;
      return { groups: translatePositions(groups, dx, dy, this.options.gridSize), nodes: translatePositions(nodes, dx, dy, this.options.gridSize).map(item => ({ ...item, groupId: this.state.nodes.get(item.id).groupId ?? null })) };
    };
    const applyPreview = positions => {
      const edgeIds = new Set();
      for (const position of positions.groups) { const original = this.state.groups.get(position.id); this.previewGroups.set(position.id, { ...original, ...position }); this.renderGroup(original); }
      for (const position of positions.nodes) { const original = this.state.nodes.get(position.id), preview = { ...original, ...position }; this.previewNodes.set(position.id, preview); const el = this.dom.nodeById.get(position.id); if (el) { el.style.left = `${preview.x}px`; el.style.top = `${preview.y}px`; } for (const edgeId of incident(this.state, position.id)) edgeIds.add(edgeId); }
      for (const edgeId of edgeIds) { const edge = this.state.edges.get(edgeId); if (edge) this.renderEdge(edge); }
    };
    event.preventDefault();
    const move = pointer => { const positions = positionsAt(pointer); applyPreview(positions); this.emit("selection.move.preview", { groupId, ...positions }, "browser", true); };
    const up = pointer => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); this.suppressSelectionClickForDrag(start, pointer); if (isClickGesture(start, pointer)) return; const positions = positionsAt(pointer); this.emit("selection.move.commit", { groupId, ...positions }, "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startGroupResize(groupId, event) {
    const group = this.state.groups.get(groupId);
    if (!group || event.button !== 0) return;
    event.preventDefault(); const initial = this.previewGroups.get(groupId) ?? group, start = { x: event.clientX, y: event.clientY, width: initial.width, height: initial.height }, minWidth = Number.isFinite(group.minWidth) ? Math.max(1, group.minWidth) : 80, minHeight = Number.isFinite(group.minHeight) ? Math.max(1, group.minHeight) : 64;
    const move = e => { const size = resizeDimensions(start, e, this.state.viewport.zoom, this.options.gridSize, minWidth, minHeight); this.previewGroups.set(groupId, { ...group, ...initial, ...size }); this.renderGroup(group); this.emit("group.resize.preview", { groupId, ...size }, "browser", true); };
    const up = e => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); this.emit("group.resize.commit", { groupId, ...resizeDimensions(start, e, this.state.viewport.zoom, this.options.gridSize, minWidth, minHeight) }, "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startNodeResize(nodeId, event) {
    const node = this.state.nodes.get(nodeId);
    if (!node || node.resizable === false || event.button !== 0) return;
    event.preventDefault(); const initial = this.previewNodes.get(nodeId) ?? node, start = { x: event.clientX, y: event.clientY, width: initial.width, height: initial.height }, minWidth = Number.isFinite(node.minWidth) ? Math.max(1, node.minWidth) : 48, configuredMinHeight = Number.isFinite(node.minHeight) ? Math.max(1, node.minHeight) : 32, minHeight = Math.max(configuredMinHeight, nodeContentMinimumHeight(this.state, node));
    const move = pointer => { const size = resizeDimensions(start, pointer, this.state.viewport.zoom, this.options.gridSize, minWidth, minHeight), preview = { ...node, ...initial, ...size }; this.previewNodes.set(nodeId, preview); const el = this.dom.nodeById.get(nodeId); if (el) { el.style.width = `${size.width}px`; el.style.height = `${size.height}px`; } for (const edgeId of incident(this.state, nodeId)) { const edge = this.state.edges.get(edgeId); if (edge) this.renderEdge(edge); } this.emit("node.resize.preview", { nodeId, ...size }, "browser", true); };
    const up = pointer => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); this.suppressSelectionClickForDrag(start, pointer); this.emit("node.resize.commit", { nodeId, ...resizeDimensions(start, pointer, this.state.viewport.zoom, this.options.gridSize, minWidth, minHeight) }, "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startNodeRotate(nodeId, event) {
    const node = this.state.nodes.get(nodeId);
    if (!node || node.rotatable === false || event.button !== 0) return;
    event.preventDefault(); const initial = this.previewNodes.get(nodeId) ?? node;
    const rotationAt = pointer => rotationForPoint(initial, this.canvasPoint(pointer), 15);
    const move = pointer => {
      const rotation = rotationAt(pointer), preview = { ...node, ...initial, rotation };
      this.previewNodes.set(nodeId, preview);
      const el = this.dom.nodeById.get(nodeId); if (el) el.style.transform = `rotate(${rotation}deg)`;
      for (const edgeId of incident(this.state, nodeId)) { const edge = this.state.edges.get(edgeId); if (edge) this.renderEdge(edge); }
      this.emit("node.rotate.preview", { nodeId, rotation }, "browser", true);
    };
    const up = pointer => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); this.suppressSelectionClickForDrag({ x: event.clientX, y: event.clientY }, pointer); this.emit("node.rotate.commit", { nodeId, rotation: rotationAt(pointer) }, "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startEdgeLabelDrag(edgeId, event) {
    const edge = this.state.edges.get(edgeId);
    if (!edge || edge.labelEditable === false || event.button !== 0) return;
    const geometry = edgeGeometry(this.state, edge, this.previewNodes), placement = edgeLabelPlacement(edge, geometry.sourcePoint, geometry.targetPoint, geometry);
    if (!placement || geometry.hidden) return;
    event.preventDefault(); const start = this.canvasPoint(event), initial = edgeLabelOffsets(edge);
    const offsetsAt = pointer => { const point = this.canvasPoint(pointer); return { labelOffsetX: initial.labelOffsetX + point.x - start.x, labelOffsetY: initial.labelOffsetY + point.y - start.y }; };
    const move = pointer => { const offsets = offsetsAt(pointer); this.previewLabelOffsets.set(edgeId, offsets); this.renderEdge(edge); this.emit("edge.labelPosition.preview", { edgeId, ...offsets }, "browser", true); };
    const up = pointer => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); this.suppressSelectionClickForDrag({ x: event.clientX, y: event.clientY }, pointer); if (!isClickGesture({ x: event.clientX, y: event.clientY }, pointer)) this.emit("edge.labelPosition.commit", { edgeId, ...offsetsAt(pointer) }, "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startWaypointDrag(edgeId, index, event) {
    const edge = this.state.edges.get(edgeId);
    if (!edge?.waypoints?.[index] || event.button !== 0) return;
    event.preventDefault(); const initial = this.previewWaypoints.get(edgeId) ?? edge.waypoints, start = { x: event.clientX, y: event.clientY, waypoint: initial[index] };
    const move = pointer => { const point = this.canvasPoint(pointer), waypoints = initial.map((waypoint, waypointIndex) => waypointIndex === index ? point : waypoint); this.previewWaypoints.set(edgeId, waypoints); this.renderEdge(edge); this.emit("edge.waypoints.preview", { edgeId, waypoints }, "browser", true); };
    const up = pointer => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", up); const point = this.canvasPoint(pointer), waypoints = initial.map((waypoint, waypointIndex) => waypointIndex === index ? point : waypoint); this.emit("edge.waypointsRequested", { edgeId, waypoints }, "browser"); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", up, { once: true });
  }
  startConnection(port, event) {
    event.preventDefault(); event.stopPropagation(); if (port.direction === "target" || port.enabled === false) return;
    this.connectionSource = port.id; this.renderConnectionPreview(port, this.canvasPoint(event)); this.emit("edge.create.started", { sourcePortId: port.id }, "browser");
    const targetAt = pointer => {
      const targetId = document.elementFromPoint(pointer.clientX, pointer.clientY)?.closest?.("[data-port-id]")?.dataset.portId;
      const target = this.state.ports.get(targetId);
      return target && target.id !== port.id && target.direction !== "source" && this.canConnect(port, target) ? target : null;
    };
    const move = pointer => {
      const target = targetAt(pointer), point = this.canvasPoint(pointer);
      this.setConnectionTarget(target?.id); this.renderConnectionPreview(port, point);
      this.emit("edge.create.preview", { sourcePortId: port.id, targetPortId: target?.id ?? null, x: point.x, y: point.y }, "browser", true);
    };
    const finish = targetEvent => {
      const target = targetAt(targetEvent); this.clearConnectionPreview(); cleanup();
      if (target) this.emit("edge.createRequested", { sourcePortId: port.id, targetPortId: target.id }, "browser");
      else this.emit("edge.create.cancelled", { sourcePortId: port.id }, "browser");
    };
    const cancel = () => { this.clearConnectionPreview(); cleanup(); this.emit("edge.create.cancelled", { sourcePortId: port.id }, "browser"); };
    const cleanup = () => { window.removeEventListener("pointermove", move); window.removeEventListener("pointerup", finish); window.removeEventListener("pointercancel", cancel); };
    window.addEventListener("pointermove", move); window.addEventListener("pointerup", finish, { once: true }); window.addEventListener("pointercancel", cancel, { once: true });
  }
  startReconnect(edgeId, end, event) {
    const edge = this.state.edges.get(edgeId), source = edge && this.state.ports.get(edge.sourcePortId), target = edge && this.state.ports.get(edge.targetPortId);
    if (!edge || resolveEdgeDescriptor(edge, this.state.edgeTypes).reconnectable === false || !source || !target || event.button !== 0) return;
    event.preventDefault();
    const candidateAt = pointer => {
      const portId = document.elementFromPoint(pointer.clientX, pointer.clientY)?.closest?.("[data-port-id]")?.dataset.portId, candidate = this.state.ports.get(portId);
      const nextSource = end === "source" ? candidate : source, nextTarget = end === "target" ? candidate : target;
      return candidate && nextSource.direction !== "target" && nextTarget.direction !== "source" && this.canReconnect(edge, nextSource, nextTarget) ? candidate : null;
    };
    const draw = pointer => { const candidate = candidateAt(pointer), point = this.canvasPoint(pointer), fixedSource = this.pointForPort(source, this.state.nodes.get(target.nodeId)), fixedTarget = this.pointForPort(target, this.state.nodes.get(source.nodeId)); this.setConnectionTarget(candidate?.id); this.renderPathPreview(edge, end === "source" ? point : fixedSource, end === "target" ? point : fixedTarget); this.emit("edge.reconnect.preview", { edgeId, end, portId: candidate?.id ?? null, x: point.x, y: point.y }, "browser", true); };
    const finish = pointer => { const candidate = candidateAt(pointer); this.clearConnectionPreview(); cleanup(); if (candidate) this.emit("edge.reconnectRequested", { edgeId, sourcePortId: end === "source" ? candidate.id : source.id, targetPortId: end === "target" ? candidate.id : target.id }, "browser"); else this.emit("edge.reconnect.cancelled", { edgeId, end }, "browser"); };
    const cancel = () => { this.clearConnectionPreview(); cleanup(); this.emit("edge.reconnect.cancelled", { edgeId, end }, "browser"); };
    const cleanup = () => { window.removeEventListener("pointermove", draw); window.removeEventListener("pointerup", finish); window.removeEventListener("pointercancel", cancel); };
    draw(event); window.addEventListener("pointermove", draw); window.addEventListener("pointerup", finish, { once: true }); window.addEventListener("pointercancel", cancel, { once: true });
  }
  canConnect(source, target) { return canConnect(this.state, source, target); }
  canReconnect(edge, source, target) { return canReconnect(this.state, edge, source, target); }
  emit(type, payload, origin, coalesce = false) {
    const event = { protocolVersion: PROTOCOL_VERSION, eventId: ++this.eventId, instanceId: this.instanceId, documentId: this.state.documentId, renderRevision: this.state.revision, type, origin, timestamp: Date.now(), payload };
    if (coalesce) { this.lastPreview = event; if (this.previewFrame) return; this.previewFrame = requestAnimationFrame(() => { this.previewFrame = 0; this.deliver(this.lastPreview, true); }); return; }
    this.deliver(event);
  }
  deliver(event, coalesced = false) {
    const sink = this.options.eventSink;
    if (!sink) return;
    try {
      // JS callbacks remain synchronous. Blazor interop is serialized because independent
      // invokeMethodAsync calls may reach .NET out of order and invalidate an earlier selection.
      if (typeof sink === "function") sink(event);
      else if (typeof sink.invokeMethodAsync === "function") this.interopDelivery.enqueue(event, coalesced);
    } catch { /* callback faults cannot break the renderer */ }
  }
  dispose() { if (this.lifecycle === "disposed") return { ok: true, instanceId: this.instanceId, disposed: true }; this.lifecycle = "disposing"; this.renderer.cancel(); if (this.previewFrame) cancelAnimationFrame(this.previewFrame); this.interopDelivery.dispose(); this.interactions.dispose(); this.abort.abort(); this.dom.root.remove(); for (const name of ["--ghostagram-grid-size", "--ghostagram-dot-grid-size", "--ghostagram-dot-grid-phase-x", "--ghostagram-dot-grid-phase-y"]) this.host.style.removeProperty(name); this.lifecycle = "disposed"; return { ok: true, instanceId: this.instanceId, disposed: true }; }
}

class InteropEventQueue {
  constructor(invoke) { this.invoke = invoke; this.commits = []; this.preview = null; this.running = null; this.disposed = false; }
  enqueue(event, coalesced = false) {
    if (this.disposed) return;
    if (coalesced) this.preview = event;
    else { this.preview = null; this.commits.push(event); }
    this.ensureRunning();
  }
  ensureRunning() { if (!this.running) this.running = this.drain(); }
  async drain() {
    try {
      while (!this.disposed && (this.commits.length || this.preview)) {
        const event = this.commits.length ? this.commits.shift() : this.takePreview();
        try { await this.invoke(event); } catch { /* callback faults cannot break the renderer */ }
      }
    } finally {
      this.running = null;
      if (!this.disposed && (this.commits.length || this.preview)) this.ensureRunning();
    }
  }
  takePreview() { const event = this.preview; this.preview = null; return event; }
  async whenIdle() { while (this.running) await this.running; }
  dispose() { this.disposed = true; this.commits.length = 0; this.preview = null; }
}

function emptyState() { return { documentId: null, revision: 0, nodes: new Map(), ports: new Map(), edges: new Map(), groups: new Map(), edgeTypes: new Map(), portsByNode: new Map(), edgesByPort: new Map(), nodesByGroup: new Map(), groupsByGroup: new Map(), selection: new Set(), viewport: { x: 0, y: 0, zoom: 1 } }; }
function editableLabelValue(value) { const label = String(value ?? "").trim(); return label || null; }
// Edge labels may inherit text from an overlay or edge type. An empty string is
// therefore an intentional override meaning "show no text"; null remains "inherit".
function editableEdgeLabelValue(value) { return String(value ?? "").trim(); }
function omitNullProperties(value, preserveNull = false) {
  if (Array.isArray(value)) return value.map(item => omitNullProperties(item, preserveNull));
  if (!value || typeof value !== "object") return value;
  return Object.fromEntries(Object.entries(value).flatMap(([key, nested]) => {
    // Optional .NET descriptors commonly serialize null, but dynamic property values are
    // data: null is a first-class JSON value and must survive renderer round-trips.
    const nextPreserveNull = preserveNull || key === "properties" || (key === "value" && "type" in value && "id" in value);
    return nested === null && !nextPreserveNull ? [] : [[key, omitNullProperties(nested, nextPreserveNull)]];
  }));
}
function buildState(model) { model = omitNullProperties(model); const state = emptyState(); state.documentId = model.documentId ?? null; for (const type of model.edgeTypes ?? []) upsertEdgeType(state, type); for (const group of model.groups ?? []) upsertGroup(state, group, true); for (const node of model.nodes ?? []) upsertNode(state, node); for (const port of model.ports ?? []) upsertPort(state, port); for (const edge of model.edges ?? []) upsertEdge(state, edge); state.selection = new Set(model.selection ?? []); state.viewport = { ...state.viewport, ...(model.viewport ?? {}) }; validateState(state); return state; }
function cloneState(s) { return { ...s, nodes: new Map(s.nodes), ports: new Map(s.ports), edges: new Map(s.edges), groups: new Map(s.groups), edgeTypes: new Map(s.edgeTypes), portsByNode: mapSets(s.portsByNode), edgesByPort: mapSets(s.edgesByPort), nodesByGroup: mapSets(s.nodesByGroup), groupsByGroup: mapSets(s.groupsByGroup), selection: new Set(s.selection), viewport: { ...s.viewport } }; }
function mapSets(source) { return new Map([...source].map(([key, value]) => [key, new Set(value)])); }
function serialiseState(s) { return { documentId: s.documentId, revision: s.revision, nodes: [...s.nodes.values()], ports: [...s.ports.values()], edges: [...s.edges.values()], groups: [...s.groups.values()], edgeTypes: [...s.edgeTypes.values()], selection: [...s.selection], viewport: s.viewport }; }
const modelOperationDispatcher = new OperationDispatcher({
  "node.upsert": ({ state, value, dirty }) => {
    const previousGroupId = state.nodes.get(value.id)?.groupId;
    upsertNode(state, value);
    dirty.nodes.add(value.id);
    if (previousGroupId) dirty.groups.add(previousGroupId);
    if (state.nodes.get(value.id)?.groupId) dirty.groups.add(state.nodes.get(value.id).groupId);
    for (const id of incident(state, value.id)) dirty.edges.add(id);
  },
  "node.remove": ({ state, value, dirty }, operation) => removeNode(state, operation.id ?? value.id, dirty),
  "port.upsert": ({ state, value, dirty }) => { upsertPort(state, value); dirty.nodes.add(value.nodeId); for (const id of state.edgesByPort.get(value.id) ?? []) dirty.edges.add(id); },
  "port.remove": ({ state, value, dirty }, operation) => removePort(state, operation.id ?? value.id, dirty),
  "edgeType.upsert": ({ state, value, dirty }) => { upsertEdgeType(state, value); for (const edge of state.edges.values()) if (edge.type === value.id) dirty.edges.add(edge.id); },
  "edgeType.remove": ({ state, value, dirty }, operation) => removeEdgeType(state, operation.id ?? value.id, dirty),
  "edge.upsert": ({ state, value, dirty }) => { upsertEdge(state, value); dirty.edges.add(value.id); },
  "edge.remove": ({ state, value, dirty }, operation) => removeEdge(state, operation.id ?? value.id, dirty),
  "group.upsert": ({ state, value, dirty }) => { upsertGroup(state, value); dirty.groups.add(value.id); dirtyGroupTree(state, value.id, dirty); },
  "group.remove": ({ state, value, dirty }, operation) => removeGroup(state, operation.id ?? value.id, dirty),
  "group.assignNode": ({ state, value, dirty }) => assignNodeGroup(state, value.nodeId, value.groupId ?? null, dirty),
  "group.assignGroup": ({ state, value, dirty }) => assignGroupParent(state, value.groupId, value.parentGroupId ?? null, dirty),
  "selection.replace": ({ state, value, dirty }) => { state.selection = new Set(value.ids ?? []); dirty.selection = true; },
  "viewport.set": ({ state, value, dirty, options }) => { state.viewport = { ...state.viewport, ...value, zoom: clamp(value.zoom ?? state.viewport.zoom, options.minZoom, options.maxZoom) }; dirty.viewport = true; },
  "viewport.fit": ({ state, value, dirty, options }) => { fitViewport(state, value.padding ?? 64, options); dirty.viewport = true; },
  "viewport.center": ({ state, value, dirty, options }) => { centerViewport(state, value.ids ?? [], options); dirty.viewport = true; }
}, operation => { throw new GhostagramError("CAPABILITY_UNSUPPORTED", `Operation '${operation.type}' is not supported.`, { operation: operation.type }); });

function applyOperation(state, op, dirty, options) {
  op = omitNullProperties(op);
  requireObject(op, "INVALID_MODEL", "Operations must be objects.");
  modelOperationDispatcher.dispatch(op, { state, dirty, options, value: op.value ?? op });
}
const propertyModes = new Set(["display", "edit", "displayAndEdit", "hidden"]);
const editablePropertyTypes = new Set(["string", "boolean", "integer", "decimal", "number", "date", "dateTime", "datetime", "enum", "json"]);
const propertyEditorKinds = new Set(["auto", "text", "multiline", "toggle", "number", "range", "color", "date", "dateTime", "select", "json"]);
function normaliseNodeProperties(raw) {
  if (raw === null) return [];
  if (!Array.isArray(raw)) throw new GhostagramError("INVALID_MODEL", "Node properties must be an array or null.");
  const ids = new Set();
  return raw.map((candidate, index) => {
    requireObject(candidate, "INVALID_MODEL", "Each node property must be an object.");
    const id = candidate.id;
    if (!id || typeof id !== "string" || ids.has(id)) throw new GhostagramError("INVALID_MODEL", "Node properties require unique string ids.");
    ids.add(id);
    const type = candidate.type ?? "string", mode = candidate.mode ?? "display";
    if (typeof type !== "string" || !type.trim()) throw new GhostagramError("INVALID_MODEL", `Node property '${id}' requires a non-empty type identifier.`);
    if (!propertyModes.has(mode)) throw new GhostagramError("INVALID_MODEL", `Node property '${id}' has an unsupported mode '${mode}'.`);
    if (candidate.name !== undefined && typeof candidate.name !== "string") throw new GhostagramError("INVALID_MODEL", "Node property name must be a string.");
    if (candidate.label !== undefined && candidate.label !== null && typeof candidate.label !== "string") throw new GhostagramError("INVALID_MODEL", "Node property label must be a string or null.");
    if (candidate.options !== undefined && candidate.options !== null && (!Array.isArray(candidate.options) || candidate.options.some(option => typeof option !== "string"))) throw new GhostagramError("INVALID_MODEL", "Node property options must be string values.");
    if (candidate.sectionId !== undefined && candidate.sectionId !== null && (typeof candidate.sectionId !== "string" || !candidate.sectionId)) throw new GhostagramError("INVALID_MODEL", `Node property '${id}' sectionId must be a non-empty string or null.`);
    const property = { ...candidate, id, name: candidate.name ?? id, type, mode, order: Number.isSafeInteger(candidate.order) ? candidate.order : index, options: candidate.options ?? null };
    const editor = normalisePropertyEditor(candidate.editor, id);
    if (candidate.sectionId != null) property.sectionId = candidate.sectionId; else delete property.sectionId;
    if (editor) property.editor = editor; else delete property.editor;
    return property;
  }).sort((left, right) => left.order - right.order || left.id.localeCompare(right.id));
}
function normalisePropertyEditor(raw, propertyId) {
  if (raw === undefined || raw === null) return null;
  requireObject(raw, "INVALID_MODEL", `Node property '${propertyId}' editor must be an object or null.`);
  const kind = raw.kind ?? "auto";
  if (!propertyEditorKinds.has(kind)) throw new GhostagramError("INVALID_MODEL", `Node property '${propertyId}' has unsupported editor kind '${kind}'.`);
  if (raw.placeholder !== undefined && raw.placeholder !== null && typeof raw.placeholder !== "string") throw new GhostagramError("INVALID_MODEL", `Node property '${propertyId}' editor placeholder must be a string or null.`);
  for (const key of ["minimum", "maximum", "step"]) if (raw[key] !== undefined && raw[key] !== null && !Number.isFinite(raw[key])) throw new GhostagramError("INVALID_MODEL", `Node property '${propertyId}' editor ${key} must be numeric or null.`);
  if (raw.minimum != null && raw.maximum != null && raw.minimum > raw.maximum) throw new GhostagramError("INVALID_MODEL", `Node property '${propertyId}' editor minimum cannot exceed maximum.`);
  if (raw.step != null && raw.step <= 0) throw new GhostagramError("INVALID_MODEL", `Node property '${propertyId}' editor step must be positive.`);
  return { ...raw, kind, placeholder: raw.placeholder ?? null, minimum: raw.minimum ?? null, maximum: raw.maximum ?? null, step: raw.step ?? null };
}
function normaliseNodeSections(raw, properties) {
  if (raw === undefined || raw === null) {
    const orphan = properties.find(property => property.sectionId);
    if (orphan) throw new GhostagramError("MISSING_REFERENCE", `Node property '${orphan.id}' references missing section '${orphan.sectionId}'.`);
    return null;
  }
  if (!Array.isArray(raw)) throw new GhostagramError("INVALID_MODEL", "Node sections must be an array or null.");
  const ids = new Set();
  const sections = raw.map((candidate, index) => {
    requireObject(candidate, "INVALID_MODEL", "Each node section must be an object.");
    if (!candidate.id || typeof candidate.id !== "string" || ids.has(candidate.id)) throw new GhostagramError("INVALID_MODEL", "Node sections require unique string ids.");
    ids.add(candidate.id);
    if (candidate.title !== undefined && typeof candidate.title !== "string") throw new GhostagramError("INVALID_MODEL", `Node section '${candidate.id}' title must be a string.`);
    if (candidate.parentSectionId !== undefined && candidate.parentSectionId !== null && (typeof candidate.parentSectionId !== "string" || !candidate.parentSectionId)) throw new GhostagramError("INVALID_MODEL", `Node section '${candidate.id}' parentSectionId must be a non-empty string or null.`);
    if (candidate.collapsible !== undefined && typeof candidate.collapsible !== "boolean") throw new GhostagramError("INVALID_MODEL", `Node section '${candidate.id}' collapsible must be boolean.`);
    return { ...candidate, title: candidate.title ?? candidate.id, parentSectionId: candidate.parentSectionId ?? null, order: Number.isSafeInteger(candidate.order) ? candidate.order : index, collapsible: candidate.collapsible ?? true };
  }).sort((left, right) => left.order - right.order || left.id.localeCompare(right.id));
  for (const section of sections) if (section.parentSectionId && !ids.has(section.parentSectionId)) throw new GhostagramError("MISSING_REFERENCE", `Node section '${section.id}' references missing parent section '${section.parentSectionId}'.`);
  for (const section of sections) {
    const ancestors = new Set([section.id]); let parentId = section.parentSectionId, depth = 0;
    while (parentId) { if (ancestors.has(parentId)) throw new GhostagramError("INVALID_MODEL", "Node section nesting contains a cycle."); if (++depth > 8) throw new GhostagramError("INVALID_MODEL", "Node section nesting cannot exceed 8 levels."); ancestors.add(parentId); parentId = sections.find(candidate => candidate.id === parentId)?.parentSectionId ?? null; }
  }
  for (const property of properties) if (property.sectionId && !ids.has(property.sectionId)) throw new GhostagramError("MISSING_REFERENCE", `Node property '${property.id}' references missing section '${property.sectionId}'.`);
  return sections;
}
function normaliseNodePresentation(raw, height, sections) {
  if (raw === undefined || raw === null) return null;
  requireObject(raw, "INVALID_MODEL", "Node presentation must be an object or null.");
  const displayMode = raw.displayMode ?? "expanded";
  if (!["expanded", "compact", "collapsed"].includes(displayMode)) throw new GhostagramError("INVALID_MODEL", `Node presentation displayMode '${displayMode}' is unsupported.`);
  const expandedHeight = raw.expandedHeight ?? height;
  if (!Number.isFinite(expandedHeight) || expandedHeight <= 0) throw new GhostagramError("INVALID_MODEL", "Node presentation expandedHeight must be positive.");
  const collapsedSectionIds = raw.collapsedSectionIds ?? [];
  if (!Array.isArray(collapsedSectionIds) || collapsedSectionIds.some(id => typeof id !== "string") || new Set(collapsedSectionIds).size !== collapsedSectionIds.length) throw new GhostagramError("INVALID_MODEL", "Node presentation collapsedSectionIds must contain unique strings.");
  const byId = new Map((sections ?? []).map(section => [section.id, section]));
  for (const id of collapsedSectionIds) {
    const section = byId.get(id);
    if (!section) throw new GhostagramError("MISSING_REFERENCE", `Node presentation references missing section '${id}'.`);
    if (!section.collapsible) throw new GhostagramError("INVALID_MODEL", `Node section '${id}' is not collapsible.`);
  }
  return { ...raw, displayMode, expandedHeight, collapsedSectionIds: [...collapsedSectionIds] };
}
function propertyEditorKind(property) {
  const explicit = property.editor?.kind;
  if (explicit && explicit !== "auto") return explicit;
  if (property.type === "boolean") return "toggle";
  if (["integer", "decimal", "number"].includes(property.type)) return "number";
  if (property.type === "date") return "date";
  if (property.type === "dateTime" || property.type === "datetime") return "dateTime";
  if (property.type === "enum") return "select";
  if (property.type === "json") return "json";
  return "text";
}
function propertyRowHeight(property, compact) {
  if (compact) return NODE_COMPACT_PROPERTY_HEIGHT;
  return ["multiline", "json"].includes(propertyEditorKind(property)) ? 48 : propertyEditorKind(property) === "range" ? 28 : NODE_PROPERTY_HEIGHT;
}
function nodeLayoutProjection(node) {
  const properties = node?.properties ?? [], sections = node?.sections ?? null, presentation = node?.presentation ?? null;
  const displayMode = presentation?.displayMode ?? "expanded", compact = displayMode === "compact", progressive = Boolean(sections?.length || presentation), header = { y: 0, height: NODE_PROPERTY_TOP };
  const layout = { progressive, displayMode, compact, header, rows: [], unsectionedRows: [], sections: [], rootSections: [], propertyAnchors: new Map(), minimumHeight: NODE_PROPERTY_TOP };
  if (displayMode === "collapsed") {
    for (const property of properties) layout.propertyAnchors.set(property.id, { type: "property", side: null, offsetY: NODE_PROPERTY_TOP / 2, proxied: true, proxy: "node" });
    return layout;
  }
  let cursor = NODE_PROPERTY_TOP;
  const visible = properties.filter(property => property.mode !== "hidden"), bySection = new Map(), unsectioned = [];
  for (const property of visible) (property.sectionId ? (bySection.get(property.sectionId) ?? bySection.set(property.sectionId, []).get(property.sectionId)) : unsectioned).push(property);
  const addRow = (property, depth, sectionId, target) => {
    const height = propertyRowHeight(property, compact), row = { property, y: cursor, height, depth, sectionId, compact };
    cursor += height + NODE_PROPERTY_GAP; layout.rows.push(row); target.push(row);
    layout.propertyAnchors.set(property.id, { type: "property", side: null, offsetY: row.y + row.height / 2 });
  };
  for (const property of unsectioned) addRow(property, 0, null, layout.unsectionedRows);
  const sectionByParent = new Map();
  for (const section of sections ?? []) (sectionByParent.get(section.parentSectionId) ?? sectionByParent.set(section.parentSectionId, []).get(section.parentSectionId)).push(section);
  const collapsed = new Set(presentation?.collapsedSectionIds ?? []);
  const descendantPropertyIds = sectionId => {
    const result = [...(bySection.get(sectionId) ?? [])].map(property => property.id);
    for (const child of sectionByParent.get(sectionId) ?? []) result.push(...descendantPropertyIds(child.id));
    return result;
  };
  const addSection = (section, depth) => {
    const start = cursor, sectionLayout = { section, y: start, height: NODE_SECTION_HEADER_HEIGHT, depth, collapsed: collapsed.has(section.id), rows: [], children: [] };
    cursor += NODE_SECTION_HEADER_HEIGHT + NODE_PROPERTY_GAP;
    if (sectionLayout.collapsed) {
      for (const propertyId of descendantPropertyIds(section.id)) layout.propertyAnchors.set(propertyId, { type: "property", side: null, offsetY: start + NODE_SECTION_HEADER_HEIGHT / 2, proxied: true, proxy: section.id });
    } else {
      for (const property of bySection.get(section.id) ?? []) addRow(property, depth + 1, section.id, sectionLayout.rows);
      for (const child of sectionByParent.get(section.id) ?? []) sectionLayout.children.push(addSection(child, depth + 1));
    }
    sectionLayout.height = Math.max(NODE_SECTION_HEADER_HEIGHT, cursor - start - NODE_PROPERTY_GAP); layout.sections.push(sectionLayout); return sectionLayout;
  };
  for (const section of sectionByParent.get(null) ?? []) layout.rootSections.push(addSection(section, 0));
  for (const property of properties) if (!layout.propertyAnchors.has(property.id)) layout.propertyAnchors.set(property.id, { type: "property", side: null, offsetY: NODE_PROPERTY_TOP / 2, proxied: true, proxy: "node" });
  layout.minimumHeight = layout.rows.length || layout.sections.length
    ? Math.max(NODE_PROPERTY_TOP, cursor + NODE_BODY_BOTTOM_PADDING - NODE_PROPERTY_GAP)
    : NODE_PROPERTY_TOP;
  return layout;
}
function nextNodePresentationRequest(node, gridSize = 16) {
  const current = node.presentation?.displayMode ?? "expanded", displayMode = current === "expanded" ? "compact" : current === "compact" ? "collapsed" : "expanded";
  const expandedHeight = current === "expanded" ? Math.max(node.height, node.presentation?.expandedHeight ?? node.height) : node.presentation?.expandedHeight ?? node.height;
  const presentation = { ...(node.presentation ?? {}), displayMode, expandedHeight, collapsedSectionIds: [...(node.presentation?.collapsedSectionIds ?? [])] };
  const minimumHeight = nodeLayoutProjection({ ...node, presentation }).minimumHeight;
  const height = ceilToGrid(displayMode === "collapsed" ? NODE_PROPERTY_TOP : displayMode === "expanded" ? Math.max(expandedHeight, minimumHeight) : minimumHeight, gridSize);
  presentation.expandedHeight = ceilToGrid(presentation.expandedHeight, gridSize);
  return { label: displayMode === "compact" ? "Use compact view for" : displayMode === "collapsed" ? "Collapse" : "Expand", payload: { nodeId: node.id, height, presentation } };
}
function nextSectionPresentationRequest(node, sectionId, gridSize = 16) {
  const collapsedIds = new Set(node.presentation?.collapsedSectionIds ?? []), collapsed = !collapsedIds.has(sectionId);
  collapsed ? collapsedIds.add(sectionId) : collapsedIds.delete(sectionId);
  const expandedHeight = node.presentation?.expandedHeight ?? node.height;
  const presentation = { ...(node.presentation ?? {}), displayMode: node.presentation?.displayMode ?? "expanded", expandedHeight, collapsedSectionIds: [...collapsedIds] };
  const minimumHeight = nodeLayoutProjection({ ...node, presentation }).minimumHeight;
  const projectedHeight = presentation.displayMode === "expanded"
    ? collapsed ? Math.max(NODE_PROPERTY_TOP, Math.min(node.height, minimumHeight)) : Math.max(minimumHeight, expandedHeight)
    : minimumHeight;
  const height = ceilToGrid(projectedHeight, gridSize);
  presentation.expandedHeight = ceilToGrid(presentation.expandedHeight, gridSize);
  return { label: collapsed ? "Collapse section" : "Expand section", payload: { nodeId: node.id, sectionId, collapsed, height, presentation } };
}
function propertyPortAnchor(node, propertyId, direction = "both") {
  const anchor = nodeLayoutProjection(node).propertyAnchors.get(propertyId);
  return anchor ? { ...anchor, side: direction === "target" ? "left" : "right" } : null;
}
function resolvePortAnchor(state, port) {
  if (port?.propertyId) return propertyPortAnchor(state.nodes.get(port.nodeId), port.propertyId, port.direction) ?? port.anchor ?? "right";
  if (port?.anchor != null) return port.anchor;
  const node = port && state.nodes.get(port.nodeId);
  return node?.properties?.length ? orderedNodePortAnchor(state, node, port) ?? "right" : "right";
}
function portRenderPlan(state, nodeId) {
  const ports = [...(state.portsByNode.get(nodeId) ?? [])].map(id => state.ports.get(id)).filter(Boolean)
    .sort((left, right) => left.order - right.order || left.id.localeCompare(right.id));
  const entries = [], proxies = new Map();
  for (const port of ports) {
    const anchor = resolvePortAnchor(state, port);
    if (!anchor?.proxied) { entries.push({ port, ports: [port], anchor }); continue; }
    const key = `${anchor.proxy ?? "node"}|${anchor.side}|${anchor.offsetY}`;
    const entry = proxies.get(key) ?? { port, ports: [], anchor };
    entry.ports.push(port); proxies.set(key, entry);
  }
  for (const entry of proxies.values()) {
    entry.port = entry.ports.find(port => port.enabled !== false && endpointDescriptor(port).type !== "blank")
      ?? entry.ports.find(port => endpointDescriptor(port).type !== "blank")
      ?? entry.ports[0];
    entries.push(entry);
  }
  return entries.sort((left, right) => (left.anchor?.offsetY ?? 0) - (right.anchor?.offsetY ?? 0) || left.port.order - right.port.order || left.port.id.localeCompare(right.port.id));
}
function proxyPortDescriptor(ports) {
  const ordered = [...ports].sort((left, right) => left.order - right.order || left.id.localeCompare(right.id)), labels = ordered.map(port => port.label ?? port.id);
  return { portIds: ordered.map(port => port.id), enabled: ordered.some(port => port.enabled !== false), label: ordered.length === 1 ? labels[0] : `Collapsed connections (${ordered.length}): ${labels.join(", ")}` };
}
function portVisualDescriptor(port, endpoint = endpointDescriptor(port)) {
  const propertyBound = typeof port?.propertyId === "string" && port.propertyId.length > 0;
  return {
    kind: propertyBound ? "property" : "node",
    borderRadius: propertyBound ? "3px" : endpoint.type === "rectangle" ? "1px" : "50%"
  };
}
function orderedNodePortAnchor(state, node, port) {
  const ordered = [...(state.portsByNode.get(node.id) ?? [])]
    .map(id => state.ports.get(id)).filter(Boolean)
    .sort((left, right) => left.order - right.order || left.id.localeCompare(right.id));
  const slot = ordered.findIndex(candidate => candidate.id === port.id);
  if (slot < 0) return null;
  return { type: "ordered", side: port.direction === "target" ? "left" : "right", offsetY: NODE_PROPERTY_TOP + NODE_PROPERTY_HEIGHT / 2 + slot * (NODE_PROPERTY_HEIGHT + NODE_PROPERTY_GAP) };
}
function nodeContentMinimumHeight(state, node) {
  const propertyBottom = nodeLayoutProjection(node).minimumHeight;
  let portBottom = 0;
  for (const portId of state.portsByNode.get(node.id) ?? []) {
    const port = state.ports.get(portId), anchor = port && resolvePortAnchor(state, port);
    if (isPixelSideAnchor(anchor)) portBottom = Math.max(portBottom, anchor.offsetY + 8);
  }
  return Math.max(propertyBottom, portBottom);
}
function propertyDisplayValue(property) {
  if (property.value === null || property.value === undefined) return "—";
  if (property.type === "boolean") return property.value ? "Yes" : "No";
  if (property.type === "json") { try { return JSON.stringify(property.value); } catch { return String(property.value); } }
  return String(property.value);
}
function propertyInput(property) {
  const type = property.type, kind = propertyEditorKind(property), editor = property.editor ?? {};
  if (kind === "toggle") { const input = document.createElement("input"); input.type = "checkbox"; input.checked = property.value === true; return input; }
  if (kind === "select") { const select = document.createElement("select"); for (const optionValue of property.options ?? []) { const option = document.createElement("option"); option.value = optionValue; option.textContent = optionValue; option.selected = optionValue === property.value; select.append(option); } return select; }
  const input = document.createElement(kind === "multiline" || kind === "json" ? "textarea" : "input");
  if (input.tagName === "INPUT") input.type = kind === "number" ? "number" : kind === "range" ? "range" : kind === "color" ? "color" : kind === "date" ? "date" : kind === "dateTime" ? "datetime-local" : "text";
  if (editor.placeholder) input.placeholder = editor.placeholder;
  if (editor.minimum != null) input.min = String(editor.minimum);
  if (editor.maximum != null) input.max = String(editor.maximum);
  if (editor.step != null) input.step = String(editor.step);
  input.value = type === "json"
    ? (property.value === null || property.value === undefined ? "" : JSON.stringify(property.value))
    : type === "dateTime" || type === "datetime" ? dateTimeInputValue(property.value) : property.value ?? "";
  return input;
}
function propertyEditorStyle(height = NODE_PROPERTY_HEIGHT) { return `min-width:0;width:100%;height:${height}px;border:1px solid ${NODE_PROPERTY_EDITOR_BORDER};border-radius:3px;background:rgba(255,255,255,.92);color:${NODE_PROPERTY_EDITOR_COLOR};font:inherit;padding:1px 4px;box-sizing:border-box;accent-color:#4f46e5;resize:none;`; }
function propertyInputValue(input, property) {
  const kind = propertyEditorKind(property);
  if (kind === "toggle" || property.type === "boolean") return Boolean(input.checked);
  if (property.type === "integer") return input.value === "" ? null : Number.parseInt(input.value, 10);
  if (property.type === "decimal" || property.type === "number") return input.value === "" ? null : Number(input.value);
  if (property.type === "dateTime" || property.type === "datetime") return input.value === "" ? null : new Date(input.value).toISOString();
  if (property.type === "json") { if (input.value.trim() === "") return null; try { return JSON.parse(input.value); } catch { throw new GhostagramError("INVALID_PROPERTY_VALUE", "Enter valid JSON."); } }
  return input.value;
}
function propertyValueSignature(value) {
  if (value === undefined) return "undefined";
  try { return JSON.stringify(value); } catch { return String(value); }
}
function propertyCommitDecision(previousSignature, value) {
  const signature = propertyValueSignature(value);
  return { signature, commit: signature !== previousSignature };
}
function dateTimeInputValue(value) {
  if (value === null || value === undefined || value === "") return "";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return String(value);
  const local = new Date(parsed.getTime() - parsed.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}
function upsertNode(s, raw) {
  const { locked: _legacyLocked, ...value } = raw;
  requireId(value, "node");
  if (value.rotation !== undefined && !Number.isFinite(value.rotation)) throw new GhostagramError("INVALID_MODEL", "Node rotation must be numeric.");
  const prior = s.nodes.get(value.id), { locked: _legacyPriorLock, ...previous } = prior ?? {};
  const height = positive(value.height, "INVALID_MODEL", "Node height is required."), properties = Object.hasOwn(value, "properties") ? normaliseNodeProperties(value.properties) : previous.properties ?? [];
  const sections = Object.hasOwn(value, "sections") ? normaliseNodeSections(value.sections, properties) : previous.sections ?? null;
  // A property delta can change its section reference while the section catalog is unchanged.
  normaliseNodeSections(sections, properties);
  const presentation = Object.hasOwn(value, "presentation") ? normaliseNodePresentation(value.presentation, height, sections) : normaliseNodePresentation(previous.presentation, height, sections);
  const node = { ...previous, ...value, x: number(value.x, "INVALID_MODEL", "Node x is required."), y: number(value.y, "INVALID_MODEL", "Node y is required."), width: positive(value.width, "INVALID_MODEL", "Node width is required."), height, rotation: value.rotation ?? previous.rotation ?? 0, icon: Object.hasOwn(value, "icon") ? iconifyIconName(value.icon) : previous.icon ?? null, resizable: value.resizable ?? previous.resizable ?? true, rotatable: value.rotatable ?? previous.rotatable ?? true, labelEditable: value.labelEditable ?? previous.labelEditable ?? true, style: value.style ?? previous.style ?? {}, properties };
  if (sections) node.sections = sections; else delete node.sections;
  if (presentation) node.presentation = presentation; else delete node.presentation;
  if (typeof node.resizable !== "boolean") throw new GhostagramError("INVALID_MODEL", "Node resizable must be boolean.");
  if (typeof node.rotatable !== "boolean") throw new GhostagramError("INVALID_MODEL", "Node rotatable must be boolean.");
  if (typeof node.labelEditable !== "boolean") throw new GhostagramError("INVALID_MODEL", "Node labelEditable must be boolean.");
  if (node.groupId && !s.groups.has(node.groupId)) throw new GhostagramError("MISSING_REFERENCE", `Node '${node.id}' references missing group '${node.groupId}'.`);
  if (prior?.groupId && prior.groupId !== node.groupId) s.nodesByGroup.get(prior.groupId)?.delete(node.id);
  if (node.groupId) (s.nodesByGroup.get(node.groupId) ?? s.nodesByGroup.set(node.groupId, new Set()).get(node.groupId)).add(node.id);
  s.nodes.set(node.id, node); if (!s.portsByNode.has(node.id)) s.portsByNode.set(node.id, new Set());
}
function upsertPort(s, raw) { requireId(raw, "port"); if (!s.nodes.has(raw.nodeId)) throw new GhostagramError("MISSING_REFERENCE", `Port '${raw.id}' references missing node '${raw.nodeId}'.`); const prior = s.ports.get(raw.id); if (prior && prior.nodeId !== raw.nodeId) s.portsByNode.get(prior.nodeId)?.delete(raw.id); const port = { ...prior, ...raw, direction: raw.direction ?? prior?.direction ?? "both", endpoint: raw.endpoint ?? prior?.endpoint ?? "dot", scope: raw.scope ?? prior?.scope ?? "*", maxConnections: raw.maxConnections ?? prior?.maxConnections ?? -1, enabled: raw.enabled ?? prior?.enabled ?? true, connectionPolicy: Object.hasOwn(raw, "connectionPolicy") ? raw.connectionPolicy : prior?.connectionPolicy, propertyId: Object.hasOwn(raw, "propertyId") ? raw.propertyId : prior?.propertyId ?? null, label: Object.hasOwn(raw, "label") ? raw.label : prior?.label ?? null, order: raw.order ?? prior?.order ?? 0 }; if (!["source", "target", "both"].includes(port.direction)) throw new GhostagramError("INVALID_MODEL", "Port direction must be source, target, or both."); if (typeof port.scope !== "string" || !port.scope) throw new GhostagramError("INVALID_MODEL", "Port scope must be a non-empty string."); if (typeof port.enabled !== "boolean") throw new GhostagramError("INVALID_MODEL", "Port enabled must be boolean."); if (port.propertyId !== null && typeof port.propertyId !== "string") throw new GhostagramError("INVALID_MODEL", "Port propertyId must be a string or null."); if (port.label !== null && typeof port.label !== "string") throw new GhostagramError("INVALID_MODEL", "Port label must be a string or null."); if (!Number.isSafeInteger(port.order)) throw new GhostagramError("INVALID_MODEL", "Port order must be an integer."); validateConnectionPolicy(port.connectionPolicy); validateEndpoint(port.endpoint); s.ports.set(port.id, port); (s.portsByNode.get(port.nodeId) ?? s.portsByNode.set(port.nodeId, new Set()).get(port.nodeId)).add(port.id); if (!s.edgesByPort.has(port.id)) s.edgesByPort.set(port.id, new Set()); }
function upsertEdgeType(s, raw) { requireId(raw, "edge type"); validateEdgeTypeDescriptor(raw); s.edgeTypes.set(raw.id, { ...raw }); }
function removeEdgeType(s, id, dirty) { if (!s.edgeTypes.delete(id)) return; for (const edge of s.edges.values()) if (edge.type === id) dirty.edges.add(edge.id); }
function upsertEdge(s, raw) { requireId(raw, "edge"); const prior = s.edges.get(raw.id), source = s.ports.get(raw.sourcePortId), target = s.ports.get(raw.targetPortId); if (!source || !target) throw new GhostagramError("MISSING_REFERENCE", "Edge references a missing source or target port."); if (source.direction === "target" || target.direction === "source") throw new GhostagramError("INVALID_MODEL", "Port directions do not permit this connection."); if (!scopesCompatible(source, target)) throw new GhostagramError("SCOPE_MISMATCH", `Port scopes '${source.scope}' and '${target.scope}' are incompatible.`); if ((!prior || prior.sourcePortId !== source.id || prior.targetPortId !== target.id) && !connectionPoliciesCompatible(source, target)) throw new GhostagramError("CONNECTION_REJECTED", `Port policy rejects '${source.id}' to '${target.id}'.`); for (const portId of [prior?.sourcePortId, prior?.targetPortId]) if (portId) s.edgesByPort.get(portId)?.delete(raw.id); for (const port of [source, target]) { const used = s.edgesByPort.get(port.id)?.size ?? 0; if (port.maxConnections >= 0 && used >= port.maxConnections && (!prior || ![prior.sourcePortId, prior.targetPortId].includes(port.id))) throw new GhostagramError("PORT_FULL", `Port '${port.id}' is at its connection limit.`); }
  const preliminary = { ...prior, ...raw, connector: raw.connector ?? prior?.connector, overlays: raw.overlays ?? prior?.overlays, style: raw.style ?? prior?.style, waypoints: raw.waypoints ?? prior?.waypoints ?? [], detachable: raw.detachable ?? prior?.detachable, reconnectable: raw.reconnectable ?? prior?.reconnectable, labelEditable: raw.labelEditable ?? prior?.labelEditable ?? true };
  const edge = preliminary.type == null ? { ...preliminary, type: undefined, connector: preliminary.connector ?? "flowchart", overlays: preliminary.overlays ?? [], style: preliminary.style ?? {}, detachable: preliminary.detachable ?? true, reconnectable: preliminary.reconnectable ?? true } : preliminary;
  const resolved = resolveEdgeDescriptor(edge, s.edgeTypes); if (!supported.connectors.includes(resolved.connector) && !connectorRegistry.has(resolved.connector)) throw new GhostagramError("CAPABILITY_UNSUPPORTED", `Connector '${resolved.connector}' is unsupported.`); if (typeof resolved.detachable !== "boolean" || typeof resolved.reconnectable !== "boolean") throw new GhostagramError("INVALID_MODEL", "Edge detachable and reconnectable flags must be boolean."); if (typeof edge.labelEditable !== "boolean") throw new GhostagramError("INVALID_MODEL", "Edge labelEditable must be boolean."); if ((edge.labelOffsetX !== undefined && !Number.isFinite(edge.labelOffsetX)) || (edge.labelOffsetY !== undefined && !Number.isFinite(edge.labelOffsetY))) throw new GhostagramError("INVALID_MODEL", "Edge label offsets must be numeric."); if (!Array.isArray(resolved.waypoints) || resolved.waypoints.some(point => !Number.isFinite(point?.x) || !Number.isFinite(point?.y))) throw new GhostagramError("INVALID_MODEL", "Edge waypoints must be { x, y } coordinates."); validateOverlays(resolved.overlays); edgeStyleDescriptor(resolved.style); flowchartOptions(resolved.connectorOptions); flowAnimationDescriptor(resolved.animation); s.edges.set(edge.id, edge); for (const portId of [edge.sourcePortId, edge.targetPortId]) (s.edgesByPort.get(portId) ?? s.edgesByPort.set(portId, new Set()).get(portId)).add(edge.id); }
function upsertGroup(s, raw, deferParentValidation = false) {
  const { locked: _legacyLocked, ...value } = raw;
  requireId(value, "group");
  const prior = s.groups.get(value.id), { locked: _legacyPriorLock, ...previous } = prior ?? {}, parentGroupId = Object.hasOwn(value, "parentGroupId") ? value.parentGroupId : previous.parentGroupId ?? null;
  if (parentGroupId !== null && typeof parentGroupId !== "string") throw new GhostagramError("INVALID_MODEL", "Group parentGroupId must be a string or null.");
  if (parentGroupId === value.id) throw new GhostagramError("INVALID_MODEL", `Group '${value.id}' cannot parent itself.`);
  if (!deferParentValidation && parentGroupId && !s.groups.has(parentGroupId)) throw new GhostagramError("MISSING_REFERENCE", `Group '${value.id}' references missing parent group '${parentGroupId}'.`);
  if (!deferParentValidation && parentGroupId && isGroupDescendant(s, value.id, parentGroupId)) throw new GhostagramError("INVALID_MODEL", `Group '${value.id}' cannot be parented by its descendant '${parentGroupId}'.`);
  const group = { ...previous, ...value, parentGroupId, x: number(value.x, "INVALID_MODEL", "Group x is required."), y: number(value.y, "INVALID_MODEL", "Group y is required."), width: positive(value.width, "INVALID_MODEL", "Group width is required."), height: positive(value.height, "INVALID_MODEL", "Group height is required."), collapsed: Object.hasOwn(value, "collapsed") ? Boolean(value.collapsed) : previous.collapsed ?? false, icon: Object.hasOwn(value, "icon") ? iconifyIconName(value.icon) : previous.icon ?? null, labelEditable: value.labelEditable ?? previous.labelEditable ?? true };
  if (typeof group.labelEditable !== "boolean") throw new GhostagramError("INVALID_MODEL", "Group labelEditable must be boolean.");
  if (prior?.parentGroupId && prior.parentGroupId !== parentGroupId) s.groupsByGroup.get(prior.parentGroupId)?.delete(group.id);
  if (parentGroupId) (s.groupsByGroup.get(parentGroupId) ?? s.groupsByGroup.set(parentGroupId, new Set()).get(parentGroupId)).add(group.id);
  s.groups.set(group.id, group);
}
function removeNode(s, id, dirty) { const node = s.nodes.get(id), ports = [...(s.portsByNode.get(id) ?? [])]; for (const port of ports) removePort(s, port, dirty); if (node?.groupId) s.nodesByGroup.get(node.groupId)?.delete(id); s.nodes.delete(id); s.portsByNode.delete(id); dirty.nodes.add(id); }
function removePort(s, id, dirty) { for (const edge of [...(s.edgesByPort.get(id) ?? [])]) removeEdge(s, edge, dirty); const port = s.ports.get(id); if (port) s.portsByNode.get(port.nodeId)?.delete(id); s.ports.delete(id); s.edgesByPort.delete(id); if (port) dirty.nodes.add(port.nodeId); }
function removeEdge(s, id, dirty) { const edge = s.edges.get(id); if (edge) { s.edgesByPort.get(edge.sourcePortId)?.delete(id); s.edgesByPort.get(edge.targetPortId)?.delete(id); } s.edges.delete(id); dirty.edges.add(id); }
function assignNodeGroup(s, nodeId, groupId, dirty) {
  const node = s.nodes.get(nodeId);
  if (!node) throw new GhostagramError("MISSING_REFERENCE", `Group assignment references missing node '${nodeId}'.`);
  if (groupId && !s.groups.has(groupId)) throw new GhostagramError("MISSING_REFERENCE", `Group assignment references missing group '${groupId}'.`);
  const previousGroupId = node.groupId;
  if (previousGroupId === groupId) return;
  if (previousGroupId) s.nodesByGroup.get(previousGroupId)?.delete(nodeId);
  if (groupId) (s.nodesByGroup.get(groupId) ?? s.nodesByGroup.set(groupId, new Set()).get(groupId)).add(nodeId);
  s.nodes.set(nodeId, { ...node, groupId }); dirty.nodes.add(nodeId);
  for (const id of incident(s, nodeId)) dirty.edges.add(id);
  if (previousGroupId) dirty.groups.add(previousGroupId); if (groupId) dirty.groups.add(groupId);
}
function assignGroupParent(s, groupId, parentGroupId, dirty) {
  const group = s.groups.get(groupId);
  if (!group) throw new GhostagramError("MISSING_REFERENCE", `Group assignment references missing group '${groupId}'.`);
  if (parentGroupId && !s.groups.has(parentGroupId)) throw new GhostagramError("MISSING_REFERENCE", `Group assignment references missing parent group '${parentGroupId}'.`);
  if (parentGroupId === groupId || (parentGroupId && isGroupDescendant(s, groupId, parentGroupId))) throw new GhostagramError("INVALID_MODEL", "A group cannot be parented by itself or one of its descendants.");
  if (group.parentGroupId === parentGroupId) return;
  if (group.parentGroupId) s.groupsByGroup.get(group.parentGroupId)?.delete(groupId);
  if (parentGroupId) (s.groupsByGroup.get(parentGroupId) ?? s.groupsByGroup.set(parentGroupId, new Set()).get(parentGroupId)).add(groupId);
  s.groups.set(groupId, { ...group, parentGroupId }); dirtyGroupTree(s, groupId, dirty);
  if (group.parentGroupId) dirty.groups.add(group.parentGroupId); if (parentGroupId) dirty.groups.add(parentGroupId);
}
function removeGroup(s, groupId, dirty) {
  for (const childId of [...(s.groupsByGroup.get(groupId) ?? [])]) assignGroupParent(s, childId, null, dirty);
  for (const nodeId of [...(s.nodesByGroup.get(groupId) ?? [])]) assignNodeGroup(s, nodeId, null, dirty);
  const group = s.groups.get(groupId);
  if (group?.parentGroupId) s.groupsByGroup.get(group.parentGroupId)?.delete(groupId);
  s.nodesByGroup.delete(groupId); s.groupsByGroup.delete(groupId); s.groups.delete(groupId); dirty.groups.add(groupId);
}
function validateState(s) {
  for (const group of s.groups.values()) {
    if (group.parentGroupId && !s.groups.has(group.parentGroupId)) throw new GhostagramError("MISSING_REFERENCE", `Group '${group.id}' references missing parent group '${group.parentGroupId}'.`);
    const ancestors = new Set([group.id]); let ancestorId = group.parentGroupId;
    while (ancestorId) { if (ancestors.has(ancestorId)) throw new GhostagramError("INVALID_MODEL", "Group nesting contains a cycle."); ancestors.add(ancestorId); ancestorId = s.groups.get(ancestorId)?.parentGroupId; }
  }
  for (const node of s.nodes.values()) if (node.groupId && !s.groups.has(node.groupId)) throw new GhostagramError("MISSING_REFERENCE", `Node '${node.id}' references missing group '${node.groupId}'.`);
  for (const port of s.ports.values()) {
    const node = s.nodes.get(port.nodeId);
    if (!node) throw new GhostagramError("MISSING_REFERENCE", `Port '${port.id}' references a missing node.`);
    if (port.propertyId && !(node.properties ?? []).some(property => property.id === port.propertyId)) throw new GhostagramError("MISSING_REFERENCE", `Port '${port.id}' references missing node property '${port.propertyId}'.`);
  }
  for (const edge of s.edges.values()) {
    resolveEdgeDescriptor(edge, s.edgeTypes);
    const source = s.ports.get(edge.sourcePortId), target = s.ports.get(edge.targetPortId);
    if (!source || !target) throw new GhostagramError("MISSING_REFERENCE", `Edge '${edge.id}' references a missing port.`);
    if (!scopesCompatible(source, target)) throw new GhostagramError("SCOPE_MISMATCH", `Edge '${edge.id}' has incompatible port scopes.`);
  }
  for (const id of s.selection) if (!s.nodes.has(id) && !s.edges.has(id) && !s.groups.has(id)) throw new GhostagramError("MISSING_REFERENCE", `Selection references missing node, edge, or group '${id}'.`);
}
function newDirty() { return { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false }; }
function mergeDirty(target, source) { target.all ||= source.all; for (const key of ["nodes", "edges", "groups"]) for (const id of source[key]) target[key].add(id); target.viewport ||= source.viewport; target.selection ||= source.selection; }
function incident(s, nodeId) { const result = new Set(); for (const port of s.portsByNode.get(nodeId) ?? []) for (const edge of s.edgesByPort.get(port) ?? []) result.add(edge); return result; }
function descendantGroupIds(s, groupId) {
  const result = [], pending = [...(s.groupsByGroup.get(groupId) ?? [])];
  while (pending.length) { const childId = pending.pop(); result.push(childId); pending.push(...(s.groupsByGroup.get(childId) ?? [])); }
  return result;
}
function descendantNodeIds(s, groupId) {
  const result = new Set();
  for (const id of [groupId, ...descendantGroupIds(s, groupId)]) for (const nodeId of s.nodesByGroup.get(id) ?? []) result.add(nodeId);
  return [...result];
}
function isGroupDescendant(s, groupId, candidateId) { return descendantGroupIds(s, groupId).includes(candidateId); }
function isGroupHiddenByCollapsedAncestor(s, group) {
  let ancestorId = group.parentGroupId;
  while (ancestorId) { const ancestor = s.groups.get(ancestorId); if (!ancestor) return false; if (ancestor.collapsed) return true; ancestorId = ancestor.parentGroupId; }
  return false;
}
function isNodeHiddenByCollapsedGroup(s, node) {
  let groupId = node.groupId;
  while (groupId) { const group = s.groups.get(groupId); if (!group) return false; if (group.collapsed) return true; groupId = group.parentGroupId; }
  return false;
}
function collapsedProxyGroupForNode(state, node) {
  let groupId = node.groupId, proxy = null;
  while (groupId) { const group = state.groups.get(groupId); if (!group) return proxy; if (group.collapsed) proxy = group; groupId = group.parentGroupId; }
  return proxy;
}
function edgeGeometry(state, edge, previewNodes = new Map()) {
  edge = resolveEdgeDescriptor(edge, state.edgeTypes);
  const source = state.ports.get(edge.sourcePortId), target = state.ports.get(edge.targetPortId), sourceNode = source && (previewNodes.get(source.nodeId) ?? state.nodes.get(source.nodeId)), targetNode = target && (previewNodes.get(target.nodeId) ?? state.nodes.get(target.nodeId));
  if (!source || !target || !sourceNode || !targetNode) return { hidden: true, proxied: false, sourcePoint: { x: 0, y: 0 }, targetPoint: { x: 0, y: 0 }, sourceSide: null, targetSide: null };
  const sourceProxy = collapsedProxyGroupForNode(state, sourceNode), targetProxy = collapsedProxyGroupForNode(state, targetNode);
  if (sourceProxy?.id && sourceProxy.id === targetProxy?.id) return { hidden: true, proxied: true, sourcePoint: { x: 0, y: 0 }, targetPoint: { x: 0, y: 0 }, sourceProxy, targetProxy, sourceSide: null, targetSide: null };
  const sourceItem = sourceProxy ?? sourceNode, targetItem = targetProxy ?? targetNode, sourceProxyAnchor = sourceProxy ? groupProxyAnchor(sourceProxy, targetItem) : null, targetProxyAnchor = targetProxy ? groupProxyAnchor(targetProxy, sourceItem) : null;
  const sourceAnchor = resolvePortAnchor(state, source), targetAnchor = resolvePortAnchor(state, target);
  const sourcePoint = sourceProxyAnchor?.point ?? anchorPoint(sourceNode, sourceAnchor, targetItem);
  const targetPoint = targetProxyAnchor?.point ?? anchorPoint(targetNode, targetAnchor, sourceItem);
  return { hidden: false, proxied: Boolean(sourceProxy || targetProxy), sourcePoint, targetPoint, sourceProxy, targetProxy, sourceNode, targetNode, sourceItem, targetItem, sourceSide: sourceProxyAnchor?.side ?? portAnchorSide(sourceAnchor), targetSide: targetProxyAnchor?.side ?? portAnchorSide(targetAnchor) };
}
function groupDepth(s, groupId) { let depth = 0, parentId = s.groups.get(groupId)?.parentGroupId; while (parentId) { depth += 1; parentId = s.groups.get(parentId)?.parentGroupId; } return depth; }
function groupsForRender(s) { return [...s.groups.values()].sort((left, right) => groupDepth(s, left.id) - groupDepth(s, right.id) || left.id.localeCompare(right.id)); }
function dirtyGroupTree(s, groupId, dirty) {
  const groupIds = [groupId, ...descendantGroupIds(s, groupId)];
  for (const id of groupIds) dirty.groups.add(id);
  for (const nodeId of descendantNodeIds(s, groupId)) { dirty.nodes.add(nodeId); for (const edgeId of incident(s, nodeId)) dirty.edges.add(edgeId); }
}
function scopesCompatible(source, target) { return source.scope === "*" || target.scope === "*" || source.scope === target.scope; }
function validateConnectionPolicy(policy) {
  // .NET optional record properties are serialized as null by JS interop.
  // Treat that identically to an omitted policy so a C# host can leave the
  // optional connection policy unspecified.
  if (policy === undefined || policy === null) return;
  if (!policy || typeof policy !== "object" || Array.isArray(policy)) throw new GhostagramError("INVALID_MODEL", "Port connectionPolicy must be an object.");
  const keys = ["allowPortIds", "denyPortIds", "allowNodeIds", "denyNodeIds"];
  for (const key of Object.keys(policy)) if (!keys.includes(key)) throw new GhostagramError("CAPABILITY_UNSUPPORTED", `Port connectionPolicy.${key} is unsupported.`);
  for (const key of keys) if (policy[key] !== undefined && (!Array.isArray(policy[key]) || policy[key].some(value => typeof value !== "string" || !value))) throw new GhostagramError("INVALID_MODEL", `Port connectionPolicy.${key} must be an array of non-empty strings.`);
}
function connectionPolicyAllows(port, peer) {
  const policy = port.connectionPolicy;
  if (!policy) return true;
  const permits = (key, value) => policy[key] === undefined || policy[key].includes(value);
  const rejects = (key, value) => !policy[key]?.includes(value);
  return permits("allowPortIds", peer.id) && permits("allowNodeIds", peer.nodeId) && rejects("denyPortIds", peer.id) && rejects("denyNodeIds", peer.nodeId);
}
function connectionPoliciesCompatible(source, target) { return connectionPolicyAllows(source, target) && connectionPolicyAllows(target, source); }
function canConnect(state, source, target) { return source.enabled !== false && target.enabled !== false && scopesCompatible(source, target) && connectionPoliciesCompatible(source, target) && [source, target].every(port => port.maxConnections < 0 || (state.edgesByPort.get(port.id)?.size ?? 0) < port.maxConnections); }
function canReconnect(state, edge, source, target) { return scopesCompatible(source, target) && connectionPoliciesCompatible(source, target) && [source, target].every(port => port.maxConnections < 0 || (state.edgesByPort.get(port.id)?.size ?? 0) - (port.id === edge.sourcePortId || port.id === edge.targetPortId ? 1 : 0) < port.maxConnections); }
function route(edge, a, b, geometry, routePoints) { const custom = connectorRegistry.get(edge.connector); if (custom) return custom(a, b, edge); if (edge.waypoints?.length) return `M ${a.x} ${a.y}${edge.waypoints.map(point => ` L ${point.x} ${point.y}`).join("")} L ${b.x} ${b.y}`; if (edge.connector === "straight") return `M ${a.x} ${a.y} L ${b.x} ${b.y}`; if (edge.connector === "bezier" || edge.connector === "state-machine") return bezierPath(a, b); return flowchartPath(routePoints ?? edgeRoutePoints(edge, a, b, geometry), flowchartOptions(edge.connectorOptions).cornerRadius); }
function bezierControlDistance(a, b) { return Math.max(48, Math.abs(b.x - a.x) * .45, Math.abs(b.y - a.y) * .28); }
function bezierPath(a, b) { const distance = bezierControlDistance(a, b), sourceControlX = svgCoordinate(a.x + distance), targetControlX = svgCoordinate(b.x - distance); return `M ${a.x} ${a.y} C ${sourceControlX} ${a.y}, ${targetControlX} ${b.y}, ${b.x} ${b.y}`; }
function svgCoordinate(value) { return Number(value.toFixed(3)); }
function anchorPoint(node, anchor, peerNode) {
  let point;
  if (isPixelSideAnchor(anchor)) point = { x: node.x + (anchor.side === "left" ? 0 : node.width), y: node.y + anchor.offsetY };
  else if (Array.isArray(anchor) && anchor.length === 2 && anchor.every(Number.isFinite)) point = { x: node.x + node.width * anchor[0], y: node.y + node.height * anchor[1] };
  else if (anchor && typeof anchor === "object" && Number.isFinite(anchor.x) && Number.isFinite(anchor.y)) point = { x: node.x + node.width * anchor.x, y: node.y + node.height * anchor.y };
  if (point) return rotateAnchorPoint(node, point);
  let side = typeof anchor === "object" ? anchor?.type : anchor ?? "right";
  if (side === "perimeter" && peerNode) return rotateAnchorPoint(node, perimeterAnchorPoint(node, peerNode));
  if ((side === "auto" || side === "continuous") && peerNode) {
    const dx = (peerNode.x + peerNode.width / 2) - (node.x + node.width / 2), dy = (peerNode.y + peerNode.height / 2) - (node.y + node.height / 2);
    side = Math.abs(dx) >= Math.abs(dy) ? (dx >= 0 ? "right" : "left") : (dy >= 0 ? "bottom" : "top");
  }
  return rotateAnchorPoint(node, { x: node.x + (side === "left" ? 0 : side === "right" ? node.width : node.width / 2), y: node.y + (side === "top" ? 0 : side === "bottom" ? node.height : node.height / 2) });
}
function portAnchorSide(anchor) {
  if (isPixelSideAnchor(anchor)) return anchor.side;
  const relative = Array.isArray(anchor) && anchor.length === 2 && anchor.every(Number.isFinite) ? anchor : anchor && typeof anchor === "object" && Number.isFinite(anchor.x) && Number.isFinite(anchor.y) ? [anchor.x, anchor.y] : null;
  if (relative) return relative[1] === 0 ? "top" : relative[1] === 1 ? "bottom" : relative[0] === 0 ? "left" : relative[0] === 1 ? "right" : null;
  const side = typeof anchor === "object" ? anchor?.type : anchor;
  return ["top", "right", "bottom", "left"].includes(side) ? side : null;
}
function rotateAnchorPoint(node, point) { const angle = node.rotation * Math.PI / 180; if (!angle) return point; const centerX = node.x + node.width / 2, centerY = node.y + node.height / 2, dx = point.x - centerX, dy = point.y - centerY; return { x: centerX + dx * Math.cos(angle) - dy * Math.sin(angle), y: centerY + dx * Math.sin(angle) + dy * Math.cos(angle) }; }
function rotationForPoint(node, point, increment = 1) { const centerX = node.x + node.width / 2, centerY = node.y + node.height / 2, degrees = Math.atan2(point.y - centerY, point.x - centerX) * 180 / Math.PI + 90, snapped = Math.round(degrees / increment) * increment; return (snapped % 360 + 360) % 360; }
function perimeterAnchorPoint(node, peerNode) {
  const centerX = node.x + node.width / 2, centerY = node.y + node.height / 2, peerX = peerNode.x + peerNode.width / 2, peerY = peerNode.y + peerNode.height / 2, dx = peerX - centerX, dy = peerY - centerY;
  if (!dx && !dy) return { x: node.x + node.width, y: centerY };
  const scale = 1 / Math.max(Math.abs(dx) / (node.width / 2), Math.abs(dy) / (node.height / 2));
  return { x: centerX + dx * scale, y: centerY + dy * scale };
}
function groupProxyAnchor(group, peer) {
  const centerX = group.x + group.width / 2, centerY = group.y + group.height / 2, peerX = peer.x + peer.width / 2, peerY = peer.y + peer.height / 2, dx = peerX - centerX, dy = peerY - centerY;
  const side = Math.abs(dx) >= Math.abs(dy) ? (dx >= 0 ? "right" : "left") : (dy >= 0 ? "bottom" : "top");
  return { side, point: side === "left" ? { x: group.x, y: centerY } : side === "right" ? { x: group.x + group.width, y: centerY } : side === "top" ? { x: centerX, y: group.y } : { x: centerX, y: group.y + group.height } };
}
function previewPointForPort(state, previewNodes, port, peerNode) {
  const node = previewNodes.get(port.nodeId) ?? state.nodes.get(port.nodeId);
  const effectivePeer = peerNode ? (previewNodes.get(peerNode.id) ?? peerNode) : undefined;
  return anchorPoint(node, resolvePortAnchor(state, port), effectivePeer);
}
function viewportPoint(pointer, bounds, viewport) {
  return { x: (pointer.clientX - bounds.left) / viewport.zoom + viewport.x, y: (pointer.clientY - bounds.top) / viewport.zoom + viewport.y };
}
function canvasCenterPoint(bounds, viewport) {
  return viewportPoint({ clientX: bounds.left + bounds.width / 2, clientY: bounds.top + bounds.height / 2 }, bounds, viewport);
}
function canvasHitDescriptor(pointer, bounds, viewport) {
  const point = viewportPoint(pointer, bounds, viewport);
  return { inside: pointer.clientX >= bounds.left && pointer.clientX <= bounds.right && pointer.clientY >= bounds.top && pointer.clientY <= bounds.bottom, ...point };
}
function dragPosition(start, pointer, zoom, gridSize) {
  return { x: snap(start.nodeX + (pointer.clientX - start.x) / zoom, gridSize), y: snap(start.nodeY + (pointer.clientY - start.y) / zoom, gridSize) };
}
function multiDragPositions(nodes, anchorId, start, pointer, zoom, gridSize) {
  const anchor = nodes.find(node => node.id === anchorId);
  if (!anchor) return [];
  const anchorPosition = dragPosition(start, pointer, zoom, gridSize), dx = anchorPosition.x - anchor.x, dy = anchorPosition.y - anchor.y;
  return nodes.map(node => node.id === anchorId ? { id: node.id, ...anchorPosition } : { id: node.id, x: snap(node.x + dx, gridSize), y: snap(node.y + dy, gridSize) });
}
function translatePositions(items, dx, dy, gridSize) { return items.map(item => ({ id: item.id, x: snap(item.x + dx, gridSize), y: snap(item.y + dy, gridSize) })); }
function groupMovePayload(groupId, parentGroupId, position, dx, dy, childGroups, members) {
  return {
    groupId,
    parentGroupId,
    ...position,
    dx,
    dy,
    groups: childGroups.map(child => ({ id: child.id, x: child.x + dx, y: child.y + dy })),
    nodes: members.map(member => ({ id: member.id, x: member.x + dx, y: member.y + dy, groupId: member.groupId ?? null }))
  };
}
function selectedMovePositions(state, selection, dx, dy, gridSize) {
  const selectedIds = [...selection], selectedGroupRoots = selectedIds.map(id => state.groups.get(id)).filter(group => group && !selectedIds.some(selectedId => selectedId !== group.id && isGroupDescendant(state, selectedId, group.id)));
  const selectedGroupIds = [...new Set(selectedGroupRoots.flatMap(group => [group.id, ...descendantGroupIds(state, group.id)]))];
  const selectedNodeIds = new Set(selectedIds.map(id => state.nodes.get(id)).filter(Boolean).map(node => node.id));
  for (const groupId of selectedGroupIds) for (const nodeId of descendantNodeIds(state, groupId)) selectedNodeIds.add(nodeId);
  const groups = translatePositions(selectedGroupIds.map(id => state.groups.get(id)).filter(Boolean), dx, dy, gridSize);
  const nodes = translatePositions([...selectedNodeIds].map(id => state.nodes.get(id)).filter(Boolean), dx, dy, gridSize).map(position => ({ ...position, groupId: state.nodes.get(position.id).groupId ?? null }));
  return { groups, nodes };
}
function resizeDimensions(start, pointer, zoom, gridSize, minWidth, minHeight) {
  return { width: Math.max(ceilToGrid(minWidth, gridSize), snap(start.width + (pointer.clientX - start.x) / zoom, gridSize)), height: Math.max(ceilToGrid(minHeight, gridSize), snap(start.height + (pointer.clientY - start.y) / zoom, gridSize)) };
}
function reconnectHandlePoint(source, target, end) {
  return end === "source" ? source : target;
}
function groupForNodePosition(groups, node, position) {
  const x = position.x + node.width / 2, y = position.y + node.height / 2;
  return [...groups]
    .filter(group => !group.collapsed && x >= group.x && x <= group.x + group.width && y >= group.y && y <= group.y + group.height)
    .sort((left, right) => left.width * left.height - right.width * right.height || left.id.localeCompare(right.id))[0] ?? null;
}
function groupForGroupPosition(state, groupId, position) {
  const group = state.groups.get(groupId);
  if (!group) return null;
  const excluded = new Set([groupId, ...descendantGroupIds(state, groupId)]);
  return groupForNodePosition([...state.groups.values()].filter(candidate => !excluded.has(candidate.id) && !isGroupHiddenByCollapsedAncestor(state, candidate)), group, position);
}
function markerFor(overlays, markerIds, end = "end") { const type = markerTypeFor(overlays, end); return type ? `url(#${markerIds[type]})` : ""; }
function markerTypeFor(overlays, end = "end") {
  const overlay = overlays?.find(candidate => {
    const type = candidate?.type ?? candidate;
    if (!markerTypes.includes(type)) return false;
    const location = typeof candidate === "object" ? candidate.location ?? (candidate.direction === "source" ? 0 : 1) : 1;
    return end === "start" ? location === 0 : location !== 0;
  });
  return overlay?.type ?? overlay ?? null;
}
function endpointDescriptor(port) {
  if (!port) return { type: "blank" };
  return typeof port.endpoint === "string" ? { type: port.endpoint } : { type: "dot", ...port.endpoint };
}
function validateEndpoint(endpoint) {
  const descriptor = typeof endpoint === "string" ? { type: endpoint } : endpoint;
  if (!descriptor || typeof descriptor !== "object" || (!supported.endpoints.includes(descriptor.type ?? "dot") && !endpointRegistry.has(descriptor.type))) throw new GhostagramError("CAPABILITY_UNSUPPORTED", `Endpoint '${typeof endpoint === "string" ? endpoint : endpoint?.type}' is unsupported.`);
  for (const key of ["size", "strokeWidth"]) if (descriptor[key] !== undefined && (!Number.isFinite(descriptor[key]) || descriptor[key] <= 0)) throw new GhostagramError("INVALID_MODEL", `Endpoint ${key} must be a positive number.`);
  for (const key of ["fill", "stroke"]) if (descriptor[key] !== undefined && typeof descriptor[key] !== "string") throw new GhostagramError("INVALID_MODEL", `Endpoint ${key} must be a CSS color string.`);
}
function validateOverlays(overlays) {
  if (!Array.isArray(overlays)) throw new GhostagramError("INVALID_MODEL", "Edge overlays must be an array.");
  for (const overlay of overlays) {
    const type = overlay?.type ?? overlay;
    if (!supported.overlays.includes(type) && !overlayRegistry.has(type)) throw new GhostagramError("CAPABILITY_UNSUPPORTED", `Overlay '${type}' is unsupported.`);
    if (type === "label" && overlay && typeof overlay === "object") {
      for (const key of ["location", "offsetX", "offsetY", "fontSize"]) if (overlay[key] !== undefined && !Number.isFinite(overlay[key])) throw new GhostagramError("INVALID_MODEL", `Label overlay ${key} must be numeric.`);
      if (overlay.location !== undefined && (overlay.location < 0 || overlay.location > 1)) throw new GhostagramError("INVALID_MODEL", "Label overlay location must be between 0 and 1.");
    }
    if (markerTypes.includes(type) && overlay && typeof overlay === "object") {
      if (overlay.location !== undefined && overlay.location !== 0 && overlay.location !== 1) throw new GhostagramError("INVALID_MODEL", "Marker overlay location must be 0 or 1.");
      if (overlay.direction !== undefined && !["source", "target"].includes(overlay.direction)) throw new GhostagramError("INVALID_MODEL", "Marker overlay direction must be source or target.");
    }
  }
}
function flowAnimationDescriptor(animation) {
  if (animation === undefined || animation === null || animation === false) return null;
  const value = animation === true ? {} : animation;
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new GhostagramError("INVALID_MODEL", "Edge animation must be boolean or an object.");
  const type = value.type ?? "flow";
  if (type !== "flow") throw new GhostagramError("CAPABILITY_UNSUPPORTED", `Edge animation '${type}' is unsupported.`);
  const enabled = value.enabled ?? true;
  if (typeof enabled !== "boolean") throw new GhostagramError("INVALID_MODEL", "Edge animation enabled must be boolean.");
  if (!enabled) return null;
  const speed = value.speed ?? 1, dash = value.dash ?? "8 6", direction = value.direction ?? "forward";
  if (!Number.isFinite(speed) || speed <= 0) throw new GhostagramError("INVALID_MODEL", "Edge animation speed must be a positive number.");
  if (typeof dash !== "string" || !dash.trim()) throw new GhostagramError("INVALID_MODEL", "Edge animation dash must be a non-empty SVG dash string.");
  if (!["forward", "reverse"].includes(direction)) throw new GhostagramError("INVALID_MODEL", "Edge animation direction must be forward or reverse.");
  return { type, speed, dash, direction };
}
function validateEdgeTypeDescriptor(descriptor) {
  requireObject(descriptor, "INVALID_MODEL", "Edge type descriptor must be an object.");
  if (descriptor.connector !== undefined && typeof descriptor.connector !== "string") throw new GhostagramError("INVALID_MODEL", "Edge type connector must be a string.");
  if (descriptor.connectorOptions !== undefined) flowchartOptions(descriptor.connectorOptions);
  if (descriptor.overlays !== undefined) validateOverlays(descriptor.overlays);
  if (descriptor.style !== undefined) edgeStyleDescriptor(descriptor.style);
  if (descriptor.animation !== undefined) flowAnimationDescriptor(descriptor.animation);
  for (const key of ["detachable", "reconnectable"]) if (descriptor[key] !== undefined && typeof descriptor[key] !== "boolean") throw new GhostagramError("INVALID_MODEL", `Edge type ${key} must be boolean.`);
}
function resolveEdgeDescriptor(edge, edgeTypes = new Map()) {
  const type = edge.type == null ? undefined : edgeTypes.get(edge.type);
  if (edge.type != null && !type) throw new GhostagramError("MISSING_REFERENCE", `Edge '${edge.id ?? "unknown"}' references unregistered edge type '${edge.type}'.`);
  return {
    ...type,
    ...edge,
    connector: edge.connector ?? type?.connector ?? "flowchart",
    overlays: edge.overlays ?? type?.overlays ?? [],
    style: { ...(type?.style ?? {}), ...(edge.style ?? {}) },
    connectorOptions: { ...(type?.connectorOptions ?? {}), ...(edge.connectorOptions ?? {}) },
    animation: edge.animation ?? type?.animation,
    detachable: edge.detachable ?? type?.detachable ?? true,
    reconnectable: edge.reconnectable ?? type?.reconnectable ?? true
  };
}
function edgeStyleDescriptor(style) {
  if (style === undefined || style === null) return { stroke: "#0f766e", strokeWidth: 2, labelColor: "#0f172a" };
  if (typeof style !== "object" || Array.isArray(style)) throw new GhostagramError("INVALID_MODEL", "Edge style must be an object.");
  const result = { stroke: style.stroke ?? "#0f766e", strokeWidth: style.strokeWidth ?? 2, labelColor: style.labelColor ?? "#0f172a", dash: style.dash, opacity: style.opacity, lineCap: style.lineCap, lineJoin: style.lineJoin };
  for (const key of ["stroke", "labelColor"]) if (typeof result[key] !== "string" || !result[key]) throw new GhostagramError("INVALID_MODEL", `Edge style ${key} must be a non-empty CSS color string.`);
  if (!Number.isFinite(result.strokeWidth) || result.strokeWidth <= 0) throw new GhostagramError("INVALID_MODEL", "Edge style strokeWidth must be a positive number.");
  if (result.dash !== undefined && (typeof result.dash !== "string" || !result.dash.trim())) throw new GhostagramError("INVALID_MODEL", "Edge style dash must be a non-empty SVG dash string.");
  if (result.opacity !== undefined && (!Number.isFinite(result.opacity) || result.opacity < 0 || result.opacity > 1)) throw new GhostagramError("INVALID_MODEL", "Edge style opacity must be between zero and one.");
  if (result.lineCap !== undefined && !["butt", "round", "square"].includes(result.lineCap)) throw new GhostagramError("INVALID_MODEL", "Edge style lineCap must be butt, round, or square.");
  if (result.lineJoin !== undefined && !["miter", "round", "bevel"].includes(result.lineJoin)) throw new GhostagramError("INVALID_MODEL", "Edge style lineJoin must be miter, round, or bevel.");
  return result;
}
function edgeLabelText(edge) { const overlay = edge.overlays?.find(item => (item.type ?? item) === "label"); return edge.label ?? (overlay && typeof overlay === "object" ? overlay.label : undefined); }
function edgeLabelOffsets(edge) { const overlay = edge.overlays?.find(item => (item.type ?? item) === "label"); return { labelOffsetX: edge.labelOffsetX ?? overlay?.offsetX ?? 0, labelOffsetY: edge.labelOffsetY ?? overlay?.offsetY ?? -6 }; }
function edgeLabelPlacement(edge, source, target, geometry, routePoints) {
  const overlay = edge.overlays?.find(item => (item.type ?? item) === "label"), text = edgeLabelText(edge);
  if (!text) return null;
  const location = overlay && typeof overlay === "object" ? overlay.location ?? .5 : .5, point = pointAlongPolyline(routePoints ?? edgeRoutePoints(edge, source, target, geometry), location);
  const offsets = edgeLabelOffsets(edge);
  return { text: String(text), x: point.x + offsets.labelOffsetX, y: point.y + offsets.labelOffsetY, fontSize: overlay?.fontSize ?? 12 };
}
function edgeRoutePoints(edge, source, target, geometry, routingContext) {
  if (edge.waypoints?.length) return [source, ...edge.waypoints, target];
  if (edge.connector === "flowchart" || !edge.connector) return flowchartRoutePoints(source, target, geometry?.sourceSide, geometry?.targetSide, flowchartOptions(edge.connectorOptions), routingContext, edge, geometry);
  return [source, target];
}
function flowchartRoutePoints(source, target, sourceSide, targetSide, options = flowchartOptions(), routingContext, edge, geometry) {
  const baseline = basicFlowchartRoutePoints(source, target, sourceSide, targetSide, options);
  if (!routingContext || !edge || !geometry || geometry.hidden) return baseline;
  const sourceVector = anchorVector(sourceSide), targetVector = anchorVector(targetSide);
  if (!sourceVector || !targetVector) return baseline;
  const sourceEscape = { x: source.x + sourceVector.x * options.stub, y: source.y + sourceVector.y * options.stub };
  const targetEscape = { x: target.x + targetVector.x * options.stub, y: target.y + targetVector.y * options.stub };
  const envelope = routingEnvelope(routingContext, geometry, source, target);
  const laneOffset = stableRouteLane(edge.id) * ROUTE_LANE_SPACING;
  const top = envelope.y - ROUTE_CORRIDOR_CLEARANCE - laneOffset;
  const bottom = envelope.y + envelope.height + ROUTE_CORRIDOR_CLEARANCE + laneOffset;
  const left = envelope.x - ROUTE_CORRIDOR_CLEARANCE - laneOffset;
  const right = envelope.x + envelope.width + ROUTE_CORRIDOR_CLEARANCE + laneOffset;
  const candidates = uniqueRoutes([
    baseline,
    simplifyRoutePoints([source, sourceEscape, { x: sourceEscape.x, y: top }, { x: targetEscape.x, y: top }, targetEscape, target]),
    simplifyRoutePoints([source, sourceEscape, { x: sourceEscape.x, y: bottom }, { x: targetEscape.x, y: bottom }, targetEscape, target]),
    simplifyRoutePoints([source, sourceEscape, { x: left, y: sourceEscape.y }, { x: left, y: targetEscape.y }, targetEscape, target]),
    simplifyRoutePoints([source, sourceEscape, { x: right, y: sourceEscape.y }, { x: right, y: targetEscape.y }, targetEscape, target])
  ]);
  let best = baseline, bestScore = Number.POSITIVE_INFINITY;
  for (const [index, candidate] of candidates.entries()) {
    const score = flowchartRouteScore(candidate, edge, geometry, routingContext) + index / 1000;
    if (score < bestScore) { best = candidate; bestScore = score; }
  }
  return best;
}
function basicFlowchartRoutePoints(source, target, sourceSide, targetSide, options = flowchartOptions()) {
  const sourceVector = anchorVector(sourceSide), targetVector = anchorVector(targetSide);
  if (sourceVector && targetVector) {
    const clearance = options.stub, sourceEscape = { x: source.x + sourceVector.x * clearance, y: source.y + sourceVector.y * clearance }, targetEscape = { x: target.x + targetVector.x * clearance, y: target.y + targetVector.y * clearance };
    let corridor;
    if (sourceVector.x && targetVector.x) {
      const middleX = (sourceEscape.x + targetEscape.x) / 2;
      corridor = [{ x: middleX, y: sourceEscape.y }, { x: middleX, y: targetEscape.y }];
    } else if (sourceVector.y && targetVector.y) {
      const middleY = (sourceEscape.y + targetEscape.y) / 2;
      corridor = [{ x: sourceEscape.x, y: middleY }, { x: targetEscape.x, y: middleY }];
    } else if (sourceVector.x) corridor = [{ x: targetEscape.x, y: sourceEscape.y }];
    else corridor = [{ x: sourceEscape.x, y: targetEscape.y }];
    return simplifyRoutePoints([source, sourceEscape, ...corridor, targetEscape, target]);
  }
  const mid = source.x + (target.x - source.x) / 2;
  return simplifyRoutePoints([source, { x: mid, y: source.y }, { x: mid, y: target.y }, target]);
}
function buildRoutingContext(state, previewNodes = new Map(), previewGroups = new Map()) {
  const nodeById = new Map([...state.nodes].map(([id, node]) => [id, previewNodes.get(id) ?? node]));
  const groupById = new Map([...state.groups].map(([id, group]) => [id, previewGroups.get(id) ?? group]));
  const groups = [...groupById.values()].filter(group => !isGroupHiddenByCollapsedAncestor(state, group));
  const obstacles = [...nodeById.values()]
    .filter(node => !isNodeHiddenByCollapsedGroup(state, node))
    .map(node => ({ id: node.id, ...expandRectangle(rotatedRectangle(node), ROUTE_OBSTACLE_PADDING) }));
  const occupiedSegments = [];
  for (const rawEdge of [...state.edges.values()].sort((left, right) => left.id.localeCompare(right.id))) {
    const edge = resolveEdgeDescriptor(rawEdge, state.edgeTypes), geometry = edgeGeometry(state, edge, previewNodes);
    if (geometry.hidden || edge.connector === "bezier" || edge.connector === "state-machine") continue;
    const points = edge.waypoints?.length
      ? [geometry.sourcePoint, ...edge.waypoints, geometry.targetPoint]
      : edge.connector === "straight"
        ? [geometry.sourcePoint, geometry.targetPoint]
        : basicFlowchartRoutePoints(geometry.sourcePoint, geometry.targetPoint, geometry.sourceSide, geometry.targetSide, flowchartOptions(edge.connectorOptions));
    for (let index = 1; index < points.length; index++) if (!samePoint(points[index - 1], points[index])) occupiedSegments.push({ edgeId: edge.id, a: points[index - 1], b: points[index] });
  }
  return {
    groupById,
    groupIndex: buildSpatialIndex(groups, item => item),
    obstacleIndex: buildSpatialIndex(obstacles, item => item),
    segmentIndex: buildSpatialIndex(occupiedSegments, item => segmentBounds(item.a, item.b, 2))
  };
}
function routingEnvelope(context, geometry, source, target) {
  const rectangles = [rectangleForItem(geometry.sourceItem), rectangleForItem(geometry.targetItem)].filter(Boolean);
  for (const node of [geometry.sourceNode, geometry.targetNode]) {
    let groupId = node?.groupId;
    while (groupId) {
      const group = context.groupById.get(groupId);
      if (!group) break;
      rectangles.push(rectangleForItem(group));
      groupId = group.parentGroupId;
    }
  }
  const direct = expandRectangle(rectangleForPoints(source, target), ROUTE_CORRIDOR_CLEARANCE);
  for (const obstacle of querySpatialIndex(context.obstacleIndex, direct)) if (rectanglesIntersect(direct, obstacle)) rectangles.push(obstacle);
  if (!rectangles.length) return direct;
  const left = Math.min(...rectangles.map(item => item.x)), top = Math.min(...rectangles.map(item => item.y));
  const right = Math.max(...rectangles.map(item => item.x + item.width)), bottom = Math.max(...rectangles.map(item => item.y + item.height));
  return { x: left, y: top, width: Math.max(1, right - left), height: Math.max(1, bottom - top) };
}
function flowchartRouteScore(points, edge, geometry, context) {
  const segments = points.slice(1).map((point, index) => ({ a: points[index], b: point }));
  const endpointIds = new Set([geometry.sourceNode?.id, geometry.targetNode?.id].filter(Boolean));
  const obstacleHits = new Set(), relevantGroups = new Set();
  let overlap = 0, crossings = 0;
  for (const [index, segment] of segments.entries()) {
    const bounds = segmentBounds(segment.a, segment.b, 2);
    for (const obstacle of querySpatialIndex(context.obstacleIndex, bounds)) {
      const endpointEscape = endpointIds.has(obstacle.id) && (index === 0 || index === segments.length - 1);
      if (!endpointEscape && segmentIntersectsRectangle(segment.a, segment.b, obstacle)) obstacleHits.add(obstacle.id);
    }
    for (const group of querySpatialIndex(context.groupIndex, bounds)) relevantGroups.add(group);
    if (index === 0 || index === segments.length - 1) continue;
    for (const occupied of querySpatialIndex(context.segmentIndex, bounds)) {
      if (occupied.edgeId === edge.id) continue;
      const shared = collinearOverlapLength(segment.a, segment.b, occupied.a, occupied.b);
      if (shared > .5) overlap += shared;
      else if (!sharesEndpoint(segment.a, segment.b, occupied.a, occupied.b) && segmentsIntersect(segment.a, segment.b, occupied.a, occupied.b)) crossings += 1;
    }
  }
  let groupPenalty = 0;
  for (const group of relevantGroups) {
    const required = pointInRectangle(points[0], group) === pointInRectangle(points.at(-1), group) ? 0 : 1;
    const crossingsForGroup = segments.reduce((total, segment) => total + segmentRectangleBoundaryCrossings(segment.a, segment.b, group), 0);
    groupPenalty += Math.max(0, crossingsForGroup - required) * 600;
  }
  return obstacleHits.size * 1_000_000 + overlap * 8 + crossings * 300 + groupPenalty + polylineLength(points) + Math.max(0, points.length - 2) * 24;
}
function uniqueRoutes(routes) { const seen = new Set(); return routes.filter(points => { const key = points.map(point => `${point.x},${point.y}`).join(";"); if (seen.has(key)) return false; seen.add(key); return true; }); }
function stableRouteLane(value = "") { let hash = 0; for (let index = 0; index < value.length; index++) hash = (hash * 31 + value.charCodeAt(index)) >>> 0; return hash % 4; }
function rotatedRectangle(item) { const angle = (item.rotation ?? 0) * Math.PI / 180; if (!angle) return rectangleForItem(item); const width = Math.abs(Math.cos(angle)) * item.width + Math.abs(Math.sin(angle)) * item.height, height = Math.abs(Math.sin(angle)) * item.width + Math.abs(Math.cos(angle)) * item.height; return { x: item.x + (item.width - width) / 2, y: item.y + (item.height - height) / 2, width, height }; }
function rectangleForItem(item) { return item && Number.isFinite(item.x) && Number.isFinite(item.y) && Number.isFinite(item.width) && Number.isFinite(item.height) ? { x: item.x, y: item.y, width: item.width, height: item.height } : null; }
function expandRectangle(rectangle, amount) { return { x: rectangle.x - amount, y: rectangle.y - amount, width: rectangle.width + amount * 2, height: rectangle.height + amount * 2 }; }
function segmentBounds(a, b, padding = 0) { return { x: Math.min(a.x, b.x) - padding, y: Math.min(a.y, b.y) - padding, width: Math.abs(b.x - a.x) + padding * 2, height: Math.abs(b.y - a.y) + padding * 2 }; }
function buildSpatialIndex(items, boundsForItem) {
  const buckets = new Map(), overflow = [];
  for (const item of items) {
    const bounds = boundsForItem(item), range = spatialCellRange(bounds), cells = (range.right - range.left + 1) * (range.bottom - range.top + 1);
    if (cells > 256) { overflow.push(item); continue; }
    for (let x = range.left; x <= range.right; x++) for (let y = range.top; y <= range.bottom; y++) { const key = `${x}:${y}`, bucket = buckets.get(key) ?? []; bucket.push(item); buckets.set(key, bucket); }
  }
  return { buckets, overflow };
}
function querySpatialIndex(index, bounds) { const found = new Set(index.overflow), range = spatialCellRange(bounds); for (let x = range.left; x <= range.right; x++) for (let y = range.top; y <= range.bottom; y++) for (const item of index.buckets.get(`${x}:${y}`) ?? []) found.add(item); return found; }
function spatialCellRange(bounds) { return { left: Math.floor(bounds.x / ROUTE_SPATIAL_CELL), top: Math.floor(bounds.y / ROUTE_SPATIAL_CELL), right: Math.floor((bounds.x + bounds.width) / ROUTE_SPATIAL_CELL), bottom: Math.floor((bounds.y + bounds.height) / ROUTE_SPATIAL_CELL) }; }
function samePoint(left, right) { return Math.abs(left.x - right.x) < 1e-9 && Math.abs(left.y - right.y) < 1e-9; }
function sharesEndpoint(a, b, c, d) { return samePoint(a, c) || samePoint(a, d) || samePoint(b, c) || samePoint(b, d); }
function collinearOverlapLength(a, b, c, d) { if (Math.abs(a.y - b.y) < 1e-9 && Math.abs(c.y - d.y) < 1e-9 && Math.abs(a.y - c.y) < 1e-9) return Math.max(0, Math.min(Math.max(a.x, b.x), Math.max(c.x, d.x)) - Math.max(Math.min(a.x, b.x), Math.min(c.x, d.x))); if (Math.abs(a.x - b.x) < 1e-9 && Math.abs(c.x - d.x) < 1e-9 && Math.abs(a.x - c.x) < 1e-9) return Math.max(0, Math.min(Math.max(a.y, b.y), Math.max(c.y, d.y)) - Math.max(Math.min(a.y, b.y), Math.min(c.y, d.y))); return 0; }
function segmentRectangleBoundaryCrossings(a, b, rectangle) { const aInside = pointInRectangle(a, rectangle), bInside = pointInRectangle(b, rectangle); if (aInside !== bInside) return 1; return !aInside && segmentIntersectsRectangle(a, b, rectangle) ? 2 : 0; }
function polylineLength(points) { return points.slice(1).reduce((total, point, index) => total + Math.hypot(point.x - points[index].x, point.y - points[index].y), 0); }
function anchorVector(side) { return side === "left" ? { x: -1, y: 0 } : side === "right" ? { x: 1, y: 0 } : side === "top" ? { x: 0, y: -1 } : side === "bottom" ? { x: 0, y: 1 } : null; }
function simplifyRoutePoints(points) {
  const unique = points.filter((point, index) => index === 0 || point.x !== points[index - 1].x || point.y !== points[index - 1].y);
  return unique.filter((point, index) => {
    if (index === 0 || index === unique.length - 1) return true;
    const previous = unique[index - 1], next = unique[index + 1], incomingX = point.x - previous.x, incomingY = point.y - previous.y, outgoingX = next.x - point.x, outgoingY = next.y - point.y;
    return incomingX * outgoingY !== incomingY * outgoingX || incomingX * outgoingX + incomingY * outgoingY < 0;
  });
}
function flowchartOptions(options) {
  if (options === undefined || options === null) return { stub: 32, cornerRadius: 0 };
  if (typeof options !== "object" || Array.isArray(options)) throw new GhostagramError("INVALID_MODEL", "Flowchart connectorOptions must be an object.");
  const stub = options.stub ?? 32, cornerRadius = options.cornerRadius ?? 0;
  if (!Number.isFinite(stub) || stub < 0) throw new GhostagramError("INVALID_MODEL", "Flowchart stub must be zero or a positive number.");
  if (!Number.isFinite(cornerRadius) || cornerRadius < 0) throw new GhostagramError("INVALID_MODEL", "Flowchart cornerRadius must be zero or a positive number.");
  return { stub, cornerRadius };
}
function flowchartPath(points, cornerRadius = 0) {
  if (!cornerRadius || points.length < 3) return `M ${points[0].x} ${points[0].y}${points.slice(1).map(point => ` L ${point.x} ${point.y}`).join("")}`;
  let path = `M ${points[0].x} ${points[0].y}`;
  for (let index = 1; index < points.length - 1; index++) {
    const previous = points[index - 1], corner = points[index], next = points[index + 1], incoming = { x: Math.sign(corner.x - previous.x), y: Math.sign(corner.y - previous.y) }, outgoing = { x: Math.sign(next.x - corner.x), y: Math.sign(next.y - corner.y) };
    const isRightAngle = (incoming.x !== 0 && outgoing.y !== 0) || (incoming.y !== 0 && outgoing.x !== 0);
    if (!isRightAngle) { path += ` L ${corner.x} ${corner.y}`; continue; }
    const distance = Math.min(cornerRadius, Math.hypot(corner.x - previous.x, corner.y - previous.y) / 2, Math.hypot(next.x - corner.x, next.y - corner.y) / 2), before = { x: corner.x - incoming.x * distance, y: corner.y - incoming.y * distance }, after = { x: corner.x + outgoing.x * distance, y: corner.y + outgoing.y * distance };
    path += ` L ${before.x} ${before.y} Q ${corner.x} ${corner.y} ${after.x} ${after.y}`;
  }
  const last = points.at(-1); return `${path} L ${last.x} ${last.y}`;
}
function pointAlongPolyline(points, location) {
  const lengths = points.slice(1).map((point, index) => Math.hypot(point.x - points[index].x, point.y - points[index].y)), total = lengths.reduce((sum, value) => sum + value, 0), target = total * clamp(location, 0, 1);
  let traversed = 0;
  for (let index = 0; index < lengths.length; index++) { const length = lengths[index]; if (traversed + length >= target || index === lengths.length - 1) { const ratio = length ? (target - traversed) / length : 0, start = points[index], end = points[index + 1]; return { x: start.x + (end.x - start.x) * ratio, y: start.y + (end.y - start.y) * ratio }; } traversed += length; }
  return points.at(-1) ?? { x: 0, y: 0 };
}
function fitViewport(s, padding, options) {
  const nodes = [...s.nodes.values()], size = options.viewportSize;
  if (!nodes.length || !validViewportSize(size)) return;
  const bounds = diagramBounds(nodes), inset = Math.max(0, padding), availableWidth = Math.max(1, size.width - inset * 2), availableHeight = Math.max(1, size.height - inset * 2);
  const zoom = clamp(Math.min(availableWidth / bounds.width, availableHeight / bounds.height), options.minZoom, options.maxZoom);
  setViewportCenter(s, bounds.left + bounds.width / 2, bounds.top + bounds.height / 2, size, zoom);
}
function centerViewport(s, ids, options) {
  const nodes = (ids.length ? ids : [...s.nodes.keys()]).map(id => s.nodes.get(id)).filter(Boolean), size = options.viewportSize;
  if (!nodes.length || !validViewportSize(size)) return;
  const bounds = diagramBounds(nodes);
  setViewportCenter(s, bounds.left + bounds.width / 2, bounds.top + bounds.height / 2, size, s.viewport.zoom);
}
function diagramBounds(nodes) {
  const left = Math.min(...nodes.map(node => node.x)), top = Math.min(...nodes.map(node => node.y)), right = Math.max(...nodes.map(node => node.x + node.width)), bottom = Math.max(...nodes.map(node => node.y + node.height));
  return { left, top, width: Math.max(1, right - left), height: Math.max(1, bottom - top) };
}
function validViewportSize(size) { return Number.isFinite(size?.width) && size.width > 0 && Number.isFinite(size?.height) && size.height > 0; }
function rectangleForPoints(start, end) { const x = Math.min(start.x, end.x), y = Math.min(start.y, end.y); return { x, y, width: Math.abs(end.x - start.x), height: Math.abs(end.y - start.y) }; }
function isClickGesture(start, end, tolerance = 4) { return Math.hypot(end.clientX - start.x, end.clientY - start.y) <= tolerance; }
function historyDirectionForKey(event) { if (!(event.ctrlKey || event.metaKey) || event.altKey) return null; const key = event.key.toLowerCase(); if (key === "z") return event.shiftKey ? "redo" : "undo"; return key === "y" ? "redo" : null; }
function iconifyIconName(value) { if (value === undefined || value === null) return null; if (typeof value !== "string" || !/^[a-z0-9][a-z0-9-]*:[a-z0-9][a-z0-9-]*$/i.test(value)) throw new GhostagramError("INVALID_MODEL", "Item icon must be an Iconify name such as 'mdi:folder-outline' or null."); return value; }
function ensureIconifyIcon() {
  if (!globalThis.document || globalThis.customElements?.get("iconify-icon") || iconifyScriptRequested) return;
  iconifyScriptRequested = true;
  const script = document.createElement("script");
  script.src = ICONIFY_ICON_SCRIPT; script.async = true; script.dataset.ghostagramIconify = "true";
  document.head?.append(script);
}
function renderIconifyIcon(element, icon) {
  if (!element) return;
  element.hidden = !icon;
  if (!icon) { element.removeAttribute("icon"); element.removeAttribute("aria-label"); element.removeAttribute("title"); return; }
  ensureIconifyIcon();
  element.setAttribute("icon", icon); element.setAttribute("aria-label", icon); element.title = icon;
}
function groupVisibilityDescriptor(group, selected) {
  const name = group?.label ?? group?.id ?? "group", hidden = Boolean(group?.collapsed);
  return { hidden: !selected, contentsHidden: hidden, expanded: !hidden, icon: hidden ? "mdi:eye-outline" : "mdi:eye-off-outline", label: hidden ? `Show contents of ${name}` : `Hide contents of ${name}` };
}
function updateGroupVisibilityControl(element, group, selected) {
  const button = element?.querySelector?.(".ghostagram-group-visibility");
  if (!button || !group) return;
  const descriptor = groupVisibilityDescriptor(group, selected);
  button.hidden = descriptor.hidden; button.dataset.hidden = String(descriptor.contentsHidden); button.title = descriptor.label;
  button.setAttribute("aria-label", descriptor.label); button.setAttribute("aria-expanded", String(descriptor.expanded));
  const slash = button.querySelector(".ghostagram-group-visibility-slash");
  if (slash) slash.style.display = descriptor.icon === "mdi:eye-off-outline" ? "" : "none";
}
function nodesInRectangle(state, rectangle) { return [...state.nodes.values()].filter(node => !isNodeHiddenByCollapsedGroup(state, node) && rectanglesIntersect(rectangle, node)).map(node => node.id); }
function edgesInRectangle(state, rectangle, routingContext = buildRoutingContext(state)) {
  return [...state.edges.values()].filter(rawEdge => {
    const edge = resolveEdgeDescriptor(rawEdge, state.edgeTypes), geometry = edgeGeometry(state, edge);
    return !geometry.hidden && polylineIntersectsRectangle(edgeSelectionPoints(edge, geometry.sourcePoint, geometry.targetPoint, geometry, routingContext), rectangle);
  }).map(edge => edge.id);
}
function edgeSelectionPoints(edge, source, target, geometry, routingContext) {
  if (edge.connector !== "bezier" && edge.connector !== "state-machine") return edgeRoutePoints(edge, source, target, geometry, routingContext);
  const dx = Math.max(48, Math.abs(target.x - source.x) * .45), points = [];
  for (let index = 0; index <= 12; index++) { const t = index / 12, inverse = 1 - t; points.push({ x: inverse ** 3 * source.x + 3 * inverse ** 2 * t * (source.x + dx) + 3 * inverse * t ** 2 * (target.x - dx) + t ** 3 * target.x, y: inverse ** 3 * source.y + 3 * inverse ** 2 * t * source.y + 3 * inverse * t ** 2 * target.y + t ** 3 * target.y }); }
  return points;
}
function polylineIntersectsRectangle(points, rectangle) { return points.some(point => pointInRectangle(point, rectangle)) || points.slice(1).some((point, index) => segmentIntersectsRectangle(points[index], point, rectangle)); }
function pointInRectangle(point, rectangle) { return point.x >= rectangle.x && point.x <= rectangle.x + rectangle.width && point.y >= rectangle.y && point.y <= rectangle.y + rectangle.height; }
function segmentIntersectsRectangle(a, b, rectangle) { const topLeft = { x: rectangle.x, y: rectangle.y }, topRight = { x: rectangle.x + rectangle.width, y: rectangle.y }, bottomLeft = { x: rectangle.x, y: rectangle.y + rectangle.height }, bottomRight = { x: rectangle.x + rectangle.width, y: rectangle.y + rectangle.height }; return pointInRectangle(a, rectangle) || pointInRectangle(b, rectangle) || [[topLeft, topRight], [topRight, bottomRight], [bottomRight, bottomLeft], [bottomLeft, topLeft]].some(([start, end]) => segmentsIntersect(a, b, start, end)); }
function segmentsIntersect(a, b, c, d) { const cross = (first, second, third) => (second.x - first.x) * (third.y - first.y) - (second.y - first.y) * (third.x - first.x), abC = cross(a, b, c), abD = cross(a, b, d), cdA = cross(c, d, a), cdB = cross(c, d, b), pointOnSegment = (point, start, end) => Math.abs(cross(start, end, point)) < 1e-9 && point.x >= Math.min(start.x, end.x) && point.x <= Math.max(start.x, end.x) && point.y >= Math.min(start.y, end.y) && point.y <= Math.max(start.y, end.y); if (Math.abs(abC) < 1e-9 && Math.abs(abD) < 1e-9 && Math.abs(cdA) < 1e-9 && Math.abs(cdB) < 1e-9) return pointOnSegment(a, c, d) || pointOnSegment(b, c, d) || pointOnSegment(c, a, b) || pointOnSegment(d, a, b); return abC * abD <= 0 && cdA * cdB <= 0; }
function selectionIdsInRectangle(state, rectangle, routingContext) { return [...nodesInRectangle(state, rectangle), ...groupsForRender(state).filter(group => !isGroupHiddenByCollapsedAncestor(state, group) && rectanglesIntersect(rectangle, group)).map(group => group.id), ...edgesInRectangle(state, rectangle, routingContext)]; }
function deletionPlan(state, selection) {
  const selectedIds = [...selection], groupIds = new Set(selectedIds.filter(id => state.groups.has(id))), nodeIds = new Set(selectedIds.filter(id => state.nodes.has(id))), edgeIds = new Set(selectedIds.filter(id => state.edges.has(id)));
  for (const groupId of [...groupIds]) for (const descendantId of descendantGroupIds(state, groupId)) groupIds.add(descendantId);
  for (const groupId of groupIds) for (const nodeId of descendantNodeIds(state, groupId)) nodeIds.add(nodeId);
  for (const nodeId of nodeIds) for (const edgeId of incident(state, nodeId)) edgeIds.add(edgeId);
  const groupDepth = groupId => { let depth = 0, parentId = state.groups.get(groupId)?.parentGroupId; while (parentId) { depth += 1; parentId = state.groups.get(parentId)?.parentGroupId; } return depth; };
  return { edgeIds: [...edgeIds].sort(), nodeIds: [...nodeIds].sort(), groupIds: [...groupIds].sort((left, right) => groupDepth(right) - groupDepth(left) || left.localeCompare(right)) };
}
function selectableIds(state) {
  const nodes = [...state.nodes.values()].filter(node => !isNodeHiddenByCollapsedGroup(state, node)).map(node => node.id);
  const groups = groupsForRender(state).filter(group => !isGroupHiddenByCollapsedAncestor(state, group)).map(group => group.id);
  const edges = [...state.edges.values()].filter(rawEdge => !edgeGeometry(state, resolveEdgeDescriptor(rawEdge, state.edgeTypes)).hidden).map(edge => edge.id);
  return [...nodes, ...groups, ...edges];
}
function rectanglesIntersect(a, b) { return a.x <= b.x + b.width && a.x + a.width >= b.x && a.y <= b.y + b.height && a.y + a.height >= b.y; }
function setViewportCenter(state, centerX, centerY, size, zoom) { state.viewport = { ...state.viewport, zoom, x: centerX - size.width / (2 * zoom), y: centerY - size.height / (2 * zoom) }; }
function exportSvgDocument(state, options = {}) { return exportSvgArtifact(state, options).svg; }
function exportSvgArtifact(state, options = {}) {
  const visibleGroups = groupsForRender(state).filter(group => !isGroupHiddenByCollapsedAncestor(state, group)), visibleNodes = [...state.nodes.values()].filter(node => !isNodeHiddenByCollapsedGroup(state, node));
  const bounds = exportSvgBounds([...visibleNodes, ...visibleGroups], options);
  const groups = visibleGroups.map(group => `<rect x="${group.x}" y="${group.y}" width="${group.width}" height="${group.height}" fill="rgba(148,163,184,.08)" stroke="#64748b" stroke-dasharray="4 3"/><text x="${group.x + 6}" y="${group.y + 18}" fill="#334155" font-size="12" font-weight="600">${xml(group.label ?? group.id)}</text>`).join("");
  const markerIds = Object.fromEntries(markerTypes.map(type => [type, `ghostagram-export-${type}`]));
  const routingContext = buildRoutingContext(state);
  const edges = [...state.edges.values()].map(rawEdge => { const edge = resolveEdgeDescriptor(rawEdge, state.edgeTypes), geometry = edgeGeometry(state, edge); if (geometry.hidden) return ""; const { sourcePoint, targetPoint } = geometry, routePoints = edgeRoutePoints(edge, sourcePoint, targetPoint, geometry, routingContext), style = edgeStyleDescriptor(edge.style), label = edgeLabelPlacement(edge, sourcePoint, targetPoint, geometry, routePoints), markerStart = markerFor(edge.overlays, markerIds, "start"), markerEnd = markerFor(edge.overlays, markerIds, "end"); return `<path d="${route(edge, sourcePoint, targetPoint, geometry, routePoints)}" fill="none" stroke="${xml(style.stroke)}" stroke-width="${style.strokeWidth}"${svgOptionalAttribute("stroke-dasharray", style.dash)}${svgOptionalAttribute("stroke-linecap", style.lineCap)}${svgOptionalAttribute("stroke-linejoin", style.lineJoin)}${svgOptionalAttribute("opacity", style.opacity)}${markerStart ? ` marker-start="${markerStart}"` : ""}${markerEnd ? ` marker-end="${markerEnd}"` : ""}/>${label ? `<text x="${label.x}" y="${label.y}" fill="${xml(style.labelColor)}" font-size="${label.fontSize}">${xml(label.text)}</text>` : ""}`; }).join("");
  const nodes = visibleNodes.map(exportSvgNode).join("");
  return { bounds, svg: `<svg xmlns="${SVG_NS}" viewBox="${bounds.x} ${bounds.y} ${bounds.width} ${bounds.height}" role="img"><defs>${Object.entries(markerIds).map(([type, id]) => exportMarker(id, type)).join("")}</defs>${groups}${edges}${nodes}</svg>` };
}
function exportSvgBounds(items, options = {}) {
  if (options.bounds) {
    const { x, y, width, height } = options.bounds;
    if ([x, y, width, height].every(Number.isFinite) && width > 0 && height > 0) return { x, y, width, height };
    throw new GhostagramError("INVALID_MODEL", "Export bounds must be finite with a positive width and height.");
  }
  const padding = options.padding ?? 32;
  if (!items.length) return { x: 0, y: 0, width: 1, height: 1 };
  const left = Math.min(...items.map(item => item.x)) - padding, top = Math.min(...items.map(item => item.y)) - padding;
  return { x: left, y: top, width: Math.max(1, Math.max(...items.map(item => item.x + item.width)) + padding - left), height: Math.max(1, Math.max(...items.map(item => item.y + item.height)) + padding - top) };
}
function viewportExportBounds(state, size) { return { x: state.viewport.x, y: state.viewport.y, width: size.width / state.viewport.zoom, height: size.height / state.viewport.zoom }; }
async function exportPngArtifact(artifact, pixelRatio = 2) {
  const scale = Math.min(pixelRatio, 4096 / artifact.bounds.width, 4096 / artifact.bounds.height);
  const width = Math.max(1, Math.round(artifact.bounds.width * scale)), height = Math.max(1, Math.round(artifact.bounds.height * scale));
  const svgBlob = new Blob([artifact.svg], { type: "image/svg+xml;charset=utf-8" }), url = URL.createObjectURL(svgBlob), image = new Image();
  try {
    await new Promise((resolve, reject) => { image.onload = resolve; image.onerror = () => reject(new GhostagramError("IMAGE_EXPORT_FAILED", "The diagram SVG could not be rasterized.")); image.src = url; });
    const canvas = document.createElement("canvas"); canvas.width = width; canvas.height = height;
    const context = canvas.getContext("2d");
    if (!context) throw new GhostagramError("IMAGE_EXPORT_FAILED", "This browser could not create an image canvas.");
    context.fillStyle = "#ffffff"; context.fillRect(0, 0, width, height); context.drawImage(image, 0, 0, width, height);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
    if (!blob) throw new GhostagramError("IMAGE_EXPORT_FAILED", "This browser could not encode a PNG image.");
    return { blob, dataUrl: canvas.toDataURL("image/png"), width, height };
  } finally { URL.revokeObjectURL(url); }
}
function exportSvgNode(node) {
  const layout = nodeLayoutProjection(node), color = node.style?.color ?? "#0f172a";
  const labelY = node.y + 20;
  const rows = layout.rows.map(row => {
    const property = row.property, y = node.y + row.y + row.height / 2 + 4;
    return `<text x="${node.x + 8}" y="${y}" fill="${xml(color)}" fill-opacity=".7" font-size="11">${xml(property.label ?? property.name ?? property.id)}</text><text x="${node.x + node.width - 8}" y="${y}" fill="${xml(color)}" font-size="11" text-anchor="end">${xml(propertyDisplayValue(property))}</text>`;
  }).join("");
  const sectionHeadings = layout.sections.map(section => `<text x="${node.x + 8 + Math.min(section.depth, 4) * 8}" y="${node.y + section.y + 15}" fill="${xml(color)}" font-size="11" font-weight="600">${xml(`${section.collapsed ? "▸ " : "▾ "}${section.section.title}`)}</text>`).join("");
  const icon = iconifyIconName(node.icon) ? `<text x="${node.x + node.width - 8}" y="${node.y + 20}" fill="${xml(color)}" font-size="12" text-anchor="end">◇</text>` : "";
  const headerRule = layout.progressive ? `<path d="M ${node.x} ${node.y + NODE_PROPERTY_TOP} L ${node.x + node.width} ${node.y + NODE_PROPERTY_TOP}" stroke="${xml(node.style?.borderColor ?? "#334155")}" stroke-opacity=".28"/>` : "";
  return `<g transform="rotate(${node.rotation ?? 0} ${node.x + node.width / 2} ${node.y + node.height / 2})"><rect x="${node.x}" y="${node.y}" width="${node.width}" height="${node.height}" rx="6" fill="${xml(node.style?.background ?? "#f8fafc")}" stroke="${xml(node.style?.borderColor ?? "#334155")}"/>${headerRule}<text x="${node.x + 8}" y="${labelY}" fill="${xml(color)}" font-size="14">${xml(node.label ?? node.id)}</text>${icon}${sectionHeadings}${rows}</g>`;
}
function exportMarker(id, type) {
  const descriptor = markerDescriptor(type);
  const shapes = descriptor.shapes.map(shape => `<${shape.tag}${Object.entries(shape.attributes).map(([name, value]) => ` ${name}="${xml(value)}"`).join("")}/>`).join("");
  return `<marker id="${id}" viewBox="${descriptor.viewBox}" refX="${descriptor.refX}" refY="${descriptor.refY}" markerWidth="${descriptor.markerWidth}" markerHeight="${descriptor.markerHeight}" orient="auto-start-reverse">${shapes}</marker>`;
}
function xml(value) { return String(value).replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" })[character]); }
function svgOptionalAttribute(name, value) { return value === undefined ? "" : ` ${name}="${xml(value)}"`; }
function setOptionalSvgAttribute(element, name, value) { if (value === undefined) element.removeAttribute(name); else element.setAttribute(name, String(value)); }
function buildRoot(host) {
  const root = document.createElement("div"), style = document.createElement("style"), stage = document.createElement("div"), groups = document.createElement("div"), nodes = document.createElement("div"), editors = document.createElement("div"), svg = document.createElementNS(SVG_NS, "svg"), overlay = document.createElementNS(SVG_NS, "svg"), selectionBox = document.createElement("div"), defs = document.createElementNS(SVG_NS, "defs"), edges = document.createElementNS(SVG_NS, "g");
  const markerIds = Object.fromEntries(markerTypes.map(type => [type, `ghostagram-${type}-${crypto.randomUUID()}`]));
  const flowAnimationName = `ghostagram-flow-${crypto.randomUUID()}`;
  style.textContent = `@keyframes ${flowAnimationName} { to { stroke-dashoffset: -14; } }
    .ghostagram-property-port::after { position:absolute;left:50%;top:50%;width:3px;height:3px;border-radius:1px;background:currentColor;content:"";transform:translate(-50%,-50%);pointer-events:none; }
    .ghostagram-node.ghostagram-selected { outline:3px solid #0f766e; outline-offset:2px; box-shadow:0 0 0 5px rgba(13,148,136,.18); }
    .ghostagram-group.ghostagram-selected { outline:3px solid #0f766e; outline-offset:2px; box-shadow:0 0 0 5px rgba(13,148,136,.14); }
    .ghostagram-group-visibility { position:absolute;z-index:6;top:-34px;left:50%;display:grid;width:30px;height:25px;padding:0;border:1px solid #0f766e;border-radius:13px;place-items:center;color:#0f766e;background:#fff;box-shadow:0 4px 12px rgba(15,118,110,.22);transform:translateX(-50%);cursor:pointer;transition:background .14s ease,box-shadow .14s ease,transform .14s ease; }
    .ghostagram-group-visibility::after { position:absolute;bottom:-8px;left:50%;width:1px;height:7px;background:#0f766e;content:"";transform:translateX(-50%); }
    .ghostagram-group-visibility:hover { background:#f0fdfa;box-shadow:0 5px 15px rgba(15,118,110,.3);transform:translateX(-50%) translateY(-1px); }
    .ghostagram-group-visibility:focus-visible { outline:2px solid #14b8a6;outline-offset:2px; }
    .ghostagram-group-visibility[data-hidden="true"] { color:#fff;background:#0f766e; }
    .ghostagram-group-visibility-icon { display:block;width:16px;height:16px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8;pointer-events:none; }
    .ghostagram-group-visibility-slash { stroke-width:2.4; }
    .ghostagram-edge.ghostagram-selected { filter:drop-shadow(0 0 2px #0f766e); stroke-width:4px; }`;
  root.className = "ghostagram-root"; root.tabIndex = 0; root.setAttribute("role", "application"); root.setAttribute("aria-label", "Ghostagram diagram canvas"); root.style.cssText = "position:relative;overflow:hidden;width:100%;height:100%;touch-action:none;";
  stage.className = "ghostagram-stage"; stage.style.cssText = "position:absolute;inset:0;transform-origin:0 0;";
  groups.style.cssText = "position:absolute;inset:0;"; nodes.style.cssText = "position:absolute;inset:0;pointer-events:none;"; editors.style.cssText = "position:absolute;inset:0;pointer-events:none;z-index:5;";
  svg.style.cssText = "position:absolute;inset:0;width:100%;height:100%;overflow:visible;pointer-events:none;";
  overlay.style.cssText = "position:absolute;inset:0;width:100%;height:100%;overflow:visible;pointer-events:none;";
  selectionBox.className = "ghostagram-selection-lasso"; selectionBox.hidden = true; selectionBox.style.cssText = "position:absolute;pointer-events:none;border:1px solid #0f766e;background:rgba(13,148,136,.12);box-sizing:border-box;z-index:4;";
  for (const type of Object.keys(markerIds)) defs.append(createMarker(markerIds[type], type));
  svg.append(defs, edges); stage.append(groups, svg, nodes, overlay, editors, selectionBox); root.append(style, stage); host.append(root);
  return { root, stage, groups, nodes, editors, edges, overlay, selectionBox, markerIds, flowAnimationName, nodeById: new Map(), groupById: new Map(), pathById: new Map(), labelById: new Map(), customOverlayByKey: new Map(), edgeHandlesById: new Map(), waypointHandlesById: new Map() };
}
function createMarker(id, type) {
  const marker = document.createElementNS(SVG_NS, "marker"), descriptor = markerDescriptor(type);
  marker.id = id; marker.setAttribute("viewBox", descriptor.viewBox); marker.setAttribute("refX", descriptor.refX); marker.setAttribute("refY", descriptor.refY); marker.setAttribute("markerWidth", descriptor.markerWidth); marker.setAttribute("markerHeight", descriptor.markerHeight); marker.setAttribute("orient", "auto-start-reverse");
  for (const shape of descriptor.shapes) {
    const element = document.createElementNS(SVG_NS, shape.tag);
    for (const [name, value] of Object.entries(shape.attributes)) element.setAttribute(name, value);
    marker.append(element);
  }
  return marker;
}
function markerDescriptor(type) {
  const path = (d, attributes = {}) => ({ tag: "path", attributes: { d, fill: "none", stroke: "context-stroke", "stroke-width": "1.35", "stroke-linecap": "round", "stroke-linejoin": "round", ...attributes } });
  const circle = (cx, cy, r) => ({ tag: "circle", attributes: { cx, cy, r, fill: "white", stroke: "context-stroke", "stroke-width": "1.35" } });
  const basic = shapes => ({ viewBox: "0 0 10 10", refX: "9", refY: "5", markerWidth: "7", markerHeight: "7", shapes });
  const cardinality = shapes => ({ viewBox: "0 0 24 12", refX: "22", refY: "6", markerWidth: "14", markerHeight: "8", shapes });
  switch (type) {
    case "plain-arrow": return basic([path("M 0 0 L 10 5 L 0 10", { "stroke-width": "1.5" })]);
    case "triangle-open": return basic([path("M 0 0 L 10 5 L 0 10 z", { fill: "white" })]);
    case "diamond": return basic([path("M 0 5 L 5 0 L 10 5 L 5 10 z", { fill: "context-stroke", stroke: "none" })]);
    case "diamond-open": return basic([path("M 0 5 L 5 0 L 10 5 L 5 10 z", { fill: "white" })]);
    case "erd-one": return cardinality([path("M 15 1 L 15 11 M 20 1 L 20 11")]);
    case "erd-zero-one": return cardinality([circle("12", "6", "3.25"), path("M 20 1 L 20 11")]);
    case "erd-one-many": return cardinality([path("M 9 1 L 9 11 M 14 6 L 22 1 M 14 6 L 22 6 M 14 6 L 22 11")]);
    case "erd-zero-many": return cardinality([circle("9", "6", "3.25"), path("M 14 6 L 22 1 M 14 6 L 22 6 M 14 6 L 22 11")]);
    default: return basic([path("M 0 0 L 10 5 L 0 10 z", { fill: "context-stroke", stroke: "none" })]);
  }
}
function setBox(el, item) { el.style.position = "absolute"; el.style.left = `${item.x}px`; el.style.top = `${item.y}px`; el.style.width = `${item.width}px`; el.style.height = `${item.height}px`; }
function applyStyle(el, style, defaults) { Object.assign(el.style, defaults, style ?? {}); }
function portAnchorStyle(anchor, size = 12, strokeWidth = 1) {
  if (isPixelSideAnchor(anchor)) {
    const offset = -(size / 2 + strokeWidth);
    return `${anchor.side}:${offset}px;top:${anchor.offsetY}px;transform:translateY(-50%);`;
  }
  const relative = Array.isArray(anchor) && anchor.length === 2 && anchor.every(Number.isFinite) ? anchor : anchor && typeof anchor === "object" && Number.isFinite(anchor.x) && Number.isFinite(anchor.y) ? [anchor.x, anchor.y] : null;
  if (relative) return `left:${relative[0] * 100}%;top:${relative[1] * 100}%;transform:translate(-50%,-50%);`;
  return sideStyle(typeof anchor === "object" ? anchor?.type : anchor, size, strokeWidth);
}
function sideStyle(side, size = 12, strokeWidth = 1) { const offset = -(size / 2 + strokeWidth); return side === "left" ? `left:${offset}px;top:50%;transform:translateY(-50%);` : side === "top" ? `top:${offset}px;left:50%;transform:translateX(-50%);` : side === "bottom" ? `bottom:${offset}px;left:50%;transform:translateX(-50%);` : `right:${offset}px;top:50%;transform:translateY(-50%);`; }
function isPixelSideAnchor(anchor) { return ["property", "ordered"].includes(anchor?.type) && ["left", "right"].includes(anchor.side) && Number.isFinite(anchor.offsetY); }
function isInteractiveNodeTarget(target) { return Boolean(target?.closest?.(NODE_INTERACTIVE_SELECTOR)); }
function requireObject(value, code, message) { if (!value || typeof value !== "object" || Array.isArray(value)) throw new GhostagramError(code, message); }
function requireId(value, kind) { requireObject(value, "INVALID_MODEL", `${kind} must be an object.`); if (!value.id || typeof value.id !== "string") throw new GhostagramError("INVALID_MODEL", `${kind} requires a string id.`); }
function number(value, code, message) { if (!Number.isFinite(value)) throw new GhostagramError(code, message); return value; }
function revision(value, code, message) { if (!Number.isSafeInteger(value) || value < 0) throw new GhostagramError(code, message); return value; }
function positive(value, code, message) { if (!Number.isFinite(value) || value <= 0) throw new GhostagramError(code, message); return value; }
function clamp(value, min, max) { return Math.min(max, Math.max(min, value)); }
function snap(value, grid) { return grid > 1 ? Math.round(value / grid) * grid : value; }
function ceilToGrid(value, grid) { return grid > 1 ? Math.ceil(value / grid) * grid : value; }
function gridCssProjection(viewport, gridSize) {
  const modelSize = gridSize > 1 ? gridSize : 1, screenSize = modelSize * viewport.zoom;
  const phase = value => ((value % screenSize) + screenSize) % screenSize;
  // CSS radial gradients paint their dot at the center of each background tile.
  // Offset the tile origin by half a cell so the visible dot, rather than the
  // tile corner, represents the same model coordinate used by snap().
  const tileOffset = screenSize / 2;
  return {
    modelSize,
    screenSize,
    phaseX: phase(-viewport.x * viewport.zoom - tileOffset),
    phaseY: phase(-viewport.y * viewport.zoom - tileOffset)
  };
}
function asProblem(error) {
  if (error instanceof GhostagramError) return { code: error.code, message: error.message, details: error.details };
  const message = error instanceof Error && error.message ? error.message : "Ghostagram encountered an internal error.";
  return { code: "INTERNAL", message, details: error instanceof Error ? { name: error.name } : undefined };
}
function ok(requestId, renderedRevision, stats) { return { ok: true, requestId, renderedRevision, stats }; }
function failed(request, code, message, renderedRevision, details) { return { ok: false, requestId: request?.requestId ?? null, renderedRevision, stats: {}, problem: { code, message, details } }; }

export const __testing = { InteropEventQueue, anchorPoint, basicFlowchartRoutePoints, bezierControlDistance, bezierPath, buildRoutingContext, buildState, applyOperation, canConnect, canReconnect, canvasCenterPoint, canvasHitDescriptor, centerViewport, cloneState, collapsedProxyGroupForNode, connectionPoliciesCompatible, connectionPolicyAllows, dateTimeInputValue, deletionPlan, descendantGroupIds, descendantNodeIds, dragPosition, editableEdgeLabelValue, editableLabelValue, edgeGeometry, edgeLabelOffsets, edgeLabelPlacement, edgeLabelText, edgeRoutePoints, edgeSelectionPoints, edgeStyleDescriptor, edgesInRectangle, endpointDescriptor, exportSvgBounds, exportSvgDocument, fitViewport, flowAnimationDescriptor, flowchartOptions, flowchartPath, flowchartRouteScore, gridCssProjection, groupForGroupPosition, groupForNodePosition, groupMovePayload, groupVisibilityDescriptor, groupsForRender, historyDirectionForKey, iconifyIconName, isClickGesture, isGroupHiddenByCollapsedAncestor, isInteractiveNodeTarget, isNodeHiddenByCollapsedGroup, markerDescriptor, markerFor, multiDragPositions, nextNodePresentationRequest, nextSectionPresentationRequest, nodeContentMinimumHeight, nodeLayoutProjection, nodesInRectangle, normaliseNodePresentation, normaliseNodeProperties, normaliseNodeSections, normalisePropertyEditor, orderedNodePortAnchor, perimeterAnchorPoint, pointAlongPolyline, polylineIntersectsRectangle, portAnchorStyle, portRenderPlan, portVisualDescriptor, previewPointForPort, propertyCommitDecision, propertyDisplayValue, propertyEditorKind, propertyEditorStyle, propertyInputValue, propertyPortAnchor, propertyRowHeight, propertyValueSignature, proxyPortDescriptor, reconnectHandlePoint, rectangleForPoints, rectanglesIntersect, resizeDimensions, resolveEdgeDescriptor, resolvePortAnchor, revision, rotateAnchorPoint, rotationForPoint, route, scopesCompatible, selectableIds, selectedMovePositions, selectionIdsInRectangle, serialiseState, sideStyle, snap, translatePositions, validateConnectionPolicy, validateEdgeTypeDescriptor, validateEndpoint, validateState, viewportExportBounds, viewportPoint };
