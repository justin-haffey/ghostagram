---
title: "[SOLUTION_NAME] Context"
artifact_type: "context-vocabulary"
id: "CONTEXT-[SOLUTION_ID]"
status: "draft"
authority: "solution"
scope: "[SOLUTION_ID]"
parent: "[PORTFOLIO_CONTEXT_MAP_ID_OR_NONE]"
upstream:
  repository: "[PORTFOLIO_REPOSITORY_ID_OR_URL]"
  artifact_id: "[PORTFOLIO_CONTEXT_MAP_ID_OR_NONE]"
  path: "[PORTFOLIO_CONTEXT_MAP_PATH_OR_NONE]"
  revision: "[OPTIONAL_COMMIT_OR_TAG]"
owners:
  - "[CONTEXT_OWNER]"
created: "[YYYY-MM-DD]"
updated: "[YYYY-MM-DD]"
template_version: "2.0.0"
---

# [SOLUTION_NAME] Context

[ONE_OR_TWO_SENTENCES_DESCRIBING_THE_SOLUTION_DOMAIN_AND_CONTEXT_BOUNDARY]

## Language

**[PREFERRED_TERM]**: [ONE_OR_TWO_SENTENCE_DOMAIN_DEFINITION]
_Avoid_: [AMBIGUOUS_OR_DEPRECATED_SYNONYMS]

## Relationships

- **[TERM_OR_CONTEXT] -> [TERM_OR_CONTEXT]**: [DOMAIN_RELATIONSHIP_OR_CONTRACT]

## Usage Rules

- Prefer the defined term in architecture, Design, code, tests, and user-facing language within this Solution.
- Include only domain-specific concepts; omit general programming vocabulary.
- Keep definitions concise and name ambiguous or deprecated alternatives under `_Avoid_`.
- This root `CONTEXT.md` is the single-context state. When the Solution contains multiple bounded contexts, move this vocabulary to `.swe/context/[SOLUTION_NAME]-CONTEXT.md` without changing its stable ID, replace the root file with a distinct `CONTEXT-MAP.md`, and link every context vocabulary from that map.
- In the multi-context state, each `.swe/context/[BOUNDED_CONTEXT]-CONTEXT.md` names the root context-map ID as its parent. Do not keep root `CONTEXT.md` beside `CONTEXT-MAP.md`; the two root forms are mutually exclusive.
