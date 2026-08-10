---
name: ghostagram-apply-operations
description: Applies revision-safe Ghostagram node, port, edge, group, selection, and viewport operations through MCP. Use when the user asks an agent to add, edit, connect, move, group, select, or remove diagram elements. Do not use for automatic layout, SVG export, or unapproved destructive changes.
---

# Apply Ghostagram Operations

Commit one coherent operation batch through `mcp__ghostagram__apply_operations`, then prove the change is visible to the shared session.

## Core Contract

Read `../../contracts/mcp-tools.md` before calling tools. The user's bounded edit request authorizes only the described changes. Removing elements or dissolving groups requires explicit intent.

## Workflow

1. Call `mcp__ghostagram__get_diagram` with exactly `sessionId`, `actorId`, and optional `afterRevision`; then prepare against the validated current revision.
2. Convert the requested edit into the smallest ordered operation batch. Validate stable IDs, references, group ancestry, and removal dependencies.
3. Generate a new `commandId`; call `mcp__ghostagram__apply_operations` with exactly `sessionId`, `actorId`, `commandId`, current `baseRevision`, and `operations`.
4. Handle `REVISION_CONFLICT` using the shared rebase protocol. Retry at most once with a new command ID; stop on destructive or ambiguous merges.
5. On `COMMITTED`, `NO_CHANGES`, or exact `IDEMPOTENT_REPLAY`, call `mcp__ghostagram__get_diagram` with `sessionId`, `actorId`, and `afterRevision` equal to the original base revision. Confirm the command ID, resulting entities, and committed revision.
6. When collaborating with a live Laboratory, call `mcp__ghostagram__list_diagrams`. Require a positive view count and `oldestBrowserRevision` at or beyond the commit before claiming every open Laboratory rendered it.

Do not retry authorization, validation, capability, or idempotency-key-reuse failures.

## Validation

Check semantic postconditions, not only response codes: requested fields changed, unrelated entities remained, references are valid, and the authoritative revision reaches the commit.

## Output

Report the exact affected IDs, committed revision, command result, conflict handling, and separate browser acknowledgement evidence.
