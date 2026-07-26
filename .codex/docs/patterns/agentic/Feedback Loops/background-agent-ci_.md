---
url: "https://agentic-patterns.com/patterns/background-agent-ci/"
title: "Background Agent with CI Feedback - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/background-agent-ci/#problem)

# Background Agent with CI Feedback

## Problem

Long-running tasks tie up the editor and require developers to babysit the agent.

## Solution

Run the agent **asynchronously**; it pushes a branch, waits for CI, ingests pass/fail output, iterates, and pings the user when green. Perfect for mobile kick-offs (“fix flaky test while I'm at soccer practice”).

## Example (flow)

FilesCIGitAgentDevFilesCIGitAgentDev"Upgrade to React 19"push branch react19-upgradetrigger tests12 failurespatch importsre-run✅ all greenPR ready

## References

- Raising An Agent - Episode 6: Background agents use existing CI as the feedback loop.

[Source](https://ampcode.com/manual#background)