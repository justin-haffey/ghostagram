# Prompt Spec

## Prompt Identity

- Prompt name: `[Prompt: Prompt Name]`
- Purpose: `<what this prompt does>`
- Trigger context: `<where it is used>`
- Owner topic or flow: `<topic or flow name>`

## Prompt Body

```text
<Insert the prompt body here.>
```

## Input Schema

```json
{
  "type": "object",
  "properties": {
    "exampleInput": {
      "type": "string",
      "description": "Replace with a real input."
    }
  },
  "required": ["exampleInput"]
}
```

## Output Schema

```json
{
  "type": "object",
  "properties": {
    "exampleOutput": {
      "type": "string",
      "description": "Replace with a real output."
    }
  },
  "required": ["exampleOutput"]
}
```

## Fallback Behavior

- On missing inputs: `<text>`
- On unsafe or unsupported requests: `<text>`
- On tool failure or low confidence: `<text>`

## Adaptive Card

- Required: `<Yes | No>`
- If yes: `<reference the card payload or binding notes>`
