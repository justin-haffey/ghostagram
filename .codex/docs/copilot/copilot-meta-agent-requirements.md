# Copilot Meta-Agent Collection Requirements

## Purpose

Refactor the project-local Copilot collection into a repo-native meta-agent system that can design Maker-ready Copilot Studio packages without checking real business-specific Copilot definitions into `src/` during this phase.

The collection must:

- live under `.codex`
- generate or describe future Maker outputs under `src/agents/<agent-name>/...`
- rely on reusable templates instead of one-off package layouts
- keep workflow logic explicit and specialist-owned
- stay aligned to current Microsoft Learn guidance for connected agents, child agents, topics, prompts, and agent flows

## Deliverables

This implementation must deliver:

- a refactored Copilot agent collection in `.codex/agents/copilot/`
- `.codex/agents/copilot/AGENTS.override.md`
- the template library in `.codex/agents/copilot/templates/`
- repo-local skills in `.codex/skills/`
- the `/new-copilot` slash command in `.codex/commands/copilot/`
- updates to `.codex/AGENTS.md` and `.codex/commands/README.md`
- this requirements document as the implementation source of truth

This implementation must not deliver:

- real customer- or business-specific Copilot definitions under `src/`
- a second legacy `.code/commands/` command path
- a separate v2 Copilot collection

## Copilot Collection Architecture

### Collection rules

- Treat `.codex/agents/copilot/AGENTS.override.md` as the only routing table for the Copilot collection.
- Use `copilot-orchestrator` as the entry point for end-to-end Copilot package design work.
- Keep the orchestrator shallow and delegation-heavy.
- Keep specialists narrow, explicit, and template-aware.

### Required agents

- `copilot-orchestrator`
  - Select workflow.
  - Spawn specialists in the required order.
  - Integrate outputs.
  - Validate completeness and assumptions.
- `copilot-role-analyst`
  - Analyze role, audience, scope, tasks, boundaries, and whether the request should stay single-agent or become MAS.
  - Produce `role-analysis-report.md`.
- `copilot-tool-expert`
  - Choose knowledge sources, tools, prompts-as-tools, web search posture, auth, and prerequisites.
  - Fill `knowledge.md`, `tools.md`, and relevant setup guidance.
- `copilot-workflow-designer`
  - Design topics, flows, trigger strategy, connected-agent decomposition, and lightweight-agent boundaries.
  - Fill `topics.md` and drive workflow decisions.
- `copilot-prompt-engineer`
  - Draft `instructions.md` and `prompt-spec.md`.
  - Keep placeholder grammar and response contracts consistent.
- `copilot-package-writer`
  - Assemble the final Maker package and `manual-configuration.md`.
  - Keep all files internally consistent.
- `adaptive-card-developer`
  - Return Adaptive Card payload JSON only when a topic or prompt explicitly requires card-driven interaction.

### Skill attachments

The Copilot agents must use explicit `[[skills.config]]` entries where relevant:

- `copilot-architecture-playbook`
  - attach to `copilot-orchestrator`, `copilot-role-analyst`, `copilot-tool-expert`, and `copilot-workflow-designer`
- `copilot-maker-package`
  - attach to `copilot-orchestrator` and `copilot-package-writer`
- `copilot-manual-configuration`
  - attach to `copilot-orchestrator`, `copilot-tool-expert`, and `copilot-package-writer`
- `copilot-prompt-patterns`
  - attach to `copilot-prompt-engineer` and `adaptive-card-developer`

Use `mcp_servers.microsoft_learn` only on agents that need live Microsoft documentation lookup.

## Template Library

Create the following markdown templates in `.codex/agents/copilot/templates/`:

- `role-analysis-report.md`
- `agent-definition.md`
- `instructions.md`
- `knowledge.md`
- `tools.md`
- `topics.md`
- `manual-configuration.md`
- `prompt-spec.md`
- `single-agent.workflow.md`
- `multi-agent.workflow.md`
- `archetype-orchestrator.md`
- `archetype-connected-agent.md`
- `archetype-lightweight-agent.md`

### Template rules

- Keep templates markdown-first and Maker-readable.
- Use placeholders consistently:
  - `[Topic: ...]`
  - `[Prompt: ...]`
  - `[Flow: ...]`
  - `[Knowledge: ...]`
  - `[ChildAgent: ...]`
  - `[ConnectedAgent: ...]`
- Make every section map to either:
  - a Copilot Studio configuration surface
  - a runtime behavior contract
  - a validation obligation
- Prefer dense, operational copy over narrative explanation.
- Allow explicit `None required` or `Generative orchestration only` sections so the templates do not force unnecessary topics, flows, or tools.

## Future Output Contract

Do not generate these outputs in this implementation, but treat them as the canonical target shape for future package generation.

### Single-agent package

`src/agents/<agent-name>/`

- `agent-definition.md`
- `instructions.md`
- `knowledge.md`
- `tools.md`
- `topics.md`
- `manual-configuration.md`
- `prompts/`
- `kb/`

### Multi-agent package

- Create one sibling folder per standalone agent under `src/agents/`.
- Treat the top-level orchestrator folder as the system root.
- Use the orchestrator package to describe:
  - connected-agent topology
  - setup order
  - handoff rules
  - child-agent boundaries
- Keep lightweight agents embedded in the parent design unless they are intentionally promoted to standalone connected agents.

## Workflow Requirements

### Single-agent workflow

The workflow file must enforce this sequence:

1. `copilot-role-analyst`
2. `copilot-tool-expert` and `copilot-workflow-designer`
3. `copilot-prompt-engineer`
4. `adaptive-card-developer` only when needed
5. `copilot-package-writer`
6. `copilot-orchestrator` validation

The workflow must require:

- a standardized analysis report before detailed design
- the smallest viable set of topics, prompts, flows, and lightweight agents
- explicit notes when generative orchestration is sufficient and topics are intentionally omitted
- a final package aligned to the template set

### Multi-agent workflow

The workflow file must require:

- one top-level orchestrator
- narrowly scoped connected agents for standalone domains
- lightweight agents for embedded micro-specialties
- topics only where deterministic control is worth the overhead
- prompts inside topics for structured model work
- flows for deterministic, side-effectful, retry-sensitive, or approval-driven work
- explicit I/O contracts and setup order for every connected or child agent

### MAS constraints

- An agent that contains connected agents must not be reused as a connected agent for another main agent.
- Connected agents must be documented with the same-environment, published, and allow-connections prerequisites.
- Child agents must be treated as lightweight parent-local capabilities, not standalone connected agents by default.

## Command Requirements

Create `/new-copilot` in `.codex/commands/copilot/`.

### Interface

`/new-copilot "<brief>" [-Name <agent-name>] [-Audience <text>] [-Domain <text>] [-Constraints <text>] [-Mas] [-WhatIf]`

### Behavior

- Normalize the request.
- Resolve the workflow:
  - single-agent by default
  - multi-agent when `-Mas` or `-MAS` is present
- Default output root to `src/agents`.
- If `-WhatIf` is present:
  - resolve paths
  - resolve workflow
  - build the orchestrator handoff payload
  - do not generate output
- If `-WhatIf` is not present:
  - hand off to `copilot-orchestrator`
  - do not generate `src/` output during this implementation task itself

### Required handoff content

- mode
- brief
- normalized agent name if available
- audience/domain/constraints if provided
- selected workflow file
- target output root
- `WhatIf` status
- reminder to follow the Copilot collection override and template contract

## Validation Requirements

Validate the implementation with:

- Copilot agent loading checks
- command discovery checks
- `WhatIf` checks for single-agent and MAS command modes
- dry-run package completeness checks against a temporary output root
- skill attachment checks
- template presence checks
- routing documentation checks

### Quality gates

- no duplicate or overlapping topics without a stated reason
- no tool inventory entry without configuration notes
- no instruction section that promises unsupported capabilities
- no real business-specific Copilot packages committed under `src/`

## Source Guidance

Use the following official references as the authoritative baseline:

- Multi-agent patterns:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/multi-agent-patterns
- Multi-agent overview and limitations:
  - https://learn.microsoft.com/he-il/microsoft-copilot-studio/authoring-add-other-agents
- Connected agents:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/add-agent-copilot-studio-agent
- Child agents:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/add-agent-child-agent
- Topics:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/topics-overview
- Prompts:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/nlu-prompt-node
- Tools:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/agent-tools
- Instructions:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/authoring-instructions
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/generative-mode-guidance
- Agent flows:
  - https://learn.microsoft.com/en-us/microsoft-copilot-studio/flow-agent
