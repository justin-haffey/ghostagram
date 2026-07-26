---
name: copilot-manual-configuration
description: Write or refine the manual setup guide for a Copilot Studio package. Use when Codex needs to turn a Copilot design into a Maker-facing configuration checklist, setup order, prerequisite list, validation prompts, or connected-agent setup guidance.
---

# Purpose

Produce a practical manual configuration guide that a Maker can follow in Copilot Studio.

## Workflow

1. Start from the approved package design.
2. Fill `manual-configuration.md` with prerequisites, setup order, connected-agent notes, child-agent notes, and validation prompts.
3. Keep the steps aligned to actual Copilot Studio configuration surfaces.
4. Make assumptions and prerequisites explicit.
5. End with validation prompts and publish gates.

## Writing Rules

- Write for a human Maker, not another architect.
- Put setup order before rationale.
- Surface auth, environment, publish, and allow-connections prerequisites early.
- When a section is intentionally unresolved, mark it clearly instead of hiding it.

## References

- Read `references/setup-surfaces.md` when you need the standard setup sequence or prerequisite checklist.
