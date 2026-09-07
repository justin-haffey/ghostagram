---
title: "DeclarativeCompositionProjection Module Architecture"
artifact_type: "module_architecture"
id: "ARCH-MODULE-GHOSTAGRAM-BRIDGE-DECLARATIVE-COMPOSITION-PROJECTION"
status: "Target"
authority: "solution"
scope: "Ghostagram.Bridge/DeclarativeCompositionProjection"
parent: "ARCH-PACKAGE-GHOSTAGRAM-BRIDGE"
upstream:
  repository: "ghostagram"
  artifact_id: "ARCH-PACKAGE-GHOSTAGRAM-BRIDGE"
  path: "architecture/packages/Ghostagram.Bridge/PACKAGE-ARCHITECTURE.md"
  revision: "None"
owners:
  - "module-architect (epic002_ghostagram_projection_module)"
created: "2026-08-29"
updated: "2026-09-06"
revision: "2"
template_version: "2.0.0"
---




# DeclarativeCompositionProjection Module Architecture

> Revision 2 is the current Target amendment. The [layered foundation reconciliation](#epic-002-layered-foundation-reconciliation) below replaces the historical P50 Feature/Plan-Pending and delivery sequencing statements. Earlier approval records apply only to their named baseline; revision 2 requires its own independent approval. No implementation or promotion is claimed.

## 1. Purpose

### 1.1 Summary

DeclarativeCompositionProjection is the read-only Ghostagram.Bridge Module that translates a typed, already accepted Ghostworx.System Component Definition or Composition IR consumer view into immutable, rebuildable diagram state. It preserves System-owned identity, hierarchy, explicit visibility, version/profile, provenance, Content Identity, and diagnostics while keeping presentation state separately owned and revisioned by Ghostagram. The Module is a projection consumer, not a definition, compiler, compatibility, runtime, or editing authority.

### 1.2 Scope

**In scope**

- Full deterministic projection of one supported accepted Component Definition or Composition IR view into one immutable diagram snapshot.
- Collision-checked projection identities that always retain their canonical System identities separately.
- Preservation of public parent/constituent hierarchy, opaque encapsulation boundaries, explicit exports, requirements and assignments, declared surfaces, implementation-reference identity, source versions, provenance, Content Identity, diagnostics, and freshness.
- A presentation-only delta decision when the System source anchor is unchanged and only a compatible Ghostagram sidecar revision changes.
- Explicit full-reprojection requirements for any semantic source revision, Content Identity, profile, visibility, lineage, or projection-identity discontinuity.
- Explicit Fresh, Stale, Unsupported, Skewed, and Failed outcomes with separately attributed System and Ghostagram diagnostics.
- A future attributable conformance-observation seam over the real projection behavior after an Accepted Feature and Implementation Plan allocate delivery.

**Out of scope**

- Definition or IR authoring, mutation, validation, compilation, migration meaning, semantic compatibility decisions, identity minting, or Content Identity generation.
- Semantic deltas inferred from graph operations or consumer comparison. No composition source-delta contract is allocated.
- Graph-command editing, diagram-operation proposals that mutate definitions, or expected-revision command processing.
- Execution, palette/catalog, materialization, instance lifecycle, supervision execution, Port interaction, control dispatch, Server hosting, Blazor, JavaScript, SignalR, MCP/HTTP, persistence services, or operational projections.
- Provider selection, Variable resolution, VariableBinding access, credentials, private locators, resolved payloads, runtime state, routes, authorization, or executable behavior.
- Exact CLR APIs, files, namespaces, algorithms, finite values, source, tests, Evidence, or Validation while Feature and Implementation Plan remain Pending.

### 1.3 Stakeholders

| Stakeholder | Primary concern | Uses this document for |
|---|---|---|
| Ghostagram callers | Read-only, deterministic, explicit projection outcomes | Integration and upgrade decisions |
| System contract owners | One semantic/compiler authority and faithful consumer behavior | Contract evolution and conformance |
| Bridge maintainers | Cohesive boundary and enforceable dependency rules | Design and change review |
| Independent validators | Attributable real-seam observations and failure evidence | Future Feature acceptance |

## 2. Drivers

### 2.1 Responsibilities

| ID | Responsibility | Architectural consequence |
|---|---|---|
| R-01 | Consume only typed accepted System definition/IR views or explicit System rejection results | The input boundary preserves System acceptance and failure meaning instead of recreating validation. |
| R-02 | Preserve public semantic facts in a diagram | Mapping retains canonical identity, hierarchy, visibility, versions, provenance, Content Identity, and source diagnostics. |
| R-03 | Keep presentation independent | Sidecar data decorates stable projection identities and cannot change System meaning or Content Identity. |
| R-04 | Make freshness and recovery explicit | Every result names its source anchor and presentation revision; discontinuity requires full reprojection. |
| R-05 | Remain non-executable and read-only | Operation, IPort, IControl, and Implementation Reference facts expose no connector, route, command, activation, or invocation surface. |
| R-06 | Fail closed | Unsupported, skewed, malformed, over-limit, cancelled, or failed input never yields a diagram labeled Fresh. |

### 2.2 Quality Goals

| Priority | Quality attribute | Concrete meaning | Evidence / measure |
|---|---|---|---|
| 1 | Authority integrity | System-owned meaning is preserved without reinterpretation or visibility widening. | System-owned negative fixtures and mapping invariants. |
| 2 | Determinism | Equivalent accepted source and sidecar inputs yield equivalent snapshots, presentation deltas, statuses, and diagnostics. | Repeat, permutation, and canonical ordering tests. |
| 3 | Failure containment | Every non-success path is explicit, bounded, redacted, and non-mutating. | Bounds, cancellation, redaction, and state tests. |
| 4 | Freshness correctness | Fresh is possible only for the exact recorded source anchor and sidecar revision. | Revision, Content Identity, profile, and lineage tests. |
| 5 | Evolvability | Unsupported System versions or extensions cannot be silently approximated. | Compatibility matrix and fail-closed upgrade tests. |

### 2.3 Constraints and Non-Goals

- The [Bridge Package Target](../../PACKAGE-ARCHITECTURE.md) and [Ghostagram Solution Target](../../../../SOLUTION-ARCHITECTURE.md#17-epic-002-declarative-composition-projection-target) are independently approved and bind this Module.
- Feature is Pending and Implementation Plan is Pending. This artifact authorizes Target architecture only.
- Ghostworx.System is the sole semantic, validation, compiler, compatibility, migration, diagnostic-meaning, and Content Identity authority.
- The current Bridge assembly references Ghostagram.Execution for unrelated existing behavior. This Module must have no type or call dependency on Execution.
- The accepted parent architecture allocates no authoritative composition change feed. Full semantic reprojection is the normative recovery path.
- The Module is an in-process net10.0 library responsibility and owns no listener, process, credential, durable semantic store, or deployment identity.
- It does not generalize GraphDiagramProjection or GraphDiagramDeltaProjector into a second ComponentModel, treat DiagramDocument metadata as portable System records, or create visual definition authoring.

## 3. Context

### 3.1 External Context

| External element | Direction | Contract / dependency | Module rule |
|---|---|---|---|
| Ghostworx.System accepted consumer view | In | Typed Component Definition, Composition IR, Compilation Result, versions, provenance, Content Identity, diagnostics | Supplies all semantic meaning; unsupported input remains explicit. |
| Ghostagram presentation sidecar | In | Immutable sidecar snapshot and presentation revision | Supplies display hints only; never widens visibility or changes source facts. |
| Ghostagram.Core diagram model | Out | Immutable DiagramDocument and diagram records | Rebuildable consumer representation, never semantic authority. |
| Ghostagram caller or future tooling | In/out | Projection request and immutable result | May retain last-known state only with its non-Fresh status. |
| System conformance kit | Future test/tooling | Corpus, profile, schema, invariants | Feature/Plan gated; validates attributable observations. |

### 3.2 Context Diagram

~~~mermaid
flowchart LR
    SYSTEM["Ghostworx.System\naccepted typed definition or IR view"]
    SIDECAR["Ghostagram presentation sidecar\nlayout and revision"]
    CALLER["Ghostagram caller\nor future conformance tooling"]
    subgraph MODULE["DeclarativeCompositionProjection"]
        PROJECTION["Read-only projection boundary"]
    end
    CORE["Ghostagram.Core\nimmutable DiagramDocument"]

    SYSTEM -->|"semantic facts and explicit failures"| PROJECTION
    SIDECAR -->|"presentation hints only"| PROJECTION
    CALLER -->|"bounded request"| PROJECTION
    PROJECTION -->|"immutable attributed result"| CALLER
    PROJECTION -->|"rebuildable diagram"| CORE
~~~

**View notes**

- **Purpose:** Show which inputs may supply semantic meaning and which state only decorates presentation.
- **Audience:** System owners, Bridge maintainers, callers, and reviewers.
- **Boundary:** Commands, Execution, Server, UI, persistence, interaction, and materialization are intentionally omitted.

## 4. Structure

### 4.1 Architecture Strategy

- Use a one-way anti-corruption projection from System-owned typed records to Ghostagram-owned presentation state.
- Keep source anchor, projection identity, presentation sidecar, and caller-retained last-known view separate.
- Produce immutable result unions rather than mutating a diagram through partial success.
- Traverse only the System-approved public consumer view; represent hidden structure as opaque and never infer exports.
- Rebuild fully after any semantic discontinuity; limit deltas to presentation-only changes over the same exact source anchor.
- Preserve source diagnostic category/code and place local diagnostics in a distinct Bridge namespace.
- Apply finite traversal, output, diagnostic, deadline, and cancellation bounds.

### 4.2 Building Blocks

| Building block | Responsibility | Depends on | Exposes |
|---|---|---|---|
| Accepted Source Gate | Require a typed accepted definition/IR view or retain explicit System rejection; check declared support without reproducing semantic validation | System public contracts | Supported source or non-success reason |
| Visibility Walker | Traverse public constituents, exports, requirements, assignments, and declared surfaces without crossing opaque boundaries | Accepted source view | Ordered public facts |
| Projection Identity Mapper | Map canonical identities into deterministic collision-checked diagram identities while retaining canonical identity separately | Public facts and profile | Projected identities or collision failure |
| Diagram Snapshot Builder | Build one immutable, deterministically ordered DiagramDocument | Identity mapper and Ghostagram.Core | Full snapshot |
| Sidecar Reconciler | Apply compatible display hints without changing semantic facts | Immutable sidecar | Decorated snapshot and presentation diagnostics |
| Freshness Evaluator | Correlate source identity/revision/versions/Content Identity/lineage and presentation revision | Source anchor, prior result, sidecar | Status and recovery decision |
| Presentation Delta Builder | Compare complete projections only when the exact source anchor is unchanged | Prior Fresh and rebuilt same-source snapshots | Presentation-only delta or full-reprojection requirement |
| Diagnostic Collector | Preserve bounded System diagnostics and add distinct redacted Bridge diagnostics | All blocks | Ordered diagnostics |
| Result Assembler | Enforce the result union and no-Fresh-partial invariant | Snapshot, status, diagnostics, source facts | Immutable result |

### 4.3 Building-Block Diagram

~~~mermaid
flowchart TB
    subgraph DCP["DeclarativeCompositionProjection Module"]
        GATE["Accepted Source Gate"]
        WALK["Visibility Walker"]
        IDS["Projection Identity Mapper"]
        BUILD["Diagram Snapshot Builder"]
        SIDE["Sidecar Reconciler"]
        FRESH["Freshness Evaluator"]
        DELTA["Presentation Delta Builder"]
        DIAG["Diagnostic Collector"]
        RESULT["Result Assembler"]

        GATE --> WALK
        WALK --> IDS
        IDS --> BUILD
        BUILD --> SIDE
        SIDE --> FRESH
        FRESH --> DELTA
        GATE --> DIAG
        WALK --> DIAG
        IDS --> DIAG
        SIDE --> DIAG
        FRESH --> DIAG
        DELTA --> DIAG
        DIAG --> RESULT
        FRESH --> RESULT
        DELTA --> RESULT
    end
~~~

**View notes**

- **Elements:** Stable responsibilities, not proposed classes or files.
- **Relations:** Arrows mean allowed data flow.
- **Key rule:** No block calls Execution, commands, Server, UI, interaction, or runtime types.

### 4.4 Contract Families

| Contract family | Role | Required facts | Stability |
|---|---|---|---|
| Accepted System source view | Authoritative typed input | Root/domain identity, exact revision, hierarchy, visibility, versions/profiles, provenance, Content Identity, attributed diagnostics | System-versioned |
| Projection request | Bounded consumer request | Source, sidecar, finite profile, deadline/cancellation, optional prior anchor | Target; exact API deferred |
| Source anchor | Freshness identity | Source kind, canonical identity, revision, source-set identity, versions, Content Identity, lineage | Immutable result fact |
| Projection snapshot | Rebuildable state | DiagramDocument, source anchor, presentation revision, projected-identity map, diagnostics | Target |
| Presentation delta | Same-source presentation optimization | Base/result presentation revisions and deterministic operations over unchanged source anchor | Target, presentation-only |
| Projection result | Closed outcome union | Fresh, Stale, Unsupported, Skewed, or Failed; result kind, source facts, optional snapshot/delta, diagnostics, recovery | Target |

## 5. Model

### 5.1 Projection Result Model

~~~mermaid
classDiagram
    class SystemSourceView {
        canonicalIdentity
        exactRevision
        versionsAndProfiles
        provenance
        contentIdentity
    }
    class PresentationSidecar {
        projectionKeys
        presentationRevision
        displayHints
    }
    class SourceAnchor
    class ProjectionSnapshot {
        immutableDiagram
        projectedIdentityMap
        presentationRevision
    }
    class PresentationDelta {
        basePresentationRevision
        resultPresentationRevision
        diagramOperations
    }
    class ProjectionResult {
        outcome
        resultKind
        recovery
        attributedDiagnostics
    }

    SystemSourceView --> SourceAnchor : "supplies"
    SystemSourceView --> ProjectionResult : "authoritative meaning"
    PresentationSidecar --> ProjectionSnapshot : "decorates only"
    SourceAnchor --> ProjectionSnapshot : "anchors"
    ProjectionSnapshot --> PresentationDelta : "same-source comparison"
    ProjectionSnapshot --> ProjectionResult : "optional"
    PresentationDelta --> ProjectionResult : "optional"
~~~

### 5.2 Outcome Semantics

| Outcome | Meaning | Diagram rule | Recovery |
|---|---|---|---|
| Fresh | A full snapshot was built from one supported accepted source anchor and presentation revision, or a presentation-only delta was derived over that same anchor | Snapshot or same-source presentation delta may be returned | None |
| Stale | A caller-retained diagram no longer matches the requested exact source anchor or freshness cannot be proved | Last-known state is never relabeled as a new Fresh snapshot | Full reprojection |
| Unsupported | The System record, construct, extension, or operation is recognized but lacks accepted projection support | No Fresh diagram or delta | Governed upgrade or supported profile |
| Skewed | Versions, profiles, lineage, sidecar keying, or compatibility facts do not form one supported coherent input | No Fresh diagram or delta | System negotiation/migration, then full reprojection |
| Failed | System supplied rejection, or Bridge hit malformed input, collision, bounds, cancellation, deadline, leakage, or internal failure | No Fresh partial diagram or delta | Correct attributed cause, then retry |

System rejection and Bridge failure remain separately attributed even when both map to Failed. Redaction never converts failure to success.

### 5.3 State, Ownership, and Invariants

- System owns Component Definition and Composition IR. Ghostagram owns projection results and sidecars. Callers own retained last-known views.
- One bounded request observes one immutable System view and one sidecar revision and yields one immutable result.
- The Module has no durable mutable state and mutates no source, Graph/Variable history, binding, prior IR, prior result, or supplied sidecar.
- Fresh requires the exact source anchor and presentation revision recorded in the result.

Core invariants:

1. Only a typed accepted System consumer view supplies semantic meaning; rejection never becomes accepted projection state.
2. Every element retains canonical System identity separately from deterministic Ghostagram projection identity.
3. Private and non-exported declarations remain absent or opaque and cannot leak through ports, labels, metadata, diagnostics, or sidecars.
4. VariableRequirement and VariableAssignment expose only approved public facts; VariableBinding, provider locators, credentials, resolved values, and provider state are forbidden.
5. Operation, IPort, IControl, and Implementation Reference projections are descriptive and non-executable.
6. Presentation deltas require identical exact source anchors; semantic changes require full reprojection.
7. Stale, Unsupported, Skewed, and Failed never include new state labeled Fresh.
8. Presentation changes never alter System identity, visibility, diagnostics, equivalence, or Content Identity.
9. Equivalent inputs produce equivalent ordered diagram structure, status, and diagnostics apart from non-semantic correlation/timing data.

## 6. Runtime

### 6.1 Full Projection

**Trigger:** Supported typed accepted definition or IR, finite profile, and optional sidecar.

**Result:** One immutable Fresh snapshot correlated to exact source and presentation revisions.

~~~mermaid
sequenceDiagram
    participant Caller
    participant Gate as Accepted Source Gate
    participant Projector as Snapshot Builder
    participant Sidecar as Sidecar Reconciler
    participant Result as Result Assembler

    Caller->>Gate: typed accepted source and bounded request
    Gate->>Projector: ordered public facts
    Projector->>Sidecar: full immutable projection
    Sidecar->>Result: decorated snapshot and revision
    Result-->>Caller: Fresh snapshot and attributed diagnostics
~~~

Failure produces one explicit non-Fresh result and no Fresh partial snapshot.

### 6.2 Update Decision

A caller supplies a prior result, current accepted source, and current sidecar. The Module deterministically returns:

- a presentation-only delta when the prior result is Fresh, exact source anchor is unchanged, and only compatible sidecar state changed;
- an equivalent Fresh snapshot when nothing changed and a snapshot was requested;
- Stale with FullReprojectionRequired when any semantic source fact or visibility identity changes; or
- Unsupported, Skewed, or Failed under the rules above.

No source semantic delta is inferred. A future semantic delta requires a separately Accepted System-owned revision/change contract and renewed parent architecture.

### 6.3 Explicit Rejection or Failure

System rejection retains source category/code and public facts when available. Local failure adds separately attributed bounded Bridge diagnostics. Neither path exposes Fresh state or mutates input.

### 6.4 Concurrency

- Request-local immutable processing; implementations are reentrant and have no ambient mutable cache.
- One request atomically yields one complete immutable result or one explicit non-Fresh result.
- Cancellation and deadline propagate through traversal, mapping, ordering, and assembly.
- Stable ordering follows semantic System order where declared; otherwise Design selects stable identity/category ordering.
- Source or sidecar changes after request creation belong to a later request and never modify the current result.

## 7. Interfaces and Compatibility

### 7.1 Public API Families

| API family | Purpose | Contract expectations |
|---|---|---|
| Project accepted definition | Full result from one supported typed definition view | Read-only, bounded, exact source anchor |
| Project accepted IR | Full result from one supported typed IR view | Preserve compiler/profile/canonicalization and Content Identity |
| Project explicit System result | Preserve accepted or rejected System result meaning | Rejection never yields Fresh |
| Evaluate projection update | Select same-source presentation delta or full reprojection | No semantic delta inference |
| Observe conformance facts | Expose actual result fields to future tooling | Attributable real seam; Feature/Plan gated |

Exact CLR names, overloads, visibility, files, and serialization remain Design decisions.

### 7.2 Extension Points

| Extension point | Mechanism | Rule |
|---|---|---|
| Presentation mapping profile | Constructor-supplied immutable strategy/data | May alter layout/visual classification only; cannot widen visibility, change identity, add interaction, or reinterpret diagnostics |
| System-compatible opaque extension view | System-approved typed/opaque representation | Preserve or explicitly reject; never interpret executable meaning |

There is no runtime plugin, reflection, implementation loader, provider, command, or semantic-validator extension point.

### 7.3 Compatibility

- Runtime is the repository net10.0 target and an in-process managed library.
- System contract/compiler/profile/canonicalization versions must be mutually supported before projection.
- Meaning-preserving System additions remain Unsupported until explicitly governed; no best-effort interpretation.
- System portable-record serialization remains System-owned. Only Ghostagram sidecars use Ghostagram serialization.
- Binary/source release policy is deferred; this Target authorizes no release.

## 8. Cross-Cutting Concerns

### 8.1 Error Handling and Observability

- Preserve System category/code, public identity, revision, and profile without changing failure meaning.
- Use a distinct Bridge namespace for collision, sidecar, bounds, cancellation, deadline, redaction, and internal failures.
- Order and bound diagnostic count, path, context, and detail.
- Never emit stack traces, credentials, protected context, private locators, resolved values, runtime handles, or non-exported declarations.
- Results include outcome, result kind, source anchor when available, presentation revision when applicable, diagnostics, recovery, and supplied correlation.
- The Module owns no health endpoint, telemetry service, runtime status, or operational projection.

### 8.2 Performance

- Bounded visibility traversal, identity mapping, deterministic ordering, sidecar reconciliation, and assembly are the critical path.
- Target O(n) traversal plus O(n log n) stable ordering; no unbounded recursion, repeated global scan, or ambient cache.
- Request-local immutable allocations stay within the accepted finite profile.
- Full reprojection favors correctness over consumer-invented semantic deltas.

### 8.3 Security and Trust

- Typed input remains untrusted until System acceptance/compatibility/content-identity facts and local projection bounds are verified.
- Semantic Addresses, Variable References, declared surfaces, Implementation References, and diagram elements grant no access, authorization, connection, invocation, or execution rights.
- The Visibility Walker consumes only a System-approved public view; generic metadata cannot bypass export/redaction rules.
- The Module adds no network trust boundary, secret, provider, endpoint, deployment identity, or control plane.

### 8.4 Persistence

- The Module persists no definition, IR, projection, or sidecar as semantic authority.
- A caller may persist sidecars under separate Package rules, keyed to stable projected identity and explicit presentation revision.
- Retained last-known projection carries its source anchor and Stale status.
- Composition sidecar schema/keying and migration remain post-Plan Design decisions.

## 9. Deployment

**Deployment status:** In-process responsibility within Ghostagram.Bridge; no separate deployable.

~~~mermaid
flowchart LR
    HOST["Ghostagram host or local tooling"]
    BRIDGE["Ghostagram.Bridge library"]
    SYSTEM["Ghostworx.System library"]
    CORE["Ghostagram.Core library"]

    HOST -->|"loads"| BRIDGE
    BRIDGE -->|"typed accepted source"| SYSTEM
    BRIDGE -->|"immutable diagram model"| CORE
~~~

No publication, release, version bump, host change, endpoint, worker, or browser asset is authorized.

## 10. Decisions

| ID | Decision | Status | Rationale | Consequence |
|---|---|---|---|---|
| AD-DCP-001 | Full projection is the semantic source path and full reprojection is recovery | Target | No accepted System composition change feed exists | Correctness over incremental efficiency |
| AD-DCP-002 | Deltas are presentation-only over an unchanged exact source anchor | Target | Ghostagram owns sidecars, not semantic change | Source changes cannot use local delta |
| AD-DCP-003 | One closed attributed result union | Target | Non-success must not appear as Fresh partial state | Callers handle explicit outcomes |
| AD-DCP-004 | Canonical identity remains separate from projected identity | Target | Diagram IDs are consumer mechanics | Mapping is deterministic and collision checked |
| AD-DCP-005 | Declared surfaces are descriptive and non-executable | Target | ADR-005 gives interaction ownership to EPIC-005 | No connector, route, dispatch, control, or activation API |
| AD-DCP-006 | Authoritative definition editing is absent | Target | No accepted System edit contract or allocation exists | Only sidecars may change locally |

A semantic delta, edit command, executable surface, new Package dependency, cross-solution contract change, or deployable/live integration requires renewed P40/P50 and independent review.

## 11. Quality

### 11.1 Quality Scenarios

| ID | Scenario | Expected response | Future verification |
|---|---|---|---|
| Q-01 | Nested constituent contains private surfaces or requirements | Public projection omits or makes boundary opaque without inferable private detail | System negative fixtures and real projection |
| Q-02 | Equivalent accepted source and sidecar are projected repeatedly | Equivalent identity, ordering, hierarchy, facts, status, and diagnostics | Repeat and permutation tests |
| Q-03 | Only compatible sidecar revision changes | Deterministic presentation delta or equivalent full snapshot over same source anchor | Delta/snapshot equivalence |
| Q-04 | Source revision, Content Identity, profile, visibility, or lineage changes | Stale plus FullReprojectionRequired; no source delta invented | Discontinuity tests |
| Q-05 | Projected IPort, IControl, Operation, or Implementation Reference is selected | No connector, dispatch, activation, loading, route, or control path | API/dependency fitness |
| Q-06 | Input is incompatible, over limit, cancelled, expired, collision-prone, or protected-data-bearing | Explicit Unsupported, Skewed, or Failed; no Fresh partial state or mutation | Compatibility, bounds, cancellation, redaction |
| Q-07 | Future conformance runs real seam | Attributable envelope records exact versions/digests, every case, observations, missing/failed/unsupported | System validation and independent local Validation |

### 11.2 Verification Strategy

- Unit checks for outcome exclusivity, identity, visibility, ordering, source anchor, sidecar isolation, and diagnostics.
- Property/repeat checks for equivalent-input snapshots and presentation deltas.
- Compatibility and failure checks for versions, bounds, cancellation, deadline, collision, leakage, and System rejection.
- Architecture checks proving no type/call dependency on Execution, graph commands, Server, Blazor, JavaScript, SignalR, operational projection, EPIC-005 interaction, or EPIC-010 materialization.
- Real-seam System conformance only after an Accepted Feature, Plan, reconciled Target, and Design.

## 12. Risks

| ID | Risk | Impact | Mitigation | Status |
|---|---|---|---|---|
| RK-01 | Coarse Bridge reference to Execution leaks into this Module | Non-execution boundary weakens | Design adds negative type/call checks; return to architecture if physical split needed | Open |
| RK-02 | Generic diagram metadata leaks private/protected facts | Encapsulation and trust failure | Allow-listed System public view and redaction invariants | Open |
| RK-03 | Diagram IDs are treated as canonical identities | Identity drift | Retain canonical identity separately and collision-check mapping | Open |
| RK-04 | Local snapshot diff is treated as semantic delta | Consumer invents source-change meaning | Same-source presentation deltas only; source discontinuity forces full reprojection | Open |
| RK-05 | Last-known state appears current | Stale meaning consumed | Explicit outcome/source anchor; never relabel as Fresh | Open |
| RK-06 | Graph commands or diagram ports gain edit/interaction meaning | Ownership boundaries collapse | No command/Execution dependency and negative API checks | Open |

## 13. Evolution

### 13.1 Current Extension Seams

- Presentation mapping profiles may change layout or visual classification without altering identity, visibility, outcome, or execution meaning.
- System-approved opaque extensions may be preserved or rejected, never interpreted by Bridge.
- Future conformance tooling may observe the real result surface after delivery gates open.

### 13.2 Planned Directions and Change Rules

- Exact immutable CLR contracts, finite values, and sidecar keying after Accepted Feature, Plan, reconciliation, and Design.
- Semantic incremental projection only after a separately Accepted System revision/change contract and renewed parent architecture.
- Authoritative definition editing only after an Accepted System command/revision/validation/conflict contract and separately Accepted architecture, Feature, and Plan.
- Any new Package, Execution/Server/UI dependency, semantic delta, command, interaction, materialization, deployment, or authority change returns to P40/P50.
- After Plan acceptance, reconcile criteria, integration paths, locators, conformance duties, API placement, and evidence workspace before Design.

## 14. Traceability and Current Divergence

| Artifact or evidence | ID / status | Module constraint |
|---|---|---|
| [EPIC-002](../../../../../../../.swe/epics/002-declarative-composition-model/EPIC.md) | EPIC-002, Accepted | Definition-only composition. |
| [Concept](../../../../../../../.swe/epics/002-declarative-composition-model/CONCEPT.md) | CONCEPT-EPIC-002, Accepted | Recursive identity, encapsulation, distinct meanings, non-execution. |
| [Architecture impact](../../../../../../../.swe/epics/002-declarative-composition-model/ARCHITECTURE-IMPACT.md) | ARCH-IMPACT-EPIC-002, Accepted | Module change; Execution, Server, UI, interaction, runtime excluded. |
| [Platform Target](../../../../../../../architecture/PLATFORM-ARCHITECTURE.md#epic-002-parent-architecture-assignment) | ARCH-PLATFORM-GHOSTWORX, Target and approved | Exact repos/ghostagram assignment; Plan Pending. |
| [ADR-008](../../../../../../../architecture/decisions/ADR-008-pre-feature-child-target-architecture-assignment.md) | ADR-008, Accepted | Architecture-only authorization and post-Plan reconciliation. |
| [Composition contract](../../../../../../../architecture/contracts/DECLARATIVE-COMPONENT-COMPOSITION.md) | CONTRACT-DECLARATIVE-COMPONENT-COMPOSITION, Accepted | Projection, failure, compatibility, finite-profile, conformance meaning. |
| [ADR-005](../../../../../../../architecture/decisions/ADR-005-componentmodel-port-definition-and-port-interaction-ownership.md) | ADR-005, Accepted | IPort/IControl definition separate from EPIC-005 interaction. |
| [Ghostagram Solution Target](../../../../SOLUTION-ARCHITECTURE.md#17-epic-002-declarative-composition-projection-target) | ARCH-SOLUTION-GHOSTAGRAM, Target and approved | Selects Ghostagram.Bridge and this Module. |
| [Bridge Package Target](../../PACKAGE-ARCHITECTURE.md) | ARCH-PACKAGE-GHOSTAGRAM-BRIDGE, Target and approved | Exact read-only projection responsibility. |
| [Ghostagram context](../../../../../CONTEXT.md) | CONTEXT-GHOSTAGRAM, Accepted | Projection, sidecar, freshness, declared-surface vocabulary. |
| [GraphDiagramProjection](../../../../../src/Ghostagram.Bridge/Projection.cs) | Current baseline | Deterministic GraphSnapshot projection and separate revisions, not EPIC-002 delivery. |
| [GraphDiagramDeltaProjector](../../../../../src/Ghostagram.Bridge/Projection.cs) | Current baseline | Full-projection fallback for graph changes, not composition delta authority. |
| [GraphPresentationSnapshot](../../../../../src/Ghostagram.Bridge/BridgeContracts.cs) | Current baseline | Separately revisioned presentation state. |
| [DiagramDocument](../../../../../src/Ghostagram.Core/DiagramModel.cs) | Current baseline | Immutable rebuildable diagram aggregate. |

No DeclarativeCompositionProjection source, API, tests, conformance result, Evidence, or Validation exists. Graph projection, graph delta, commands, Execution, palette/catalog, Server, UI, and live collaboration remain neighboring evidence only.

## 15. Approval and Lifecycle

- **Architecture lifecycle:** Target.
- **Feature:** Pending.
- **Implementation Plan:** Pending.
- **Post-Plan:** Reconcile the accepted Feature and Plan against this Target before Design.
- **Promotion:** Evidence is required for Implemented; independent validation and reconciliation for Current.

### Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | module-architect (epic002_ghostagram_projection_module) |
| Approver | elon-musk (`epic002_concept_approval`) |
| Decision | Accepted |
| Recorded | `2026-08-29T06:13:59.2481503-04:00` |
| Evidence | Independent review verified a read-only typed System consumer, immutable rebuildable projection, separately owned sidecar state, same-source presentation-only deltas, mandatory full reprojection after every semantic discontinuity, explicit Fresh/Stale/Unsupported/Skewed/Failed outcomes, canonical-versus-projection identity separation, and no Execution, command, Server, UI, interaction, provider, or materialization dependency. |
| Bypass reason | None |

## 16. Glossary

| Term | Meaning |
|---|---|
| Accepted System source view | Typed System-owned consumer representation whose acceptance, compatibility, visibility, and diagnostics remain System-owned. |
| Source anchor | Exact source identity/revision/source set, versions/profiles, provenance, Content Identity, and lineage represented by a projection. |
| Projection identity | Deterministic Ghostagram diagram identity retained alongside canonical source identity; never semantic identity. |
| Presentation sidecar | Ghostagram-owned layout, waypoints, selection, viewport, color, collapsed state, and presentation revision. |
| Presentation delta | Diagram-operation change over an unchanged exact source anchor; no semantic source-change authority. |
| Full reprojection | Complete reconstruction from current supported accepted System source; normative after semantic discontinuity. |
## EPIC-002 Layered Foundation Reconciliation

### Accepted authority and revision scope

This revision follows Accepted [EPIC-002 revision 2](../../../../../../../.swe/epics/002-declarative-composition-model/EPIC.md), [CONCEPT-EPIC-002 revision 2](../../../../../../../.swe/epics/002-declarative-composition-model/CONCEPT.md), [ARCH-IMPACT-EPIC-002 revision 2](../../../../../../../.swe/epics/002-declarative-composition-model/ARCHITECTURE-IMPACT.md), the [Platform layered Target amendment](../../../../../../../architecture/PLATFORM-ARCHITECTURE.md), human-Accepted [ADR-009](../../../../../../../architecture/decisions/ADR-009-epic-002-definition-packaging-and-planning-gates.md), and accepted Graph/composition contract revisions. The independent parent decisions are recorded in [REVIEW-EPIC-002-LAYERED-PLANNING](../../../../../../../architecture/reviews/EPIC-002-LAYERED-PLANNING-REVIEW.md). The author is consumer_planning; Justin explicitly named independent @elon-musk as approver. No bypass is used.

The exact foundation handoff is [FEATURE-004](../../../../../../../.swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/FEATURE.md) and [IMPL-PLAN-EPIC-002-FEATURE-004](../../../../../../../.swe/epics/002-declarative-composition-model/features/004-layered-system-definition-foundation/IMPLEMENTATION-PLAN.md), repository ghostworx, revision 1. It assigns this child workspace [.swe/implementations/EPIC-002/FEATURE-004/](../../../../../.swe/implementations/EPIC-002/FEATURE-004/). Detailed Feature/Plan approval is separate from this Target review.

### Module refinement

Retain `ARCH-MODULE-GHOSTAGRAM-BRIDGE-DECLARATIVE-COMPOSITION-PROJECTION` inside `ARCH-PACKAGE-GHOSTAGRAM-BRIDGE`. FEATURE-004 establishes the portable reference and typed presentation boundary; FEATURE-003 implements this read-only composition capability.

The Module consumes exact System Composition D3 definitions/IR or explicit rejected results and canonical Variable D2/Core D0/D1 facts. It neither parses competing semantic DTOs nor creates an accepted result. System admission and compatibility ports are authoritative. Already admitted public consumer views retain visibility/export constraints; private declarations stay opaque. Inert descriptors never gain diagram connection, command, route or control affordances.

Its immutable source anchor, presentation revision, collision-checked identity mapping and explicit `Fresh | Stale | Unsupported | Skewed | Failed` result semantics remain normative. A semantic discontinuity marks retained output Stale and requires full reprojection. Same-source presentation-only changes may produce presentation deltas. Failures mutate neither source nor sidecar and return no fresh partial projection.

The Module has no type/call dependency on Execution, palette/catalog, System runtime ComponentModel, graph stores, protected bindings, provider/resolver, Server, Blazor, SignalR or operational ports. It uses only portable System meaning plus Bridge/Core presentation abstractions. FEATURE-003 Design fixes exact APIs against the accepted System F001 contract/profile/schema freeze and maps AC-001 through AC-007, including visible read-only verification and attributable producer-bound conformance.

### Design and delivery gates

F004 Design requires Accepted FEATURE-004, its Plan and this applicable Target revision; consumer Design finalizes against the accepted System F004 Design. Consumer reference work can use that same System checkout/build outputs after accepted local Designs without waiting for F004 to accept itself. F004 portfolio acceptance requires the System and all three consumer Evidence and independent local Validation records.

The composition allocation is [FEATURE-003](../../../../../../../.swe/epics/002-declarative-composition-model/features/003-ghostagram-composition-projection/FEATURE.md), with [IMPL-PLAN-EPIC-002-FEATURE-003](../../../../../../../.swe/epics/002-declarative-composition-model/features/003-ghostagram-composition-projection/IMPLEMENTATION-PLAN.md). Its revision 2 detailed approval and this Target reconciliation must be Accepted before Design. The accepted local F004 Design and accepted System F001 Design freeze establish the Design inputs. F003 source delivery waits for F004 and F001 portfolio acceptance, producer Evidence/independent Validation, and the accepted producer manifest/envelope.

Architecture approval keeps lifecycle Target. Source, tests, Evidence, Validation and architecture promotion are subsequent phases. Earlier P50 Pending statements describe the historical bootstrap only; these current locators and distinct Design/delivery gates govern EPIC-002. A material conflict returns to the owning architecture/Plan before dependent work.

### Revision 2 Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | consumer_planning |
| Approver | elon-musk (`/root/elon_musk_approval`) |
| Decision | Accepted |
| Recorded | 2026-09-06T22:55:30+00:00 |
| Evidence | [Independent @elon-musk review](../../../../../../../architecture/reviews/EPIC-002-LAYERED-PLANNING-REVIEW.md#consumer-module-targets-revision-2). Accepted Package parent verified. Explicit freshness, visibility, identity-keyed sidecars and inert declared surfaces prevent fresh partial results or execution affordances. Design and delivery gates preserved. No conditions; lifecycle remains Target. |
| Bypass reason | None |
