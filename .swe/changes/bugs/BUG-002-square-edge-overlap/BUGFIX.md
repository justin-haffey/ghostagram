---
title: "Square edge routes overlap for long endpoint legs"
artifact_type: "bugfix"
id: "BUG-002"
status: "Implemented"
authority: "solution"
scope: "Ghostagram browser renderer"
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

# Square edge routes overlap for long endpoint legs

## Expected and Actual Behavior

- Expected: Square edges sharing a port may share the configured endpoint stub, then choose separate orthogonal lanes when another clear route exists.
- Actual: Route scoring ignored the complete first and last segments, allowing long shared legs that split only at visually incorrect branch points.
- Reproduction: In `ghostworx-platform-architecture-voice-avatar-proposal` revision 399, the marked `entity framework`, `mcp`, and `response generation` edges share long endpoint-adjacent legs. The attached screenshot identifies their late branch points.

## Eligibility and Impact

- Fast-path eligible: Yes; this restores intended browser routing without changing model or protocol contracts.
- Architecture/contracts affected: None.
- Escalation: None.

## Root Cause

`flowchartRouteScore` skipped overlap and crossing checks for the entire first and last route segments. Route simplification can merge the configured 32px stub into a much longer segment, so the exemption was substantially wider than the intended endpoint allowance.

## Fix Design and Change Map

- `src/Ghostagram/ghostagram.js`: Score occupied-route overlap on every segment; shared endpoint equality remains protected by existing endpoint checks.
- `src/Ghostagram/test/ghostagram.test.mjs`: Prove a branch leaves a shared lane after one configured stub.

## Verification

| Check | Result | Evidence |
|---|---|---|
| JavaScript regression suite | Pass | `cmd.exe /d /c npm.cmd test` from `src/Ghostagram`: 100 passed, 0 failed. |
| Release solution build | Pass | `dotnet build .\Ghostagram.slnx -c Release --no-restore -v:minimal`: 0 warnings, 0 errors. |
| Live browser behavior | Pass | Fresh live document render produced `M 72 192 L 72 160 L 88 160 L 88 16`, `M 176 232 L 208 232 L 208 344 L 368 344`, and `M 72 272 L 72 304 L 120 304 L 120 416`; browser warning/error log was empty. |
| Git whitespace check | Pass | `git diff --check` exited 0; only existing LF-to-CRLF notices were reported. |

## Validation and Closure

| Field | Value |
|---|---|
| Independent validation required | Yes; routing is externally visible browser behavior. |
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

- Dense diagrams may still require manual waypoints when all available orthogonal corridors are occupied.
