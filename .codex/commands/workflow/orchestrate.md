---
description: Route a task through the dev-orchestrator with optional plan-only mode
argument-hint: <context> [-Prompt <prompt>] [-Plan]
script: ./orchestrate.ps1
shell: powershell
---

# Orchestrate

Execute `/orchestrate` with arguments: `$ARGUMENTS`

## Intent

Start a dev-orchestrated workflow for a user objective. This command packages the current request for the `dev-orchestrator` agent and supports a plan-only mode that must not produce code changes.

## Arguments

- `<context>`: High-signal context, scope, or problem framing for the workstream.
- `-Prompt <prompt>`: Optional explicit instruction payload to send to the `dev-orchestrator`. If omitted, derive the objective from `<context>`.
- `-Plan`: Plan only. The `dev-orchestrator` may inspect the workspace and produce a plan, but it must not write code, edit files, or execute implementation work.

## Execution

1. Inspect this command file, the paired `orchestrate.ps1` helper, `.codex/AGENTS.md`, `.codex/agents/coredev/AGENTS.override.md`, and `.codex/agents/coredev/dev-orchestrator.toml`.
2. Run the paired helper from this directory to normalize `<context>`, `-Prompt`, and `-Plan`, and to prepare the exact handoff message for `dev-orchestrator`.
3. If both `<context>` and `-Prompt` are missing, ask the user for one concise objective before continuing.
4. Spawn the `dev-orchestrator` agent using the helper's handoff payload.
5. If `-Plan` is present, instruct the `dev-orchestrator` to stop at planning only:
   produce sequencing, delegation guidance, risks, and decision points,
   do not write or modify code,
   do not make file edits,
   do not execute implementation work.
6. If `-Plan` is not present, let the `dev-orchestrator` run its normal orchestration flow to plan, delegate, integrate, and complete the task.
7. Return the orchestrator's plan or integrated result to the user, clearly labeling whether the run was `plan` or `execute`.

## Output

- Report that the request was routed to `dev-orchestrator`.
- Mention whether the run was `plan` or `execute`.
- Summarize the objective that was handed off.
- If `-Plan` was used, make it explicit that no code changes were permitted.
