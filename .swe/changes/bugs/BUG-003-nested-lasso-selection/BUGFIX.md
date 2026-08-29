---
title: "Nested lasso selects its parent group"
artifact_type: "bugfix"
id: "BUG-003"
status: "Implemented"
authority: "solution"
scope: "Ghostagram browser interaction controller"
parent: "Ghostagram renderer design"
upstream:
  repository: "ghostagram"
  artifact_id: "Ghostagram renderer design"
  path: "src/Ghostagram/docs/DESIGN.md"
owners:
  - "Codex"
created: "2026_08_25"
updated: "2026_08_25"
template_version: "2.0.0"
---

# Nested lasso selects its parent group

## Expected and Actual Behavior

- Expected: Shift-drag inside an expanded group selects enclosed child nodes and child groups without automatically selecting the containing parent.
- Actual: Any group intersecting the lasso was selected, so the parent containing the lasso was always included; the synthesized post-drag click could select it again.
- Reproduction: Shift-drag a marquee around a node or nested group within an expanded parent group.

## Eligibility and Impact

- Fast-path eligible: Yes; this restores intended nested selection semantics without changing persisted operations or host contracts.
- Architecture/contracts affected: None.
- Escalation: None.

## Root Cause

`selectionIdsInRectangle` used intersection for groups, unlike the enclosing gesture semantics needed for containers, and `startSelectionLasso` did not suppress the click synthesized after a drag.

## Fix Design and Change Map

- `src/Ghostagram/ghostagram.js`: Select groups only when fully enclosed by the marquee and suppress the post-lasso click after a real drag.
- `src/Ghostagram/test/ghostagram.test.mjs`: Prove a child group can be selected while its containing parent remains excluded.

## Verification

| Check | Result | Evidence |
|---|---|---|
| JavaScript regression suite | Pass | `cmd.exe /d /c npm.cmd test` from `src/Ghostagram`: 100 passed, 0 failed. |
| Release solution build | Pass | `dotnet build .\Ghostagram.slnx -c Release --no-restore -v:minimal`: 0 warnings, 0 errors. |
| Live browser behavior | Pass | In the isolated nested-group demo, Shift-drag selected `review-task`, its child node, and intersecting edges while excluding parent `review-stage`; group positions were unchanged and both browser warning/error logs were empty. |
| Git whitespace check | Pass | `git diff --check` exited 0; only existing LF-to-CRLF notices were reported. |

## Validation and Closure

| Field | Value |
|---|---|
| Independent validation required | Yes; selection is externally visible browser behavior. |
| Implemented recorded | 2026-08-26T03:39:46.4572379Z |
| Validator | Not assigned in this run |
| Independence | Blocked pending an independent solution-validator |
| Decision | Blocked |
| Validation recorded | Pending |
| Evidence | Implementation checks above pass; independent acceptance remains pending. |
| Closure owner | Pending |
| Closure recorded | Pending |
| Waiver rationale | None |

## Residual Risk

- Very small marquees still select intersecting nodes and edges by design; only group selection requires full enclosure.
