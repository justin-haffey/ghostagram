# ADR-004: Server persistence catalogs and the SQLite boundary

- Status: Accepted
- Date: 2026-08-09

## Context

Ghostagram.Server now owns two durable aggregates: revisioned diagrams and named palette catalogs. Diagrams contain the complete graph JSON plus their change and idempotency journals. Palette catalogs contain custom groups, custom node definitions, item placements, and the presentation state needed to reopen the same palette. `Ghostagram.Core`, `Ghostagram.Blazor`, and the JavaScript runtime must remain usable without a database.

The original file document store is atomic for a single write because it writes a temporary file and replaces the target. Its command queue is process-local, however, and `IDocumentStore.SaveAsync` cannot express a database compare-and-swap on the persisted revision.

## Decision

Introduce storage-neutral catalog boundaries now and retain atomic JSON files as the current local-development provider:

- `IDocumentCatalog` discovers named diagrams while `DiagramCommandService` remains the only graph mutation authority.
- `IPaletteCatalogRepository` owns named, versioned palette catalogs independently of diagrams.
- Palette creation is create-only and palette updates use an expected revision. A stale circuit receives a conflict instead of silently overwriting another session.
- `GhostPalette` remains controlled and storage-neutral. The Server host persists the values emitted by its callbacks.
- Graphs and custom node definitions remain versioned JSON aggregates. A relational provider must not split the extensible graph model into node/property tables.

SQLite is the planned Server provider, but it must arrive with a transactional repository contract rather than being placed behind the current whole-document `SaveAsync` interface. That increment will introduce a persisted revision predicate and atomically update the snapshot, append the change, and record the idempotency key. Existing JSON files will be imported without deletion and remain portable recovery artifacts.

Until authenticated users and multi-device preferences exist, palette expanded/open/pinned state belongs to the selected local palette catalog. A future user-preference provider may move that presentation state without changing `GhostPalette`.

## SQLite adoption gates

The provider is ready to become the default when the following ship together:

1. Transactional `TryCommitAsync(documentId, expectedRevision, ...)` for diagrams and `TrySaveAsync(snapshot, expectedRevision)` parity for palette catalogs, each with a stale-revision result.
2. Ordered schema migrations, WAL, foreign keys, busy timeout, and a coordinated backup API.
3. Lossless import of existing documents, explicit nulls, extension data, revisions, changes, and command ledgers.
4. Concurrent-writer, rollback, restart-recovery, replay, and migration tests.
5. Filesystem ACL and secrets guidance; SQLite is not encrypted by default.

SQLite remains a single-host choice. A future multi-instance deployment needs the same repository contract backed by a server database and distributed publication; changing file formats alone does not provide scale-out correctness.

## Consequences

- Named diagram and palette workflows can ship without coupling portable libraries to storage technology.
- The current provider remains simple, inspectable, and recoverable for local collaboration.
- The solution now has explicit replacement seams for SQLite.
- Cross-process document commits remain unsupported until the transactional repository increment. File-backed palette compare-and-swap is coordinated across repository instances in one Server process; SQLite will extend that guarantee to the database transaction boundary. Neither limitation may be described as multi-host scale-out.
