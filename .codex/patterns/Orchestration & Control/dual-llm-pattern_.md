---
url: "https://agentic-patterns.com/patterns/dual-llm-pattern/"
title: "Dual LLM Pattern - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/dual-llm-pattern/#problem)

# Dual LLM Pattern

## Problem

A privileged agent that both sees untrusted text **and** wields tools can be coerced into dangerous calls.

## Solution

Split roles:

- **Privileged LLM:** Plans and calls tools but **never sees raw untrusted data**.
- **Quarantined LLM:** Reads untrusted data but **has zero tool access**.
- Pass data as **symbolic variables** or validated primitives; privileged side only manipulates references.

```pseudo
var1 = QuarantineLLM("extract email", text)  # returns $VAR1
PrivLLM.plan("send $VAR1 to boss")           # no raw text exposure
execute(plan, subst={ "$VAR1": var1 })

```

## How to use it

Email/calendar assistants, booking agents, API-powered chatbots.

## Trade-offs

- **Pros:** Clear trust boundary; compatible with static analysis.
- **Cons:** Complexity; debugging across two minds.

## References

- Willison, _Dual LLM Pattern_ (Apr 2023); adopted in Beurer-Kellner et al., §3.1 (4).

**Source:** [https://arxiv.org/abs/2506.08837](https://arxiv.org/abs/2506.08837)