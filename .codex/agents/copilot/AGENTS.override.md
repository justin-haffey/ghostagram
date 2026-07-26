# AGENTS.override.md

This override governs the Copilot meta-agent collection in [`.codex/agents/copilot/`](C:/Users/JustinHaffey/Documents/Ghostworx/projects/source/codex-project/.codex/agents/copilot).

## Agent Routing

Use this table as the single routing reference for the `copilot` collection.

| Agent | Spawn when |
| --- | --- |
| `copilot-orchestrator` | The request needs end-to-end Copilot package orchestration, workflow selection, specialist sequencing, or integrated Maker-ready outputs. |
| `copilot-role-analyst` | The first job is to analyze the requested role, audience, scope, tasks, boundaries, or whether the design should stay single-agent or become MAS. |
| `copilot-tool-expert` | The work centers on tools, knowledge sources, connectors, prompts as tools, auth, prerequisites, web search, or rejected alternatives. |
| `copilot-workflow-designer` | The work centers on topics, triggers, prompt placement, agent flows, connected-agent decomposition, lightweight-agent use, or deterministic vs generative control. |
| `copilot-prompt-engineer` | The work centers on instructions, prompt specs, placeholders, response contracts, or instruction alignment to the package. |
| `copilot-package-writer` | The work needs final file assembly, template filling, package consistency, or `manual-configuration.md`. |
| `adaptive-card-developer` | A topic, prompt, approval, or tool response needs a concrete Adaptive Card payload. |

## Copilot Rules

- `copilot-orchestrator` owns orchestration only and should not keep specialist work to itself.
- Use `copilot-role-analyst` before drafting instructions, tools, or topics when the role shape is still ambiguous.
- Prefer the smallest viable architecture: single agent first, MAS only when role boundaries, ownership, or specialization justify it.
- Use connected agents for standalone domain capabilities and lightweight agents for parent-local micro-specialties.
- Do not position an agent that contains connected agents as a connected agent for another main agent.
- Keep Maker-facing outputs aligned to the templates in `.codex/agents/copilot/templates/`.
