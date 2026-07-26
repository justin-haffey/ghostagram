# AGENTS.override.md

This override governs the core engineering agent collection in [`.codex/agents/coredev/`].

## Agent Routing

Use this table as the single routing reference for the `coredev` collection.

| Agent | Spawn when |
| --- | --- |
| `dev-orchestrator` | The task is complex, multi-step, ambiguous, or benefits from sequencing and specialist delegation across the `coredev` agents. |
| `solution-architect` | System design, Azure service selection, contracts, major tradeoffs, or agentic pattern decisions are still unresolved. |
| `azure-engineer` | Azure provisioning, environment configuration, deployment execution, RBAC-aware operational changes, or cloud diagnostics are needed. |
| `csharp-developer` | The work is implementation-ready and primarily needs fast C#/.NET, Razor, or supporting JavaScript changes. |
| `full-stack-developer` | The change crosses UI, API, integration, and application structure boundaries. |
| `database-developer` | Azure SQL or Cosmos DB design, migrations, indexing, partitioning, or query-shape work is central. |
| `ui-designer` | UX, layout, accessibility, responsive behavior, or browser-validated interface flow is a first-class concern. |
| `maf-developer` | The task is about Microsoft Agent Framework, orchestration code, tool loops, memory, evals, or voice-agent work. |

## Coredev Rules

- `dev-orchestrator` is responsible only for orchestrating the agents in `.codex/agents/coredev/`.
- Use `solution-architect` before implementation agents when architecture, contracts, or major tradeoffs are still unresolved.
- Prefer parallel delegation when tasks have clean boundaries and separate write scopes.
- Delegate specialized work to the most appropriate subagent instead of overloading a single agent with mixed responsibilities.
- Keep write ownership clear when multiple `coredev` agents are active in parallel.
