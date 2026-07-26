---
name: copilot-prompt-patterns
description: Author Copilot Studio instructions, prompt specs, placeholder patterns, and response contracts. Use when Codex needs to write `instructions.md`, `prompt-spec.md`, placeholder-driven workflow language, or Adaptive Card binding notes for a Copilot package.
---

# Purpose

Write dense, operational prompt artifacts that match the approved Copilot architecture.

## Workflow

1. Start from the role-analysis report and the approved architecture.
2. Fill `instructions.md` for the agent-level behavior contract.
3. Fill `prompt-spec.md` only for prompts that are actually needed.
4. Use placeholder grammar consistently:
   - `[Topic: ...]`
   - `[Prompt: ...]`
   - `[Flow: ...]`
   - `[Knowledge: ...]`
   - `[ChildAgent: ...]`
   - `[ConnectedAgent: ...]`
5. Keep the language operational, grounded, and aligned to the package's actual tools and knowledge.

## Writing Rules

- Do not invent tools, topics, flows, or agents that are not part of the package.
- Prefer short, direct constraints over generic assistant prose.
- Make fallback behavior explicit.
- Use structured input and output schemas when a prompt performs a narrow task.

## References

- Read `references/patterns.md` when you need the placeholder grammar or prompt-writing guardrails.
