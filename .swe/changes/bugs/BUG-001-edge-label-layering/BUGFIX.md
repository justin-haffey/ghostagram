---
title: "Keep edge labels readable and aligned"
artifact_type: "bugfix"
id: "BUG-001"
status: "Closed"
authority: "solution"
scope: "Ghostagram browser renderer"
parent: "Ghostagram retained DOM/SVG rendering design"
upstream:
  repository: "ghostagram"
  artifact_id: "GHOSTAGRAM-DESIGN"
  path: "src/Ghostagram/docs/DESIGN.md"
  revision: "63a0227"
owners:
  - "Codex"
created: "2026_08_24"
updated: "2026_08_24"
template_version: "2.0.0"
---

# Keep edge labels readable and aligned

## Expected and Actual Behavior

- Expected: An edge label is centered on its computed routed location, remains readable when its route approaches or crosses a node, and can be dragged to a persistent manual offset.
- Actual: The label starts at the computed point instead of being centered on it, and the edge SVG containing the label is painted below the node DOM. Long labels therefore extend into a destination node and are then partially hidden by it.
- Reproduction: The supplied screenshot shows the `preliminary_report` edge label extending under the destination Text Node. Current source establishes that `renderEdge` appends the SVG text to `dom.edges`, while `buildRoot` appends the node layer after the edge SVG. Existing `edge.labelPosition.commit` handling establishes that manual label dragging is already supported and persisted.

## Eligibility and Impact

- Fast-path eligible: YES. This corrects presentation behavior already owned by the Ghostagram renderer.
- Architecture/contracts affected: No semantic, persistence, protocol, or cross-solution contract change. The existing `labelOffsetX`, `labelOffsetY`, and `edge.labelPosition.commit` contract remains unchanged. The renderer design documentation needs a paint-order clarification.
- Escalation: NONE.

## Root Cause

`edgeLabelPlacement` returns the route point plus offsets, but the SVG text uses the browser's default start anchor. The entire label therefore grows to the right of the intended location. In addition, `renderEdge` inserts the text into the edge-path SVG, and `buildRoot` paints the node DOM later. Node boxes consequently occlude any label text that overlaps their bounds.

## Fix Design and Change Map

- `src/Ghostagram/ghostagram.js`: Center labels on the routed point, add a contrast halo, paint live labels in the foreground interaction SVG below handles, and emit exported labels after nodes for matching foreground order.
- `src/Ghostagram/test/ghostagram.test.mjs`: Add regression coverage for centered label export and foreground ordering.
- `src/Ghostagram/docs/DESIGN.md`: Clarify the renderer layer order and existing draggable-label ownership.
- `.swe/changes/bugs/BUG-001-edge-label-layering/BUGFIX.md`: Record lifecycle, evidence, validation, and closure.

## Verification

| Check | Result | Evidence |
|---|---|---|
| JavaScript renderer suite including new edge-label regression | PASS | `cmd.exe /d /c npm.cmd test -- --test-name-pattern="SVG export centers edge labels"` from `src/Ghostagram`: 99 passed, 0 failed; the Node runner executed the complete suite |
| Release solution build | PASS | `dotnet build Ghostagram.slnx -c Release --no-restore`: 0 warnings, 0 errors. The first sandboxed run was blocked from writing a referenced `ghostworx-system/obj` cache; the identical approved rerun passed |
| Visible browser rendering and drag persistence | PASS | Local server `http://127.0.0.1:5256` reused owned PID 25540, `/healthz` returned HTTP 200 with `{ "status": "ok" }`. The saved laboratory case rendered `preliminary_report` centered before Text Node with the label as the top hit-tested element. The isolated `/ghostagram/demo/demo.html` harness persisted two label drags and two Undo actions restored original coordinates. Laboratory and harness warning/error consoles were empty |
| Repository diff integrity | PASS | `git diff --check` exited 0; only line-ending conversion warnings were reported |

## Validation and Closure

| Field | Value |
|---|---|
| Independent validation required | YES - renderer behavior is externally visible |
| Implemented recorded | 2026-08-24T05:30:35-04:00 |
| Validator | Codex solution-validator `/root/solution_validator` |
| Independence | Confirmed - validator did not author, design, or implement the fix and made no tracked-file edits |
| Decision | Accepted |
| Validation recorded | 2026-08-24T05:36:23.2824664-04:00 |
| Evidence | Independently verified the pre-fix root cause, foreground live paint order, centered/haloed browser export, drag persistence and Undo restoration, empty browser warning/error consoles, 99/99 JavaScript tests, Release build with 0 warnings/errors, and `git diff --check` exit 0 |
| Closure owner | Codex `/root` |
| Closure recorded | 2026-08-24T05:37:22.8862302-04:00 |
| Waiver rationale | None |

## Residual Risk

- Automatic placement remains route-relative and does not solve general text-versus-obstacle collision avoidance. Persistent dragging remains the escape hatch for unusually dense diagrams.
- Live DOM layering and drag/Undo behavior were independently browser-verified but are not yet covered by a durable automated browser test.
- The separate server/MCP `SvgDiagramExporter` retains its own label ordering and does not currently apply browser label offsets. This bugfix is scoped to the browser renderer and its browser SVG export; server/MCP export parity is follow-up work if required.
