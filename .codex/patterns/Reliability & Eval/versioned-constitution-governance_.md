---
url: "https://agentic-patterns.com/patterns/versioned-constitution-governance/"
title: "Versioned Constitution Governance - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/versioned-constitution-governance/#problem)

# Versioned Constitution Governance

## Problem

When an agent rewrites its own "constitution," it may accidentally violate safety or regress on alignment objectives if changes aren't reviewed.

## Solution

Store the constitution in a **version-controlled, signed repository**:

- YAML/TOML rules live in Git.
- Each commit is signed (e.g., Sigstore); CI runs automated policy checks.
- Only commits signed by approved reviewers or automated tests are merged.
- The agent can _propose_ changes, but a gatekeeper merges them.

## How to use it

- Require `git commit -S` or similar.
- Run diff-based linting to flag deletions of critical rules.
- Expose constitution `HEAD` as read-only context in every agent episode.

## References

- Hiveism, _Self-Alignment by Constitutional AI_
- Anthropic, _Constitutional AI_ white-paper

**Source:** [https://substack.com/home/post/p-161422949?utm\_campaign=post&utm\_medium=web](https://substack.com/home/post/p-161422949?utm_campaign=post&utm_medium=web)