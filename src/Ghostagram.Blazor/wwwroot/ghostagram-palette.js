const controllers = new WeakMap();

export function initialize(root, dotNetReference) {
  dispose(root);
  const abort = new AbortController();
  const state = { root, dotNetReference, abort, target: null, overlay: null, drag: null, categoryTarget: null };
  const signal = abort.signal;
  const ownerDocument = root.ownerDocument;

  root.addEventListener("pointerdown", event => beginDrag(state, event, event.pointerId), { capture: true, signal });
  ownerDocument.addEventListener("pointermove", event => moveDrag(state, event, event.pointerId), { capture: true, signal });
  ownerDocument.addEventListener("pointerup", event => completeDrag(state, event, event.pointerId), { capture: true, signal });
  ownerDocument.addEventListener("pointercancel", event => cancelDrag(state, event.pointerId), { capture: true, signal });
  root.addEventListener("mousedown", event => { if (!state.drag) beginDrag(state, event, "mouse"); }, { capture: true, signal });
  ownerDocument.addEventListener("mousemove", event => moveDrag(state, event, "mouse"), { capture: true, signal });
  ownerDocument.addEventListener("mouseup", event => completeDrag(state, event, "mouse"), { capture: true, signal });
  root.addEventListener("keydown", event => invokeFromKeyboard(state, event), { capture: true, signal });
  root.dataset.ghostPalette = "ready";
  controllers.set(root, state);
}

export function setDropTarget(root, target, overlay) {
  const state = controllers.get(root);
  if (!state) throw new Error("Initialize the Ghostagram palette before attaching its drop target.");
  state.target = target ?? null;
  state.overlay = overlay ?? null;
}

export function dispose(root) {
  const state = controllers.get(root);
  if (!state) return;
  clearDrag(state);
  state.abort.abort();
  delete root.dataset.ghostPalette;
  controllers.delete(root);
}

function beginDrag(state, event, pointerId) {
  if (event.button !== 0 || event.target.closest?.("[data-palette-action]")) return;
  const item = event.target.closest?.("[data-template-id]");
  if (!item || !state.root.contains(item)) return;
  state.drag = { id: item.dataset.templateId, item, pointerId, startX: event.clientX, startY: event.clientY, moved: false };
  if (typeof pointerId === "number") {
    try { item.setPointerCapture?.(pointerId); }
    catch { /* Document-level listeners still keep the drag coherent. */ }
  }
  item.classList.add("is-dragging");
  event.preventDefault();
  event.stopPropagation();
}

function moveDrag(state, event, pointerId) {
  const drag = state.drag;
  if (!drag || drag.pointerId !== pointerId) return;
  if (!drag.moved && Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) >= 5) drag.moved = true;
  if (drag.moved) {
    const overTarget = pointIsInTarget(state, event.clientX, event.clientY);
    showOverlay(state, overTarget);
    setCategoryTarget(state, overTarget ? null : categoryAtPoint(state, event.clientX, event.clientY));
  }
  event.preventDefault();
  event.stopPropagation();
}

async function completeDrag(state, event, pointerId) {
  const drag = state.drag;
  if (!drag || drag.pointerId !== pointerId) return;
  const overTarget = drag.moved && pointIsInTarget(state, event.clientX, event.clientY);
  const category = drag.moved && !overTarget ? categoryAtPoint(state, event.clientX, event.clientY)?.dataset.paletteCategory : null;
  const itemId = drag.id;
  clearDrag(state);
  event.preventDefault();
  event.stopPropagation();
  if (!itemId || (!overTarget && !category)) return;
  try {
    if (overTarget) await state.dotNetReference.invokeMethodAsync("OnPaletteItemDropped", itemId, event.clientX, event.clientY);
    else await state.dotNetReference.invokeMethodAsync("OnPaletteItemMoved", itemId, category);
  } catch (error) {
    console.error("Ghostagram palette action failed", error);
  }
}

function cancelDrag(state, pointerId) {
  if (state.drag?.pointerId === pointerId) clearDrag(state);
}

async function invokeFromKeyboard(state, event) {
  if (event.target.closest?.("[data-palette-action]")) return;
  const item = event.target.closest?.("[data-template-id]");
  if (!item || !state.root.contains(item) || (event.key !== "Enter" && event.key !== " ")) return;
  event.preventDefault();
  try {
    await state.dotNetReference.invokeMethodAsync("OnPaletteItemInvoked", item.dataset.templateId);
  } catch (error) {
    console.error("Ghostagram palette keyboard action failed", error);
  }
}

function pointIsInTarget(state, x, y) {
  if (!state.target?.isConnected) return false;
  const rect = state.target.getBoundingClientRect();
  return x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom;
}

function categoryAtPoint(state, x, y) {
  const candidate = state.root.ownerDocument.elementFromPoint(x, y)?.closest?.("[data-palette-category]");
  return candidate && state.root.contains(candidate) ? candidate : null;
}

function showOverlay(state, visible) { state.overlay?.classList.toggle("is-visible", visible); }

function setCategoryTarget(state, target) {
  if (state.categoryTarget === target) return;
  state.categoryTarget?.classList.remove("is-drop-target");
  state.categoryTarget = target;
  state.categoryTarget?.classList.add("is-drop-target");
}

function clearDrag(state) {
  state.drag?.item.classList.remove("is-dragging");
  state.drag = null;
  showOverlay(state, false);
  setCategoryTarget(state, null);
}
