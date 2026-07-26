# Manual Configuration

## Prerequisites

- Tenant or environment prerequisites: `<text>`
- Licensing prerequisites: `<text>`
- Connection or auth prerequisites: `<text>`
- Publish prerequisites for connected agents: `<text>`
- Allow-connections prerequisites for connected agents: `<text>`

## Setup Order

1. Create or open the target Copilot Studio agent.
2. Set the Name and Description.
3. Paste `instructions.md`.
4. Add knowledge from `knowledge.md`.
5. Configure tools from `tools.md`.
6. Create topics from `topics.md` if topics are required.
7. Create prompt assets from `prompts/` and wire them into topics when needed.
8. Create or connect flows when `[Flow: ...]` is required.
9. Connect standalone agents when `[ConnectedAgent: ...]` is required.
10. Validate the package with the test prompts and publish only after unresolved assumptions are closed.

## Connected Agents

- Connected agent: `[ConnectedAgent: ...]`
- Why it exists: `<text>`
- Setup notes: `<same environment, publish, allow connections, other constraints>`

## Child Agents

- Child agent: `[ChildAgent: ...]`
- Why it exists: `<text>`
- Setup notes: `<how it is embedded and when it is called>`

## Validation Prompts

- Prompt 1: `<text>`
- Prompt 2: `<text>`
- Prompt 3: `<text>`

## Known Assumptions

- Assumption 1: `<text>`
- Assumption 2: `<text>`
