---
url: "https://agentic-patterns.com/patterns/context-minimization-pattern/"
title: "Context-Minimization Pattern - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/context-minimization-pattern/#problem)

# Context-Minimization Pattern UPDATED

## Problem

User-supplied or tainted text lingers in the conversation, enabling it to influence later generations.

## Solution

**Purge or redact** untrusted segments once they've served their purpose:

- After transforming input into a safe intermediate (query, structured object), strip the original prompt from context.
- Subsequent reasoning sees **only trusted data**, eliminating latent injections.

```pseudo
sql = LLM("to SQL", user_prompt)
remove(user_prompt)              # tainted tokens gone
rows = db.query(sql)
answer = LLM("summarize rows", rows)

```

## How to use it

Customer-service chat, medical Q&A, any multi-turn flow where initial text shouldn't steer later steps.

## Trade-offs

- **Pros:** Simple; no extra models needed; helps prevent [context window anxiety](https://agentic-patterns.com/patterns/context-window-anxiety-management/) by reducing overall context usage.
- **Cons:** Later turns lose conversational nuance; may hurt UX; overly aggressive minimization can remove useful context.

## References

- Beurer-Kellner et al., §3.1 (6) Context-Minimization.

**Source:** [https://arxiv.org/abs/2506.08837](https://arxiv.org/abs/2506.08837)