---
url: "https://agentic-patterns.com/patterns/tool-capability-compartmentalization/"
title: "Tool Capability Compartmentalization - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/tool-capability-compartmentalization/#problem)

# Tool Capability Compartmentalization

## Problem

Model Context Protocol (MCP) encourages "mix-and-match" tools—often combining private-data readers, web fetchers, and writers in a single callable unit. This amplifies the lethality of prompt-injection chains.

## Solution

Adopt **capability compartmentalization** at the tool layer:

- Split monolithic tools into _reader_, _processor_, and _writer_ micro-tools.
- Require explicit, per-call user consent when composing tools across capability classes.
- Run each class in an isolated subprocess with scoped API keys and file permissions.

```yaml
# tool-manifest.yml
email_reader:
  capabilities: [private_data, untrusted_input]
  permissions:
    fs: read-only:/mail
    net: none

issue_creator:
  capabilities: [external_comm]
  permissions:
    net: allowlist:github.com

```

## How to use it

- Generate the manifest automatically from CI.
- Your agent runner consults the manifest before constructing action plans.
- Flag any attempt to chain tools that would recreate the lethal trifecta.

## Trade-offs

**Pros:** Fine-grained; plays well with modular architectures.
**Cons:** More tooling overhead; risk of permission creep over time.

## References

- Willison's warning that "one MCP mixed all three patterns in a single tool."

**Source:** [https://simonwillison.net/2025/Jun/16/lethal-trifecta/](https://simonwillison.net/2025/Jun/16/lethal-trifecta/)