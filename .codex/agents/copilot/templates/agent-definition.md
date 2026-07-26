# Agent Definition

## Identity

- Agent name: `<Agent name>`
- Archetype: `<orchestrator | connected-agent | lightweight-agent | single-agent>`
- Parent agent: `<None | [ConnectedAgent: ...] | [ChildAgent: ...]>`
- Audience: `<Audience>`
- Description: `<One or two sentences>`

## Operating Scope

- Primary objectives:
  - `<objective>`
  - `<objective>`
- In scope:
  - `<supported task>`
  - `<supported task>`
- Out of scope:
  - `<boundary>`
  - `<boundary>`

## Architecture

- Conversation control: `<generative orchestration | topics | mixed>`
- Topics used:
  - `[Topic: ...]`
- Prompts used:
  - `[Prompt: ...]`
- Agent flows used:
  - `[Flow: ...]`
- Connected agents:
  - `[ConnectedAgent: ...]`
- Child agents:
  - `[ChildAgent: ...]`

## Knowledge And Tools

- Knowledge sources:
  - `[Knowledge: ...]`
- Tools:
  - `[Tool: ...]`
- Web search: `<Enabled | Disabled>`

## Output Contract

- Required files:
  - `instructions.md`
  - `knowledge.md`
  - `tools.md`
  - `topics.md`
  - `manual-configuration.md`
- Additional folders:
  - `prompts/`
  - `kb/`

## Validation

- Test prompt 1: `<prompt>`
- Test prompt 2: `<prompt>`
- Failure boundary: `<what the agent should refuse, defer, or escalate>`
