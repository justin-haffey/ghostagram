---
name: ghostagram-open-session
description: Opens or resumes a collaborative Ghostagram diagram session through MCP. Use when an agent must join an existing diagram, recover a known session, or establish authoritative revision state before work. Do not use to create a new document or mutate diagram content.
---

# Open Ghostagram Session

Join an existing diagram through `mcp__ghostagram__open_session` and retain the server-issued session state for later skills.

## Core Contract

Read `../../contracts/mcp-tools.md` before calling tools. Require a document ID and stable actor ID. An open is read-only; never create, replace, or edit the diagram as a fallback.

## Workflow

1. Reuse a supplied `sessionId`; otherwise leave it absent for the server to issue.
2. Call `mcp__ghostagram__open_session` with exactly `documentId`, `actorId`, and optional `sessionId` when resuming.
3. Require the response to identify the same document and actor, a non-empty session ID, a nonnegative revision, and an authoritative snapshot.
4. Preserve `sessionId` and revision exactly. Use `$ghostagram-sync-diagram` if a refreshed projection is needed.

On `NOT_FOUND`, stop and offer `$ghostagram-create-diagram`; do not create without explicit user intent. Retry one transient read failure at most.

## Validation

Confirm no diagram revision was advanced by opening the session. Report the document ID, session ID, and current authoritative revision. Browser acknowledgement is not exposed by MCP.

## Output

Return a compact session handoff containing `documentId`, `sessionId`, `actorId`, and the current revision for later writes.
