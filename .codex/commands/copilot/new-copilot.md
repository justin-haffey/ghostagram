---
description: Route a Copilot package request through the copilot-orchestrator using the single-agent or multi-agent workflow
argument-hint: "<brief>" [-Name <agent-name>] [-Audience <text>] [-Domain <text>] [-Constraints <text>] [-Mas] [-WhatIf]
script: ./new-copilot.ps1
shell: powershell
---

# New Copilot

Execute `/new-copilot` with arguments: `$ARGUMENTS`

## Intent

Normalize a Copilot package request, choose the correct Copilot workflow, and hand the work off to `copilot-orchestrator`.

## Arguments

- `"<brief>"`: Required user objective or package brief.
- `-Name <agent-name>`: Optional canonical agent or system name.
- `-Audience <text>`: Optional target audience.
- `-Domain <text>`: Optional business or capability domain.
- `-Constraints <text>`: Optional design or operating constraints.
- `-Mas`: Select the multi-agent workflow.
- `-WhatIf`: Resolve the workflow, paths, and orchestrator handoff without generating output.

`-Mas` and `-MAS` are equivalent.

## Execution

1. Inspect this command file, the paired script, `.codex/AGENTS.md`, `.codex/agents/copilot/AGENTS.override.md`, `.codex/agents/copilot/copilot-orchestrator.toml`, and the workflow files in `.codex/agents/copilot/templates/`.
2. Run the paired `new-copilot.ps1` helper to normalize the brief, optional metadata, selected workflow, and target output root.
3. If the brief is missing, ask the user for one concise objective before continuing.
4. If `-WhatIf` is present, return the helper output and do not spawn agents or generate files.
5. If `-WhatIf` is not present, spawn `copilot-orchestrator` and pass the helper's handoff message.
6. Keep the work aligned to the Copilot template library and the future `src/agents/<agent-name>/...` output contract.
7. Do not generate `src/` output as part of implementing this meta-agent system itself.

## Output

- Report that the request was routed to `copilot-orchestrator`.
- Mention whether the selected workflow is `single-agent` or `multi-agent`.
- Mention the resolved output root and workflow file.
- If `-WhatIf` was used, make it explicit that no generation was allowed.
