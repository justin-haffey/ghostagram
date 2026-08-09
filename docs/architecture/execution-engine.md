# Execution engine boundary

## Separation from authoring

Graph execution never runs inside the document command queue. The command queue serializes durable diagram edits; execution has separate run scopes, state, budgets, event streams, and cancellation.

The initial implementation supplies compilation and provider-neutral contracts. It deliberately does not execute customer-facing agents.

## Compilation profiles

- `DagOnly` produces a deterministic topological plan and rejects the first exact strongly connected component.
- `BoundedCycles` accepts a cyclic component only when it contains a registered loop-controller node with an explicit iteration, activation, or deadline bound.
- `AdapterNative` preserves validated topology for an adapter that declares the required capability.

An arbitrary cycle is never treated as a safe agentic loop. A bounded loop guard makes termination policy explicit and inspectable.

Compilation returns structured diagnostics instead of throwing for normal authoring problems. Plans are immutable and fingerprinted so checkpoints cannot resume against a different graph.

## Runtime contracts

The provider-neutral boundary separates:

1. graph compilation;
2. adapter plan preparation;
3. start or resume;
4. ordered event consumption;
5. external/HITL responses;
6. cancellation and disposal.

Execution identities include run, node, activation, and attempt. Adapters promise at-least-once attempts; handlers and external effects use those identities for idempotency.

## Concurrency ownership

Definitions and compiled plans are immutable singletons. A run creates its own DI scope and owns activation state. Future execution coordination should use a bounded channel and per-node concurrent state while maintaining deterministic fan-in reduction.

`MaxDegreeOfParallelism`, activation budgets, loop budgets, deadlines, failure policy, and cancellation are run inputs. No serialized graph field exposes a `ConcurrentDictionary` or runtime synchronization primitive.

Run state is accessed through atomic, versioned operations with run, node, and conversation scopes. A provider may implement delayed cross-node visibility, but the adapter must document the visibility point.

## Events and checkpoints

Events have an adapter-neutral monotonic sequence and include run/node start, deltas, completion, external input, checkpoint, failure, cancellation, and run completion. The stream is bounded and backpressured.

Checkpoint references contain adapter ID, checkpoint ID, and plan fingerprint only. Provider-native checkpoint objects remain private to the adapter package.

