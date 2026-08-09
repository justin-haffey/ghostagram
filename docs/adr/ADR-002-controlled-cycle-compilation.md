# ADR-002: Controlled-cycle execution compilation

Status: Accepted

## Decision

Allow cycles in authored diagrams. `DagOnly` compilation rejects cycles; `BoundedCycles` requires an explicit registered loop controller and termination budget in every cyclic component; `AdapterNative` additionally requires an adapter capability.

## Consequences

- Diagrams support state machines, conversations, and agentic loops.
- Arbitrary cycles cannot accidentally become infinite executions.
- Cycle diagnostics name the exact component and missing guard.
- Layout and authoring remain independent from execution policy.

