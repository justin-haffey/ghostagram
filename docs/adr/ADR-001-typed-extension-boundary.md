# ADR-001: Heterogeneous persistence and typed extension boundary

Status: Accepted

## Decision

Persist non-generic `DiagramNode` records with JSON properties. Provide generic, typed definitions and handlers through DI, then erase those types into an immutable runtime catalog.

## Consequences

- Old files and heterogeneous diagrams remain simple JSON.
- Extensions do not subclass model records or edit graph reducers.
- Type validation occurs during registration, node creation, and execution compilation.
- CLR implementation types are not part of the file format.

