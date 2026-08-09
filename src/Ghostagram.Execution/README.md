# Ghostagram.Execution

`Ghostagram.Execution` turns persisted Ghostagram data into validated, immutable execution plans without coupling Core to an orchestration provider.

- `NodeTypeRegistry` is an immutable DI catalog of node sets and type versions.
- `DeterministicNodeFactory` materializes registered nodes, properties, and stable ports.
- `NodeRegistration.Create<THandler,TState>` is the typed extension path: bind persisted properties once, then implement `NodeHandler<TState>` without managing graph topology.
- `GraphCompiler` supports `DagOnly`, guarded `BoundedCycles`, and capability-gated `AdapterNative` plans with structured diagnostics and execution-relevant fingerprints.
- `IOrchestrationAdapter` and `IOrchestrationRun` isolate prepare/start/resume, ordered events, checkpoints, external input, cancellation, and disposal.
- `IExecutionComponentActivator` leases provider-neutral components through streaming `ComponentUpdate` values; provider objects never cross the public boundary.
- `ConcurrentExecutionData` provides scoped, versioned compare-and-update state for parallel runs.

The package intentionally contains no Microsoft Agent Framework reference. A future adapter project may translate compiled plans to MAF while the public contracts stay provider-neutral.
