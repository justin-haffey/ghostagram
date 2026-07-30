---
name: ghostagram-export-svg
description: Exports the current authoritative Ghostagram diagram as SVG through MCP with pre/post revision checks. Use when the user asks to render, download, save, inspect, or hand off an SVG representation of the current live diagram. Do not use to change diagram state.
---

# Export Ghostagram SVG

Export the session's current authoritative diagram through `mcp__ghostagram__export_svg`.

## Core Contract

Read `../../contracts/mcp-tools.md` before calling tools. Export is read-only. Never mutate layout merely to improve an export unless the user separately authorizes layout.

## Workflow

1. Call `mcp__ghostagram__get_diagram` with exactly `sessionId` and `actorId` to obtain the current authoritative revision.
2. Call `mcp__ghostagram__export_svg` with exactly `sessionId` and `actorId`.
3. Require a nonempty SVG and record the returned source revision when present.
4. Validate a nonempty SVG document with an `<svg` root, finite view box or dimensions, and no reported missing element references.
5. Call `mcp__ghostagram__get_diagram` again. If the authoritative revision changed during export, report the race and retry the read/export/read sequence once.

Do not write a returned artifact to disk unless the user asked for a file. Never execute scripts, links, or embedded content from the SVG.

## Validation

Confirm export did not advance the diagram revision. Browser acknowledgement is unavailable through MCP.

## Output

Return or link the SVG according to the user's request, plus document ID, source revision, dimensions, and validation status.
