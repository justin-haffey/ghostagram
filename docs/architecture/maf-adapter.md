# Microsoft Agent Framework adapter

## Boundary

Ghostagram Core, execution abstractions, and the portable MAF node set do not reference Microsoft Agent Framework packages. A future `Ghostagram.Adapters.MicrosoftAgentFramework` package is the only place allowed to reference MAF types.

The adapter translates an immutable Ghostagram execution plan to provider-native executors and edges, then projects events, checkpoints, external-input requests, results, and cancellation back into Ghostagram-owned contracts. No `AIAgent`, workflow executor, context, checkpoint, or provider message type crosses the public boundary.

## Current MAF alignment

Microsoft Agent Framework .NET is GA; the current official release research for this design targets v1.17.0 (released August 4, 2026). Workflows use bulk-synchronous supersteps: executors activated in a step may run concurrently, their queued state becomes visible to other executors in the next superstep, and checkpoints occur at completed supersteps.

The adapter boundary therefore supports streaming and non-streaming runs, cancellation, human input, checkpoint/resume, and delayed cross-node state visibility. Specialized orchestration features such as Magentic remain capability-gated because their contracts have higher churn.

Official references:

- [MAF overview](https://learn.microsoft.com/agent-framework/overview/)
- [Workflows](https://learn.microsoft.com/agent-framework/workflows/workflows)
- [Executors](https://learn.microsoft.com/agent-framework/workflows/executors)
- [Workflow events](https://learn.microsoft.com/agent-framework/workflows/events)
- [Checkpoints](https://learn.microsoft.com/agent-framework/workflows/checkpoints)
- [Orchestrations](https://learn.microsoft.com/agent-framework/workflows/orchestrations/)
- [.NET v1.17.0 release](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.17.0)

## Portable envelope

Dynamic Ghostagram nodes cannot require a generated MAF executor type per diagram. The future adapter should use one adapter-local, source-generated executor over a portable message envelope containing contract ID, JSON payload, source/target port, correlation ID, activation ID, and attempt.

Ghostagram validates property and port schemas before adapter preparation. The MAF package routes the already-validated envelope. This remains compatible with source generation and AOT without runtime generic construction.

## Agent component activation seam

An Agent Component node stores a stable component key plus JSON configuration. `IExecutionComponentActivator` leases a component for a run/session and returns `IExecutionComponentLease`, which streams neutral `ComponentUpdate` values. The future DI implementation may assemble and boot an agent, tools, memory, voice, and customer context, but none of that behavior is implemented in this phase.

The lease owns scope and disposal. Public contracts never expose `IServiceProvider` or a provider agent object.

## Mock facade

Tests use a deterministic mock adapter/component activator that captures prepared nodes and transitions, emits scripted events, honors cancellation, and verifies lease disposal. Tests do not require credentials, models, network access, or MAF packages.
