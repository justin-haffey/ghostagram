---
name: ghostagram-layout-diagram
description: Previews or commits deterministic server-side Ghostagram layout through MCP. Use when the user asks to arrange, organize, align, rank, or automatically lay out a diagram or nested groups. Do not use for manual element edits or when the user only wants SVG export.
---

# Layout Ghostagram Diagram

Run deterministic layout through `mcp__ghostagram__layout_diagram` with explicit preview and commit semantics.

## Core Contract

Read `../../contracts/mcp-tools.md` before calling tools. A committed layout changes many positions; use `dryRun: true` when the user asks to preview, compare, or recommend rather than apply.

## Workflow

1. Call `mcp__ghostagram__get_diagram` and record the current revision and structural counts.
2. Select `ghost-layered`, a stable integer seed, and explicit options. Default direction to `right`; preserve user constraints and server limits.
3. Generate a new `commandId` and call `mcp__ghostagram__layout_diagram` with exactly `sessionId`, `actorId`, `commandId`, current `baseRevision`, and any chosen optional `algorithm`, `seed`, `dryRun`, and `options`.
4. For dry run, require unchanged server revision and inspect operations/metrics without claiming a commit.
5. For commit, handle `REVISION_CONFLICT` with the shared conflict protocol. Rebase once only when current topology still supports the same intent.
6. Verify the committed command through `mcp__ghostagram__get_diagram` with `sessionId`, `actorId`, and the original base revision as `afterRevision`; confirm non-overlapping finite bounds, group containment, and authoritative revision.
7. For a live human review, verify the committed revision through `mcp__ghostagram__list_diagrams` before opening its returned browser URL for visual inspection.

Do not silently switch algorithms, seeds, directions, or dry-run mode during a retry.

## Validation

Report algorithm/version, seed, direction, affected count, crossings before/after, elapsed time, bounds, authoritative revision, and separate browser acknowledgement. Flag degraded metrics rather than calling the result optimal.

## Output

For preview, return a reviewable proposal and metrics. For commit, return the verified revision and layout evidence.
