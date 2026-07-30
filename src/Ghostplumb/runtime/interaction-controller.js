/**
 * DOM event wiring is kept outside the engine's document/reducer lifecycle.
 * Gesture-specific methods remain on the engine so all browser proposals use
 * the existing event envelope and cannot mutate committed state directly.
 */
export class InteractionController {
  constructor(engine) {
    this.engine = engine;
    this.abort = new AbortController();
  }

  bind() {
    const engine = this.engine, signal = this.abort.signal;
    engine.dom.root.addEventListener("pointerdown", event => {
      if (!event.target.closest?.(".ghostplumb-label-editor")) engine.finishLabelEdit(true);
    }, { capture: true, signal });

    engine.dom.root.addEventListener("wheel", event => {
      if (!event.ctrlKey && !engine.options.wheelZoom) return;
      event.preventDefault();
      const zoom = clamp(engine.state.viewport.zoom * (event.deltaY < 0 ? 1.1 : .9), engine.options.minZoom, engine.options.maxZoom);
      engine.state.viewport = { ...engine.state.viewport, zoom };
      engine.pending.viewport = true;
      engine.schedule();
      engine.emit("viewport.changing", { viewport: engine.state.viewport }, "browser");
    }, { passive: false, signal });

    engine.dom.root.addEventListener("keydown", event => this.#onKeyDown(event), { signal });
    engine.dom.root.addEventListener("pointerdown", event => this.#onPointerDown(event), { signal });
  }

  dispose() { this.abort.abort(); }

  #onKeyDown(event) {
    const engine = this.engine, historyDirection = historyDirectionForKey(event);
    if (historyDirection) {
      event.preventDefault();
      engine.emit(`history.${historyDirection}Requested`, { documentId: engine.state.documentId }, "browser");
      return;
    }
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === "a") {
      const ids = engine.selectableIds();
      event.preventDefault();
      engine.previewSelection = new Set(ids);
      engine.pending.selection = true;
      engine.schedule();
      engine.emit("selection.changed", { kind: "all", ids }, "browser");
      return;
    }
    if (event.ctrlKey || event.metaKey || event.altKey) return;
    if (event.key === "F2") {
      const selected = [...(engine.previewSelection ?? engine.state.selection)];
      const selectedId = selected.length === 1 ? selected[0] : null;
      if (selectedId && (engine.state.nodes.has(selectedId) || engine.state.groups.has(selectedId) || engine.state.edges.has(selectedId))) {
        event.preventDefault();
        engine.startLabelEdit(engine.state.nodes.has(selectedId) ? "node" : engine.state.groups.has(selectedId) ? "group" : "edge", selectedId);
      }
      return;
    }
    if (event.key === "Escape") {
      if (!(engine.previewSelection ?? engine.state.selection).size) return;
      event.preventDefault();
      engine.previewSelection = new Set();
      engine.pending.selection = true;
      engine.schedule();
      engine.emit("selection.changed", { kind: "keyboard", ids: [] }, "browser");
      return;
    }
    if (event.key === "Delete" || event.key === "Backspace") {
      if (engine.requestSelectionDeletion()) event.preventDefault();
      return;
    }
    const movement = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[event.key];
    if (!movement) return;
    const step = Math.max(1, engine.options.gridSize) * (event.shiftKey ? 10 : 1);
    if (engine.moveSelectionByKeyboard(movement[0] * step, movement[1] * step)) event.preventDefault();
  }

  #onPointerDown(event) {
    const engine = this.engine, inGroup = event.target.closest?.("[data-group-id]");
    if (event.target.closest?.("[data-node-id],[data-port-id],[data-edge-id],button") || (inGroup && !event.shiftKey)) return;
    if (event.button === 0 && event.shiftKey) { engine.startSelectionLasso(event); return; }
    if (event.button === 0) { engine.startCanvasDeselection(event); return; }
    if (event.button !== 1) return;
    event.preventDefault();
    const start = { x: event.clientX, y: event.clientY, viewport: { ...engine.state.viewport } };
    const move = pointer => {
      const zoom = engine.state.viewport.zoom;
      engine.state.viewport = { ...start.viewport, x: start.viewport.x - (pointer.clientX - start.x) / zoom, y: start.viewport.y - (pointer.clientY - start.y) / zoom };
      engine.pending.viewport = true;
      engine.schedule();
      engine.emit("viewport.changing", { viewport: engine.state.viewport }, "browser", true);
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      engine.emit("viewport.changed", { viewport: engine.state.viewport }, "browser");
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up, { once: true });
  }
}

function clamp(value, min, max) { return Math.min(max, Math.max(min, value)); }
function historyDirectionForKey(event) {
  if (!(event.ctrlKey || event.metaKey) || event.altKey) return null;
  const key = event.key.toLowerCase();
  return key === "z" ? (event.shiftKey ? "redo" : "undo") : key === "y" ? "redo" : null;
}
