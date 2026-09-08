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
    revision: "2"
owners:
  - "consumer_planning"
created: "2026-09-06"
updated: "2026-09-06"
revision: "1"
template_version: "2.0.0"
---

# Layered System Definition Foundation — Ghostagram Design

## Assignment and Governing Inputs

The accepted [FEATURE-004](../../../../../../.swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/FEATURE.md) and [IMPL-PLAN-EPIC-002-FEATURE-004](../../../../../../.swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/IMPLEMENTATION-PLAN.md), both revision 1 under [EPIC-002 revision 2](../../../../../../.swe/epics/002-declarative-composition-model/EPIC.md), allocate this child only its portions of AC-007 and AC-009. The [Solution Target revision 2](../../../../architecture/SOLUTION-ARCHITECTURE.md#epic-002-layered-foundation-reconciliation) is independently Accepted; applicable Package/Module amendments retain that boundary.

The [System F004 Design revision 2](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-004/DESIGN.md#callable-operation-seams) is independently Accepted. Its exact portable interface/project map is reconciled here and freezes this consumer Design input. The System Design is the cross-repository implementation input, not a new semantic authority. No source changed during planning.

## Current State and Change Map

Bridge and Execution reference System Core. Existing `GraphCompiler` builds System snapshots whose metadata contains `DiagramNode`, `DiagramPort` collections and `ProjectedEdge` objects; Bridge metadata projection similarly accepts CLR values. Host persistence uses Graph Serialization. These are real existing boundaries affected by purification.

| Area or path | Direct change | Boundary |
|---|---|---|
| `src/Ghostagram.Bridge/Ghostagram.Bridge.csproj`, `Projection.cs`, `BridgeContracts.cs` | Consume typed portable Graph snapshots/changes and semantic values; update pure codec/reference uses | Presentation stays local; no arbitrary objects in System metadata |
| `src/Ghostagram.Execution/GraphCompiler.cs` and project | Build a typed local compilation context/sidecar keyed by NodeId/EdgeId; pass only pure snapshots into System algorithms and join results by identity | DiagramNode/ports/projected-edge objects remain Ghostagram-owned |
| Existing Bridge command adapters | Update direct runtime type references only where existing mutation behavior needs them | Outward adapter; pure projection paths cannot depend back on it |
| `src/Ghostagram.Server/` graph workspace/persistence callers and project | Decode immutable documents/snapshots through pure Graph.Serialization; materialize only through explicit Graph.Runtime owner | Host supplies authority, graph/profile, finite bounds and strong retention for portable documents |
| Existing Bridge, Execution, Cutover, Persistence, Server.GraphWorkspace and Graph.Conformance test projects | Update references/setup and targeted fixtures to typed semantic metadata and local sidecar | Existing behavior remains; no composition projection added |
| Ghostagram solution file | Update affected project entries if needed | No visual redesign or new service |

## Interfaces, Data and Behavior

Introduce a private typed compilation context in `Ghostagram.Execution` holding the portable `GraphSnapshot` plus read-only dictionaries of original diagram nodes, ports and projected edges. Snapshot metadata contains only genuine semantic values such as a typed logical identifier or boolean; diagram objects and presentation fields never cross the System boundary. Algorithm calls receive the snapshot; returned NodeId/EdgeId results resolve through those dictionaries. Preserve existing duplicate/collision diagnostics rather than overwriting sidecar entries.

Bridge converts `GraphSemanticValue` through the public typed codec/projection rules to presentation JSON as needed. It does not preserve legacy `object` normalization in pure paths. Existing presentation stores retain layout/waypoints/selection/viewport under their own revision, independent of the portable snapshot identity.

Existing host command/store behavior may reference System Graph.Runtime through outward adapters. Pure projection consumes snapshots/ports without a type/call dependency on live stores. Codec decode returns an immutable document/snapshot. Host materialization is an explicit following operation with authority, profile, finite bounds and strong retention; it cannot default to a shared graph. The exact System revision 2 seam is `IGraphDocumentCodec`/`GraphJsonSerializer` over typed `GraphDocument` plus `GraphExchangeContext`; the runtime owner is `GraphStoreDocumentMapper` and `GraphStoreMaterializer.Materialize(document, context)`. The runtime GraphStoreMaterializationContext carries the host-created empty strong-retention target, node factory and pure GraphExchangeContext; the materializer preflights before an atomic import. Snapshot algorithms remain in System Graph.Runtime under the System Design, but receive only portable snapshots; the typed local compilation context never enters their contracts.

FEATURE-004 preserves existing Graph/Variable/Link and local execution behavior. It adds no F003 composition projection, visual design, definition editing authority, live Server projection, control, route or runtime feature.

## Failure, Security and Operations

Typed metadata rejection, unknown identities, sidecar collisions, codec/profile skew, limits, cancellation and deadline failures stay explicit. Construct new snapshots/sidecars before replacing accepted local state; failure preserves prior source and presentation state and returns no accepted partial import. Legacy unsupported CLR presentation fields remain a local diagnostic or explicit sidecar field, never silently coerced into semantic identity. No new network, secret, database or deployment mechanism is introduced.

## Test and Evidence Plan

| Criterion | Planned verification | Evidence |
|---|---|---|
| AC-007 | Build changed Bridge/Execution/host projects against the same System checkout outputs. Run existing Bridge, Execution, Cutover, Persistence and Server.GraphWorkspace tests relevant to changed seams; compile Graph conformance tooling. Add focused checks for typed-only System metadata, sidecar identity joins, explicit retention and no new F003 behavior. | Local `EVIDENCE.md`: short affected-project/reference map, source revision and changed-file digest, commands/results and boundary findings |
| AC-009 | Run existing Graph/Variable/Semantic Link conformance regression tests; compare existing projection hierarchy, identities, graph algorithm results, sidecar round trips and explicit failures. Preserve existing wire/profile meaning; an intended semantic/persisted contract change returns upstream for successor review. | Same Evidence grouped by existing profile; independent local `VALIDATION.md` covers AC-007 and AC-009 |

F004 needs no bespoke conformance envelope, comprehensive API migration inventory, shims or compatibility window. System owns the other F004 criteria. F003 visible composition verification remains its later separate Design/delivery obligation.

## Exact Graph API and Source Baseline

The accepted System map supplies `IGraphDocumentCodec.Encode(GraphDocumentEncodeRequest)` -> `GraphOperationResult<ReadOnlyMemory<byte>>`, and `Decode`/`Migrate(GraphDocumentDecodeRequest)` -> `GraphOperationResult<GraphDocument>`. The requests are respectively `(GraphDocument Document, GraphExchangeContext Context)` and `(ReadOnlyMemory<byte> Utf8Json, GraphExchangeContext Context)`. Both requests/context are pure Graph.Serialization types. `GraphDocument` is immutable and admitted; raw JSON DTOs are internal. `GraphPortableDocumentMapper.ToDocument(snapshot, history, context)` and `ToSnapshot(document, context)` convert portable data without a live store. `GraphStoreDocumentMapper.Capture(source, runtimeExportContext)` and `GraphStoreMaterializer.Materialize(document, runtimeMaterializationContext)` are separate Graph.Runtime calls; runtime attachment policy, target store and factories never enter the pure codec context. Failure returns no accepted document/snapshot/store, and materialization leaves its explicit target unchanged.

Before source movement, record the live child build/test baseline with exact case outcomes, source commit and dirty-file hashes. Re-run after direct updates against the same System output fingerprint: required EPIC-001 and new F004 checks pass, no prior passing observation is lost, and no failure is hidden or recategorized. The System F004 revision 2 records its own live Composition baseline separately; historical 355/52 observations are not current evidence and cannot waive a child regression or prove F001 completion. Existing child failures remain explicit and block the applicable assigned pass requirement until resolved or separately governed.

## Rollout, Compatibility and Reversal

Implement the accepted Design in the coordinated development checkout after the accepted System F004 Design supplies its build outputs. Record the System and child source commit plus changed-file hashes when the checkout is dirty. Update known callers directly and run the affected builds/tests; source and binary breaks are intentional and permitted. No independent release/deprecation program is required.

Consumer work does not wait for F004 to accept itself. F004 portfolio delivery acceptance still requires System and all three child Evidence and independent local Validation. Revert only the scoped refactor changes together if needed, preserving unrelated work. No F001/F002/F003 source delivery is authorized by this Design.

## Risks and Divergence

The current source still uses the old physical package shape. The accepted System F004 revision 2 supplies the exact signature/project reconciliation; local independent approval remains required before source work. A change in System semantic meaning, admitted-record behavior or an actual wire/profile contract returns to its owner; reference relocation alone never justifies reinterpretation.

## Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | consumer_planning |
| Approver | elon-musk (`/root/elon_musk_approval`) |
| Decision | Accepted |
| Recorded | 2026-09-06T23:32:48+00:00 |
| Evidence | [Independent @elon-musk review](../../../../../../architecture/reviews/EPIC-002-LAYERED-PLANNING-REVIEW.md#sdk-ghostagram-f004-designs-revision-1). Accepted System F004 Design revision 2 API map and ownership verified. Exact AC-007/009, live regression baseline, outward existing conveniences or local sidecars, and coordinated checkout handoff are implementation-ready without new behavior. No conditions; Design acceptance only. |
| Bypass reason | None |
