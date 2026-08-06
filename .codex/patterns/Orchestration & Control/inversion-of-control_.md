---
url: "https://agentic-patterns.com/patterns/inversion-of-control/"
title: "Inversion of Control - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/inversion-of-control/#problem)

# Inversion of Control

## Problem

Traditional "prompt-as-puppeteer" workflows force humans to spell out every step, limiting scale and creativity.

## Solution

Give the agent **tools + a high-level goal** and let _it_ decide the orchestration.
Humans supply guard-rails (first 10 % + last 3 %) while the agent handles the middle 87 %.

## Example (flow)

ToolsRepoAgentDevToolsRepoAgentDev"Refactor UploadService to async"git grep "UploadService"edit\_file, run\_testsPR with green CI

## References

- Raising An Agent - Episode 1, "It's a big bird, it can catch its own food."

[Source](https://www.nibzard.com/ampcode)