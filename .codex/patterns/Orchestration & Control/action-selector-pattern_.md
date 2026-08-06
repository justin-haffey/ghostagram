---

url: "https://agentic-patterns.com/patterns/action-selector-pattern/"
title: "Action-Selector Pattern - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/action-selector-pattern/#problem)

# Action-Selector Pattern

## Problem

Untrusted input can hijack an agent's reasoning once tool feedback re-enters the context window, leading to arbitrary, harmful actions.

## Solution

Treat the LLM as an **"instruction decoder" only**:

- Map the user's natural-language request to a _pre-approved_ action (or action template).
- **No tool outputs are fed back** into the LLM.
- The agent therefore cannot be influenced after selecting the action.

```pseudo
action = LLM.translate(prompt, allowlist)
execute(action)
# tool output NOT returned to LLM

```

## How to use it

Provide a hard allowlist of safe actions (API calls, SQL templates, page links).
Useful for customer-service bots, notification routers, kiosk interfaces.

## Trade-offs

- **Pros:** Near-immunity to prompt injection; trivial to audit.
- **Cons:** Limited flexibility; new capabilities require code updates.

## References

- Beurer-Kellner et al., §3.1 (1) Action-Selector.

**Source:** [https://arxiv.org/abs/2506.08837](https://arxiv.org/abs/2506.08837)