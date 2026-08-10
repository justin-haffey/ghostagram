---
name: ghostagram-create-diagram
description: Creates a new Ghostagram diagram and opens its collaborative session atomically through MCP. Use when the user explicitly asks to start, generate, or initialize a new diagram. Do not use to replace an existing diagram or make ordinary edits to an open document.
---

# Create Ghostagram Diagram

Create one new document through `mcp__ghostagram__create_diagram` and verify that its initial state is visible to the opened session.

## Core Contract

Read `../../contracts/mcp-tools.md` before calling tools. Creation is an external write and requires clear user intent. Never overwrite on identifier collision.

## Workflow

1. Call `mcp__ghostagram__describe_capabilities` when constructing unfamiliar nodes, properties, ports, groups, edge types, or markers. Build optional `initialOperations` with stable IDs and dependency order: edge types, groups, nodes, ports, then edges. Omit the field when creating an empty diagram.
2. Choose `documentId` and stable `actorId`, then generate a new `commandId`.
3. Call `mcp__ghostagram__create_diagram` once with exactly `documentId`, `actorId`, `commandId`, and optional `initialOperations`.
4. Require an accepted creation result or exact idempotent replay, then preserve the returned `sessionId` and revision.
5. Call `mcp__ghostagram__get_diagram` with the returned `sessionId` and `actorId`. Confirm IDs and relationships in the authoritative snapshot and the resulting revision.

On `DIAGRAM_EXISTS`, stop and offer to join it with `$ghostagram-open-session`; do not reinterpret creation as replacement. For an uncertain result, follow the exact-payload idempotency protocol and retry at most once.

## Validation

Confirm every edge references existing endpoints, every port references an existing node, group ancestry is acyclic, and the verification read matches the committed revision.

## Output

Return the document/session handoff, revision, counts by element type, authoritative verification, direct browser URL, and any live-browser acknowledgement.
