---
description: Move an existing project-scoped Codex agent into a collection directory and update workspace references
argument-hint: "<agent-name>" -Collection "<collection-name>" [-Source "<source-collection-name>"]
script: ./move-agent.ps1
shell: powershell
---

# Move Agent

Execute `/move-agent` with arguments: `$ARGUMENTS`

## Intent

Move an existing project-scoped Codex agent `.toml` file into a collection directory under `.codex/agents/`, creating the destination collection only if it does not already exist, and update documented references across the workspace, including `AGENTS.md`.

## Arguments

- `"<agent-name>"`: Existing agent identifier. Accept the current agent slug, the current `.toml` filename without extension, or the canonical `name =` value inside the agent file.
- `-Collection "<collection-name>"`: Destination collection directory under `.codex/agents/`.
- `-Source "<source-collection-name>"`: Optional source collection directory under `.codex/agents/` to narrow the search when the agent name is ambiguous.

## Execution

1. Inspect this command file, the paired `move-agent.ps1` helper, existing `.codex/agents/**/*.toml` files, and `AGENTS.md` before making edits.
2. Run the paired helper from this directory to resolve the source agent file, normalize the source and target collections, create the destination collection directory only if it is missing, infer the target path, and list workspace files that reference the old agent path.
3. If the helper reports zero matches or multiple agent matches, pause and ask the user only for the missing disambiguation.
4. Move the agent `.toml` file to `.codex/agents/<collection>/<existing-file-name>.toml`.
5. Update all documented references in the workspace, including `AGENTS.md`, using the helper's discovered reference files plus any additional `rg` matches for the old relative `.toml` path and any collection-specific references that are now stale.
6. Validate that the moved agent still lives under `.codex/agents/`, the destination directory exists, and no stale documented references remain for the old `.toml` path unless they are intentionally historical.

## Output

- Report the old and new `.toml` paths.
- Mention whether the destination collection directory was newly created or already existed.
- List the files updated for reference changes, including `AGENTS.md` when applicable.
- Mention any ambiguity that was resolved or any references intentionally left unchanged.
