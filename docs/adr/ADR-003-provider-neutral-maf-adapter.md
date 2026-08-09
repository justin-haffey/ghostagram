# ADR-003: Provider-neutral Microsoft Agent Framework adapter

Status: Accepted

## Decision

Keep MAF packages and types behind a separate future adapter. Ghostagram owns execution plans, envelopes, events, checkpoint references, external responses, and agent-component activation contracts.

## Consequences

- Core remains stable as MAF evolves.
- The portable MAF node set is testable without models or network access.
- Future customer/client boot composition is implemented through a DI activator and scoped lease.
- Experimental provider capabilities are explicit adapter flags, not permanent file-format promises.

