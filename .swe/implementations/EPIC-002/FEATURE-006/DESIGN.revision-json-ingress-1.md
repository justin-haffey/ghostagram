---
title: "Ghostagram bounded JSON metadata ingress"
artifact_type: "design"
id: "DESIGN-GHOSTAGRAM-F006-JSON-INGRESS-1"
status: "Accepted"
authority: "solution"
scope: "GHOSTAGRAM-F006-PROJECTION-AND-METADATA"
parent: "DESIGN-GHOSTAGRAM-F006"
upstream: {repository: "ghostagram", artifact_id: "DESIGN-GHOSTAGRAM-F006", path: ".swe/implementations/EPIC-002/FEATURE-006/DESIGN.md", revision: "F006-current-1"}
traceability:
  feature: {repository: "ghostworx", artifact_id: "FEATURE-006", path: ".swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/FEATURE.md", revision: "1"}
  implementation_plan: {repository: "ghostworx", artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-006", path: ".swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md"}
owners: ["/root/system_f006_design"]
revision: "json-ingress-1"
created: "2026-09-10"
updated: "2026-09-10"
template_version: "3.0.0"
---
## Pending technical amendment F006-json-ingress-1

Status: Accepted; independent technical decision: Accepted. Author `/root/system_f006_design`, 2026-09-10. The [Accepted original Design](DESIGN.md) describes the accepted predecessor; it does not approve this separate amendment. Preserve cycle-0 acceptance, current-route amendment and all review history; this is no reset of consumed cycles. No implementation is authorized until the independent decision is recorded. This compact amendment changes only the current metadata input conversion needed by the existing AC-006 obligation.

### Trigger and exact scope

The frozen workspace test reaches `GraphWorkspaceService.TryApply` for the future-property replacement but receives `INVALID_PROPOSAL`, `Property 'future' is not admitted: unsupported-semantic-value`. The [scratch diagnostic receipt](../../../../../../.swe/checks/f006-ghost-diag-r1/results/55d7deb3-790c-4c62-b3b5-43f46dcb5bc5/receipt.json) remains Blocked as an official harness result; its native observation supplies the rejection cause, not acceptance. `CommandAdapter.ToClr` currently clones a JsonElement for a property declared Json; `ApplyNode` passes that value into System's normalizer, whose supported inputs do not include JsonElement. The rejected edit is a required current local workspace behavior. The prior current-only disposition does not retire it.

Upon independent acceptance, supersede only the Change Map/implementation exclusions that prevent correcting `src/Ghostagram.Bridge/CommandAdapter.cs` and adding essential regressions in `tests/Ghostagram.Server.GraphWorkspace.Tests/Program.cs`. Existing metadata replacement/capture/save/history/reload, projection/browser/geometry and AC-001/002/006/007 requirements remain unchanged. System code, canonical formats, profiles, operation limits, opaque admission and definition/IR semantics are outside this repair. No dependency or public API change is proposed.

### Minimal conversion and bounds

At the existing declared-Json branch, convert the JSON value to supported ordinary CLR metadata: null, Boolean, String, Number using Int32 then Int64 then Decimal then finite Double, arrays of ordinary values, and ordinal string-key dictionaries. A string remains a string; do not infer Guid/date from JSON content. Objects with `$opaque` or `$bytes` names remain ordinary objects; do not invoke local serialization codecs that interpret those marker names. Undefined/non-finite values fail the existing proposal flow. Reject duplicate or blank object keys instead of silently overwriting them.

Use `GraphLegacyMetadataPolicy.Default.Limits` for conversion admission; do not create larger bridge budgets. Before materializing a complete intermediate tree, perform a bounded walk with root depth1, one item charge per array element/object entry, key UTF-8 bytes, scalar UTF-8 bytes and aggregate UTF-8 accounting corresponding to System's ordinary CLR normalization. Stop at the first excess; use the named MaxSemanticValueDepth, MaxSemanticValueItems, MaxMetadataKeyUtf8Bytes, MaxScalarValueUtf8Bytes and MaxSemanticValueUtf8Bytes values. Numeric CLR accounting remains the existing System policy. Bound depth before recursive descent and count before appending; preflight strings/keys with their decoded UTF-8 size. The input JsonElement already exists at the operation boundary; this repair must not add an unbounded clone/serialization round trip before checking it.

Keep System `GraphSemanticValueNormalizer.Normalize(..., Default.Limits)` and default Reject extension policy as final admission authority. The same `ToClr` branch serves creation and replacement; neither may insert JsonElement into runtime metadata. Existing transaction/revision rejection behavior stays authoritative. Small private conversion/preflight helpers in CommandAdapter are sufficient; no general converter framework or new production owner.

### Required fresh proof and freeze

Extend the existing real workspace test to show: the previously failing object replacement is accepted; authoritative capture contains changed=true; save/reload preserves the replacement and unrelated imported extension/history facts. Add compact parameterized inputs exercising nested objects/arrays, null/Boolean/string, numeric precedence and finite fallback, including marker-like object names remaining ordinary JSON. For hostile input, exercise duplicate keys, undefined where representable, and the exact/next depth, aggregate item, key/scalar/aggregate-byte bounds using System's named limits. Assert rejection preserves graph/diagram revision and authoritative metadata; do not infer that from an exception alone. Existing descriptor/type checks and scalar property behavior must continue to pass.

All execution belongs to Luna through root's official lease. Preserve original frozen failure and scratch diagnostic; obtain a new reviewed source generation for this repair, rebuild only affected Ghost dependencies/tests, and rerun affected metadata and consumer/browser proof on the coordinated successor snapshot. System's unchanged accepted generation and native receipts need not be rerun solely for this Ghost adapter repair. No current metadata, resource, security or reproducibility waiver is introduced. Independent validation decides adequacy and acceptance.

### Amendment decision

Pending independent review by the root-assigned reviewer. No self-approval, source change, passing test or delivery acceptance is claimed by this authoring record.

Independent decision reconciliation: [GHOSTAGRAM-JSON-INGRESS-TECHNICAL-REVIEW](reviews/GHOSTAGRAM-JSON-INGRESS-TECHNICAL-REVIEW.md) Accepted the frozen amendment SHA-256 `5F936DF841A95E755F4D7F7CC4FFF9551EFC36A85AA64A08E8C06C8C1EE90663` and parent `D744F4545CA745133B40F8149C6B2FA16BBE07BCD4A1149EC713D6F65D024EC3`. Review SHA-256 `B3524A51E2B6A44432EDFE3218E2025F0B08AA1C04C3F78343CE819290FCFD4C`. This derived state follows the actual independent decision; preceding Pending wording describes the submitted packet, not a new pending gate. No executable or delivery acceptance is inferred.
