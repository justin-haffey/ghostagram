# Codex Slash Commands

This template supports project-local slash commands from `.codex/commands/`.

## Directory Contract

Commands are organized by category:

```text
.codex/
  commands/
    <category>/
      <command>.md
      <command>.ps1
```

- The `.md` file is the command prompt/instruction file.
- A same-name script is optional and may live beside the prompt.
- On this template, PowerShell is the default script format: `.ps1`.
- Folders whose names start with `_` are scaffolding folders, not runnable command categories.

## Current Command Catalog

- `setup/new-command`
- `setup/new-agent`
- `setup/new-skill`
- `setup/rename-agent`
- `setup/move-agent`
- `copilot/new-copilot`
- `style-transfer/capture-style`
- `style-transfer/mimic-style`
- `workflow/orchestrate`
- `utilities/list-commands`

Use these from chat as:

```text
/new-command setup/my-command
/new-agent -AgentName "Example Agent" -PrimaryPurpose "Handle one specific workflow well" -WorkStyle mixed -MainTasks "Task one; Task two" -NonGoals "Do not do X" -PreferredModelBehavior balanced -SandboxPreference workspace-write -ToolsOrIntegrations none -SkillsNeeded none
/new-skill repo-conventions
/rename-agent "example-agent" -Name "example-agent-v2"
/move-agent "example-agent" -Collection "experimental" -Source "legacy"
/new-copilot "Create a policy analyst copilot for HR FAQs" -Name "hr-policy-analyst"
/capture-style .\samples\essay-1.md .\samples\essay-2.md -ContextPath .\samples\context.txt
/mimic-style .\styles\author-style-card.md .\briefs\new-doc.md .\drafts\candidate-a.md
/orchestrate investigate flaky builds -Prompt "Coordinate a fix for the flaky coredev engineering workflow"
/list-commands
```

## Command Resolution Rules

When an agent sees `/command-name`:

1. Search `.codex/commands/**/command-name.md`.
2. If exactly one match exists, load that prompt file first.
3. If a same-name script exists, inspect it and run it when the command instructions or task call for execution.
4. Pass any trailing text after the slash command as the command arguments.
5. If multiple matches exist, ask the user which category they want.
6. If no matches exist, suggest the closest available commands from the catalog.

## Recommended Front Matter

Command prompt files can include YAML front matter to help agents resolve intent:

```yaml
---
description: One-line summary of what the command does
argument-hint: Optional hint for command arguments
script: ./command-name.ps1
shell: powershell
---
```

The markdown body should describe:

- The command goal
- What arguments mean
- Whether the paired script should be executed
- How the agent should summarize results

## Authoring Notes

- Keep command names lowercase and hyphenated.
- Keep prompts task-focused and implementation-oriented.
- Prefer one command per user intent.
- If a command edits files, say what it owns and what it must avoid.
- If a command shells out, keep the script idempotent when practical.
- Update `AGENTS.md` whenever you add, rename, or remove a runnable command.

## Templates

Reusable starter files live in `.codex/commands/_templates/`.
