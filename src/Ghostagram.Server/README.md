# Ghostagram Server

`Ghostagram.Server` is the authoritative document, layout, realtime, and MCP boundary. Browser events, REST callers, Razor hosts, and MCP tools all commit through `DiagramCommandService`.

## Collaborative MCP

The server uses the official C# MCP SDK and exposes streamable HTTP at:

```text
http://127.0.0.1:5256/mcp
```

The authoring surface exposes nine tools: `describe_capabilities`, `list_diagrams`, `open_session`, `create_diagram`, `get_diagram`, `apply_operations`, `layout_diagram`, `export_svg`, and `close_session`. `describe_capabilities` returns the complete machine-readable authoring schema; `list_diagrams` reports durable documents plus active Laboratory views and their acknowledged render revisions. Sessions are ephemeral collaboration handles; documents and revisions remain durable. Every mutating call uses the same `actorId`, `commandId`, `baseRevision`, reducer, idempotency ledger, and post-commit publication path as the REST API.

Open a specific durable document in the integrated Laboratory at `/?documentId=<encoded-document-id>`. An external MCP commit is complete for live collaboration only after the authoritative command succeeds, `list_diagrams` reports the Laboratory has acknowledged that revision, and the visible browser plus console have been checked.

Start the standard local profile with:

```powershell
dotnet run --project src/Ghostagram.Server/Ghostagram.Server.csproj
```

## Deterministic layout

List registered algorithms:

```http
GET /api/layouts
```

Preview without persistence:

```http
POST /api/documents/{documentId}/layout
Content-Type: application/json

{
  "documentId": "workflow",
  "actorId": "human",
  "commandId": "layout-preview-1",
  "baseRevision": 12,
  "algorithm": "ghost-layered",
  "seed": 42,
  "dryRun": true,
  "options": {
    "direction": "right",
    "layerSpacing": 140,
    "nodeSpacing": 56,
    "componentSpacing": 120,
    "groupPadding": 32,
    "groupHeader": 28,
    "crossingSweeps": 8
  }
}
```

Set `dryRun` to `false` to commit. The response carries the algorithm/version, seed, proposed revision, operation batch, layout metrics, and the normal command result. Reusing the same actor and command ID with identical intent is an idempotent replay; changing the intent under that key is rejected.

Supported directions are `right`, `left`, `down`, and `up`. Nested groups are laid out recursively, cycles are condensed into deterministic strongly connected components, and disconnected components are packed independently. Limits default to 10,000 nodes and 50,000 edges.

Run the dependency-free verification suite with:

```powershell
dotnet run --project tests/Ghostagram.Layout.Verification/Ghostagram.Layout.Verification.csproj -c Release
```
