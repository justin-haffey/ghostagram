---
description: Rename an existing project-scoped Codex agent and update workspace references
argument-hint: "<agent-name>" -Name "<new-name>"
script: ./rename-agent.ps1
shell: powershell
---

# Rename Agent

Execute `/rename-agent` with arguments: `$ARGUMENTS`

## Intent

Rename an existing project-scoped Codex agent by updating its `.toml` filename, its internal `name` field, and every documented reference to that agent across the workspace, including `AGENTS.md`.

## Arguments

- `"<agent-name>"`: Existing agent identifier. Accept the current agent slug, the current `.toml` filename without extension, or the canonical `name =` value inside the agent file.
- `-Name "<new-name>"`: The new canonical agent name. This also drives the new `.toml` filename slug.

## Execution

1. Inspect this command file, the paired `rename-agent.ps1` helper, existing `.codex/agents/**/*.toml` files, and `AGENTS.md` before making edits.
2. Run the paired helper from this directory to resolve the source agent file, infer the new slug and target file path, and list workspace files that reference the old agent name, slug, or file path.
3. If the helper reports zero matches or multiple agent matches, pause and ask the user only for the missing disambiguation.
4. Rename the agent `.toml` file within its current directory to match the new slug.
5. Update the renamed `.toml` file:
   set `name = "<new-name>"`,
   update any self-referential text that still uses the old canonical name or slug when it would now be inaccurate.
6. Update all documented references in the workspace, including `AGENTS.md`, using the helper's discovered reference files plus any additional `rg` matches for the old canonical name, old slug, and old relative path.
7. Validate that no stale documented references remain for the old agent name, old slug, or old `.toml` path unless they are intentionally historical.

## Output

- Report the old and new agent names.
- Report the old and new `.toml` paths.
- List the files updated for reference changes, including `AGENTS.md` when applicable.
- Mention any ambiguity that was resolved or any references intentionally left unchanged.
