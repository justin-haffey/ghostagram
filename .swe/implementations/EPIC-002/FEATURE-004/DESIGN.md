---
title: "Layered System Definition Foundation — Ghostagram Design"
artifact_type: "design"
id: "DESIGN-EPIC-002-FEATURE-004-GHOSTAGRAM"
status: "Accepted"
authority: "solution"
scope: "ghostagram"
parent: "IMPL-PLAN-EPIC-002-FEATURE-004"
upstream:
  repository: "ghostworx"
  artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-004"
  path: ".swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/IMPLEMENTATION-PLAN.md"
  revision: "1"
traceability:
  epic:
    repository: "ghostworx"
    artifact_id: "EPIC-002"
    path: ".swe/epics/002-declarative-composition-model/EPIC.md"
    revision: "2"
  feature:
    repository: "ghostworx"
    artifact_id: "FEATURE-004"
    path: ".swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/FEATURE.md"
    revision: "1"
  implementation_plan:
    repository: "ghostworx"
    artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-004"
    path: ".swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/IMPLEMENTATION-PLAN.md"
    revision: "1"
  system_design:
    repository: "ghostworx-system"
    artifact_id: "DESIGN-EPIC-002-FEATURE-004-GHOSTWORX-SYSTEM"
    path: ".swe/implementations/EPIC-002/FEATURE-004/DESIGN.md"
    revision: "3"
owners:
  - "ghostagram_bridge_implementation"
created: "2026-09-06"
updated: "2026-09-07"
revision: "2"
template_version: "2.0.0"
---

# Layered System Definition Foundation — Ghostagram Design

## Assignment and Revision Basis

[FEATURE-004 revision 1](../../../../../../.swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/FEATURE.md) and [IMPL-PLAN-EPIC-002-FEATURE-004 revision 1](../../../../../../.swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/IMPLEMENTATION-PLAN.md) allocate Ghostagram its portions of AC-007 and AC-009. This is the smallest as-built successor to [Accepted Design revision 1](DESIGN-REVISION-001.md), whose bytes and approval remain unchanged. Revision 2 is independently Accepted at 2026-09-07T10:27:51Z; this Design decision is separate from delivery Validation.

The user authorized implementation under prototype run `PROTOTYPE-RUN-20260907T094049Z`. [System F004 Design revision 3](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-004/DESIGN.md) and [Graph contract revision 3](../../../../../../architecture/contracts/SEMANTIC-GRAPH-AND-FEDERATION.md#revision-3-legacy-local-persistence-boundary) separate local structural candidates from admitted Graph records. [Solution Target revision 3](../../../../architecture/SOLUTION-ARCHITECTURE.md#epic-002-legacy-local-boundary-reconciliation) and [Bridge Package Target revision 3](../../../../architecture/packages/Ghostagram.Bridge/PACKAGE-ARCHITECTURE.md#epic-002-legacy-local-boundary-reconciliation) are independently Accepted at 2026-09-07T10:23:15Z, cycle 0 with no conditions; their lifecycle remains Target. Prototype deferral did not retrospectively accept these successors; the approval records document the subsequent independent decisions.

Accepted System Design revision 3 SHA-256: `563711439a2e5f15810d72b622bfb82cd8c0cc1f5f7a241ec7235180a1d69432`. This content anchor supplements the stable ID/path/revision locator.

## Implemented Ownership and Interfaces

| Surface | Direct implementation and owner |
|---|---|
| Existing Bridge projection, presentation reconciliation and deltas | Consume System `GraphLocalSnapshot` and `GraphLocalChangeBatch`; typed `GraphSemanticValue` converts explicitly to display JSON. Pure projection never invokes a live store. |
| Diagram compilation | `ProjectSnapshot(document).Input` captures an immutable `GraphCompilationInput` with local snapshot, detached node/property/presentation JSON, scalar port facts and projected edge joins. No cache or CLR presentation metadata crosses the System boundary. |
| Compiler and engine native paths | Separate overloads accept local snapshots, lossless input handles and governed `GraphSnapshot`. Governed input uses `GraphLocalInspection.FromSnapshot`; this is one-way local inspection and grants no local admission. Snapshot-only compilation consumes semantic facts only. |
| Runtime adapters | Graph command/store/algorithm types come from System Graph.Runtime. Bridge-created nodes inherit `GraphNode` with initial metadata and `register: false`; registration happens in the explicit transaction. System owns mutation-state capture and rollback. |
| Workspace persistence | Host uses `IGraphLocalDocumentCodec` (`GraphJsonSerializer`), `CaptureLocal` and `MaterializeLocal`; explicit strong-retention target and node factory remain host-owned. |
| Existing profiled Cutover tooling | Retains governed `Capture`/`Encode`/`Decode`/`Materialize` and exact schema-2 byte round-trip checks. It does not use local persistence to claim admission. |

System runtime algorithms consume local snapshots after the layer split. Bridge and Execution explicitly reference Graph.Serialization/Graph.Runtime. Existing Variable runtime facts use the runtime Variable serializer; canonical Definition/Reference types remain pure. No new Feature is introduced by this F004 migration. F003 source is separately attributable and is not accepted by this Design.

## Local Persistence Contract

`GraphLocalExchangeContext` contains origin authority and `GraphLocalOperationContext(GraphLocalLimits.PersistenceV1, deadline, cancellation)`. The host supplies a 30-second absolute deadline and the caller cancellation token. `GraphStoreOptions` explicitly uses strong retention, configured history capacity and the existing built-in authority.

`DecodeLocal` returns a local immutable document. `GraphStoreMaterializer.MaterializeLocal(document, new GraphLocalStoreMaterializationContext(exchange, target, nodeFactory))` populates an explicitly created empty strong-retention target atomically. `CaptureLocal(graph, new GraphLocalStoreExportContext(exchange, IncludeHistory: true, HistoryPolicy: AllowUnavailableSnapshotFallback))` retains complete available history; only unavailable complete history permits snapshot-only export. Other failures stay explicit. `EncodeLocal` preserves schema 1, custom kind identity, existing qualified tuples and opaque document/node extensions without minting profile/vocabulary authority.

Historical schema-1 DTO output includes null contract/profile/origin/address/extension-policy fields and empty vocabulary/reference arrays. The implementation preserves that shape. It neither upgrades ordinary workspace storage to schema 2 nor downgrades governed input to claim schema-1 preservation.

## Finite Local Policy and Failure Behavior

`GraphCompilationLimits.General` uses System `GraphLocalLimits`: 100,000 nodes, 250,000 relationships, 4 MiB input bytes, 100,000 history batches and 1,024 metadata entries/extensions. The diagram compiler enforces applicable node/relationship/metadata counts and validates all six values as positive and below `int.MaxValue`. Bytes, history and extensions are codec-only fields; no corresponding serialization-byte/history/extension limit is claimed for in-memory diagrams. Caller-supplied finite limits are supported. Direct snapshot and explicit-handle compilation enforce the receiving compiler's policy, so moving a handle between compiler instances cannot bypass a smaller receiver limit.

System `GraphLegacyMetadataPolicy.Default.Limits` retains typed metadata key 256 UTF-8 bytes, scalar 64 KiB, semantic value 256 KiB, depth 16 and collection-item 1,024 bounds. `GraphValueAdmission` validates typed values; Bridge's outward command adapter alone performs CLR normalization. Unsupported fingerprint kinds produce explicit diagnostics. Generic structured metadata retains the Bridge's prior omission/diagnostic behavior unless a descriptor declares JSON.

Duplicate/colliding identities, invalid values and exceeded local limits yield diagnostics with no partial snapshot, context or compilation. The explicit context detaches mutable source collections and clones returned node presentation, preserving inferred loop guards, property-sensitive fingerprints and port endpoints across independent compiler instances. Transactional host failure leaves prior graph/presentation state intact. No default/shared graph or unqualified-to-qualified vocabulary conversion is introduced.

## Observed Verification and Reversal

[Complete implementation evidence](EVIDENCE.md) records 18 ordered local Release builds, all seven ordinary suites, capacity/handle/persistence regressions and six existing conformance commands. The original execution baseline failed on a CLR `DiagramNode` semantic value; the current suite passes. Initial candidate failures and repairs remain recorded, including the Bridge rollback gap exposed by the new history-ring test.

Verification binds to Ghostagram [138-file source archive](checks/candidate-release/source.zip), source fingerprint `41a988f64d4c7ed22590ef1ddc51c131b85d4675b82f5db6ed45f43a59fb470d`, and System archived generation `f004-generation-20260907T100038Z`, source fingerprint `7d0b1b09c9300ee8305a204531fff730ed148f488097ee50c2bc59fdef135eb1`. All 15 frozen System owner DLLs and 72 copied System DLLs were checked. Newer unbuilt System F001 source is outside that tested generation. F003 foundation tests are separately identified and prove neither hierarchy projection nor consumer-envelope completion.

Reversal, if requested, must keep these directly updated callers coherent with their matching System generation and preserve unrelated work. No Git, release, deployment, dependency version change or compatibility shim is part of this Design.

## Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve with the user-named independent approver |
| Author | ghostagram_bridge_implementation |
| Approver | elon-musk (`/root/elon_bootstrap_diagnosis`), user-named independent approver |
| Decision | Accepted; initial cycle 0, no conditions |
| Recorded | 2026-09-07T10:27:51Z |
| Evidence | [Independent decision transcription](checks/candidate-release/design-review-decision.json); reviewed SHA-256 `2ee9b85021c7c7d850a346ab2fcbb345b3da772de774cb84557e9eae2dab08ae`. Design acceptance only; no delivery Validation. |
| Bypass reason | None; prototype deferral is recorded separately and does not constitute acceptance |
| Basis | Prototype source and execution evidence; original Accepted revision 1 preserved |
| Validation | Independent local Validation still required; implementation does not approve itself |
