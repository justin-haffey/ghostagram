---
name: copilot-architecture-playbook
description: Choose the right Copilot Studio architecture boundary for a proposed solution. Use when Codex needs to decide between a single agent, orchestrator plus connected agents, lightweight child agents, topics, prompts, and agent flows, or when it must document those choices in a Maker-ready package.
---

# Purpose

Make the smallest viable Copilot architecture decision and document it clearly.

## Workflow

1. Start from the user brief or role-analysis report.
2. Decide whether the request stays a single agent or becomes a multi-agent system.
3. Place each capability boundary in the right bucket:
   - orchestrator
   - connected agent
   - lightweight child agent
   - topic
   - prompt
   - agent flow
4. Prefer the simplest architecture that still keeps ownership and reasoning boundaries clear.
5. Record the decision in the package using the Copilot templates.

## Decision Rules

- Use a single agent when one well-bounded role can satisfy the request cleanly.
- Use an orchestrator when the request spans multiple specialist domains or needs result synthesis across agents.
- Use connected agents for standalone capabilities with their own domain boundary, lifecycle, or grounding boundary.
- Use lightweight child agents for embedded micro-specialties inside a parent agent.
- Use topics only when deterministic conversation control is worth the overhead.
- Use prompts inside topics when the topic needs structured model work.
- Use agent flows for deterministic, side-effectful, retry-sensitive, or approval-driven execution.
- Do not let an agent that contains connected agents also be reused as a connected agent elsewhere.

## References

- Read `references/decision-rules.md` when you need the rule matrix or Microsoft guidance summary.
