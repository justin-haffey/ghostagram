---
title: "EPIC-002 FEATURE-006 Ghostagram Target-to-Plan Reconciliation"
artifact_type: "architecture_reconciliation"
id: "RECONCILIATION-EPIC-002-FEATURE-006-GHOSTAGRAM"
status: "Accepted"
authority: "solution"
scope: "GHOSTAGRAM-F006-PROJECTION-AND-METADATA"
parent: "ARCH-SOLUTION-GHOSTAGRAM"
upstream:
  repository: "ghostworx"
  artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-006"
  path: ".swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md"
traceability:
  epic:
    repository: "ghostworx"
    artifact_id: "EPIC-002"
    path: ".swe/epics/002-declarative-composition-model/EPIC.md"
    revision: "3"
  feature:
    repository: "ghostworx"
    artifact_id: "FEATURE-006"
    path: ".swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/FEATURE.md"
    revision: "1"
  solution_target:
    repository: "ghostagram"
    artifact_id: "ARCH-SOLUTION-GHOSTAGRAM"
    path: "architecture/SOLUTION-ARCHITECTURE.md"
    revision: "3"
  bridge_package:
    repository: "ghostagram"
    artifact_id: "ARCH-PACKAGE-GHOSTAGRAM-BRIDGE"
    path: "architecture/packages/Ghostagram.Bridge/PACKAGE-ARCHITECTURE.md"
    revision: "3"
  projection_module:
    repository: "ghostagram"
    artifact_id: "ARCH-MODULE-GHOSTAGRAM-BRIDGE-DECLARATIVE-COMPOSITION-PROJECTION"
    path: "architecture/packages/Ghostagram.Bridge/modules/DeclarativeCompositionProjection/MODULE-ARCHITECTURE.md"
    revision: "2"
owners:
  - "solution-architect (/root/ghostagram_f006_reconciliation)"
created: "2026-09-09"
updated: "2026-09-09"
template_version: "3.0.0"
---

# EPIC-002 FEATURE-006 Ghostagram Target-to-Plan Reconciliation

## Decision Scope

This record compares the Accepted [`GHOSTAGRAM-F006-PROJECTION-AND-METADATA` allocation](../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md#ghostagram) with the independently Accepted [`ARCH-SOLUTION-GHOSTAGRAM` revision 3](../SOLUTION-ARCHITECTURE.md), [`ARCH-PACKAGE-GHOSTAGRAM-BRIDGE` revision 3](../packages/Ghostagram.Bridge/PACKAGE-ARCHITECTURE.md), and [`ARCH-MODULE-GHOSTAGRAM-BRIDGE-DECLARATIVE-COMPOSITION-PROJECTION` revision 2](../packages/Ghostagram.Bridge/modules/DeclarativeCompositionProjection/MODULE-ARCHITECTURE.md).

The result is exact correspondence with no semantic Target change. The Plan coordinates evidence through existing Ghostagram seams; it does not add a public API, Package, Module, semantic authority, persistence model, trust boundary, runtime, deployment unit, or migration. The accepted Target files remain unchanged. This reconciliation must receive independent acceptance before local Design preparation.

Profile: Compact. The assignment is a bounded proof allocation over existing seams, and no new critical architecture concern requires a successor Target or Detailed profile.

## Frozen Inputs

| Input | Exact locator | Current file SHA-256 or accepted decision fingerprint |
|---|---|---|
| Epic revision 3 | `ghostworx / EPIC-002 / .swe/epics/002-declarative-composition-model/EPIC.md / revision 3` | current `0EAFD63A84364168CC7C044645AD7479972DF574434E5CEE8F8545F7F4AB56F2` |
| Composition contract revision 3 | `ghostworx / CONTRACT-DECLARATIVE-COMPONENT-COMPOSITION / architecture/contracts/DECLARATIVE-COMPONENT-COMPOSITION.md / revision 3` | current `D53D242744421CA6154A7F26F2D59F2D4FCA532D34E622BED844CE1DB42B3479` |
| Accepted Feature revision 1 | `ghostworx / FEATURE-006 / .swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/FEATURE.md / revision 1` | current `F63361B1A7BECAA27DB733439BC9B76595B4D5ECEA5EB1B557B32F6E30484D33`; accepted cycle-0 decision bytes `58155B59BFC25CDE3A98A2BE66EB24A28BACF5CC239330C83158E53E36F7053C` |
| Accepted Plan | `ghostworx / IMPL-PLAN-EPIC-002-FEATURE-006 / .swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md` | current `C8D1C9B9E7B34BC3D4EA19796C7701F26C56E1F76D666D531835FC4EE8381139`; accepted cycle-2 decision bytes `78A357E6680D3B38207C58621F374B3CAF860A5FB74F805E2F7C1E065AE28CA2` |
| Accepted Solution Target revision 3 | `ghostagram / ARCH-SOLUTION-GHOSTAGRAM / architecture/SOLUTION-ARCHITECTURE.md / revision 3` | current `9338C7DA545620F289A9407F47252BD7D6A754DCA87566182E9143DDCC2CEAF1`; reviewed revision-3 decision bytes `6EDCDE8945E63D6C16821C7007BAD72AF98A66DC1C84903F6C528BBDEDD64D8B` |
| Accepted Bridge Package revision 3 | `ghostagram / ARCH-PACKAGE-GHOSTAGRAM-BRIDGE / architecture/packages/Ghostagram.Bridge/PACKAGE-ARCHITECTURE.md / revision 3` | current `B126E5A336EEF7D4CE4E22201E04A0C35CECBF363EBD82D5A6685E54414C56AB` |
| Accepted projection Module revision 2 | `ghostagram / ARCH-MODULE-GHOSTAGRAM-BRIDGE-DECLARATIVE-COMPOSITION-PROJECTION / architecture/packages/Ghostagram.Bridge/modules/DeclarativeCompositionProjection/MODULE-ARCHITECTURE.md / revision 2` | current `892E80B5EFCE3C15EAD1F9233B4F044743EE023F4E2A99645CB84A3B77EEEF39` |

Reachable governing links: [EPIC-002 revision 3](../../../../.swe/epics/002-declarative-composition-model/EPIC.md#revision-3-accepted--review-follow-up-and-delivery-authorization--2026-09-09), [FEATURE-006](../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/FEATURE.md), [Implementation Plan](../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md), and [composition contract revision 3](../../../../architecture/contracts/DECLARATIVE-COMPONENT-COMPOSITION.md#revision-3-bounded-canonical-machinery-and-reproducible-conformance).

## Exact Plan Correspondence

| Criterion | Existing Target correspondence | Required proof gate |
|---|---|---|
| `AC-001` | Revision 3 already requires attributable exact corpus/profile/schema/source revision, environment, intervals, outputs, and missing/failed/unsupported observations through the real Ghostagram seam. | `F006-GEN-INVENTORY-REPEAT` adds the Accepted Plan's filtered authored inventory/archive, starting HEAD and dirty-byte capture, pre/post additions/deletions, exact argument arrays, tool/SDK/dependency/runtime identity, outputs, and changed-during-run facts. |
| `AC-002` | Revision 3 already requires deterministic results for identical source revision, corpus, profile, and environment. | A second run from the same frozen closure must match every declared stable payload identity and digest. Raw run IDs, timestamps, and other run-scoped fields remain retained and explicitly classified; every unclassified difference fails reproduction. |
| `AC-006` | The accepted Bridge Module already owns read-only admitted Composition projection with identity, hierarchy, public visibility, provenance, Content Identity, opaque extensions, diagnostics, and `Fresh | Stale | Unsupported | Skewed | Failed` outcomes. Existing layout/export/browser and GraphWorkspace command/persistence behavior are neighboring Solution seams. | First run [materialization](../../.swe/implementations/EPIC-002/FEATURE-003/checks/final16-17case-20260908-r1/materialize/85378cdb-69ee-452c-bf88-d484e727482b/request.json), then [replay](../../.swe/implementations/EPIC-002/FEATURE-003/checks/final16-17case-20260908-r1/replay/1d161bf0-77a5-407c-8d34-bf4cf2329c16/request.json) using the fresh manifest/digest, fresh output, and a new 40-hex source revision. Only the final admitted envelope feeds the [nested preview/export command context](../../.swe/implementations/EPIC-002/FEATURE-003/EVIDENCE.md) recorded at line 165. Separately run the [GraphWorkspace metadata/geometry command context](../../.swe/implementations/EPIC-002/FEATURE-004/checks/corrective-134751-opaque-workspace-01/commands.json). |
| `AC-007` | Revision 3 keeps Evidence, independent local Validation, documentation truth, portfolio acceptance, and Target promotion as later gates. | Return `.swe/implementations/EPIC-002/FEATURE-006/{DESIGN.md,EVIDENCE.md,VALIDATION.md}`, the common-generation receipt, README/status reconciliation, and the independent portfolio decision. |

The proof order is materialize, replay, and System admission, followed by visible nested preview, SVG export, status, provenance, browser observation, and build attribution. Materialization alone does not prove admission; a historical final16/final17 output cannot satisfy F006; a failed or partial replay cannot feed preview proof.

The metadata/history/geometry observation is independent from preview. “Opaque” means imported unknown metadata codec and payload meaning, including the `future.codec` predecessor and its extension data; it does not mean visual transparency. The real diagram command replaces that value, capture and persisted history retain exact old/new meaning, save/reload returns the replacement, and the 107px fixture height is the minimum required for its two visible property rows.

## Current Baseline and Design Boundary

The current [`GraphWorkspaceService.Create`](../../src/Ghostagram.Server/GraphWorkspaces/GraphWorkspaceService.cs) names `features: null` at line 91, while the consumed System [`GraphStore`](../../../ghostworx-system/src/Ghostworx.System.Graph.Runtime/GraphStore.cs) constructor names its second parameter `extensions` at line 161. This is a routine named-argument compatibility repair inside the existing GraphWorkspace seam under a future Accepted Design. Replacing the argument name adds no API, authority, Package, Module, or semantic behavior.

A `--no-build` runtime after a failed current-source build executes stale binaries. Its result does not prove the current source repaired and does not establish a separate source defect. F006 evidence must build the scoped current source successfully before executing the metadata/geometry suite.

No source or test change is authorized here. Literal paths, argument values, output directories, and the compatibility edit remain Design-owned.

## Dependencies, Failure, and Change Rules

- `GHOSTAGRAM-F006-PROJECTION-AND-METADATA` is Major and non-deferrable.
- Design preparation requires Accepted Feature, Accepted Plan, and independent acceptance of this exact reconciliation record.
- Implementation and the final common generation additionally require independent Accepted F005 local Validation satisfying the `GWX-SYSTEM-F005-CANONICAL-BOUNDS` `ValidatedBehavior` dependency. Preparation cannot consume or claim that behavior early.
- Missing F005 Validation, source drift, digest mismatch, unclassified reproduced difference, failed admission, missing visible observation, failed current-source build, or incomplete replacement/history/save/reload proof blocks the affected criterion. Failed receipts remain durable.
- A new API, Package/Module, semantic adapter, authority, runtime, deployment behavior, or change to revision-3 meaning returns to architecture review. Ordinary repair inside the named existing seam proceeds only under an Accepted Design.
- All builds, tests, static checks, and visible browser validation run through `$swe-test` using the actual `test-runner` on `gpt-5.6-luna` at `medium`; implementation authors do not approve their own Evidence.

## Reconciliation Result

`GHOSTAGRAM-F006-PROJECTION-AND-METADATA` maps exactly to accepted Solution Target revision 3 and its accepted lower architecture. No Target semantic edit or successor is required. The Accepted Feature/Plan clarify the Epic and contract shorthand “107px opacity” as two distinct proof obligations: unknown opaque metadata replacement/history/persistence/reload, and 107px two-row minimum geometry. This is a bounded terminology and proof-allocation clarification, not a contract or Target divergence.

## Review Packet

- Mode: explicit developer-authorized `auto-approve`; force is not authorized.
- Author: solution-architect (`/root/ghostagram_f006_reconciliation`).
- Independent approver: architecture-reviewer assigned by the root coordinator.
- Decision candidate: exact correspondence; no semantic Target change.
- Review cycle: 0; reconciliation repair cycles consumed: 0. The upstream Plan was Accepted at cycle 2 with both permitted Plan repair cycles consumed and cannot be semantically repaired again without human disposition.
- Decision bytes: freeze this complete Draft once; approval metadata is excluded from its own circular decision fingerprint and may be appended only after exact fingerprint correspondence and root-routed static validation.

## Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | solution-architect (`/root/ghostagram_f006_reconciliation`) |
| Approver | architecture-reviewer (`/root/f005_f006_gate_reviewer`) |
| Decision | Accepted; cycle 0 |
| Recorded | `2026-09-09T20:56:28Z` |
| Evidence | Frozen candidate SHA-256 `977DF79956737345DD9675832EC2419D3E814D9871EFB9B56E8528578916507E`; bound Luna receipt `.swe/implementations/EPIC-002/FEATURE-006/checks/target-plan-reconciliation/e2640389-884e-4618-a916-6da74813ded1/receipt.json` (4 cases, 31 checks, no reported drift or missing coverage). |
| Bypass reason | None; force is not authorized. |

## Independent Review Decision

**Accepted, cycle 0.** The allocation corresponds to the unchanged accepted revision-3 Solution and Bridge Package plus revision-2 projection Module. It assigns evidence to existing projection, workspace, persistence, layout, export, and browser seams without creating semantic or runtime authority in Ghostagram.

The proof sequence correctly requires fresh materialization, replay, System admission, and only then consumer preview/export observations. It keeps opaque metadata replacement/history/save/reload separate from the 107px two-row geometry assertion, rejects stale binaries and historical envelopes as current proof, and preserves the independently Accepted F005 `ValidatedBehavior` gate for implementation and final generation.

The upstream `revision` field is optional under the governing locator contract. Exact stable IDs, paths, frozen hashes, and accepted decision fingerprints provide sufficient correspondence here; absence of that optional field is not a finding.

| Field | Value |
|---|---|
| Reviewer | architecture-reviewer (`/root/f005_f006_gate_reviewer`) |
| Independence | The reviewer did not author, materially repair, or implement this reconciliation or its Targets. |
| Repair cycles consumed | 0 |
| Unresolved findings | None |
