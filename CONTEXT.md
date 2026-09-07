---
title: "Ghostagram Context"
artifact_type: "context-vocabulary"
id: "CONTEXT-GHOSTAGRAM"
status: "Accepted"
authority: "solution"
scope: "ghostagram"
parent: "CONTEXT-MAP-GHOSTWORX"
upstream:
  repository: "ghostworx"
  artifact_id: "CONTEXT-MAP-GHOSTWORX"
  path: "CONTEXT-MAP.md"
  revision: "None"
owners:
  - "Ghostagram solution architecture"
created: "2026-08-29"
updated: "2026-08-29"
template_version: "2.0.0"
---

# Ghostagram Context

Ghostagram is the Ghostworx diagram and presentation Solution. Its EPIC-002 boundary is a read-only, rebuildable projection of accepted System Component Definitions and Composition IR; it does not become the definition, compiler, runtime, or operational authority.

## Language

**Semantic Authority**: The owner of canonical definition identity, validation, compilation, compatibility, and failure meaning. For EPIC-002, `ghostworx-system` is the sole semantic and compiler authority.
_Avoid_: treating a diagram, palette, bridge, or consumer-local cache as canonical definition state.

**Definition Projection**: A read-only translation of accepted Component Definitions or Composition IR into rebuildable Ghostagram diagram and presentation state that preserves public identity, hierarchy, explicit visibility, version/profile, provenance, content identity, diagnostics, and freshness/skew state.
_Avoid_: calling the projection an authoring authority, compiler result, runtime plan, or materialized instance.

**Presentation Sidecar**: Ghostagram-owned display state such as layout, waypoints, selection, viewport, color, collapsed state, and presentation revision. It is isolated from portable System records.
_Avoid_: serializing presentation state as Component Definition or Composition IR meaning.

**Projection Freshness**: The explicit relationship between an accepted System definition or IR revision and the corresponding Ghostagram projection revision, including a visible unavailable, stale, skewed, or failed state.
_Avoid_: inferring current semantic state from a locally retained diagram.

**Declared Surface**: The public, non-executable capability, operation, `IPort`, or `IControl` definition explicitly exposed by a Component boundary. Ghostagram may present its accepted meaning but never invent, reinterpret, bind, dispatch, or execute it.
_Avoid_: conflating a diagram port with EPIC-005 interaction, routing, transport, or runtime-control semantics.

## Relationships

- **`ghostworx-system` -> Ghostagram**: System publishes the accepted Component Definition and Composition IR meaning; Ghostagram consumes that meaning as a bounded presentation projection.
- **Definition Projection -> Presentation Sidecar**: A projection may be decorated with Ghostagram-owned display state, but sidecars neither change nor become portable definition/IR records.
- **Composition IR -> Projection Freshness**: Identity, revision, provenance, content identity, visibility, and failure meaning remain traceable at the projection boundary; a failed or skewed source does not become a consumer-authored success.
- **Declared Surface -> EPIC-005**: `IPort` and `IControl` identities retain System-owned definition-time meaning; EPIC-005 owns later interaction semantics, and Ghostagram owns neither.
- **Diagram editing -> System command contract**: Existing Ghostagram diagram operations do not authorize editing accepted Component Definitions. Such editing requires a separately Accepted System-owned command, expected-revision, validation, and conflict contract plus separately Accepted architecture, Feature, and Plan allocation.
- **Diagram view -> System view**: A diagram or definition projection is not a runtime or operational System view, and it does not grant materialization, lifecycle, control, or observability authority.

## Usage Rules

- Use this vocabulary for Ghostagram architecture, Design, tests, and evidence that consume EPIC-002 definition or IR records.
- Preserve System canonical identity, hierarchy, explicit visibility, version/profile, provenance, content identity, diagnostics, and explicit freshness/skew/failure state. Do not expose private declarations, `VariableBinding` data, resolved values, credentials, runtime state, or executable behavior.
- Keep the local palette, `Ghostagram.Execution`, `Ghostagram.Server`, Blazor, SignalR, and live operational-projection capabilities outside the EPIC-002 projection authority unless a later Accepted portfolio allocation explicitly changes their scope.
- Do not choose CLR types, public APIs, serialization formats, storage, package placement, or module placement here; those are child Target architecture and Design decisions after the ADR-008 gate is Accepted.
- The parent contract is [Declarative Component Composition](../../architecture/contracts/DECLARATIVE-COMPONENT-COMPOSITION.md); the governing assignment decision is [ADR-008](../../architecture/decisions/ADR-008-pre-feature-child-target-architecture-assignment.md). This context must be independently Accepted before it can satisfy the EPIC-002 P50 context prerequisite.

## Approval Record

| Field | Value |
|---|---|
| Mode | `-auto-approve` |
| Author | repo-author (`epic002_ghostagram_governance`) |
| Approver | elon-musk (`epic002_concept_approval`) |
| Decision | Accepted |
| Recorded | `2026-08-29T04:59:50.8249633-04:00` |
| Evidence | Cycle-one review verified `InReview` lifecycle state and consistent parent/upstream locators using `CONTEXT-MAP-GHOSTWORX` and `CONTEXT-MAP.md`. The Declarative Component Composition contract remains a body governing link. The vocabulary preserves Ghostagram as a read-only, rebuildable System-definition/Composition-IR projection; isolates presentation sidecars; preserves System semantic authority and EPIC-005 interaction ownership; and excludes runtime, operational, materialization, and authoritative editing scope. |
| Bypass reason | None |
