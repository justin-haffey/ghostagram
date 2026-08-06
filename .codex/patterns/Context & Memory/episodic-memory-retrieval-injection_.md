---
url: "https://agentic-patterns.com/patterns/episodic-memory-retrieval-injection/"
title: "Episodic Memory Retrieval & Injection - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/episodic-memory-retrieval-injection/#problem)

# Episodic Memory Retrieval & Injection

## Problem

Stateless calls make agents forget prior decisions, causing repetition and shallow reasoning.

## Solution

Add a **vector-backed episodic memory store**:

1. After every episode, write a short "memory blob" (event, outcome, rationale) to the DB.
2. On new tasks, embed the prompt, retrieve top-k similar memories, and inject as _hints_ in the context.
3. Apply TTL or decay scoring to prune stale memories.

## Trade-offs

**Pros:** richer continuity, fewer repeated mistakes.

**Cons:** retrieval noise if memories aren't curated; storage cost.

## References

- Cursor "10x-MCP" persistent memory layer
- Windsurf Memories docs

**Source:** [https://forum.cursor.com/t/agentic-memory-management-for-cursor/78021](https://forum.cursor.com/t/agentic-memory-management-for-cursor/78021)