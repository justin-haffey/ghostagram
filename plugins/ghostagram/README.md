# Ghostagram Codex plugin

This project-scoped plugin gives Codex agents a safe workflow for collaborating with a live Ghostagram diagram through the `ghostagram` MCP server.

## Skills and tools

| Skill | Primary MCP tool | Purpose |
| --- | --- | --- |
| `$ghostagram-open-session` | `mcp__ghostagram__open_session` | Join an existing diagram session. |
| `$ghostagram-create-diagram` | `mcp__ghostagram__create_diagram` | Create a diagram and open its first session atomically. |
| `$ghostagram-sync-diagram` | `mcp__ghostagram__get_diagram` | Read a full snapshot or changes after a revision. |
| `$ghostagram-apply-operations` | `mcp__ghostagram__apply_operations` | Apply an ordered, idempotent operation batch. |
| `$ghostagram-layout-diagram` | `mcp__ghostagram__layout_diagram` | Preview or commit deterministic server layout. |
| `$ghostagram-export-svg` | `mcp__ghostagram__export_svg` | Export SVG pinned to a known revision. |
| `$ghostagram-close-session` | `mcp__ghostagram__close_session` | Close only the caller's collaboration session. |

Every skill reads the shared contract in [`contracts/mcp-tools.md`](contracts/mcp-tools.md). Mutations use optimistic revision checks, exact-payload idempotency, one bounded conflict rebase, and a `get_diagram` verification read.

## Connection

The packaged [`.mcp.json`](.mcp.json) registers a streamable HTTP server named `ghostagram` at `http://127.0.0.1:5273/mcp`. Start Ghostagram on that endpoint before invoking a skill. If the server is hosted elsewhere, change only the `url` value while preserving the logical server name; the skills depend on that stable name.

The plugin contains no credentials or durable diagram state. Diagram documents, revisions, sessions, command ledgers, and SignalR publication remain server-owned. MCP verifies authoritative state through `get_diagram`; the current tool contract does not expose browser-render acknowledgement.
