---
name: copilot-maker-package
description: Assemble Maker-ready Copilot Studio package files from approved design decisions. Use when Codex needs to fill the standard Copilot markdown templates, align outputs to the future `src/agents/<agent-name>/...` contract, or keep package files internally consistent.
---

# Purpose

Turn approved Copilot design decisions into a coherent Maker-facing package.

## Workflow

1. Start from approved analysis, tool, workflow, and prompt outputs.
2. Use the templates in `.codex/agents/copilot/templates/` as the required package contract.
3. Fill the package files in this order:
   - `agent-definition.md`
   - `instructions.md`
   - `knowledge.md`
   - `tools.md`
   - `topics.md`
   - `manual-configuration.md`
4. Keep placeholders explicit where detail is intentionally unresolved.
5. Check for drift so one file does not promise capabilities that another file does not support.

## Packaging Rules

- Keep the language Maker-readable and configuration-oriented.
- For single-agent work, assemble one package shape.
- For multi-agent work, assemble one package set per standalone agent and use the orchestrator package as the system root.
- Do not generate real business-specific `src/` output unless the parent task explicitly asks for it.
