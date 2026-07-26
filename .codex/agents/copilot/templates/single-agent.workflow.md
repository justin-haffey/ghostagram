# Single-Agent Workflow

## Purpose

Use this workflow when one Copilot Studio agent is the smallest viable architecture for the requested outcome.

## Inputs

- User brief
- Optional agent name
- Audience
- Domain
- Constraints

## Required Spawn Order

1. Spawn `copilot-role-analyst`.
2. Spawn `copilot-tool-expert` and `copilot-workflow-designer`.
3. Spawn `copilot-prompt-engineer`.
4. Spawn `adaptive-card-developer` only if a topic or prompt explicitly needs a card.
5. Spawn `copilot-package-writer`.
6. Validate in `copilot-orchestrator`.

## Step Details

### Step 1: Analyze

- Produce `.codex/agents/copilot/templates/role-analysis-report.md`.
- Confirm the smallest viable agent scope.
- Capture the role, audience, non-goals, candidate knowledge domains, and candidate orchestration choices.

### Step 2: Architecture

- Choose the minimum viable set of:
  - knowledge sources
  - tools
  - prompts
  - topics
  - flows
  - lightweight agents
- Prefer generative orchestration when explicit topics are unnecessary.
- Do not create topics merely to mirror every intent.

### Step 3: Author Instructions And Prompts

- Fill `.codex/agents/copilot/templates/instructions.md`.
- Fill `.codex/agents/copilot/templates/prompt-spec.md` when structured prompt assets are needed.
- Keep placeholder grammar consistent with the template library.

### Step 4: Card Authoring

- Only run when the approved design explicitly needs Adaptive Cards.
- Return host-compatible JSON and connect it back to the owning topic or prompt spec.

### Step 5: Package Assembly

- Fill:
  - `agent-definition.md`
  - `instructions.md`
  - `knowledge.md`
  - `tools.md`
  - `topics.md`
  - `manual-configuration.md`
- Keep assumptions explicit and unresolved sections marked as pending.

### Step 6: Validation

- Confirm there is no unsupported capability drift between files.
- Confirm the package can map to the future `src/agents/<agent-name>/...` structure.

## Quality Gates

- no duplicate or overlapping topics without a reason
- no tool without setup notes
- no instruction section that mentions a missing tool, topic, flow, or agent
- no multi-agent split unless the role analysis justified it
