# Prompt And Placeholder Patterns

## Placeholder Grammar

- `[Topic: Name]`
- `[Prompt: Name]`
- `[Flow: Name]`
- `[Knowledge: Name]`
- `[ChildAgent: Name]`
- `[ConnectedAgent: Name]`

## Prompt Rules

- Keep the prompt narrow and purpose-built.
- Always state missing-input behavior.
- Always state failure or low-confidence behavior.
- Provide a structured output schema when the prompt returns data to another step.

## Instruction Rules

- Focus on role, boundaries, knowledge use, tool use, workflow behavior, coordination, style, and escalation.
- Never describe capabilities the package does not actually contain.
