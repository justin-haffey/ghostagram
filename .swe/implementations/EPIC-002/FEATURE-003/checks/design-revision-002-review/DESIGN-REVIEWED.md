---
title: "Read-Only Composition Projection — ghostagram Design"
artifact_type: "design"
id: "DESIGN-EPIC-002-FEATURE-003-GHOSTAGRAM"
status: "InReview"
authority: "solution"
scope: "ghostagram"
parent: "IMPL-PLAN-EPIC-002-FEATURE-003"
upstream:
  repository: "ghostworx"
  artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-003"
  path: ".swe/epics/002-declarative-composition-model/features/003-ghostagram-composition-projection/IMPLEMENTATION-PLAN.md"
  revision: "2"
traceability:
  epic:
    repository: "ghostworx"
    artifact_id: "EPIC-002"
    path: ".swe/epics/002-declarative-composition-model/EPIC.md"
    revision: "2"
  feature:
    repository: "ghostworx"
    artifact_id: "FEATURE-003"
    path: ".swe/epics/002-declarative-composition-model/features/003-ghostagram-composition-projection/FEATURE.md"
    revision: "2"
  implementation_plan:
    repository: "ghostworx"
    artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-003"
    path: ".swe/epics/002-declarative-composition-model/features/003-ghostagram-composition-projection/IMPLEMENTATION-PLAN.md"
    revision: "2"
  system_design:
    repository: "ghostworx-system"
    artifact_id: "DESIGN-EPIC-002-FEATURE-001-GHOSTWORX-SYSTEM"
    path: ".swe/implementations/EPIC-002/FEATURE-001/DESIGN.md"
    revision: "4"
  foundation_design:
    repository: "ghostagram"
    artifact_id: "DESIGN-EPIC-002-FEATURE-004-GHOSTAGRAM"
    path: ".swe/implementations/EPIC-002/FEATURE-004/DESIGN-REVISION-001.md"
    revision: "1"
owners:
  - "ghostagram child implementation coordinator"
created: "2026-09-06"
updated: "2026-09-07"
revision: "2"
template_version: "2.0.0"
---

# Read-Only Composition Projection — ghostagram Design

## Revision 2 Prototype Backtracking

This InReview successor records the concrete F003 source interface and applicability clarification authorized by the portfolio coordinator for `PROTOTYPE-RUN-20260907T094049Z`. [Accepted revision 1](DESIGN-REVISION-001.md) is preserved byte-for-byte; its approval applies only to that historical revision. Revision 2 has no independent approval yet. The user-named approver remains `elon-musk`. Feature and Implementation Plan revision 2 intent and criteria remain authoritative.

The delivered-source work is in progress. Provisional checks do not establish final owner generation compatibility. Portfolio coordination reports independent final FEATURE-004 owner repair rejection with the automatic repair budget exhausted; integrated delivery is blocked pending human disposition. No additional Graph repair or consumer workaround is authorized by this Design. The frozen local F004 Validation is unchanged and does not accept a corrected or current owner generation.

## Assignment, Current State and Accepted Inputs

[FEATURE-003 revision 2](../../../../../../.swe/epics/002-declarative-composition-model/features/003-ghostagram-composition-projection/FEATURE.md) and its [Implementation Plan revision 2](../../../../../../.swe/epics/002-declarative-composition-model/features/003-ghostagram-composition-projection/IMPLEMENTATION-PLAN.md) are Accepted under [EPIC-002 revision 2](../../../../../../.swe/epics/002-declarative-composition-model/EPIC.md). Applicable [Solution Target](../../../../architecture/SOLUTION-ARCHITECTURE.md#epic-002-layered-foundation-reconciliation), [Ghostagram.Bridge Package Target](../../../../architecture/packages/Ghostagram.Bridge/PACKAGE-ARCHITECTURE.md#epic-002-layered-foundation-reconciliation) and [DeclarativeCompositionProjection Module Target](../../../../architecture/packages/Ghostagram.Bridge/modules/DeclarativeCompositionProjection/MODULE-ARCHITECTURE.md#epic-002-layered-foundation-reconciliation) revision 2 were the independently Accepted planning inputs. Current frozen F004 Solution and Bridge Target successors are revision 3; the composition Module remains revision 2.

The historical local [F004 Design revision 1](../FEATURE-004/DESIGN-REVISION-001.md), [historical System F004 Design revision 2](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-004/DESIGN-REVISION-002.md) and [System F001 Design revision 4](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-001/DESIGN.md) are Accepted. The latter freezes the exact portable operation/profile/schema target, lossless raw compile request, compiler-produced hierarchical PublicView, and distinct System-owned consumer-envelope schema/admission. [Independent decisions](../../../../../../architecture/reviews/EPIC-002-LAYERED-PLANNING-REVIEW.md) provide the approval evidence.

Frozen local F004 Design revision 2 and its independent local Validation are preserved separately. F001 source evolution is not accepted merely because historical Design revision 4 was accepted. Current source contains the composition projection module and test tooling described below, with source-only replay integration still in progress. Earlier provisional checks are retained under checks/source-integration; they do not validate the current complete source. Prototype entry sequencing was explicitly deferred; ordinary delivery/evidence gates and independent backtracking review remain open.

## Proposed Design and Public Boundary

Implement `DeclarativeCompositionProjector` and its immutable request/result records inside `Ghostagram.Bridge/DeclarativeCompositionProjection`. The Module depends only on D3 Composition, canonical D2/Core records and Bridge/Core presentation abstractions. It has no type/call dependency on Execution, palette/catalog, Server, Blazor, runtime ComponentModel, stores, resolver/provider, transport or control ports. Test/tooling may reference rendering or System implementation assemblies separately.

The public-view input is an explicit System compilation result or verified exchange result, raw System profile/context declarations, a typed immutable presentation sidecar and optional previous projection. Accepted inputs supply `CompositionIr.PublicView`; a Definition is inspected through that same view from accepted compilation or verified IR. The Module never traverses raw/private aggregates, compiles implicitly, recomputes compatibility or accepts a visibility predicate. Rejected System results retain their exact source diagnostic category and are projected as failure/status information without an accepted diagram.

Six additional typed `Project` overloads accept actual System `CompositionDecodeResult`, `CompositionEncodeResult`, `CompositionMigrationResult`, `CompatibilityInspectionResult`, `CompatibilitySelectionResult`, and `DefinitionReferenceResult` for diagnostic-only projection of rejected outcomes. Each preserves the exact source diagnostic collection through the same admitted projection invocation, with no fabricated compilation/verification result, IR identity or fresh diagram. A successful no-view input accidentally passed to one of these overloads returns local `GRAM-COMP-SOURCE-KIND` Unsupported while retaining successful source diagnostics; this defensive result is not a source rejection or a conformance pass.

The test runner separately executes any explicitly required successful Decode/Encode/Migrate output verification through the actual owner Verify port before Project. Successful Inspect, Negotiate or GetExact, and successfully verified non-IR records without PublicView, are semantic successes that are not projectable. Their exact operation, fixture/result evidence and reason are recorded as semantic inapplicability, separately from tooling. Their rejected variants remain required projection cases. Missing decoder variants, unmet prerequisites, absent owner observed-field contracts, unreached scheduled checkpoints or replay/producer mismatches remain failed/unavailable required cases; they cannot justify exclusion.

The projector receives System `ICompositionProfileAdmission` and `ICompositionOperationContextAdmission` ports plus a BCL `TimeProvider`. It admits profile/context once for its own projection invocation and uses the same admitted context throughout local traversal, sidecar merge and final publication. It delegates semantic admission/skew meaning and performs only local presentation bounds/freshness checks.

| Planned operation/type | Contract |
|---|---|
| `Project(source, profile, context, sidecar)` | Full read-only projection of an accepted System public view or explicit non-success; no source/sidecar mutation |
| `Refresh(previous, source, profile, context, sidecar)` | Same exact source anchor permits presentation-only delta; any semantic anchor discontinuity returns Stale with FullReprojectionRequired |
| `CompositionProjectionSourceAnchor` | Root Definition identity/revision, enclosing IR Content Identity, exact contract/compiler/profile/canonicalization/extension tuple and provenance/lineage anchors, including copied ordered actual IR ReferenceProvenance |
| `CompositionProjectionResult` | Closed Fresh, Stale, Unsupported, Skewed or Failed union; immutable anchor/presentation revision and distinctly attributed diagnostics; only Fresh carries a newly accepted complete diagram |
| `CompositionPresentationSidecar` | Immutable canonical-identity-keyed layout/waypoint/selection/viewport/display hints and its own revision |
| `CompositionProjectionDiagnostic` | Bounded public local code/path/message, separate from unchanged System diagnostics; never protected details or exception internals |

The PublicView does not contain a nested copy of Content Identity; take identity only from the enclosing accepted IR. Root/public component entries retain canonical occurrence/Definition identities, parent/owner boundaries, export chains, visibility, provenance and `IsOpaque`. Read `PublicRequirements`, `PublicAssignments` and `PublicImplementationReferences` as canonical VariableRequirement, VariableAssignment and ImplementationReference lists from each visible component view; opaque entries carry none. Display those and surfaces only as carried by the System views. Never infer a private member from a missing field, sidecar or name.

## Mapping, Presentation and Freshness

Map each producer public occurrence/surface identity to a deterministic diagram ID using a kind prefix plus base64url of an unambiguous length-prefixed canonical identity tuple. Retain the exact source identity separately. Detect duplicate/colliding keys before adding an element; never overwrite another source. IDs exclude presentation revision and remain stable when the same canonical declaration survives a compatible revision.

Keep nested Composite boundaries as nested groups/opaque nodes with their original parent identity. Opaque entries show only their supplied public boundary facts. Render capabilities, operations, IPort and IControl as display-only node properties: `Mode = DiagramPropertyModes.Display`, `Connectable = false`, no editor, and no runtime descriptor/handler. Set node `LabelEditable`, `Resizable` and `Rotatable` false. Declared ports are not `DiagramPort` connectors; no semantic edge/route/control operation is emitted. Presentation-only movement through an explicit sidecar change grants no definition-editing authority.

Use deterministic canonical child order and a simple nested layout with finite spacing/padding; derive node height from the displayed row count, and group bounds from children. Compatible sidecar geometry may override defaults after finite-coordinate and identity checks. Count raw sidecar entries before filtering; obsolete/orphan presentation entries are deterministically ignored and may produce one bounded summary diagnostic, never new source elements. This permits independent exact/max-plus-one sidecar-count tests without requiring nonexistent semantic elements. Unsupported custom renderer keys, callback-like metadata or private source keys are rejected, not loaded. Sidecar state never enters System identity or records.

Fresh requires exact source anchor plus the result's presentation revision. Refresh with the same anchor rebuilds complete proposed output first, then diffs only geometry/display/selection/viewport fields; its delta cannot alter semantic labels, identities, source descriptors or hierarchy. A change to source revision, content identity, profile, export/visibility, ordered ReferenceProvenance or lineage returns Stale and requires a separate full Project call. Last-known output may be retained only as explicitly stale; failure never relabels it Fresh.

## Bounds, Failure, Security and State

Use the accepted System conformance-v1 profile for source dimensions and context admission. A producer-side over-limit/malformed/skewed result stays explicit; it never becomes a fresh projection. Local checked traversal respects MaxRecursionDepth, MaxDefinitions/Constituents, MaxVisitedIdentities, string/schema/semantic-value bytes and diagnostic count/detail/aggregate bounds, checking cancellation/deadline before costly work and terminal publication.

Freeze a separate presentation profile `ghostagram.composition.projection/1`: maximum 2,048 raw sidecar entries, absolute coordinate magnitude 1,000,000 and 1,048,576 serialized UTF-8 projection bytes. Element/row counts derive from the bounded admitted public view using checked arithmetic; they are not separately tunable duplicate source limits. These are local resource limits, not weakened System semantics; check both local limits and applicable System limits, using the tighter applicable limit. System L32 limits conformance envelope observations only and is never reinterpreted as a Ghostagram layout allowance. Invalid local maxima, non-finite geometry or a named-profile value mismatch fail explicitly.

System Unsupported/Incompatible results map only to Unsupported/Skewed while preserving their source categories; malformed, bound, cancellation, deadline, collision, leakage or local failure yields Failed. Prefix local codes `GRAM-COMP-`; source codes remain unchanged. No fresh partial result, sidecar mutation, source mutation, private content, bindings, resolved payloads, provider locators, credentials or runtime handle is permitted. The Module owns no IO, service, persistence, execution or logging sink.

## Change Map

| Path | Change |
|---|---|
| `src/Ghostagram.Bridge/DeclarativeCompositionProjection/ProjectionContracts.cs` | Immutable input/anchor/sidecar/result union and local finite profile |
| `src/Ghostagram.Bridge/DeclarativeCompositionProjection/DeclarativeCompositionProjector.cs` | Pure public-view mapping, display-only descriptors, freshness and presentation delta |
| `src/Ghostagram.Bridge/Ghostagram.Bridge.csproj` | Direct portable Composition reference; no Module dependency on implementation/runtime/Server |
| `tests/Ghostagram.Bridge.Tests/DeclarativeComposition/` | Real projection, hierarchy/opacity, identity/freshness, finite/cancellation, immutability and negative-affordance checks |
| `tests/Ghostagram.Graph.Conformance/Composition/` and existing runner entry | Add composition projection profile, concrete consumer manifest and actual observed envelope |
| `tests/Ghostagram.Layout.Verification/CompositionPreview.cs` and existing runner entry | Opt-in visible preview of actual projection output through existing SvgDiagramExporter; production renderer/UI unchanged |
| Existing affected test project files and solution file | Test-only System support/implementation and preview input references as needed |

## Consumer Conformance and Visible Verification

Use the distinct System-owned ConsumerCaseManifest/ConsumerResultEnvelope schema and `IConsumerEnvelopeAdmission` from existing Composition Conformance Support. The runner first admits exact producer corpus/manifest and producer envelope. It executes actual Project/Refresh calls, never labels compiler, exchange, corpus admission or producer-run output as Ghostagram execution.

Prepare source operations explicitly, then materialize the concrete manifest before any consumer Project/Refresh case execution. The test-only System CompositionFixtureExecutionDriver constructs actual owner ports and applies the exact raw fixture profile/context and real owner checkpoint schedule. The portable module has no dependency on this driver. Replay explicit Compile/Decode prerequisites with exact fixture/output anchors, bounded dependency traversal and cycle rejection; bind actual accepted records through the owner prerequisite APIs. Do not infer operations from case names or substitute a normal profile/context, entry cancellation or alternate operation for declared controls. Retain ordered owner operations, prerequisite receipts and observed checkpoints separately from consumer calls.

Materialize a concrete manifest before running. For every admitted producer fixture exposing an accepted public view or an explicit rejected public result, create `GRAM:{producerCaseId}:PROJECT` with originating IDs/digests and required public fields; producer tooling-only fixtures lacking such a source are explicitly inapplicable. Separately record actual matching semantic successes without a PublicView as non-projectable, with exact owner result evidence. Neither category counts as a consumer pass. A rejection, incomplete owner observation, mismatch, unavailable variant or unreached checkpoint remains required and cannot be filtered out. Add exact consumer entries `GRAM-NESTED-OPAQUE`, `GRAM-PUBLIC-EXPORTS`, `GRAM-DIAGNOSTICS`, `GRAM-IR-ANCHOR`, `GRAM-INERT-SURFACES`, `GRAM-SAME-SOURCE-DELTA`, `GRAM-SOURCE-STALE`, `GRAM-UNSUPPORTED`, `GRAM-SKEWED`, `GRAM-COLLISION`, `GRAM-PRIVATE-LEAK`, `GRAM-CANCEL`, `GRAM-DEADLINE`, `GRAM-NONMUTATION`, and `GRAM-NO-RELABEL`. Add named exact/max-plus-one/invalid cases for each applicable local dimension and System profile/context admission; realize all fixture data rather than shipping patterns alone.

Evidence and independent Validation freeze the concrete manifest digest outside the runner. `ConsumerEnvelopeAdmissionRequest` binds it to admitted producer inputs and the raw consumer candidate. System admission validates full disjoint passed/failed/missing/unsupported coverage and exact producer/compiler/profile/schema/fixture anchors. Publish only an admitted envelope; invalid/missing/failed/unsupported cases fail conformance. No child schema or approval claim enters the output.

| Criterion | Required real-seam checks and visible evidence |
|---|---|
| AC-001 | Project recursive public/opaque hierarchy with exact parent boundaries, exports, requirements/assignments and implementation-reference facts; compare canonical identities and visibly inspect nested diagram structure. |
| AC-002 | Project representative rejected System results; retain category/code/actionable public meaning in result and visible status panel, without runtime/execution claims. |
| AC-003 | Check source root/IR anchors, enclosing identity, exact profile/provenance, unresolved references and immutable input/result; no resolution or instance creation. |
| AC-004 | All four descriptor kinds use display-only nonconnectable properties; inspect public APIs and generated model for no editing/route/control operations; visibly confirm no such affordances in exported output. |
| AC-005 | Same-source sidecar revision yields presentation-only delta and unchanged semantic facts; semantic discontinuity returns Stale and FullReprojectionRequired; full Project restores Fresh only for the exact new source. |
| AC-006 | Exercise every Fresh/Stale/Unsupported/Skewed/Failed arm, supported/unknown tuples and source failure; admitted consumer envelope binds exact producer manifest/envelope and concrete case inventory. |
| AC-007 | Applicable source/profile and local exact/+1/invalid bounds, controlled cancellation/deadline, identity collision and private leak attempts return no fresh partial output; snapshot source/sidecar before/after to prove no mutation. |

Visible verification reuses the existing `Ghostagram.Server.Export.SvgDiagramExporter.Export(DiagramSnapshot)` from `src/Ghostagram.Server/Export/SvgDiagramExporter.cs`, already exercised by `tests/Ghostagram.Layout.Verification`. Add an opt-in `--composition-preview <projection-result.json> --output <directory>` mode to that existing test executable. It reads actual conformance Project/Refresh result JSON, serializes the returned DiagramDocument using existing camel-case diagram conventions, constructs a DiagramSnapshot with the result revision and calls that exporter. It emits the unmodified renderer SVG plus an HTML-escaped source/status/diagnostic panel; it draws no substitute nodes or boundaries. Rejected results produce a status panel without a fresh diagram.

Open those actual SVG/panel artifacts visibly at full size for recursive/opaque, descriptor, rejected, stale and unsupported/skewed cases. Record screenshot paths, source/result digest and findings on labels, clipping, nested bounds, visible public fields and absence of edit/connect/control affordances. Missing visual capability is an explicit uncompleted delivery check. Static SVG cannot prove a future interactive host's controls are disabled; F003 delivers a read-only library/export surface and claims no production UI integration.

Build Bridge and changed test tooling in Release; run `dotnet run --project tests/Ghostagram.Bridge.Tests -c Release`, the existing conformance project with the newly assigned `--profile composition` selector and explicit producer/consumer-manifest/output paths, and `dotnet run --project tests/Ghostagram.Layout.Verification -c Release -- --composition-preview ... --output ...`. Preserve existing default harness modes. Store concrete manifest, admitted envelope, actual projection JSON, SVG/panel/screenshots and command results under this Feature's `conformance/` and `visual/`; map all seven criteria in EVIDENCE and independent Validation.

## Rollout, Compatibility and Reversal

Design acceptance permits the ordinary implementation handoff only after FEATURE-004 and FEATURE-001 portfolio delivery acceptance plus their required child Evidence, independent Validation, exact producer manifest/envelope and source-output fingerprints. Design readiness never substitutes for those artifacts. Record the live local dirty baseline and coordinate the same accepted System source outputs; preserve unrelated work and all F004 package changes.

Use the accepted v1 contract/compiler/profile and predecessor exchange support supplied by System. This consumer adds no wire version, compatibility shim, deprecation window, publication process or migration framework. A semantic incompatibility returns to System; the consumer never repairs it locally. Reversal removes only this Feature's local changes/outputs before adoption while preserving F004 and System history. Later published evidence/identities are retained with explicit successors.

## Risks and Remaining Delivery Duties

The typed diagnostic-only input and result-based applicability clarification above requires independent revision 2 review. The producer artifacts do not yet prove delivered behavior; implementation must use the accepted delivered fingerprints, realize every concrete fixture/case-manifest entry, exercise real consumer operations, and produce independent local Validation before portfolio acceptance. Any unimplemented verification, failed case, missing output or unavailable visible check remains explicit and cannot be reported as passed.

## Approval Record

| Field | Value |
|---|---|
| Mode | User-named independent approver; prototype backtracking |
| Author | Ghostagram child implementation coordinator |
| Approver | elon-musk; review pending |
| Decision | Pending; InReview revision 2 is not Accepted |
| Recorded | Submitted 2026-09-07T11:59:35Z; decision Pending (not an acceptance timestamp) |
| Evidence | Actual source is being completed; final generation builds, producer-linked conformance, visible checks and independent Validation remain open. Historical revision 1 approval is preserved in DESIGN-REVISION-001.md. |
| Bypass reason | Prototype entry sequencing only, under PROTOTYPE-RUN-20260907T094049Z; no delivery acceptance bypass. |
