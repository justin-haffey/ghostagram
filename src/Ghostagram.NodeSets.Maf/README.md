# Ghostagram.NodeSets.Maf

This package supplies the portable `MAF Orchestration` authoring catalog. It does not reference or execute Microsoft Agent Framework.

The node set includes Start, Agent Component, Data Capture, Route/Decision, Parallel Split, Join, Handoff, Human Input, Loop Guard, and Output. Definitions carry dimensions, icons, dynamic property schemas, defaults, and property-bound ports. `Ghostagram.Server` registers the set through DI and projects it into the reusable `GhostPalette` control.

Execution remains behind the neutral contracts in `Ghostagram.Execution`. A future MAF adapter will assemble agent components through `IExecutionComponentActivator`, translate the compiled graph, and project events/checkpoints back without exposing MAF types.
