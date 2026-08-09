# Server persistence architecture

Ghostagram.Server treats diagrams and palettes as separate durable aggregates.

## Diagrams

`DiagramCommandService` is the authoritative mutation boundary. Each accepted command is revision-checked, reduced against the current graph, committed with its change and idempotency entry, and only then published. `IDocumentCatalog` adds discovery without moving mutation logic into the UI. The Laboratory supports autosave, explicit Save status, Save As to an independent document identity, and Open from the catalog.

Opening a document replaces the complete `DiagramDocument`: nodes, dynamic property values, ports, edges and connector geometry, nested/collapsed groups, edge types, selection, viewport, styles, icons, and extension data. Undo/redo history is cleared at the document boundary. Delayed browser events carrying the previous document ID are ignored.

## Palette catalogs

`IPaletteCatalogRepository` stores a schema-versioned `PaletteCatalogSnapshot`. It includes:

- named custom groups;
- complete custom node definitions, styles, icons, ports, properties, defaults, options, and JSON metadata;
- placements for custom, built-in, and DI-registered items;
- expanded group IDs and palette open/pinned state;
- catalog revision and timestamps.

Built-in and registered node definitions remain application/DI-owned. Their catalog records are placement overrides, not duplicate executable definitions. Opening a palette never changes the active diagram.

`FilePaletteCatalogRepository` validates and deep-clones the snapshot, creates catalogs without overwriting an existing ID, and commits updates only when the persisted revision matches the caller's expected revision. It writes a same-directory temporary file and atomically replaces the named catalog. Corrupt files are omitted from library listings but remain untouched as recovery evidence; opening one directly produces an explicit recovery state and requires Save As. The interface is suitable for a later transactional SQLite implementation.

## Layer independence

- `Ghostagram.Core` owns serializable graph data types and knows nothing about files, SQLite, Blazor, or browsers.
- `Ghostagram.Blazor` owns reusable controlled components. `GhostPalette` renders supplied state and emits intent; it does not select a persistence provider.
- The JavaScript runtime owns interaction and rendering. It carries document identity in events but does not save catalogs.
- `Ghostagram.Server` composes persistence, command authority, DI node sets, collaboration, and the Laboratory application workflow.

See [ADR-004](../adr/ADR-004-server-persistence-catalogs-and-sqlite.md) for the SQLite decision and adoption gates.
