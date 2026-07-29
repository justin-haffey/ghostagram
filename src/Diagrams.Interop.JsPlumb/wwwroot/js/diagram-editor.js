import { newInstance } from "../vendor/jsplumb/jsplumb.browser-ui.es.js";

let activeRuntime = null;

export function initialize(host, bridge) {
    dispose();

    const root = document.createElement("div");
    root.className = "diagram-runtime-host";
    root.tabIndex = 0;

    const viewport = document.createElement("div");
    viewport.className = "diagram-viewport";
    viewport.setAttribute("aria-label", "Diagram canvas");

    const stage = document.createElement("div");
    stage.className = "diagram-stage";

    viewport.append(stage);
    root.append(viewport);
    host.replaceChildren(root);

    const instance = newInstance({
        container: stage,
        connector: { type: "Flowchart", options: { cornerRadius: 8, stub: 24 } },
        endpoint: { type: "Dot", options: { radius: 5 } },
        paintStyle: { stroke: "#1f827b", strokeWidth: 3 },
        hoverPaintStyle: { stroke: "#2ca8a0", strokeWidth: 4 }
    });

    const runtime = {
        bridge,
        connectionIds: new WeakMap(),
        document: null,
        edgeIds: new Set(),
        groupIds: new Set(),
        host,
        instance,
        layoutKind: "Freeform",
        nodeIds: new Set(),
        root,
        stage,
        suppressConnectionEvents: false,
        suppressDragEvents: false,
        suppressViewportEvent: false,
        viewport,
        zoom: 1
    };

    activeRuntime = runtime;
    bindRuntimeEvents(runtime);
}

export function renderDocument(documentModel) {
    const runtime = requireRuntime();
    runtime.document = documentModel;
    runtime.suppressConnectionEvents = true;
    runtime.suppressDragEvents = true;
    runtime.host.dataset.renderCount = String((Number(runtime.host.dataset.renderCount) || 0) + 1);

    try {
        runtime.instance.reset();
        runtime.stage.replaceChildren();

        const canvas = documentModel.canvas ?? {};
        const width = positiveNumber(canvas.width, 6000);
        const height = positiveNumber(canvas.height, 4000);
        const gridSize = positiveNumber(canvas.gridSize, 24);
        runtime.stage.style.width = `${width}px`;
        runtime.stage.style.height = `${height}px`;
        runtime.viewport.style.backgroundSize = `${gridSize}px ${gridSize}px, ${gridSize}px ${gridSize}px, auto, auto`;

        const layers = new Map((documentModel.layers ?? []).map(layer => [layer.id, layer]));
        const styles = new Map((documentModel.styles ?? []).map(style => [style.id, style]));
        const defaultStyle = styles.get("default") ?? {
            fill: "#f8f4ea",
            border: "#314052",
            text: "#101723",
            edge: "#1f827b",
            accent: "#2ca8a0"
        };
        const groups = documentModel.groups ?? [];
        const collapsedNodeIds = new Set(
            groups
                .filter(group => group.collapsed)
                .flatMap(group => group.childNodeIds ?? []));

        runtime.nodeIds = retainExisting(runtime.nodeIds, (documentModel.nodes ?? []).map(node => node.id));
        runtime.groupIds = retainExisting(runtime.groupIds, groups.map(group => group.id));
        runtime.edgeIds = retainExisting(runtime.edgeIds, (documentModel.edges ?? []).map(edge => edge.id));

        for (const group of groups) {
            const layer = resolveLayer(layers, group.layerId);
            if (layer && !layer.isVisible) {
                continue;
            }

            const element = createGroupElement(runtime, group, layer);
            runtime.stage.append(element);
            runtime.instance.manage(element);
        }

        const nodeElements = new Map();
        const portElements = new Map();
        const endpointByPortId = new Map();

        for (const node of documentModel.nodes ?? []) {
            const layer = resolveLayer(layers, node.layerId);
            if ((layer && !layer.isVisible) || collapsedNodeIds.has(node.id)) {
                continue;
            }

            const style = styles.get(node.styleToken) ?? defaultStyle;
            const element = createNodeElement(runtime, node, style, layer);
            runtime.stage.append(element);
            runtime.instance.manage(element);
            nodeElements.set(node.id, element);

            for (const portId of node.portIds ?? []) {
                const port = (documentModel.ports ?? []).find(candidate => candidate.id === portId);
                if (!port) {
                    continue;
                }

                const portElement = createPortElement(port);
                element.append(portElement);
                portElements.set(port.id, portElement);
            }
        }

        for (const port of documentModel.ports ?? []) {
            const element = portElements.get(port.id);
            if (!element) {
                continue;
            }

            const role = lower(port.role);
            const endpoint = runtime.instance.addEndpoint(element, {
                anchor: "Center",
                endpoint: endpointSpec(port.endpointKind),
                maxConnections: Number.isFinite(port.maxConnections) ? port.maxConnections : -1,
                paintStyle: {
                    fill: "#faf9f5",
                    outlineStroke: "#1f827b",
                    outlineWidth: 2
                },
                source: role !== "input",
                target: role !== "output"
            });
            endpointByPortId.set(port.id, endpoint);
        }

        for (const edge of documentModel.edges ?? []) {
            const source = endpointByPortId.get(edge.sourcePortId);
            const target = endpointByPortId.get(edge.targetPortId);
            if (!source || !target) {
                continue;
            }

            const style = styles.get(edge.styleToken) ?? defaultStyle;
            try {
                const connection = runtime.instance.connect({
                    connector: connectorSpec(edge.connectorKind),
                    cssClass: edgeCssClass(edge),
                    overlays: edgeOverlays(edge, style),
                    paintStyle: { stroke: style.edge ?? defaultStyle.edge, strokeWidth: 3 },
                    source,
                    target
                });
                runtime.connectionIds.set(connection, edge.id);
            } catch (error) {
                console.warn(`Unable to render edge '${edge.id}'.`, error);
            }
        }

        applySelectionClasses(runtime);
        runtime.instance.repaintEverything();
        setViewport(documentModel.viewportState ?? { zoom: 1, scrollLeft: 0, scrollTop: 0 });
    } finally {
        runtime.suppressConnectionEvents = false;
        runtime.suppressDragEvents = false;
    }
}

export function applyPatch(documentModel) {
    renderDocument(documentModel);
}

export function setLayout(layoutKind) {
    const runtime = requireRuntime();
    runtime.layoutKind = layoutKind ?? "Freeform";
    runtime.stage.dataset.layoutKind = runtime.layoutKind;
}

export function setViewport(viewportState) {
    const runtime = requireRuntime();
    const zoom = clamp(numberOr(viewportState?.zoom, 1), 0.2, 3);

    runtime.suppressViewportEvent = true;
    runtime.zoom = zoom;
    runtime.stage.style.transform = `scale(${zoom})`;
    runtime.stage.style.transformOrigin = "0 0";
    runtime.instance.setZoom(zoom);
    runtime.viewport.scrollLeft = Math.max(0, numberOr(viewportState?.scrollLeft, 0));
    runtime.viewport.scrollTop = Math.max(0, numberOr(viewportState?.scrollTop, 0));

    requestAnimationFrame(() => {
        if (activeRuntime === runtime) {
            runtime.suppressViewportEvent = false;
        }
    });
}

export function fitToDiagram() {
    const runtime = requireRuntime();
    const bounds = contentBounds(runtime.document);
    if (!bounds) {
        return;
    }

    const padding = 96;
    const availableWidth = Math.max(1, runtime.viewport.clientWidth - padding);
    const availableHeight = Math.max(1, runtime.viewport.clientHeight - padding);
    const zoom = clamp(Math.min(availableWidth / bounds.width, availableHeight / bounds.height), 0.2, 2);
    centerBounds(runtime, bounds, zoom);
}

export function centerOnSelection(nodeIds) {
    const runtime = requireRuntime();
    const requestedIds = new Set(nodeIds ?? []);
    const nodes = (runtime.document?.nodes ?? []).filter(node => requestedIds.has(node.id));
    const bounds = boundsFor(nodes.map(node => node.bounds));
    if (!bounds) {
        return;
    }

    centerBounds(runtime, bounds, runtime.zoom);
}

export function downloadSvg(fileName, svgMarkup) {
    downloadBlob(fileName, new Blob([svgMarkup], { type: "image/svg+xml;charset=utf-8" }));
}

export async function downloadPng(fileName, svgMarkup) {
    const source = new Blob([svgMarkup], { type: "image/svg+xml;charset=utf-8" });
    const sourceUrl = URL.createObjectURL(source);

    try {
        const image = await loadImage(sourceUrl);
        const canvas = document.createElement("canvas");
        canvas.width = Math.max(1, image.naturalWidth || image.width);
        canvas.height = Math.max(1, image.naturalHeight || image.height);
        const context = canvas.getContext("2d");
        if (!context) {
            throw new Error("PNG export requires a 2D canvas context.");
        }

        context.drawImage(image, 0, 0);
        const png = await new Promise((resolve, reject) => {
            canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error("Unable to encode PNG.")), "image/png");
        });
        downloadBlob(fileName, png);
    } finally {
        URL.revokeObjectURL(sourceUrl);
    }
}

export function dispose() {
    if (!activeRuntime) {
        return;
    }

    activeRuntime.instance.destroy();
    activeRuntime.host.replaceChildren();
    activeRuntime = null;
}

function bindRuntimeEvents(runtime) {
    runtime.root.addEventListener("pointerdown", () => runtime.root.focus({ preventScroll: true }));
    runtime.root.addEventListener("keydown", event => handleHotkey(runtime, event));
    runtime.stage.addEventListener("click", event => {
        if (event.target === runtime.stage) {
            runtime.nodeIds.clear();
            runtime.groupIds.clear();
            runtime.edgeIds.clear();
            publishSelection(runtime, "none", null);
        }
    });
    runtime.viewport.addEventListener("scroll", () => {
        if (!runtime.suppressViewportEvent) {
            invokeBridge(
                runtime,
                "OnViewportChanged",
                runtime.zoom,
                runtime.viewport.scrollLeft,
                runtime.viewport.scrollTop);
        }
    }, { passive: true });

    runtime.instance.bind("drag:stop", payload => {
        if (runtime.suppressDragEvents) {
            return;
        }

        const gridSize = positiveNumber(runtime.document?.canvas?.gridSize, 1);
        let movedElement = false;
        for (const item of payload?.elements ?? []) {
            const element = item.el;
            const position = item.pos ?? {};
            if (element?.dataset.nodeId) {
                const bounds = snapDraggedBounds(element, position, gridSize);
                updateRuntimeBounds(runtime, "nodes", element.dataset.nodeId, bounds);
                invokeBridge(
                    runtime,
                    "OnNodeMoved",
                    element.dataset.nodeId,
                    bounds.x,
                    bounds.y,
                    bounds.width,
                    bounds.height);
                movedElement = true;
            } else if (element?.dataset.groupId) {
                const bounds = snapDraggedBounds(element, position, gridSize);
                updateRuntimeBounds(runtime, "groups", element.dataset.groupId, bounds);
                invokeBridge(
                    runtime,
                    "OnGroupBoundsChanged",
                    element.dataset.groupId,
                    bounds.x,
                    bounds.y,
                    bounds.width,
                    bounds.height);
                movedElement = true;
            }
        }

        if (movedElement) {
            runtime.instance.repaintEverything();
        }
    });

    runtime.instance.bind("connection", payload => {
        if (runtime.suppressConnectionEvents) {
            return;
        }

        const sourcePortId = endpointPortId(payload?.sourceEndpoint);
        const targetPortId = endpointPortId(payload?.targetEndpoint);
        if (sourcePortId && targetPortId) {
            invokeBridge(runtime, "OnEdgeCreated", sourcePortId, targetPortId);
        }
    });

    runtime.instance.bind("connection:detach", payload => {
        if (runtime.suppressConnectionEvents) {
            return;
        }

        const edgeId = runtime.connectionIds.get(payload?.connection);
        if (edgeId) {
            invokeBridge(runtime, "OnEdgeDeleted", edgeId);
        }
    });

    runtime.instance.bind("connection:click", connection => {
        const edgeId = runtime.connectionIds.get(connection);
        if (!edgeId) {
            return;
        }

        runtime.nodeIds.clear();
        runtime.groupIds.clear();
        runtime.edgeIds = new Set([edgeId]);
        publishSelection(runtime, "edge", edgeId);
    });
}

function createNodeElement(runtime, node, style, layer) {
    const element = document.createElement("div");
    element.id = domId("diagram-node", node.id);
    element.className = "diagram-node";
    element.dataset.nodeId = node.id;
    element.style.left = `${numberOr(node.bounds?.x, 0)}px`;
    element.style.top = `${numberOr(node.bounds?.y, 0)}px`;
    element.style.width = `${positiveNumber(node.bounds?.width, 180)}px`;
    element.style.height = `${positiveNumber(node.bounds?.height, 96)}px`;
    element.style.zIndex = `${numberOr(node.zIndex, 0) + numberOr(layer?.order, 0) * 1000}`;
    element.style.background = style.fill;
    element.style.borderColor = style.border;
    element.style.color = style.text;
    element.setAttribute("role", "button");
    element.setAttribute("aria-label", node.label || node.stencilKey || "Diagram node");

    if (layer?.isLocked) {
        element.classList.add("is-locked");
        element.setAttribute("data-jtk-not-draggable", "true");
    }

    const title = document.createElement("div");
    title.className = "diagram-node__title";
    title.textContent = node.label || "Untitled";

    const meta = document.createElement("div");
    meta.className = "diagram-node__meta";
    meta.textContent = node.stencilKey || "Node";

    element.append(title, meta);

    const tags = Array.from(node.tags ?? []);
    if (tags.length > 0) {
        const tagElement = document.createElement("div");
        tagElement.className = "diagram-node__tags";
        tagElement.textContent = tags.join(" • ");
        element.append(tagElement);
    }

    element.addEventListener("click", event => {
        event.stopPropagation();
        selectElement(runtime, "node", node.id, event.ctrlKey || event.metaKey);
    });
    return element;
}

function createGroupElement(runtime, group, layer) {
    const element = document.createElement("section");
    element.id = domId("diagram-group", group.id);
    element.className = "diagram-group";
    element.dataset.groupId = group.id;
    element.style.left = `${numberOr(group.bounds?.x, 0)}px`;
    element.style.top = `${numberOr(group.bounds?.y, 0)}px`;
    element.style.width = `${positiveNumber(group.bounds?.width, 420)}px`;
    element.style.height = `${positiveNumber(group.bounds?.height, 260)}px`;
    element.style.zIndex = `${numberOr(group.zIndex, 0) + numberOr(layer?.order, 0) * 1000}`;
    element.setAttribute("aria-label", group.label || "Diagram group");

    if (group.collapsed) {
        element.classList.add("is-collapsed");
    }
    if (layer?.isLocked) {
        element.classList.add("is-locked");
        element.setAttribute("data-jtk-not-draggable", "true");
    }

    const header = document.createElement("div");
    header.className = "diagram-group__header";

    const label = document.createElement("strong");
    label.textContent = group.label || "Group";

    const toggle = document.createElement("button");
    toggle.className = "diagram-group__toggle";
    toggle.type = "button";
    toggle.textContent = group.collapsed ? "+" : "−";
    toggle.setAttribute("aria-label", group.collapsed ? "Expand group" : "Collapse group");
    toggle.addEventListener("click", event => {
        event.stopPropagation();
        invokeBridge(runtime, "OnGroupCollapseChanged", group.id, !group.collapsed);
    });

    header.append(label, toggle);
    element.append(header);
    element.addEventListener("click", event => {
        event.stopPropagation();
        selectElement(runtime, "group", group.id, event.ctrlKey || event.metaKey);
    });
    return element;
}

function createPortElement(port) {
    const element = document.createElement("span");
    element.id = domId("diagram-port", port.id);
    element.className = `diagram-port side-${lower(port.side)} role-${lower(port.role)}`;
    element.dataset.portId = port.id;
    element.title = port.label || port.role || "Port";
    element.setAttribute("aria-label", element.title);
    return element;
}

function selectElement(runtime, kind, id, additive) {
    if (!additive) {
        runtime.nodeIds.clear();
        runtime.groupIds.clear();
        runtime.edgeIds.clear();
    }

    const target = kind === "node"
        ? runtime.nodeIds
        : kind === "group"
            ? runtime.groupIds
            : runtime.edgeIds;

    if (additive && target.has(id)) {
        target.delete(id);
    } else {
        target.add(id);
    }

    applySelectionClasses(runtime);
    publishSelection(runtime, kind, target.has(id) ? id : null);
}

function publishSelection(runtime, kind, id) {
    invokeBridge(
        runtime,
        "OnSelectionChanged",
        kind,
        id,
        Array.from(runtime.nodeIds),
        Array.from(runtime.groupIds),
        Array.from(runtime.edgeIds));
}

function applySelectionClasses(runtime) {
    for (const element of runtime.stage.querySelectorAll("[data-node-id]")) {
        element.classList.toggle("is-selected", runtime.nodeIds.has(element.dataset.nodeId));
    }
    for (const element of runtime.stage.querySelectorAll("[data-group-id]")) {
        element.classList.toggle("is-selected", runtime.groupIds.has(element.dataset.groupId));
    }
}

function handleHotkey(runtime, event) {
    const modifier = event.ctrlKey || event.metaKey;
    let command = null;

    if (modifier && lower(event.key) === "z") {
        command = event.shiftKey ? "redo" : "undo";
    } else if (modifier && lower(event.key) === "y") {
        command = "redo";
    } else if (modifier && lower(event.key) === "c") {
        command = "copy";
    } else if (modifier && lower(event.key) === "v") {
        command = "paste";
    } else if (modifier && lower(event.key) === "d") {
        command = "duplicate";
    } else if (modifier && lower(event.key) === "a") {
        command = "selectAll";
    } else if (event.key === "Delete" || event.key === "Backspace") {
        command = "delete";
    } else if (event.key === "ArrowLeft") {
        command = "nudgeLeft";
    } else if (event.key === "ArrowRight") {
        command = "nudgeRight";
    } else if (event.key === "ArrowUp") {
        command = "nudgeUp";
    } else if (event.key === "ArrowDown") {
        command = "nudgeDown";
    }

    if (command) {
        event.preventDefault();
        invokeBridge(runtime, "OnHotkeyPressed", command);
    }
}

function connectorSpec(connectorKind) {
    switch (lower(connectorKind)) {
        case "straight":
            return "Straight";
        case "bezier":
            return { type: "Bezier", options: { curviness: 72 } };
        case "statemachine":
            return { type: "StateMachine", options: { curviness: 48 } };
        default:
            return { type: "Flowchart", options: { cornerRadius: 8, stub: 24 } };
    }
}

function endpointSpec(endpointKind) {
    switch (lower(endpointKind)) {
        case "rectangle":
            return { type: "Rectangle", options: { width: 12, height: 12 } };
        case "blank":
            return "Blank";
        default:
            return { type: "Dot", options: { radius: 5 } };
    }
}

function edgeOverlays(edge, style) {
    const overlays = [];
    if (edge.label) {
        overlays.push({
            type: "Label",
            options: { cssClass: "jtk-default-label", id: `label-${edge.id}`, label: edge.label }
        });
    }

    const source = markerOverlay(edge.markers?.source, 0, -1, style.edge);
    const target = markerOverlay(edge.markers?.target, 1, 1, style.edge);
    if (source) {
        overlays.push(source);
    }
    if (target) {
        overlays.push(target);
    }
    return overlays;
}

function markerOverlay(marker, location, direction, fill) {
    switch (lower(marker)) {
        case "arrow":
        case "triangle":
            return {
                type: "Arrow",
                options: { direction, fill, foldback: marker === "Triangle" ? 1 : 0.7, location, width: 13 }
            };
        case "diamond":
        case "hollowdiamond":
            return {
                type: "Diamond",
                options: {
                    direction,
                    fill: lower(marker) === "hollowdiamond" ? "#faf9f5" : fill,
                    location,
                    outlineStroke: fill,
                    width: 14
                }
            };
        default:
            return null;
    }
}

function edgeCssClass(edge) {
    const animation = lower(edge.animation);
    return animation === "none" || !animation ? "" : `edge-animation-${animation}`;
}

function centerBounds(runtime, bounds, zoom) {
    setViewport({
        zoom,
        scrollLeft: Math.max(0, bounds.x * zoom - (runtime.viewport.clientWidth - bounds.width * zoom) / 2),
        scrollTop: Math.max(0, bounds.y * zoom - (runtime.viewport.clientHeight - bounds.height * zoom) / 2)
    });
}

function contentBounds(documentModel) {
    if (!documentModel) {
        return null;
    }

    return boundsFor([
        ...(documentModel.nodes ?? []).map(node => node.bounds),
        ...(documentModel.groups ?? []).map(group => group.bounds)
    ]);
}

function boundsFor(boundsList) {
    const values = boundsList.filter(Boolean);
    if (values.length === 0) {
        return null;
    }

    const left = Math.min(...values.map(bounds => numberOr(bounds.x, 0)));
    const top = Math.min(...values.map(bounds => numberOr(bounds.y, 0)));
    const right = Math.max(...values.map(bounds => numberOr(bounds.x, 0) + positiveNumber(bounds.width, 1)));
    const bottom = Math.max(...values.map(bounds => numberOr(bounds.y, 0) + positiveNumber(bounds.height, 1)));
    return { x: left, y: top, width: right - left, height: bottom - top };
}

function resolveLayer(layers, layerId) {
    return layers.get(layerId) ?? layers.values().next().value ?? null;
}

function retainExisting(current, availableIds) {
    const available = new Set(availableIds);
    return new Set(Array.from(current).filter(id => available.has(id)));
}

function snapDraggedBounds(element, position, gridSize) {
    const x = Math.round(numberOr(position.x, element.offsetLeft) / gridSize) * gridSize;
    const y = Math.round(numberOr(position.y, element.offsetTop) / gridSize) * gridSize;
    element.style.left = `${x}px`;
    element.style.top = `${y}px`;
    return {
        x,
        y,
        width: element.offsetWidth,
        height: element.offsetHeight
    };
}

function updateRuntimeBounds(runtime, collectionName, id, bounds) {
    const item = (runtime.document?.[collectionName] ?? []).find(candidate => candidate.id === id);
    if (item) {
        item.bounds = bounds;
    }
}

function endpointPortId(endpoint) {
    return endpoint?.element?.dataset?.portId
        ?? endpoint?.canvas?.parentElement?.dataset?.portId
        ?? null;
}

function invokeBridge(runtime, method, ...args) {
    runtime.bridge
        .invokeMethodAsync(method, ...args)
        .catch(error => console.error(`Diagram bridge call '${method}' failed.`, error));
}

function requireRuntime() {
    if (!activeRuntime) {
        throw new Error("The diagram runtime has not been initialized.");
    }
    return activeRuntime;
}

function downloadBlob(fileName, blob) {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 0);
}

function loadImage(url) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error("Unable to load the SVG for PNG export."));
        image.src = url;
    });
}

function domId(prefix, value) {
    return `${prefix}-${String(value).replace(/[^a-zA-Z0-9_-]/g, "-")}`;
}

function numberOr(value, fallback) {
    return Number.isFinite(value) ? value : fallback;
}

function positiveNumber(value, fallback) {
    return Number.isFinite(value) && value > 0 ? value : fallback;
}

function clamp(value, minimum, maximum) {
    return Math.min(maximum, Math.max(minimum, value));
}

function lower(value) {
    return String(value ?? "").toLowerCase();
}
