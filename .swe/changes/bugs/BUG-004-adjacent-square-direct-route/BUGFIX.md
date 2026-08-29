---
title: "Adjacent Square Connectors Prefer a Direct Route"
artifact_type: "bugfix"
id: "BUG-004"
status: "Implemented"
authority: "solution"
scope: "Ghostagram browser renderer"
parent: "Ghostagram-DESIGN"
upstream:
  repository: "ghostagram"
  artifact_id: "BUG-002"
  path: ".swe/changes/bugs/BUG-002-square-edge-overlap/BUGFIX.md"
owners:
  - "Codex"
created: "2026_08_26"
updated: "2026_08_26"
template_version: "2.0.0"
---

# Adjacent Square Connectors Prefer a Direct Route

## Expected and Actual Behavior

- Expected: A Square/flowchart connector between horizontally or vertically aligned, mutually facing ports renders as one straight segment when no node blocks the corridor.
- Actual: The congestion score can prefer a longer multi-bend corridor around otherwise adjacent nodes because another edge occupies part of the direct corridor.
- Reproduction: Place two nodes side by side with a right-facing source port and left-facing target port on the same horizontal coordinate. With an occupied segment in the same corridor, the connector detours above the nodes instead of rendering directly between them.

## Eligibility and Impact

- Fast-path eligible: Yes; this corrects existing Square connector presentation behavior owned by Ghostagram.
- Architecture/contracts affected: None.
- Escalation: None.

## Root Cause

The overlap correction in `flowchartRouteScore` applies congestion penalties before preserving the fundamental direct-route invariant. A clear two-point baseline can therefore lose to a longer candidate even though aligned, mutually facing ports require no bend and cross no obstacle.

## Fix Design and Change Map

- `src/Ghostagram/ghostagram.js`: Preserve a clear two-point baseline before congestion scoring alternative multi-bend routes.
- `src/Ghostagram/test/ghostagram.test.mjs`: Add a regression model with aligned adjacent nodes and an occupied direct corridor.

## Verification

| Check | Result | Evidence |
|---|---|---|
| Adjacent Square direct-route regression | Pass | `flowchart routing keeps a clear direct path between adjacent aligned nodes` returned exactly `[{ x: 80, y: 20 }, { x: 480, y: 20 }]` with an occupied corridor |
| Ghostagram JavaScript test suite | Pass | `cmd.exe /d /c npm.cmd test` from `src/Ghostagram`: 101 passed, 0 failed |
| Release solution build | Pass | `dotnet build .\Ghostagram.slnx -c Release --no-restore -v:minimal`: 0 warnings, 0 errors |
| Diff hygiene | Pass | `git diff --check` exited 0; only existing LF-to-CRLF working-copy warnings were reported |
| Visible browser and console inspection | Pass | Live document `ghostworx-platform-architecture-voice-avatar-proposal` revision 552 rendered the adjacent `mcp` edge as `M 176 232 L 1120 232`; the non-aligned `mcp` edge retained bends; browser warning/error log was empty |
| Local server readiness | Pass | Checkout-owned server returned `reused-owned`, healthy at `http://127.0.0.1:5256/healthz`, PID 25540, elapsed 382 ms |

## Validation and Closure

| Field | Value |
|---|---|
| Independent validation required | Yes; connector routing is externally visible browser behavior |
| Implemented recorded | 2026-08-26T00:27:59.0039850-04:00 |
| Validator | Pending independent solution validator |
| Independence | Blocked; current execution policy does not permit spawning a validator without an explicit user request |
| Decision | Blocked |
| Validation recorded | Pending |
| Evidence | Implementation checks above; independent decision pending |
| Closure owner | Pending |
| Closure recorded | Pending |
| Waiver rationale | None |

## Residual Risk

- Direct aligned routes may intentionally overlap other edges; this invariant applies only when no non-endpoint node obstructs the direct corridor.
