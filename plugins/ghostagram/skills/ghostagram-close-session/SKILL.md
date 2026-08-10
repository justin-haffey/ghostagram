---
name: ghostagram-close-session
description: Closes the caller's Ghostagram collaboration session through MCP without deleting its diagram. Use when the user asks to leave, disconnect, end, or cleanly close an open diagram session. Do not use to delete documents, close another actor's session, or close a session needed for continuing work.
---

# Close Ghostagram Session

Close one known session through `mcp__ghostagram__close_session` while preserving the diagram.

## Core Contract

Read `../../contracts/mcp-tools.md` before calling tools. Closing affects session presence but never diagram content. Require explicit user intent unless the current workflow created a private temporary session and its completion policy already authorizes cleanup.

## Workflow

1. Call `mcp__ghostagram__get_diagram` with exactly `sessionId` and `actorId` to capture the latest revision and identify any unverified prior mutation.
2. Stop if a write outcome is uncertain; reconcile it before closing.
3. Call `mcp__ghostagram__close_session` with exactly `sessionId` and `actorId`.
4. Accept only `SESSION_CLOSED`. Treat `SESSION_NOT_FOUND` as already unavailable, not as proof that this close request succeeded. Do not retry an uncertain close blindly; reconcile session state first.
5. Verify the returned session names the same session, document, actor, and final authoritative revision.

Never close all sessions, delete a document, or infer authority over another actor.

## Validation

Confirm the session is terminal, retain the pre-close document revision for audit context, and ensure no later skill reuses the closed session ID. Closing is not diagram deletion.

## Output

Report the closed session ID, document ID, final revision, and terminal result.
