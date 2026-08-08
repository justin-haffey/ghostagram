# Ghostagram Codex plugin

This project-scoped plugin gives Codex agents a safe workflow for starting the local Ghostagram host and collaborating with a live diagram through the `ghostagram` MCP server.

## Skills and tools

| Skill | Primary MCP tool | Purpose |
| --- | --- | --- |
| `$start-server` | Bundled PowerShell script | Start or reuse the local Development server and wait for health. |
| `$stop-server` | Bundled PowerShell script | Stop only the server process owned by `$start-server`. |
| `$ghostagram-open-session` | `mcp__ghostagram__open_session` | Join an existing diagram session. |
| `$ghostagram-create-diagram` | `mcp__ghostagram__create_diagram` | Create a diagram and open its first session atomically. |
| `$ghostagram-sync-diagram` | `mcp__ghostagram__get_diagram` | Read a full snapshot or changes after a revision. |
| `$ghostagram-apply-operations` | `mcp__ghostagram__apply_operations` | Apply an ordered, idempotent operation batch. |
| `$ghostagram-layout-diagram` | `mcp__ghostagram__layout_diagram` | Preview or commit deterministic server layout. |
| `$ghostagram-export-svg` | `mcp__ghostagram__export_svg` | Export SVG pinned to a known revision. |
| `$ghostagram-close-session` | `mcp__ghostagram__close_session` | Close only the caller's collaboration session. |

Every MCP diagram skill reads the shared contract in [`contracts/mcp-tools.md`](contracts/mcp-tools.md). Mutations use optimistic revision checks, exact-payload idempotency, one bounded conflict rebase, and a `get_diagram` verification read. The lifecycle skills instead use their bundled local PowerShell scripts and ownership state beneath the system temporary directory.

## Connection

The packaged [`.mcp.json`](.mcp.json) registers a streamable HTTP server named `ghostagram` at `http://127.0.0.1:5256/mcp`. Use `$start-server` to start or reuse Ghostagram on that endpoint before invoking a diagram skill. If the server is hosted elsewhere, pass the same local `-Url` override to the lifecycle scripts and change only the MCP `url` value while preserving the logical server name.

The plugin contains no credentials or durable diagram state. Diagram documents, revisions, sessions, command ledgers, and SignalR publication remain server-owned. MCP verifies authoritative state through `get_diagram`; the current tool contract does not expose browser-render acknowledgement.
