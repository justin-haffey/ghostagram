---
title: "Ghostagram bounded JSON metadata ingress technical review"
artifact_type: "design_review"
id: "REVIEW-DESIGN-GHOSTAGRAM-F006-JSON-INGRESS-1"
status: "Complete"
authority: "solution"
scope: "GHOSTAGRAM-F006-PROJECTION-AND-METADATA"
parent: "DESIGN-GHOSTAGRAM-F006-JSON-INGRESS-1"
upstream:
  repository: "ghostagram"
  artifact_id: "DESIGN-GHOSTAGRAM-F006-JSON-INGRESS-1"
  path: ".swe/implementations/EPIC-002/FEATURE-006/DESIGN.revision-json-ingress-1.md"
  revision: "json-ingress-1"
owners:
  - "architecture-reviewer (/root/f005_f006_postimplementation_analysis)"
created: "2026-09-10"
updated: "2026-09-10"
---

# Ghostagram bounded JSON metadata ingress technical review

## Decision

**Accepted.** The amendment corrects the existing Ghostagram command-boundary representation while retaining System as final semantic-value admission authority. It is consistent with the accepted AC-006 metadata replacement, capture, persistence, history, reload, and visible-consumer obligations. Implementation may proceed only within the amendment's exact Bridge and workspace-test scope. This is a Design decision; it is not implementation, executable verification, Evidence completion, local Validation, portfolio acceptance, architecture promotion, release, or deployment.

| Field | Value |
|---|---|
| Mode | `auto-approve` |
| Recorded | `2026-09-10T03:35:44Z` |
| Author | `/root/system_f006_design` |
| Reviewer | `architecture-reviewer (/root/f005_f006_postimplementation_analysis)` |
| Independence | The reviewer did not author, materially repair, implement, or execute the amendment. |
| Profile | Compact V1: a focused internal command-boundary conversion with explicit limits and persistence/rollback proof obligations; no public or cross-solution contract changes. |
| Frozen amendment | `DESIGN.revision-json-ingress-1.md`, SHA-256 `5F936DF841A95E755F4D7F7CC4FFF9551EFC36A85AA64A08E8C06C8C1EE90663` |
| Accepted parent | `DESIGN.md`, ID `DESIGN-GHOSTAGRAM-F006`, restored SHA-256 `D744F4545CA745133B40F8149C6B2FA16BBE07BCD4A1149EC713D6F65D024EC3` |
| Decision set | Declared-Json conversion to bounded ordinary CLR values; focused workspace regressions; successor proof and reuse treatment. |
| Repair-cycle treatment | New owner-authorized technical amendment. Cycle-0 acceptance, the accepted current-route disposition, and their recorded history remain unchanged. |
| Force/bypass | None |

## Technical assessment

The failure is located at a concrete representation seam. Current `CommandAdapter.ToClr` clones a declared-Json `JsonElement`; `ApplyNode` then asks System's `GraphSemanticValueNormalizer` to admit that CLR object. The normalizer supports ordinary scalar, dictionary, and enumerable CLR values but rejects `JsonElement`, matching the retained diagnostic `INVALID_PROPOSAL: Property 'future' is not admitted: unsupported-semantic-value.` The diagnostic receipt is correctly classified Blocked and cannot support delivery acceptance.

Converting the already parsed JSON token tree to ordinary CLR values at the Ghostagram command boundary is the smallest coherent fix. It does not interpret composition semantics or bypass System. The amendment keeps `GraphSemanticValueNormalizer.Normalize(..., GraphLegacyMetadataPolicy.Default.Limits)` with the default Reject extension policy as the final decision and applies the same conversion to node creation and replacement.

The conversion rules align with System's supported ordinary values and preserve JSON intent:

- null, Boolean, and String remain their corresponding ordinary values; declared-Json strings do not acquire Guid or date meaning.
- Number uses deterministic `Int32`, `Int64`, `Decimal`, then finite `Double` precedence, all types System already admits.
- arrays become bounded ordinary sequences, and objects become ordinal string-key dictionaries.
- blank or duplicate object keys fail instead of being normalized ambiguously or overwritten.
- `$opaque` and `$bytes` are ordinary keys. No local marker decoder or codec can manufacture opaque-extension authority.

The preflight uses the same named default depth, item, key-byte, scalar-byte, and aggregate-byte limits and root-depth/item-count conventions as System's ordinary CLR normalizer. It checks before recursive descent and before appending, and it avoids an unbounded clone or serialization round trip. System runs the authoritative normalization afterward, so any accounting mismatch fails closed. Existing transaction and expected-revision rejection preserve the graph and diagram state.

This amendment does not change System source, profiles, canonical formats, public APIs, opaque handling, composition semantics, or the accepted current-only compatibility disposition. It repairs a currently required local workspace behavior and therefore is not obsolete-fixture compatibility work.

## Required post-implementation evidence

Implementation does not turn the retained diagnostic into a pass. The smallest adequate successor proof is:

1. Include the exact Bridge/test edits in the new ordinary immutable four-repository closure and full inventories. Preserve the r4 closure, failed workspace run, scratch diagnostic receipt, and raw outputs unchanged.
2. Through `$swe-test`, run focused workspace regressions for accepted object replacement, authoritative `changed=true` capture, save/reload, unrelated imported extension/history retention, creation and replacement parity, nested arrays/objects, scalar kinds, numeric precedence, and marker-like keys remaining ordinary.
3. Verify exact and plus-one depth, item, key, scalar, and aggregate-byte limits plus duplicate keys, undefined where constructible, and non-finite fallback rejection. Every rejection must prove graph revision, diagram revision, authoritative metadata, and prior persisted state remain unchanged.
4. Execute the affected Ghostagram consumer and visible-browser/SVG/provenance proof in both independently extracted successor runs. The existing 361-case System producer and SDK obligations remain separate; this review does not claim them.
5. Reuse unchanged System or Server observations only after recorded complete relevant-input and binding equivalence. Cite original receipt IDs and generations as reused evidence, never as new successor execution. Any relevant source, test, fixture, command/option, configuration, tool/runtime, dependency-output, or shared-output change invalidates reuse and requires the affected check to run. Independent validators decide adequacy.

## Evidence inspected

- Frozen amendment and restored accepted parent identified above, plus the parent's preserved independent review history.
- Current Ghostagram `GraphDiagramCommandAdapter.ToClr` and `ApplyNode` source; `CommandAdapter.cs` observed SHA-256 `6A6A5ED555C02A6CB1BBC565C0E058743D53CBA2666392C69FFD8A4AC12FD62D`.
- Current System `GraphSemanticValueNormalizer` and `GraphLegacyMetadataPolicy.Default.Limits`; source observed SHA-256 `DE13117B61E50B3E6C4CA8A54FD6440C6235011B8D349CFAEE1A6287B7CCA7AF`.
- Blocked diagnostic receipt `55d7deb3-790c-4c62-b3b5-43f46dcb5bc5`, receipt SHA-256 `9E627CF3FB1CCA59EE98CF5960B7D5CB32E220DEDBD258B6F6DFC9E176A1D784`, and its `INVALID_PROPOSAL` observation.
- Accepted FEATURE-006, Implementation Plan, declarative-composition contract revision 3, Ghostagram Targets, and F006 Target reconciliation.

No tests, builds, lint/static checks, browser checks, repaired source, or repaired runtime output were reviewed or executed for this decision.
