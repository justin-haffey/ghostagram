const bridges = new WeakMap();

export function canvasClientCenter(root) {
  const dropZone = root.querySelector("[data-ghostagram-drop-zone]");
  if (!dropZone) throw new Error("The Ghostagram canvas drop zone was not found.");
  const rect = dropZone.getBoundingClientRect();
  return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
}

export function initializePaletteDrop(root, dotNetReference) {
  disposePaletteDrop(root);

  const dropZone = root.querySelector("[data-ghostagram-drop-zone]");
  const overlay = root.querySelector("[data-ghostagram-drop-overlay]");
  if (!dropZone) throw new Error("The Ghostagram canvas drop zone was not found.");

  let drag = null;
  const pointIsInCanvas = (x, y) => {
    const rect = dropZone.getBoundingClientRect();
    return x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
  };
  const showOverlay = (visible) => overlay?.classList.toggle("is-visible", visible);
  const clearDrag = () => {
    drag?.item.classList.remove("is-dragging");
    drag = null;
    showOverlay(false);
  };
  const beginDrag = (event, pointerId) => {
    const item = event.target.closest?.("[data-template-id]");
    if (!item || !root.contains(item) || event.button !== 0) return;
    drag = { id: item.dataset.templateId, item, pointerId, startX: event.clientX, startY: event.clientY, moved: false };
    if (typeof pointerId === "number") item.setPointerCapture?.(pointerId);
    item.classList.add("is-dragging");
    root.dataset.paletteLastInput = String(event.type);
    event.preventDefault();
    event.stopPropagation();
  };
  const pointerDown = (event) => beginDrag(event, event.pointerId);
  const pointerMove = (event) => {
    if (!drag || event.pointerId !== drag.pointerId) return;
    if (!drag.moved && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= 5) drag.moved = true;
    if (drag.moved) showOverlay(pointIsInCanvas(event.clientX, event.clientY));
    event.preventDefault();
    event.stopPropagation();
  };
  const pointerUp = async (event) => {
    if (!drag || event.pointerId !== drag.pointerId) return;
    const completed = drag.moved && pointIsInCanvas(event.clientX, event.clientY);
    const id = drag.id;
    clearDrag();
    event.preventDefault();
    event.stopPropagation();
    if (!completed || !id) return;
    try {
      await dotNetReference.invokeMethodAsync("DropPaletteItem", id, event.clientX, event.clientY);
    } catch (error) {
      console.error("Ghostagram palette drop failed", error);
    }
  };
  const pointerCancel = (event) => {
    if (drag && event.pointerId === drag.pointerId) clearDrag();
  };
  const mouseDown = (event) => {
    if (!drag) beginDrag(event, "mouse");
  };
  const mouseMove = (event) => {
    if (!drag || drag.pointerId !== "mouse") return;
    if (!drag.moved && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= 5) drag.moved = true;
    if (drag.moved) showOverlay(pointIsInCanvas(event.clientX, event.clientY));
    event.preventDefault();
    event.stopPropagation();
  };
  const mouseUp = async (event) => {
    if (!drag || drag.pointerId !== "mouse") return;
    const completed = drag.moved && pointIsInCanvas(event.clientX, event.clientY);
    const id = drag.id;
    clearDrag();
    event.preventDefault();
    event.stopPropagation();
    if (!completed || !id) return;
    try {
      await dotNetReference.invokeMethodAsync("DropPaletteItem", id, event.clientX, event.clientY);
    } catch (error) {
      console.error("Ghostagram palette drop failed", error);
    }
  };
  const keyDown = async (event) => {
    const item = event.target.closest?.("[data-template-id]");
    if (!item || !root.contains(item) || (event.key !== "Enter" && event.key !== " ")) return;
    event.preventDefault();
    const rect = dropZone.getBoundingClientRect();
    try {
      await dotNetReference.invokeMethodAsync("DropPaletteItem", item.dataset.templateId, rect.left + rect.width / 2, rect.top + rect.height / 2);
    } catch (error) {
      console.error("Ghostagram palette add failed", error);
    }
  };

  root.addEventListener("pointerdown", pointerDown, true);
  root.addEventListener("pointermove", pointerMove, true);
  root.addEventListener("pointerup", pointerUp, true);
  root.addEventListener("pointercancel", pointerCancel, true);
  root.addEventListener("mousedown", mouseDown, true);
  root.addEventListener("mousemove", mouseMove, true);
  root.addEventListener("mouseup", mouseUp, true);
  root.addEventListener("keydown", keyDown, true);
  root.dataset.paletteBridge = "ready";
  bridges.set(root, { pointerDown, pointerMove, pointerUp, pointerCancel, mouseDown, mouseMove, mouseUp, keyDown });
}

export function disposePaletteDrop(root) {
  const bridge = bridges.get(root);
  if (!bridge) return;
  root.removeEventListener("pointerdown", bridge.pointerDown, true);
  root.removeEventListener("pointermove", bridge.pointerMove, true);
  root.removeEventListener("pointerup", bridge.pointerUp, true);
  root.removeEventListener("pointercancel", bridge.pointerCancel, true);
  root.removeEventListener("mousedown", bridge.mouseDown, true);
  root.removeEventListener("mousemove", bridge.mouseMove, true);
  root.removeEventListener("mouseup", bridge.mouseUp, true);
  root.removeEventListener("keydown", bridge.keyDown, true);
  delete root.dataset.paletteBridge;
  bridges.delete(root);
}
