# PLAN-01: Phase 1 Canvas-First Shell Plan

**Plan ID:** PLAN-01  
**Phase:** Phase 1 — Canvas-First Shell  
**Status:** Draft  
**Owner:** Diagram Studio maintainers  
**Target release:** Diagram Studio 2.0, phase 1  
**Last updated:** 2026-07-26

Related documents:

- Design: [Diagram Studio Version 2](../01-DESIGN/DESIGN.md)
- ADRs: [ADR-001](../02-ADR/ADR-001-canvas-first-adaptive-editor-shell.md), [ADR-002](../02-ADR/ADR-002-separate-workspace-preferences-from-diagram-data.md)
- Features: [FEATURE-01](../04-FEATURE/FEATURE-01-adaptive-shell-foundation.md), [FEATURE-02](../04-FEATURE/FEATURE-02-contextual-drawers.md), [FEATURE-03](../04-FEATURE/FEATURE-03-canvas-overlays-and-command-parity.md)
- Architecture: [Current architecture snapshot](../00-CONCEPT/CURRENT-ARCHITECTURE.md)

---

## Summary

Phase 1 replaces the fixed three-column editor layout with the viewport-bound canvas-first shell. It establishes the compact persistent regions, moves tool inventory into overlay-first drawers, and floats canvas navigation and status controls without changing diagram-domain behavior or persistence.

The phase uses direct, temporary adapters to existing `DiagramEditorState` workflows. The unified command registry, command palette, full accessibility hardening, persisted workspace preferences, and removal of migration scaffolding remain later-phase work.

This plan is a review candidate. The upstream design and ADRs remain Proposed; implementation cannot begin until reviewers accept the applicable artifacts and mark the selected feature Ready.

### Phase objectives

1. At 1440 by 900 with drawers closed, allocate at least 80 percent of viewport area to the canvas region while limiting persistent chrome to a 52-pixel command bar, 44-pixel activity rail, and 24-pixel status bar.
2. Replace the permanent 280-pixel Assets rail and 320-pixel Inspector with overlay-first drawers that preserve the mounted canvas and current viewport.
3. Move the minimap and viewport controls into canvas overlays and preserve all current editor workflows through explicit temporary command mappings.
4. Keep `DiagramDocument` schema version 2, existing IndexedDB content stores, sync contracts, and `DiagramEditorState` mutation behavior unchanged.

### Non-goals

- Unified command registry, `Ctrl+K` command palette, or finalized shortcut architecture.
- Persisted drawer, minimap, density, or workspace preferences.
- Full phase-2 keyboard/focus/accessibility certification, although new Phase 1 controls must be semantic and keyboard operable.
- Focus mode, final presentation-mode behavior, or 92-percent focus-mode canvas target.
- Mobile authoring parity below 768 pixels.
- Domain, rendering-patch, validation, autosave, sync, collaboration, authentication, or diagram-schema redesign.

---

## Scope and boundaries

### In scope

- Viewport-bound editor frame, layout tokens, compact command bar, activity rail, canvas region, and compact status bar.
- In-memory shell state sufficient to open, close, and responsively adapt drawers.
- Assets, Library, Inspector, Layers, Snapshots/History, Validation, and Review drawer modes using existing state and callbacks.
- Floating minimap, fit, center-selection, and viewport-control presentation.
- Transitional mapping of every current `EditorShell.razor` action to a Phase 1 surface.
- Playwright geometry, workflow-parity, responsive, and canvas-preservation coverage.

### Out of scope

- Changes to `Diagrams.Core`, server sync behavior, document storage records, snapshots, library records, or export formats.
- New server endpoints, databases, external services, identity boundaries, or telemetry.
- Saving workspace state to IndexedDB or any other durable store.
- Introducing an icon package without separate review.
- Removing or changing current domain workflows merely to simplify the shell.

### Constraints and invariants

- `DiagramCanvas` remains mounted when an overlay drawer opens or closes; shell changes must not reinitialize jsPlumb or reset viewport state.
- UI-only state must not enter `DiagramDocument`, `DiagramEditorState`, exports, snapshots, clipboard payloads, or sync requests.
- Existing commands invoke the same `DiagramEditorState` methods and retain their current availability and side effects.
- Imported diagram content remains untrusted and rendered as text.
- Sync remains disabled by default and retains its current prototype warnings and behavior.
- At widths below 1280 pixels, drawers cannot pin or reserve canvas width; they behave as overlays or modal sheets.
- No implementation feature may be marked Ready while ADR-001 is Proposed.

---

## Implementation changes

1. **Adaptive shell foundation**

   - Change: Replace the padded auto-row/fixed-column shell with stable viewport regions, reusable visual tokens, responsive geometry, and in-memory shell state.
   - Boundaries: `EditorShell.razor`, new workspace components/state, `editor.css`, and `Editor.Client` service registration.
   - Configuration/data: No durable configuration or data migration; use documented constants and CSS custom properties.
   - Verification: R1.1 requirements through POS-101, NEG-101, EDGE-101, and INT-101.
2. **Contextual drawer system**

   - Change: Move current Assets/Library and Inspector content into one accessible overlay-first drawer host with task-oriented modes.
   - Boundaries: Existing `DiagramEditorState` read models and callbacks; new drawer components; no domain changes.
   - Configuration/data: Active drawer and pin eligibility are memory-only. No preference store.
   - Verification: R1.2 requirements through POS-201, NEG-201, EDGE-201, and INT-201.
3. **Canvas overlays and transitional command parity**

   - Change: Float minimap/navigation controls, compact status, and map current commands to the compact bar, overflow, drawer, or canvas overlay.
   - Boundaries: `MiniMap.razor`, `DiagramCanvas.razor`, `EditorShell.razor`, CSS, current state methods, and Playwright tests.
   - Configuration/data: Maintain a reviewed command inventory in code/tests; do not introduce the phase-2 command registry.
   - Verification: R1.3 requirements through POS-301, NEG-301, EDGE-301, and INT-301.

### Delivery sequence

- [ ] Accept the version 2 design and ADR-001; confirm Phase 1 may proceed without ADR-002 implementation.
- [ ] Implement and verify FEATURE-01 with the existing UI content temporarily hosted in the new frame.
- [ ] Implement FEATURE-02 and prove canvas identity/viewport preservation across drawer transitions.
- [ ] Implement FEATURE-03, complete the command inventory, and restore end-to-end workflow parity.
- [ ] Run build, focused suites, full regression, Playwright geometry/responsive checks, and manual keyboard/zoom inspection.
- [ ] Record evidence, residual accessibility limitations, and deferred Phase 2/3 work before the phase release gate.

---

## Features

### Feature registry

| Feature ID | Feature name | Priority | Detailed plan | Requirements | Verification | Status |
| --- | --- | --- | --- | --- | --- | --- |
| FEATURE-01 | Adaptive shell foundation | Must | [Detailed plan](../04-FEATURE/FEATURE-01-adaptive-shell-foundation.md) | R1.1-* | POS-101, NEG-101, EDGE-101, INT-101 | Not started |
| FEATURE-02 | Contextual drawers | Must | [Detailed plan](../04-FEATURE/FEATURE-02-contextual-drawers.md) | R1.2-* | POS-201, NEG-201, EDGE-201, INT-201 | Not started |
| FEATURE-03 | Canvas overlays and command parity | Must | [Detailed plan](../04-FEATURE/FEATURE-03-canvas-overlays-and-command-parity.md) | R1.3-* | POS-301, NEG-301, EDGE-301, INT-301 | Not started |

### Feature blocks

#### FEATURE-01 — Adaptive Shell Foundation

**Purpose:** Establish the compact, viewport-bound frame that gives the canvas the remaining editor space without changing editor workflows.  
**Detailed plan:** [FEATURE-01](../04-FEATURE/FEATURE-01-adaptive-shell-foundation.md)

**Scope**

- In scope: visual tokens, 52/44/24-pixel regions, stable canvas slot, responsive geometry, semantic landmarks, and memory-only shell state.
- Out of scope: functional drawer content, persisted preferences, final commands, and focus mode.
- Dependencies: proposed ADR-001 must be accepted before implementation.

**Requirements**

| Requirement ID | Requirement | Priority | Source / rationale | Verification IDs |
| --- | --- | --- | --- | --- |
| R1.1-shell_geometry | Render persistent regions at 52px top, 44px rail, and 24px status. | Must | Design 5.1 and ADR-001 | POS-101 |
| R1.1-canvas_area | Give the closed-drawer canvas region at least 80 percent of a 1440x900 viewport. | Must | Design 12 and 20 | POS-101 |
| R1.1-stable_canvas | Shell-only state changes do not unmount or reinitialize `DiagramCanvas`. | Must | ADR-001 | INT-101, EDGE-101 |
| R1.1-responsive_frame | At widths below 1280px, the frame does not stack tool panels above or below the canvas. | Must | Design 2.1 | EDGE-101 |
| R1.1-data_isolation | Shell state does not change document revision or persistence. | Must | ADR-002 boundary | NEG-101 |

**User and system flows**

1. **Open editor** — Maker opens a document; the shell initializes; the canvas fills remaining space and current content renders.
2. **Shell failure** — Optional shell state fails; defaults render and the document remains editable.
3. **Viewport boundary** — Viewport crosses 1280 pixels; layout adapts without remounting the canvas.

**Acceptance criteria**

- [ ] R1.1-shell_geometry and R1.1-canvas_area pass geometry assertions.
- [ ] R1.1-stable_canvas passes identity and viewport checks.
- [ ] R1.1-responsive_frame prevents panel stacking at target viewports.
- [ ] R1.1-data_isolation proves no document revision or storage write from shell-only changes.

**Implementation touchpoints**

| Layer | Modules / files | Responsibility | Contract or migration impact |
| --- | --- | --- | --- |
| Application | `src/Editor.Client/Services/EditorWorkspaceState.cs` | Memory-only shell geometry and mode state | New internal client service; no durable state |
| UI | `src/Editor.Client/Components/EditorShell.razor`, `Components/Workspace/*` | Stable viewport regions and canvas slot | Razor component boundaries only |
| Styling | `src/Editor.Client/wwwroot/editor.css` | Tokens, dimensions, responsive behavior | CSS contract for geometry tests |

**Verification mapping**

| Verification ID | Level | Scenario | Expected result | Test / command |
| --- | --- | --- | --- | --- |
| POS-101 | UI | Open at 1440x900 | Exact chrome bounds and canvas area at least 80% | Playwright geometry test |
| NEG-101 | Integration | Change shell-only state | Document revision and autosave remain unchanged | State/integration assertion |
| EDGE-101 | UI | Resize across 1280px and to 1024x768 | Canvas stays mounted; no vertical panel stack | Playwright responsive test |
| INT-101 | UI | Render nodes, resize shell, continue drag/select | Same canvas runtime remains usable | Playwright runtime-identity test |

**Feature completion checklist**

- [ ] Requirements are implemented and linked to tests.
- [ ] Positive, negative, edge, and integration scenarios pass.
- [ ] CSS geometry contracts and component responsibilities are documented.
- [ ] Review evidence and remaining limitations are recorded.

#### FEATURE-02 — Contextual Drawers

**Purpose:** Recover permanent sidebar space while retaining task-oriented access to all current Assets and Inspector content.  
**Detailed plan:** [FEATURE-02](../04-FEATURE/FEATURE-02-contextual-drawers.md)

**Scope**

- In scope: activity-rail triggers, overlay drawer host, Assets/Library, Inspector, Layers, Snapshots, Validation, and Review modes.
- Out of scope: preference persistence, command palette, final keyboard architecture, and multiple simultaneous drawers.
- Dependencies: FEATURE-01.

**Requirements**

| Requirement ID | Requirement | Priority | Source / rationale | Verification IDs |
| --- | --- | --- | --- | --- |
| R1.2-overlay_default | Drawers overlay by default and do not reserve canvas width below 1280px. | Must | ADR-001 | POS-201, EDGE-201 |
| R1.2-single_mode | At most one drawer mode is active at a time. | Must | Design 5.1 | NEG-201 |
| R1.2-content_parity | Every current left-rail and inspector section is reachable in a named drawer mode. | Must | Design 20 | INT-201 |
| R1.2-dismissal | Escape, close control, and invoking rail action dismiss the drawer and restore focus. | Must | ADR-001 minimum accessibility | POS-201 |
| R1.2-canvas_preservation | Drawer transitions preserve canvas identity, selection, and viewport. | Must | ADR-001 | EDGE-201 |

**User and system flows**

1. **Use a tool drawer** — Maker opens Assets, inserts a stencil, closes the drawer, and continues on the unchanged canvas.
2. **Unavailable action** — A selection-dependent control remains disabled or absent with current semantics.
3. **Responsive transition** — A pinned-eligible layout narrows; the drawer becomes an overlay without resetting canvas state.

**Acceptance criteria**

- [ ] All R1.2 requirements map to passing tests.
- [ ] Drawer modes expose the complete existing content inventory.
- [ ] Invalid or unavailable mode requests fail safely.
- [ ] Focus and canvas state are restored after dismissal.

**Implementation touchpoints**

| Layer | Modules / files | Responsibility | Contract or migration impact |
| --- | --- | --- | --- |
| Application | `EditorWorkspaceState` | Active drawer and pin eligibility | Memory only |
| UI | `Components/Workspace/ActivityRail.razor`, `DrawerHost.razor`, `Components/Drawers/*` | Drawer triggers, host, and task content | Reuses current state callbacks |
| Styling/tests | `editor.css`, `tests/Editor.E2E.Tests/EditorSmokeTests.cs` | Overlay geometry and workflow verification | Selector updates required |

**Verification mapping**

| Verification ID | Level | Scenario | Expected result | Test / command |
| --- | --- | --- | --- | --- |
| POS-201 | UI | Open Assets, insert node, close with Escape | Node added; drawer closes; focus returns | Playwright drawer workflow |
| NEG-201 | Unit/UI | Request unknown mode or selection action with no selection | No invalid state or mutation | State unit test and UI assertion |
| EDGE-201 | UI | Open drawer, resize below 1280px, close | Overlay adaptation; canvas identity/viewport preserved | Playwright responsive test |
| INT-201 | UI | Exercise all migrated drawer sections | Existing workflows remain reachable | Playwright parity matrix |

**Feature completion checklist**

- [ ] Requirements are implemented and linked to tests.
- [ ] Positive, negative, edge, and integration scenarios pass.
- [ ] Drawer inventory and deferred accessibility work are documented.
- [ ] Review evidence and remaining limitations are recorded.

#### FEATURE-03 — Canvas Overlays and Command Parity

**Purpose:** Remove the separate minimap row and multi-row command cost while keeping current workflows accessible through temporary direct adapters.  
**Detailed plan:** [FEATURE-03](../04-FEATURE/FEATURE-03-canvas-overlays-and-command-parity.md)

**Scope**

- In scope: floating minimap, fit/center controls, compact status, 52-pixel command bar, overflow groupings, current-command inventory, editor-ready marker, and parity tests.
- Out of scope: unified command registry, command palette, persisted minimap state, focus mode, and final presentation behavior.
- Dependencies: FEATURE-01 and FEATURE-02.

**Requirements**

| Requirement ID | Requirement | Priority | Source / rationale | Verification IDs |
| --- | --- | --- | --- | --- |
| R1.3-floating_controls | Minimap and viewport controls overlay the canvas and reserve no grid row. | Must | Design 5.4 | POS-301 |
| R1.3-command_parity | Every current toolbar action maps to a compact primary, overflow, drawer, or overlay control. | Must | Design phase 1 | INT-301 |
| R1.3-direct_adapter | Phase 1 controls invoke existing state/shell methods without duplicating workflow logic. | Must | Code graph evidence | NEG-301 |
| R1.3-ready_marker | Editor-ready state requires successful shell and jsPlumb initialization. | Must | Design 11 | EDGE-301 |
| R1.3-status_density | Status fits the 24px region and preserves template, counts, validation, and sync state access. | Must | Design 5.1 | POS-301 |

**User and system flows**

1. **Navigate canvas** — Maker uses Fit, Center Selection, or minimap without losing canvas area.
2. **Unavailable command** — Center Selection and sync actions preserve current disabled behavior.
3. **Initialization failure** — jsPlumb initialization fails; ready marker remains false and an actionable error is exposed.

**Acceptance criteria**

- [ ] Overlay and status geometry meet R1.3 requirements.
- [ ] A reviewed inventory maps every current toolbar action.
- [ ] Direct adapters preserve current availability and effects.
- [ ] Browser initialization and existing E2E workflows pass.

**Implementation touchpoints**

| Layer | Modules / files | Responsibility | Contract or migration impact |
| --- | --- | --- | --- |
| UI | `EditorShell.razor`, `MiniMap.razor`, `DiagramCanvas.razor`, `Components/Workspace/CanvasOverlayLayer.razor` | Compact commands, overlays, status, readiness | Internal UI contract |
| Interop | `Diagrams.Interop.JsPlumb` adapter/module | Existing initialization signal and viewport commands | No public API change unless readiness requires a minimal internal callback |
| Tests | AppHost and Editor E2E suites | Static asset, readiness, geometry, and workflow parity | Update selectors to semantic roles/test IDs |

**Verification mapping**

| Verification ID | Level | Scenario | Expected result | Test / command |
| --- | --- | --- | --- | --- |
| POS-301 | UI | Use Fit, Center, minimap, and inspect status | Controls work without reserved minimap row | Playwright overlay test |
| NEG-301 | UI | Invoke unavailable selection/sync actions | Controls remain disabled; no mutation | Playwright availability test |
| EDGE-301 | Integration/UI | Block or break jsPlumb module initialization | Ready marker remains false; failure is visible | AppHost/Playwright failure test |
| INT-301 | UI | Execute current save/snapshot/undo/redo/sync/export/layout/group/review/presentation inventory | Each action remains reachable and retains behavior | Playwright parity matrix |

**Feature completion checklist**

- [ ] Requirements are implemented and linked to tests.
- [ ] Positive, negative, edge, and integration scenarios pass.
- [ ] Transitional command inventory and Phase 2 replacement notes are documented.
- [ ] Review evidence and remaining limitations are recorded.

---

## Cross-cutting requirements

| Area | Requirement | Verification |
| --- | --- | --- |
| Security / privacy | Render imported text safely; do not add content telemetry or broaden sync exposure. | Existing import tests plus DOM inspection and network-request assertion |
| Reliability | Shell/drawer failures retain a usable canvas; responsive changes do not remount it. | EDGE-101, EDGE-201, EDGE-301 |
| Performance | Meet 80-percent canvas area; use transform/opacity transitions no longer than 150ms; avoid canvas reinitialization. | POS-101, browser timing assertion, INT-101 |
| Observability | Provide a deterministic editor-ready marker and content-free initialization/drawer error logging. | EDGE-301 and console inspection |
| Compatibility | Preserve schema version 2, IndexedDB records, exports, snapshots, sync payloads, and state workflow outcomes. | Regression suites and payload/revision assertions |
| Accessibility | New controls use semantic roles, accessible names, visible focus, Escape dismissal, and reduced-motion-safe CSS. | Playwright keyboard checks and manual 200-percent/reduced-motion review |

---

## Test plan

### Test environment

- Runtime / platform: Windows or CI with .NET 10 SDK; Chromium through Playwright.
- Data and reset strategy: unique browser context per test; clear IndexedDB between independent scenarios; existing template fixtures.
- External dependencies: local AppHost at `http://127.0.0.1:5087`; no live sync dependency beyond the local file-backed prototype.
- Required environment variables / secrets: none expected. Playwright Chromium must be installed.

### Test commands

- Build: `dotnet build DiagramStudio.slnx`
- Unit tests: `dotnet test tests/Diagrams.Core.Tests/Diagrams.Core.Tests.csproj`
- Integration tests: `dotnet test tests/AppHost.Tests/AppHost.Tests.csproj`
- End-to-end tests: `dotnet test tests/Editor.E2E.Tests/Editor.E2E.Tests.csproj`
- Format / static analysis: `dotnet format DiagramStudio.slnx --verify-no-changes`
- Full regression: `dotnet test DiagramStudio.slnx`

### Scenario matrix

| ID | Scenario | Level | Expected result | Evidence |
| --- | --- | --- | --- | --- |
| POS-101 | Open editor at 1440x900 | UI | 52/44/24 chrome; canvas area at least 80% | Geometry test screenshot and measurements |
| NEG-101 | Change memory-only shell state | Integration | No document revision/autosave | State assertions |
| EDGE-101 | Resize across breakpoint | UI | Canvas persists; no stacked panels | Runtime identity and responsive assertions |
| POS-201 | Insert through Assets drawer | UI | Existing add-node flow succeeds | E2E result |
| NEG-201 | Invalid drawer/action state | Unit/UI | Safe no-op or disabled action | Unit and E2E result |
| EDGE-201 | Drawer open during responsive transition | UI | Overlay conversion preserves viewport | E2E result |
| POS-301 | Use floating viewport controls | UI | Navigation works without a layout row | E2E result |
| NEG-301 | Use unavailable command | UI | Disabled; no mutation | E2E result |
| EDGE-301 | jsPlumb initialization unavailable | Integration/UI | Ready remains false; error exposed | Host and E2E result |
| INT-301 | Full current command inventory | UI | Every workflow remains reachable | Parity matrix |

### Release gate

- [ ] Design and ADR-001 are explicitly accepted; each implemented feature was marked Ready before code work.
- [ ] Every feature requirement maps to a passing automated test or documented manual check.
- [ ] Build, focused suites, full regression, format/static analysis, and Playwright tests are green.
- [ ] Playwright Chromium was installed and live browser checks were executed rather than skipped.
- [ ] Canvas geometry, runtime identity, workflow parity, keyboard basics, 200-percent zoom, and reduced-motion evidence is recorded.
- [ ] Diagram schema, persisted records, exports, snapshots, and sync payloads show no Phase 1 UI state.
- [ ] Known failures, Phase 2/3 deferrals, rollback steps, and residual accessibility risks are documented.

---

## Acceptance criteria

The phase is complete only when:

1. The default editor meets the 52/44/24-pixel chrome and 80-percent canvas-area targets at 1440 by 900.
2. Assets and Inspector capabilities are removed from permanent columns and remain available through overlay-first drawers.
3. The minimap and viewport controls reserve no canvas grid row, and current toolbar/inspector workflows remain reachable.
4. Drawer and responsive transitions preserve the canvas runtime, selection, viewport, and document revision.
5. Existing diagram data, storage, import/export, snapshot, review, layout, and sync contracts remain behaviorally compatible.
6. New controls meet Phase 1 semantic, accessible-name, visible-focus, Escape, and reduced-motion requirements.
7. The full release gate passes with live Playwright evidence and recorded residual risks.

---

## Assumptions, risks, and decisions

### Assumptions

- The user's `PHASE-01` request selects the design's “Phase 1: Canvas-First Shell,” not all version 2 phases.
- Proposed design and ADRs may inform a Draft plan but do not authorize implementation.
- Phase 1 uses memory-only workspace state; ADR-002 persistence work remains Phase 3.
- The checked-in `diagram-editor.js` and current direct `DiagramEditorState` workflows remain the implementation baseline.
- No new icon dependency is required; text labels or repository-native SVG/CSS icons can satisfy Phase 1.

### Risks and mitigations

| Risk | Likelihood | Impact | Mitigation | Owner / residual risk |
| --- | --- | --- | --- | --- |
| Hidden commands reduce discoverability | Medium | High | Reviewed command inventory, stable overflow groups, drawer labels, parity E2E | Product/UX; palette deferred to Phase 2 |
| Layout changes remount jsPlumb | Medium | High | Stable canvas component key/slot and runtime identity tests | Engineering |
| Large `EditorShell.razor` refactor causes workflow regression | High | High | Incremental features, direct adapters, full parity matrix | Engineering/QA |
| Drawer overlays hide selected content | Medium | Medium | Preserve viewport, quick dismiss, responsive tests | UX; pinning hardening later |
| Accessibility remains incomplete after Phase 1 | High | Medium | Minimum semantic gates now; record debt for Phase 2 | Product/QA |
| Playwright binary unavailable | Medium | High | Install Chromium before release evidence; do not waive live gate | QA |

### Open decisions

| Decision | Options | Decision owner | Due date | Result / linked ADR |
| --- | --- | --- | --- | --- |
| Accept the v2 design and ADR-001 for implementation planning | Accept, revise, or reject | Diagram Studio reviewer | Before FEATURE-01 Ready | Pending |
| Use a temporary feature flag for side-by-side shell rollback | Flagged shell or direct replacement with source rollback | Engineering owner | Before FEATURE-01 implementation | Pending; ADR-001 permits either |
| Final compact-bar grouping for secondary commands | Document, Edit, Arrange, View, Review/Sync or revised grouping | Product/UX owner | Before FEATURE-03 implementation | Pending Phase 1 interaction review |

---

## References

- [Diagram Studio Version 2 design](../01-DESIGN/DESIGN.md)
- [ADR-001: Canvas-first adaptive editor shell](../02-ADR/ADR-001-canvas-first-adaptive-editor-shell.md)
- [ADR-002: Separate workspace preferences from diagram data](../02-ADR/ADR-002-separate-workspace-preferences-from-diagram-data.md)
- [Current architecture snapshot](../00-CONCEPT/CURRENT-ARCHITECTURE.md)
- Current shell: `src/Editor.Client/Components/EditorShell.razor`
- Current styles: `src/Editor.Client/wwwroot/editor.css`
- Current E2E tests: `tests/Editor.E2E.Tests/EditorSmokeTests.cs`

