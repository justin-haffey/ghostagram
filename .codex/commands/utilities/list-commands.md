---
description: Inventory runnable project slash commands from .codex/commands
argument-hint: [-Params] [optional text filter]
script: ./list-commands.ps1
shell: powershell
---

# List Commands

Execute `/list-commands` with arguments: `$ARGUMENTS`

## Intent

Show the runnable slash commands currently available in this project using the checked-in command registry at `.codex/commands/commands.json`.

## Execution

1. Run the paired `list-commands.ps1` script to read `.codex/commands/commands.json`.
2. Treat `-Params` as an optional switch that includes parameter descriptions and examples from the registry.
3. Treat any remaining argument text as an optional filter against category, command name, or description.
4. Print the results in a CLI-style plain-text listing with no extra reasoning.
5. If no commands match, suggest the closest command names that do exist.

## Output

- Print only command information.
- Include the slash form, category, and description.
- When `-Params` is used, include each parameter and at least one example.
- Do not emit reasoning or workflow narration around the listing.
