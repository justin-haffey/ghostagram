---
name: start-server
description: Starts or reuses the local Ghostagram ASP.NET development server with a deterministic URL, bounded health check, and ownership-safe process state. Use when Codex needs the Ghostagram designer, API, SignalR hub, or MCP endpoint available for local collaboration. Do not use to deploy Ghostagram or start a remote or production service.
---

# Start Ghostagram Server

Start Ghostagram locally with the bundled PowerShell script and return only after `/healthz` reports ready.

## Core Contract

Run `scripts/Start-GhostagramServer.ps1` from this skill. Default to `http://127.0.0.1:5256` and Development. Reuse a healthy instance immediately; claim ownership only for a process the script starts.

The script skips compilation when the existing app host is newer than its source inputs. Otherwise it runs an incremental `dotnet build --no-restore` and launches the app host directly. Never add an automatic restore or dependency install to the start path.

## Workflow

1. From the repository root, run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\plugins\ghostagram\skills\start-server\scripts\Start-GhostagramServer.ps1
   ```

2. To use another loopback endpoint, pass `-Url`, for example `-Url http://127.0.0.1:5273`. Do not use a non-loopback host.
3. Accept `started`, `reused-owned`, or `reused-unmanaged` only when the response reports `healthy: true`.
4. Preserve the returned URL and ownership status in the task handoff. An unmanaged reused instance is intentionally not stoppable by `$stop-server`.
5. If the no-restore build fails because assets are unavailable, report that a separate restore is required. Do not silently perform it.

Use `-ForceBuild` only when the user asks for a rebuild or source freshness is in doubt. Keep the readiness timeout bounded; the default is 20 seconds.

## Safety and Permissions

- Bind only to `localhost`, `127.0.0.1`, or `::1` over HTTP.
- Never kill a process by name or port to make startup succeed.
- Never overwrite or adopt runtime state for an unrelated process.
- Write PID, executable identity, and start-time ticks only beneath the system temporary directory.
- On a failed launch, terminate only the exact process created by that invocation.

## Validation

Require the script's compact JSON result and confirm the reported health URI ends in `/healthz`. For additional verification, request that URI and require HTTP 200 with `{ "status": "ok" }`. Report `elapsedMs` so startup performance remains visible.

## Output

Report status, URL, PID when owned, whether compilation ran, health result, and elapsed milliseconds. Mention that the designer is at the returned base URL and MCP is at `<base-url>/mcp`.
