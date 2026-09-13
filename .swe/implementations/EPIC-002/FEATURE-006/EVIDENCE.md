---
title: "Reproducible Composition Integration - Ghostagram Evidence"
artifact_type: "implementation_evidence"
id: "EVIDENCE-EPIC-002-FEATURE-006-GHOSTAGRAM"
status: "Draft"
authority: "solution"
scope: "GHOSTAGRAM-F006-PROJECTION-AND-METADATA"
parent: "DESIGN-GHOSTAGRAM-F006"
upstream:
  repository: "ghostagram"
  artifact_id: "DESIGN-GHOSTAGRAM-F006"
  path: ".swe/implementations/EPIC-002/FEATURE-006/DESIGN.md"
  revision: "F006-current-1"
traceability:
  epic: {repository: "ghostworx", artifact_id: "EPIC-002", path: ".swe/epics/002-declarative-composition-model/EPIC.md", revision: "3"}
  feature: {repository: "ghostworx", artifact_id: "FEATURE-006", path: ".swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/FEATURE.md", revision: "1"}
  implementation_plan: {repository: "ghostworx", artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-006", path: ".swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md"}
  design: {repository: "ghostagram", artifact_id: "DESIGN-GHOSTAGRAM-F006", path: ".swe/implementations/EPIC-002/FEATURE-006/DESIGN.md", revision: "F006-current-1"}
owners: ["Ghostagram implementer (/root/f006_ghostagram_readiness)"]
created: "2026-09-09"
updated: "2026-09-09"
template_version: "3.0.0"
---

# Implementation Evidence

The repository owner [accepted portfolio FEATURE-006 by explicit `-force` override](../../../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/VALIDATION.md) on 2026-09-13 and directed no additional run. The progress below preserves the local Evidence snapshot; pending criteria and missing independent local Validation were bypassed, not passed.

## Delivery Progress

```yaml
delivery_progress:
  implementation: InProgress
  verification: InProgress
  acceptance: Pending
  source_generation: current-child-20260913 (functional subset; no frozen common-generation acceptance)
  acceptance_locator: null
  pending_obligations:
    - {check_id: F006-GEN-INVENTORY-REPEAT, criteria: [AC-001, AC-002], reason: "Two isolated fresh generations and strict reproduction comparison pending", owner: "root coordinator and test-runner", due: "Before acceptance"}
    - {check_id: F006-GHOSTAGRAM-REPLAY-ADMISSION, criteria: [AC-006], reason: "One current-child consumer replay passed; separate reproduction and complete generation binding remain pending", owner: test-runner, due: "Before acceptance"}
    - {check_id: F006-GHOSTAGRAM-PREVIEW-BROWSER, criteria: [AC-006], reason: "One nested preview generated; remaining status/export and visible browser observations pending", owner: test-runner, due: "Before acceptance"}
    - {check_id: F006-GHOSTAGRAM-METADATA-GEOMETRY, criteria: [AC-006], reason: "Current-child native workspace checks passed; separate reproduction and generation binding pending", owner: test-runner, due: "Before acceptance"}
    - {check_id: F006-DOCS-VALIDATION, criteria: [AC-007], reason: "Final evidence, README reconciliation, static checks and independent Validation pending", owner: "implementer and independent validator", due: "Before acceptance"}
```

## Authority and scope

The accepted [Design](DESIGN.md), [Feature](../../../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/FEATURE.md) and [Implementation Plan](../../../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md) allocate AC-001, AC-002, AC-006 and AC-007. Root released implementation after actual Accepted [System F005 Validation](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-005/VALIDATION.md), ID VALIDATION-EPIC-002-FEATURE-005-GWX-SYSTEM, SHA-256 `4EAC28809B6300993091D560F6345749FC6F0E3B8AB97EB281B91518E02A9CA5`. That prerequisite does not validate this assignment.

Prototype Mode remains Off. Existing modified prototype STATE/journal and architecture review artifacts are preserved. No Git operation, dependency upgrade, deployment or compatibility adapter was performed. The historical metadata fixture remains because current unknown-value replacement, extension retention and history are explicit accepted behavior; obsolete System tooling corpus compatibility is not required.

## Implemented changes

- `src/Ghostagram.Server/GraphWorkspaces/GraphWorkspaceService.cs`: use the current GraphStore named argument `extensions: null` without changing behavior.
- `tests/Ghostagram.Server.GraphWorkspace.Tests/Program.cs`: explicitly observe authoritative capture before save, retain history `oldValue.value.x = 1`, exercise the descriptor's 106px rejection, and assert both visible String/Json rows remain at 107px in the captured node.
- [Current proof helper](checks/Invoke-GhostagramCurrentProof.ps1) and [browser/comparison observation contract](checks/BROWSER-AND-COMPARISON.md): prepare exact current native materialize/replay/preview steps and required observations. The Composition CLI source is unchanged; its optional published-source mode already permits direct current corpus admission without E0/S0.

## Verification and generation

The focused current-child functional run below uses source in the actual `repos/ghostagram` checkout and accepted System r4 generated data as input. It is a passing functional subset, not the Feature's two-run frozen generation. Common archive, complete pre/post inventories, separate reproduction, browser observations, and independent local Validation remain pending.

| Criterion | Required evidence | Current result |
|---|---|---|
| AC-001 | Common archive plus both extracted authored inventories and dependency/tool/output bindings | Pending |
| AC-002 | Strict stable payload comparison with every run-specific difference causally classified | Pending |
| AC-006 | System-admitted current replay; eight visible preview/export cases; metadata capture/history/persistence/reload and 107px geometry | Partial: one replay 176/176, one nested preview, and workspace native checks passed; browser and remaining cases pending |
| AC-007 | Complete attributable Evidence, README reconciliation and independent local Validation | Pending |

No Design or architecture divergence is proposed. Missing execution and browser observations remain obligations, not waived checks or accepted delivery.

## Focused current-child functional proof (2026-09-13)

- Ghostagram GraphWorkspace Release build and native executable passed in the real child checkout; the native-only wrapper receipt is `ghostworx/.swe/checks/epic002-f006-current-child-20260913/ghostagram-native-only/d82517d3-bbc2-447f-8ded-500154b199d4/receipt.json`. The native System serializer suite passed 34/34 after the owning System repairs; its aggregate wrapper remained Blocked, so no official passing System F006 revalidation is claimed.
- Materialization passed with 176 cases and manifest digest `cb2a83d28363fc756f9b96a8174e7d181130a08b0e4e8afaf3673e23fb7f8193`: `ghostworx/.swe/checks/epic002-f006-current-child-20260913/ghost-replay/materialize-r2/9a0620df-881d-461e-8fc2-02f5ec69e092/receipt.json`.
- Actual current-child composition replay passed 176/176 and produced an admitted consumer envelope: `ghostworx/.swe/checks/epic002-f006-current-child-20260913/ghost-replay/replay/36c092df-6854-4ef3-8af9-0dea65e6666a/receipt.json`. Replay used the earlier materialized manifest path, whose reported digest matches the separately retained fresh materialization above.
- `GRAM-NESTED-OPAQUE` was Fresh and its native preview produced `index.html`, `diagram.svg`, and `preview-provenance.json`: `ghostworx/.swe/checks/epic002-f006-current-child-20260913/ghost-replay/preview/5b63953e-a2b9-47c6-800d-2051965a3f5b/receipt.json`. Output: `results/current-child-20260913/ghost-replay/preview/GRAM-NESTED-OPAQUE/`.
- The initial materialize launch failure and earlier workspace failures remain in raw receipt history. This run did not perform a second reproduction, all eight preview statuses, or visible-browser inspection; AC-006 and local acceptance remain pending.
