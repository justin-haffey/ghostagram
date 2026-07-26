---
url: "https://agentic-patterns.com/patterns/explicit-posterior-sampling-planner/"
title: "Explicit Posterior-Sampling Planner - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/explicit-posterior-sampling-planner/#problem)

# Explicit Posterior-Sampling Planner

## Problem

Agents that rely on ad-hoc heuristics explore poorly, wasting tokens and API calls on dead ends.

## Solution

Embed a _fully specified_ RL algorithm—Posterior Sampling for Reinforcement Learning (PSRL)—inside the LLM's reasoning:

- Maintain a Bayesian posterior over task models.
- Sample a model, compute an optimal plan/policy, execute, observe reward, update posterior.
- Express each step in natural language so the core LLM can carry it out with tool calls.

## How to use it

Wrap the algorithm in a reusable prompt template or code skeleton the LLM can fill.

## References

- Arumugam & Griffiths, _Toward Efficient Exploration by LLM Agents_

**Source:** [https://arxiv.org/abs/2504.20997](https://arxiv.org/abs/2504.20997)