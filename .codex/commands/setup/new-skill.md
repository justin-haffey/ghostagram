---
description: Scaffold a new project-local Codex skill in .codex/skills
argument-hint: <skill-name> [-NoMetadata] [-NoReferences] [-NoScripts] [-WhatIf]
script: ./new-skill.ps1
shell: powershell
---

# New Skill

Execute `/new-skill` with arguments: `$ARGUMENTS`

## Intent

Create a new project-local Codex skill scaffold that follows this project's `.codex/skills/<skill-name>/` convention.

## Arguments

- `<skill-name>`: Lowercase, hyphenated folder name for the skill.
- `-NoMetadata`: Skip `agents/openai.yaml`.
- `-NoReferences`: Skip the `references/` folder.
- `-NoScripts`: Skip the `scripts/` folder.
- `-WhatIf`: Preview the generated paths without writing files.

## Execution

1. Expect the first argument to be the skill folder name.
2. Run the paired script to scaffold the skill directory and starter files.
3. If the user provided extra context, tailor `SKILL.md` after scaffolding so the description and workflow match the intended use.
4. Remind the user that this project keeps checked-in skills under `.codex/skills/`.
5. If the skill is meant for broad distribution or bundled integrations, recommend packaging it later as a plugin instead of expanding local scaffolding indefinitely.

## Output

- Report the files and directories created.
- Mention whether metadata, scripts, or references were omitted.
- Call out any naming normalization or assumptions applied.
