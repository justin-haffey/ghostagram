---
description: Create a new project-scoped Codex agent from either a structured definition or a research brief
argument-hint: -Research "<example: Create a Q# coding assistant agent>" [-Collection <directory>] or -AgentName <name> [-Collection <directory>] -PrimaryPurpose <purpose> -WorkStyle <read-heavy|write-heavy|mixed> -MainTasks "<task1; task2>" -NonGoals "<goal1; goal2>" -PreferredModelBehavior <speed-first|balanced|deep-reasoning> -SandboxPreference <inherit|read-only|workspace-write|danger-full-access>
script: ./new-agent.ps1
shell: powershell
---

# New Agent

Execute `/new-agent` with arguments: `$ARGUMENTS`

## Intent

Create a new project-scoped Codex agent in `.codex/agents/` by translating either a structured agent definition or a research brief into a `.toml` agent file and updating `AGENTS.md` so the agent is discoverable.

## Arguments

Use named arguments that mirror the `Inputs` schema from `.codex/prompts/create-agent.prompt.md`.

- `-Research "<example: Create a Q# coding assistant agent>"`
- `-Collection <directory>`
- `-AgentName <name>`
- `-PrimaryPurpose <purpose>`
- `-WorkStyle <read-heavy|write-heavy|mixed>`
- `-MainTasks "<task1; task2>"`
- `-NonGoals "<goal1; goal2>"`
- `-PreferredModelBehavior <speed-first|balanced|deep-reasoning>`
- `-SandboxPreference <inherit|read-only|workspace-write|danger-full-access>`
- `-ToolsOrIntegrations "<tool1; tool2>"` or `-ToolsOrIntegrations none`
- `-SkillsNeeded "<skill1; skill2>"` or `-SkillsNeeded none`
- `-NicknameCandidates "<name1; name2>"`
- `-ExtraConstraints <text>`

List arguments may be passed as repeated arguments or as a single quoted value separated by commas, semicolons, or new lines.

When `-Research` is provided, it is enough to start the workflow on its own and the other arguments become optional. Use any additional parameters as constraints or overrides on top of the research brief.

When `-Collection` is provided, create or use `.codex/agents/<directory>/` and write the new agent as `.codex/agents/<directory>/<agent-name>.toml`. Create that directory only if it does not already exist.

## Execution

1. Inspect `.codex/prompts/create-agent.prompt.md`, this command file, the paired script output, and existing `.codex/agents/**/*.toml` files before drafting the new agent.
2. Run the paired `new-agent.ps1` helper from this directory with the provided arguments to normalize values, validate `-Collection`, create the collection directory only if it is missing, infer the target filename when possible, and detect whether the request is in structured-input mode or research mode.
3. If `-Research` is present, use it to research and infer the agent definition, then use any supplied structured parameters as constraints or overrides.
4. If `-Research` is not present and required structured fields are missing, ask the user only for those missing inputs. Keep follow-up questions concise and aligned to the original schema.
5. Determine the best fit agent positioning from the provided purpose, work style, tasks, non-goals, tools, constraints, and any research findings. Reuse local conventions from existing agent `.toml` files instead of inventing a new format.
6. Create the new agent configuration file in `.codex/agents/<slug>.toml`, or in `.codex/agents/<collection>/<slug>.toml` when `-Collection` is provided.
7. Update `AGENTS.md` by adding the agent to the roster and refining routing guidance when the new agent introduces a distinct responsibility.
8. If `.codex/meta/meta-create-agent.prompt.md` does not exist, treat that reference in the source prompt as stale and synthesize the configuration directly from the provided inputs plus local agent examples.
9. Validate that the new `.toml` is internally consistent: the name, description, model choice, reasoning effort, sandbox mode, and developer instructions should match the requested scope.

## Output

- Report the created agent file path and the `AGENTS.md` changes.
- Mention the resolved collection directory when `-Collection` is used.
- Mention whether the request ran in research mode or structured-input mode.
- Mention any assumptions made from incomplete inputs.
- Call out when the referenced meta prompt path was missing and a local-example fallback was used.
