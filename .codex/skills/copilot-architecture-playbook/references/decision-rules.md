# Copilot Architecture Decision Rules

## Boundary Matrix

- Orchestrator:
  - Use for cross-domain routing, synthesis, clarifications, and final answer composition.
- Connected agent:
  - Use for independently bounded domain capabilities.
  - Requires same environment, publish, and allow-connections readiness.
- Lightweight child agent:
  - Use for parent-local micro-specialties that should not become standalone connected agents.
- Topic:
  - Use for deterministic conversation control, slot filling, guided collection, or exception handling.
- Prompt:
  - Use for structured model work inside a topic or workflow step.
- Agent flow:
  - Use for deterministic execution with side effects, retries, approvals, or sequencing.

## Simplicity Bias

- Prefer a single agent before adding orchestration layers.
- Prefer generative orchestration before adding explicit topics.
- Prefer tools and flows over additional agents when the need is execution rather than domain reasoning.

## Guardrails

- An agent with connected agents must not also be reused as a connected agent elsewhere.
- Do not create connected agents for single-tool capabilities.
- Do not create topics for every possible intent.
