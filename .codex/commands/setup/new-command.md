---
description: Scaffold a new slash command prompt and optional PowerShell script in .codex/commands
argument-hint: <category/command-name> [-NoScript]
script: ./new-command.ps1
shell: powershell
---

# New Command

Execute `/new-command` with arguments: `$ARGUMENTS`

## Intent

Create a new slash-command scaffold that follows this template's `.codex/commands/<category>/<command>` convention and register it in `.codex/commands/commands.json`.

## Execution

1. Expect the first argument to be `category/command-name`.
2. Run the paired script to scaffold the command files.
3. If `-NoScript` is provided, create only the markdown prompt file.
4. Ensure the new command is added to `.codex/commands/commands.json` with a description, examples, and parameter placeholders.
5. After scaffolding, tailor the prompt content to the user's requested workflow if they supplied extra context.
6. Remind the user to update `AGENTS.md` if the command should be discoverable in the preload catalog.

## Output

- Report the files created.
- Mention the `commands.json` entry that was created or updated.
- Mention whether a script companion was generated.
- Call out any assumptions or naming cleanup applied during scaffolding.
