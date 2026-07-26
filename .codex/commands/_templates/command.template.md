---
description: Describe what this slash command should do
argument-hint: Optional arguments, for example [target] or [category/command-name]
script: ./command-name.ps1
shell: powershell
---

# Command Name

Execute `/command-name` with arguments: `$ARGUMENTS`

## Intent

Describe the user outcome this command is meant to produce.

## Inputs

- Explain the expected argument shape.
- Note any defaults or assumptions.

## Execution

1. Read the relevant project files first.
2. Run the paired script if it is needed for discovery or scaffolding.
3. Apply the command-specific workflow.
4. Validate the result before finishing.

## Output

- Summarize what changed or what was discovered.
- Call out follow-up actions, if any.
