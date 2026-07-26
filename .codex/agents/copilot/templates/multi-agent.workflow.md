# Multi-Agent Workflow

## Purpose

Use this workflow when one Copilot Studio agent would be too broad, too ambiguous, or too weakly bounded, and the solution needs an orchestrator plus specialist connected agents.

## Inputs

- User brief
- Optional system name
- Audience
- Domain
- Constraints

## Required Spawn Order

1. Spawn `copilot-role-analyst`.
2. Spawn `copilot-workflow-designer` to define the agent topology.
3. Spawn `copilot-tool-expert` for knowledge, tools, and prerequisites per agent.
4. Spawn `copilot-prompt-engineer` for each agent archetype that needs instructions or prompt specs.
5. Spawn `adaptive-card-developer` only where an approved topic or prompt requires a card.
6. Spawn `copilot-package-writer`.
7. Validate in `copilot-orchestrator`.

## Step Details

### Step 1: Decompose The System

- Decide on one top-level orchestrator.
- Define narrowly scoped connected agents for standalone domains.
- Define lightweight agents only for parent-local micro-specialties.
- Do not create connected agents for simple single-tool capabilities.

### Step 2: Define Boundaries

- For each standalone agent, document:
  - mission
  - audience
  - supported tasks
  - out-of-scope tasks
  - handoff criteria
  - required knowledge
  - required tools
- Do not allow an agent that contains connected agents to also be reused as a connected agent elsewhere.

### Step 3: Conversation And Execution Design

- Use topics only where deterministic conversation control is worth the overhead.
- Use prompts inside topics when structured model work is needed.
- Use agent flows for deterministic, side-effectful, retry-sensitive, or approval-driven paths.
- Keep child agents embedded in the parent design unless they are intentionally promoted.

### Step 4: Package Assembly

- Produce one package set per standalone agent.
- Use the top-level orchestrator package as the system root for topology and setup order.
- Document connected-agent prerequisites:
  - same environment
  - published
  - allow connections

### Step 5: Validation

- Confirm the topology is legible and minimally complex.
- Confirm every handoff has an explicit condition.
- Confirm every agent has a bounded role and success criteria.

## Quality Gates

- no connected agent without a clear standalone domain boundary
- no lightweight agent promoted to connected-agent status without justification
- no handoff without an explicit trigger
- no cross-agent capability duplication without a reason
