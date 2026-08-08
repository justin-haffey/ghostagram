---
name: stop-server
description: Stops only the local Ghostagram server process previously owned and recorded by the start-server skill. Use when Codex should end a Ghostagram development collaboration server or release its loopback port. Do not use to stop production, remote, manually started, or otherwise unmanaged processes.
---

# Stop Ghostagram Server

Stop a skill-owned Ghostagram process using the persisted PID, executable path, and process start-time identity.

## Core Contract

Run `scripts/Stop-GhostagramServer.ps1` from this skill. Default to the same `http://127.0.0.1:5256` identity used by `$start-server`. If the endpoint is healthy but no valid ownership record exists, leave it running.

## Workflow

1. From the repository root, run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\plugins\ghostagram\skills\stop-server\scripts\Stop-GhostagramServer.ps1
   ```

2. If `$start-server` used an override, pass the exact same `-Url`, for example `-Url http://127.0.0.1:5273`.
3. Accept `stopped` when the owned process terminates, `already-stopped` when its recorded process no longer exists, or `not-managed` when no ownership record exists.
4. Treat `ownership-mismatch` as a safety stop. Do not bypass it with `Stop-Process`, process-name matching, port-owner termination, or task-manager automation.

## Safety and Permissions

- Stop only a PID whose repository root, URL, executable path, and process start-time ticks exactly match the state written by `$start-server`.
- Never stop a server merely because it answers `/healthz` or occupies port 5256.
- Never scan for or terminate every `dotnet` or `Ghostagram.Server` process.
- Remove only the matching temporary state file after the owned process is confirmed stopped.
- Keep termination waiting bounded; the default timeout is 10 seconds.

## Validation

Require the script's compact JSON result. For `stopped`, confirm `managed: true` and that the recorded PID is no longer running. For `not-managed`, report whether the endpoint remains healthy and make clear that no process was stopped.

## Output

Report status, URL, stopped PID when applicable, whether an unmanaged healthy endpoint remains, and elapsed milliseconds.
