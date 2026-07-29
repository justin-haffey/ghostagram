# Diagram Studio

Diagram Studio is a local-first browser editor for creating technical diagrams. It combines a Blazor WebAssembly interface with a jsPlumb canvas, a reusable stencil and library catalog, validation, snapshots, export, and optional prototype document sync.

The current Phase 1 interface is canvas-first: a 52px command bar, 72px activity rail, and 24px status bar keep persistent chrome compact while preserving readable tool labels. Assets, Library, Inspector, Layers, Snapshots, Validation, and Review open as contextual drawers, while the minimap and viewport controls float over the canvas. Drawer and shell state is memory-only; it is deliberately not saved with a diagram.

## Architecture

| Project | Responsibility |
| --- | --- |
| `src/AppHost` | ASP.NET Core host, Razor routing, static assets, health endpoint, and optional file-backed sync API. |
| `src/Editor.Client` | Interactive WebAssembly UI, `DiagramEditorState`, IndexedDB-backed catalog and document persistence, and the canvas-first shell. |
| `src/Diagrams.Core` | Diagram model, commands, layouts, templates, validation, serialization, routing, and export. |
| `src/Diagrams.Interop.JsPlumb` | jsPlumb adapter and browser interaction bridge. |

Browser IndexedDB is the normal system of record for diagrams, snapshots, and reusable library items. The optional server sync feature is disabled by default and is intended for the current prototype workflow, not as a production collaboration service.

## Prerequisites

- .NET 10 SDK
- PowerShell (to install the Playwright browser on Windows)
- Playwright Chromium for end-to-end tests

## Run locally

From the repository root:

```powershell
dotnet restore DiagramStudio.slnx
dotnet run --project src/AppHost --launch-profile http
```

Open `http://localhost:5181`. The document catalog is available at `/documents`; the editor is available at `/editor`.

To install the browser used by the end-to-end suite, build the E2E project first, then run:

```powershell
dotnet build tests/Editor.E2E.Tests/Editor.E2E.Tests.csproj --configuration Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Editor.E2E.Tests/bin/Release/net10.0/playwright.ps1 install chromium
```

## Build and test

```powershell
dotnet build DiagramStudio.slnx --configuration Release
dotnet test tests/Diagrams.Core.Tests/Diagrams.Core.Tests.csproj --configuration Release
dotnet test tests/Editor.E2E.Tests/Editor.E2E.Tests.csproj --configuration Release
dotnet test DiagramStudio.slnx --configuration Release
dotnet format DiagramStudio.slnx --verify-no-changes
```

On Windows environments where the Event Log provider is unavailable to the AppHost integration test process, use this scoped override:

```powershell
$env:Logging__EventLog__LogLevel__Default = 'None'
dotnet test tests/AppHost.Tests/AppHost.Tests.csproj --configuration Release
```

The override affects only the current PowerShell session. It is a test-environment workaround, not application configuration.

## Phase 1 UI boundaries

- The mounted diagram canvas remains in place when a drawer opens, closes, or the viewport crosses the responsive breakpoint.
- Active drawer, shell layout, and viewport UI state are scoped to the browser session and are not written to documents, snapshots, exports, clipboard payloads, or sync requests.
- Diagram mutations continue through the existing `DiagramEditorState` workflows, including persistence, validation, history, and optional sync behavior.
- Command adapters in the compact bar and overlays are transitional; a unified command registry and command palette are deferred beyond Phase 1.

## Project status

Diagram Studio is a prototype. It does not provide authentication, authorization, multi-user collaboration, or production-grade server persistence. See the approved Phase 1 plan in [`.swe/03-PLAN/PLAN-01-PHASE1-canvas-first-shell.md`](.swe/03-PLAN/PLAN-01-PHASE1-canvas-first-shell.md) and the current architecture snapshot in [`.swe/00-CONCEPT/CURRENT-ARCHITECTURE.md`](.swe/00-CONCEPT/CURRENT-ARCHITECTURE.md) for implementation boundaries and deferred work.
