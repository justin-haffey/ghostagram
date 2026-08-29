---
title: "Governed Semantic Links — Ghostagram Design"
artifact_type: "design"
id: "DESIGN-EPIC-001-FEATURE-003-GHOSTAGRAM"
status: "Accepted"
authority: "solution"
scope: "ghostagram"
parent: "IMPL-PLAN-EPIC-001-FEATURE-003"
upstream:
  repository: "ghostworx"
  artifact_id: "IMPL-PLAN-EPIC-001-FEATURE-003"
  path: ".swe/epics/001-semantic-system-foundation/features/003-governed-semantic-links/IMPLEMENTATION-PLAN.md"
  revision: "None"
traceability:
  epic:
    repository: "ghostworx"
    artifact_id: "EPIC-001"
    path: ".swe/epics/001-semantic-system-foundation/EPIC.md"
    revision: "None"
  feature:
    repository: "ghostworx"
    artifact_id: "FEATURE-003"
    path: ".swe/epics/001-semantic-system-foundation/features/003-governed-semantic-links/FEATURE.md"
    revision: "None"
  implementation_plan:
    repository: "ghostworx"
    artifact_id: "IMPL-PLAN-EPIC-001-FEATURE-003"
    path: ".swe/epics/001-semantic-system-foundation/features/003-governed-semantic-links/IMPLEMENTATION-PLAN.md"
    revision: "None"
  architecture:
    repository: "ghostagram"
    artifact_id: "ARCH-SOLUTION-GHOSTAGRAM"
    path: "architecture/SOLUTION-ARCHITECTURE.md"
    revision: "None"
prerequisites:
  feature_001_portfolio_validation:
    repository: "ghostworx"
    artifact_id: "VALIDATION-EPIC-001-FEATURE-001-PORTFOLIO"
    path: ".swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/VALIDATION.md"
    revision: "None"
  feature_002_portfolio_validation:
    repository: "ghostworx"
    artifact_id: "VALIDATION-EPIC-001-FEATURE-002-PORTFOLIO"
    path: ".swe/epics/001-semantic-system-foundation/features/002-graph-native-variable-system/VALIDATION.md"
    revision: "None"
owners:
  - "dennis-ritchie (Ghostagram Design author)"
created: "2026-08-28"
updated: "2026-08-28"
template_version: "2.0.0"
---

# Governed Semantic Links — Ghostagram Design

## Assignment and Boundaries

This Design implements only the bounded `ghostagram` consumer-conformance assignment in the accepted [FEATURE-003 Plan](../../../../../../.swe/epics/001-semantic-system-foundation/features/003-governed-semantic-links/IMPLEMENTATION-PLAN.md). It extends the accepted [Ghostagram Solution Target](../../../../architecture/SOLUTION-ARCHITECTURE.md) and the already delivered FEATURE-001/FEATURE-002 test-tooling runner through the real `Ghostagram.Bridge` full- and delta-projection seams. Ghostagram consumes System-owned Semantic Link records and reports observations made by its own projection boundary; it does not implement or reinterpret link semantics.

The sequential gates are closed: [FEATURE-001 portfolio Validation](../../../../../../.swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/VALIDATION.md), [FEATURE-002 portfolio Validation](../../../../../../.swe/epics/001-semantic-system-foundation/features/002-graph-native-variable-system/VALIDATION.md), Ghostagram [FEATURE-001 local Validation](../FEATURE-001/VALIDATION.md), and Ghostagram [FEATURE-002 local Validation](../FEATURE-002/VALIDATION.md) are independently `Accepted`. System FEATURE-003 implementation is currently in progress: provisional Semantic Link Core, Serialization, corpus/profile, and reusable neutral-support files exist, but they are not yet a delivery-frozen, evidenced, and independently validated contract/corpus boundary. Ghostagram implementation remains stopped until the System owner completes that delivery, records the exact frozen corpus/profile/schema digests and passing producer result, and publishes Complete Evidence plus independently Accepted local Validation under the accepted [System FEATURE-003 Design](../../../../../../repos/ghostworx-system/.swe/implementations/EPIC-001/FEATURE-003/DESIGN.md).

### Assigned outcome

- Execute every corpus case assigned to participant `ghostagram` through the existing Bridge full-projection or delta-projection seam, or through the System-owned compatibility gate when rejection must occur before projection.
- Observe canonical Link identity and authority, Link Kind, exactly two ordered endpoint roles, local or origin-preserving federated endpoint identity, Link revision, observation revision, and projection freshness without resolving or mutating the Link.
- Build a rebuildable Ghostagram view of System-owned backreference entries, invalidate it from delivered public invalidation facts, and rebuild from authoritative inputs without minting, retargeting, tombstoning, or otherwise owning canonical Link state.
- Prove presentation fields and private/runtime fields do not enter portable Link records, public observations, canonical state, console output, or the result envelope.
- Emit one schema-, corpus-, profile-, digest-, and invariant-valid envelope with fixed participant identity `ghostagram`, separate from FEATURE-001 Graph and FEATURE-002 Variable evidence.
- Preserve every non-assigned manifest case as explicit `unsupported`; fail closed when an assigned Ghostagram case has no approved real-seam mapping.

### Explicit exclusions

- Semantic Link authority, identity, Link Kind or role meaning, endpoint validation, link lifecycle, successor/tombstone, observation, backreference, freshness, outcome, serialization, migration, or profile semantics.
- A second graph, Link, endpoint, vocabulary, resolver, projection, or evidence authority; copied System expected outcomes; relabeled System producer/adapter output.
- Production `Ghostagram.Bridge` API changes; graph/link mutation; implicit endpoint resolution; authorization; persistence; runtime activation; Port, Binding, Contract, Channel, route, transport, control, or endpoint behavior.
- UI/browser acceptance, JavaScript, Blazor Laboratory, SignalR, Server live Projection or Control Ports, operational overlays, deployment, publication, release, or architecture promotion.

## Current State

| Area | Verified current fact | Design consequence |
|---|---|---|
| Feature gates | Portfolio and Ghostagram local Validation for FEATURE-001 and FEATURE-002 are `Accepted` | FEATURE-003 Design entry is open and the earlier child-runner capacity interruption is not a lifecycle blocker |
| Reusable runner | `tests/Ghostagram.Graph.Conformance` fixes participant identity for FEATURE-001/002, validates System-owned inputs, and writes child envelopes | Add one explicit `feature-003` route; do not create another executable or accept participant identity from the caller |
| Reusable tests | `tests/Ghostagram.Graph.Conformance.Tests` executes the real adapter and validates attribution, partition, public output, and repeatability | Add FEATURE-003-only tests while preserving all FEATURE-001/002 behavior |
| Full projection | `IGraphDiagramProjection.Project` maps authoritative `GraphSnapshot` plus separately owned `GraphPresentationSnapshot` into a rebuildable `DiagramDocument` | Project only public System Link/backreference records and inspect returned document fields |
| Delta projection | `IGraphDiagramDeltaProjector.Project` advances a compatible authoritative change batch or returns `RequiresFullProjection` | Stale/gapped/skewed backreference views cannot be labeled fresh; rebuild uses authoritative inputs |
| Presentation ownership | `GraphPresentationSnapshot` and `presentationRevision` are independent of System graph state | Layout, viewport, selection, bounds, waypoints, and presentation revision cannot become Link identity, role, origin, or freshness facts |
| Semantic Link delivery | Partial provisional source exists under `src/Ghostworx.System.Core/Graph/Links/`, `src/Ghostworx.System.Graph.Serialization/Links/`, and `tests/Ghostworx.System.SemanticLinks.Conformance/Support/`; provisional `Corpus/V1` and profile paths also exist, but System FEATURE-003 has no Complete `EVIDENCE.md` or independently Accepted `VALIDATION.md` | Treat current files as in-progress implementation, not a frozen delivery boundary; preflight stops until exact public/support project locators, frozen corpus/profile/schema digests, passing producer output, Complete Evidence, and Accepted Validation are recorded |
| Worktree | Ghostagram contains unrelated dirty/untracked renderer, documentation, test, scaffold, and prior Feature work | Implementation edits only the Change Map and preserves all unrelated paths |

## Proposed Design

### Fixed neutral-support runner

`tests/Ghostagram.Graph.Conformance/Program.cs` remains the single non-deployable entry point. Parsing changes from the current “any `--feature` means FEATURE-002” behavior to an exact closed discriminator: absent `--feature` preserves FEATURE-001, `--feature feature-002` preserves FEATURE-002, and `--feature feature-003` selects `Feature003RunnerOptions`. Every other value returns non-zero before corpus loading or Bridge invocation.

The FEATURE-003 command requires `--corpus`, `--profile`, `--result`, and `--participant-version`. Participant identity is a compile-time constant equal to `ghostagram`; there is no `--participant` option. The runner consumes the delivered System-owned neutral corpus/result support and writes only:

`.swe/implementations/EPIC-001/FEATURE-003/results/ghostagram/semantic-link-v1.json`

`Feature003ConformanceRunner` executes this sequence:

1. Resolve the corpus root, profile, schemas, manifest, and every fixture beneath the declared root; reject absolute paths, traversal, aliases outside the root, unknown files, malformed canonical bytes, or unsupported versions.
2. Invoke the delivered System-owned Semantic Link corpus loader, canonical digest validation, finite-profile validation, result writer, schema validator, and envelope invariants. It never invokes a System producer or authority-adapter participant.
3. Verify the manifest declares `ghostagram`; derive the required Ghostagram partition from immutable participant and operation fields and compare it to a reviewed closed case/operation allowlist.
4. Dispatch assigned cases through `Feature003CaseAdapter`; emit one explicit `unsupported` observation for each non-assigned case.
5. Validate exactly one result per manifest case, exact assigned/unsupported partition, public-field allowlists, stable categories, bounded diagnostics, summary counts, canonical encoding, and corpus/profile/schema/result digests.
6. Replace the output atomically only after validation succeeds. Return zero only when every assigned case passes, every non-assigned case is explicitly unsupported, and no case is failed, missing, malformed, skewed, or privately contaminated.

The runner is “neutral support” because System owns all fixture, profile, digest, schema, and envelope rules while Ghostagram owns only invocation of its real projection seam and observation of returned state. Reusing System support cannot become execution of a System participant, and a System result cannot be copied or relabeled.

### Feature-specific Bridge mapping

`Feature003CaseAdapter` owns no Semantic Link rules. It uses delivered System public serializers/contracts to materialize already validated public inputs, then selects one closed handler by manifest operation. Each handler calls `Feature003BridgeHarness`, which builds a fresh in-memory owner graph containing public Link/backreference projection records, captures authoritative graph and presentation snapshots, invokes the existing Bridge seam, and derives observations only from the returned `DiagramDocument`, `GraphDiagramOperationBatch`, and authoritative before/after snapshots.

| Mapping category | Actual seam | Required observation |
|---|---|---|
| Canonical Link projection | `IGraphDiagramProjection.Project` | Link canonical address/authority, accepted kind, Link revision, lifecycle, and exactly two endpoint entries remain byte/semantically equivalent to the System public record |
| Endpoint origin and ordered roles | `IGraphDiagramProjection.Project` | Endpoint ordinal and role order are unchanged; local endpoint stays a canonical Semantic Address; remote endpoint retains the complete origin-preserving Federation Reference |
| Observation revision | `IGraphDiagramProjection.Project` | Exact Link revision, observed endpoint revisions when present, profile/provenance/freshness facts remain explicit and do not claim simultaneous or distributed-snapshot state |
| Backreference rebuild | `IGraphDiagramProjection.Project` over System `SemanticLinkProjectionBuilder` output | Entries retain canonical Link identity, Link/observation revision, endpoint origin, ordinal/role, projection revision, and freshness; duplicate canonical Link identity is rejected by System support before Bridge invocation |
| Freshness and invalidation | `IGraphDiagramDeltaProjector.Project`, followed by full projection only when requested | Contiguous public invalidation advances or marks the view stale; a gap, incompatible path, or skew requests authoritative rebuild and never claims stale output is fresh |
| Presentation/private-field exclusion | Full projection, delta output, presentation snapshot, and recursive output scans | No layout, bounds, viewport, selection, waypoints, presentation revision, credential, physical/private locator, runtime binding, authorization, payload, or provider detail enters portable semantic observations |
| Consumer-version skew | System compatibility gate before any Bridge call | Unsupported contract/profile/schema/extension is stable and explicit; Bridge invocation count is zero and prior state/output remains unchanged |

The harness may represent a delivered public Link or backreference document in a Graph node’s durable JSON metadata solely to traverse the existing generic projection mapper. The metadata value must be the canonical public bytes parsed by System APIs; the harness may not reconstruct meaning from expected fixture fields, create a foreign `INode`, model a Link as a cross-graph `GraphEdge`, or add a Link-specific production mapper. A required case that cannot traverse the existing Bridge projection without a production API change is a design-drift failure and returns the Plan/Target to review.

### Backreference authority and non-mutation

- The System-produced `SemanticLinkProjectionEntryDocumentV1` is the only backreference input. Ghostagram creates no canonical backreference record and owns no discovery index semantics.
- Each case snapshots canonical Link records, owner graph state, endpoint graph identity facts, and presentation state before Bridge execution.
- Full projection and delta projection are read-only with respect to canonical Link/endpoint state. After execution, the harness proves canonical bytes/revisions and endpoint graphs are identical.
- Invalidation produces a new derived public projection view or an explicit full-reprojection request. It cannot revise, retarget, tombstone, observe, resolve, or authorize a Link.
- Rebuild discards the stale derived Diagram view and projects fresh System-authoritative entries. It cannot merge stale presentation data into Link identity, origin, roles, or freshness.

### Partition and fail-closed behavior

- `assigned`: manifest participant requirements include `ghostagram` and operation belongs to one of the seven approved mapping categories above.
- `unsupported`: manifest does not require `ghostagram`; record the exact case/operation and a bounded neutral-support reason.
- `design-drift failure`: manifest requires `ghostagram` but the case lacks an allowlisted handler, demands mutation/resolution/persistence/UI/runtime behavior, or cannot use the existing Bridge seam.
- `missing`: manifest has no exactly corresponding result.
- `failed`: executed Ghostagram observations disagree with the frozen expected public outcome/category or violate the System envelope invariants.

Tests freeze the delivered assigned case IDs and operation names only after matching the immutable manifest and its digest. Corpus change never silently activates a handler. Every case appears exactly once, and `assigned + unsupported = manifest total` for an eligible Ghostagram envelope.

### Failure and state semantics

- Every case uses a fresh in-memory graph, Bridge projection, and presentation store; no state crosses cases.
- Malformed input, digest/profile/schema mismatch, unknown assigned operation, timeout/cancellation, bounds violation, leakage, duplicate result, missing result, or invariant failure returns non-zero and leaves any previous valid result untouched.
- Unsupported/skewed/failure paths do not invoke Bridge unless the case explicitly requires an observed Bridge failure; invocation counters and before/after snapshots prove the boundary.
- Diagnostics are stable, bounded, and redacted. No absolute path, machine/user name, raw exception, credential, endpoint, private locator, protected payload, provider policy, or stack trace enters public output.

## Interfaces, Data, and Contracts

- Normative meaning remains in the accepted [Semantic Link contract](../../../../../../architecture/contracts/SEMANTIC-LINK.md), [ADR-004](../../../../../../architecture/decisions/ADR-004-semantic-link-ownership-lifecycle.md), [Semantic Graph and Federation contract](../../../../../../architecture/contracts/SEMANTIC-GRAPH-AND-FEDERATION.md), and [ADR-001](../../../../../../architecture/decisions/ADR-001-semantic-authority-federation-vocabulary.md).
- Test/tooling dependencies point from Ghostagram to delivered System Semantic Link Core, Serialization, and neutral conformance support. No Ghostagram production project references System conformance tooling, and no System production project references Ghostagram.
- The implementation uses the exact delivered System public types, including `SemanticLinkDefinition`, the closed local/federated endpoint-reference union, canonical Link/observation/projection documents, `SemanticLinkProjectionBuilder` output, and `SemanticLinkConformanceProfile`; Ghostagram defines no substitutes.
- If the delivered System conformance executable does not expose a reusable neutral corpus/result support boundary, implementation stops for Design/architecture review rather than copying validators or executing a System participant to manufacture the envelope.
- Canonical Link and observation documents remain immutable System-authority inputs. `DiagramDocument` is rebuildable Ghostagram projection output; `GraphPresentationSnapshot` remains a separate Ghostagram-owned sidecar.
- The System integration owner copies the child envelope byte-identically to its intake locator `results/ghostagram/semantic-link-v1.json`; Ghostagram owns the source envelope and its Evidence.

## Failure, Security, Observability, and Operations

- The adapter is local, non-interactive, network-free, and test/tooling-only. It opens no listener and requires no browser, Server, SignalR, credential, provider, remote authority, protected registry, production data, or deployed resource.
- Console output is limited to participant, corpus/profile digests, case counts, exit status, and the repository-relative result path. Detailed public observations remain in the canonical envelope.
- Unsupported version/profile/schema/extension skew is explicit; no permissive fallback or projection attempt occurs.
- Leakage tests may use synthetic sentinels only inside the process. Sentinel values must not appear in persisted Evidence or envelope output.
- Smart App Control or apphost restrictions may be bypassed only by the already accepted signed `dotnet <dll>` host execution. No policy, trust, registry, or machine configuration change is authorized.

## Change Map

| Area or path | Change | Owner |
|---|---|---|
| `tests/Ghostagram.Graph.Conformance/Program.cs` | Replace ambiguous feature routing with exact FEATURE-001/002/003 dispatch and unknown-feature rejection | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Feature003ConformanceRunner.cs` | FEATURE-003 options, fixed `ghostagram` participant, neutral System support, partition, validation, and atomic output | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Feature003CaseAdapter.cs` | Closed assigned-operation dispatch and explicit unsupported mapping | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Feature003BridgeHarness.cs` | Actual Link/backreference full/delta projection observations, freshness, non-mutation, and exclusion scans | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Ghostagram.Graph.Conformance.csproj` | Add only delivered System Semantic Link contract, serialization, and neutral conformance-support test/tool references | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance.Tests/Program.cs` | Preserve FEATURE-001/002 tests and select FEATURE-003 verification exactly | Ghostagram conformance tests |
| `tests/Ghostagram.Graph.Conformance.Tests/Feature003Tests.cs` | Attribution, exact partition, real seam, Link/origin/role/revision, backreference rebuild/invalidation, exclusion, skew, bounds, digest/schema, and repeat-run tests | Ghostagram conformance tests |
| `.swe/implementations/EPIC-001/FEATURE-003/results/ghostagram/semantic-link-v1.json` | Generated attributable FEATURE-003 envelope | Ghostagram conformance tooling |
| `.swe/implementations/EPIC-001/FEATURE-003/EVIDENCE.md` | Commands, versions, gates, corpus/profile/schema/digests, observations, partition, exclusions, and unresolved behavior | Ghostagram implementation owner |

No other file is authorized. In particular, implementation does not edit `src/Ghostagram.Bridge`, `src/Ghostagram.Blazor`, `src/Ghostagram.Server`, JavaScript, renderer/docs, existing dirty Bridge/Cutover tests, portfolio artifacts, System source, architecture, or local `VALIDATION.md`.

## Test and Evidence Plan

### Planned commands

From `repos/ghostagram` after System promotes the current provisional Semantic Link artifacts into the frozen, evidenced, and independently validated delivery boundary required above:

```powershell
dotnet restore .\Ghostagram.slnx
dotnet build .\Ghostagram.slnx -c Release --no-restore
dotnet .\tests\Ghostagram.Graph.Conformance.Tests\bin\Release\net10.0\Ghostagram.Graph.Conformance.Tests.dll --feature feature-003 --corpus ..\ghostworx-system\tests\Ghostworx.System.SemanticLinks.Conformance\Corpus\V1 --profile ..\ghostworx-system\tests\Ghostworx.System.SemanticLinks.Conformance\Profiles\semantic-link-conformance-v1.json
dotnet .\tests\Ghostagram.Graph.Conformance\bin\Release\net10.0\Ghostagram.Graph.Conformance.dll --feature feature-003 --corpus ..\ghostworx-system\tests\Ghostworx.System.SemanticLinks.Conformance\Corpus\V1 --profile ..\ghostworx-system\tests\Ghostworx.System.SemanticLinks.Conformance\Profiles\semantic-link-conformance-v1.json --result .\.swe\implementations\EPIC-001\FEATURE-003\results\ghostagram\semantic-link-v1.json --participant-version <source-revision>
dotnet .\tests\Ghostagram.Bridge.Tests\bin\Release\net10.0\Ghostagram.Bridge.Tests.dll
dotnet .\tests\Ghostagram.Cutover.Tests\bin\Release\net10.0\Ghostagram.Cutover.Tests.dll
dotnet .\tests\Ghostagram.Graph.Conformance\bin\Release\net10.0\Ghostagram.Graph.Conformance.dll --feature feature-002 --fixtures ..\ghostworx-system\tests\Ghostworx.System.Variable.Conformance\Fixtures\variable\v1 --result "$env:TEMP\ghostagram-feature002-regression.json" --participant-version regression
git diff --check -- .\tests\Ghostagram.Graph.Conformance .\tests\Ghostagram.Graph.Conformance.Tests .\.swe\implementations\EPIC-001\FEATURE-003
```

The implementer also invokes the delivered System FEATURE-003 verifier in verify-only mode against the unchanged child envelope and records the exact command and result. A blocked apphost is replaced with `dotnet <dll>` as shown. `<source-revision>` is resolved to the exact revision or `None`; it is not emitted literally. Temporary FEATURE-002 regression output is not evidence and is removed by the runner/test lifecycle.

### Criterion traceability

| Criterion | Ghostagram verification | Planned evidence location |
|---|---|---|
| AC-001 | Deserialize one System-owned Link surface and project its canonical address/authority, accepted kind, two ordered roles, and revision with no host/UI/store dependency | `EVIDENCE.md#ac-001`; `results/ghostagram/semantic-link-v1.json` |
| AC-002 | Preserve local Semantic Address versus origin-preserving Federation Reference and show forbidden/bare cross-graph forms fail in System support before Bridge invocation | `EVIDENCE.md#ac-002`; endpoint-origin matrix and envelope cases |
| AC-003 | Snapshot Link and both endpoint graph facts before/after all projection paths; prove Bridge projection never performs owner-local or endpoint mutation | `EVIDENCE.md#ac-003`; non-mutation matrix and invocation observations |
| AC-004 | Preserve canonical predecessor/current identity facts and reject any fixture that presents retarget/retype as an in-place projection update | `EVIDENCE.md#ac-004`; identity/history projection cases |
| AC-005 | Preserve exact Link/observation revisions, endpoint identities/revisions, compatibility/profile/provenance/time/freshness without a distributed-snapshot claim | `EVIDENCE.md#ac-005`; observation projection matrix |
| AC-006 | Propagate stable public outcomes assigned to Ghostagram, preserve redaction/bounds, and prove unsupported/failure/skew paths are non-mutating | `EVIDENCE.md#ac-006`; outcome/non-mutation matrix and envelope |
| AC-007 | Project supported canonical Link/observation/projection documents after System round-trip validation and reject version/migration skew before Bridge use | `EVIDENCE.md#ac-007`; exchange/skew matrix |
| AC-008 | Build, invalidate, and rebuild backreference views retaining canonical Link/origin/role/revision/freshness; prove no projection mutation API or canonical state change | `EVIDENCE.md#ac-008`; backreference freshness/authority matrix and envelope |
| AC-009 | Scan canonical inputs, `DiagramDocument`, delta operations, presentation snapshot, diagnostics, console, and envelope for presentation/private/runtime/authorization/Port leakage | `EVIDENCE.md#ac-009`; exclusion scan matrix |
| AC-010 | Execute the frozen `ghostagram` partition through the real Bridge seam with fixed attribution; detect duplicate identity, role/kind redefinition, bare address, implicit resolution, leakage, and skew | `EVIDENCE.md#ac-010`; participant partition and `results/ghostagram/semantic-link-v1.json` |
| AC-011 | Separate accepted FEATURE-001/002 prerequisites, new FEATURE-003 behavior, baseline reuse, changed files, unsupported cases, and GhostPort/UI/runtime exclusions | `EVIDENCE.md#ac-011`; prerequisite and baseline/change/unresolved matrices |
| AC-012 | Consume the finite System profile, exercise assigned boundary/boundary-plus-one cases, and prove over-limit work cannot corrupt canonical, endpoint, presentation, or prior output state | `EVIDENCE.md#ac-012`; finite-limits/integrity matrix and envelope |

Independent local Validation evaluates all twelve criteria against Ghostagram's bounded assignment. It does not claim System-owned construction, lifecycle, observation, migration, or profile delivery from Ghostagram evidence alone.

## Rollout, Compatibility, and Reversal

Delivery is additive to an existing non-deployable test/tooling project. FEATURE-001 remains the default CLI path and FEATURE-002 retains the exact `feature-002` path and envelope semantics. FEATURE-003 adds only the exact `feature-003` discriminator; unknown values fail instead of falling through. No data migration, deployment, package publication, UI rollout, or production compatibility window applies.

Before evidence handoff, reversal is removal of the FEATURE-003 files, dispatch branch, test/tool references, and generated FEATURE-003 envelope while preserving FEATURE-001/002 behavior and every unrelated dirty file. A handed-off envelope is immutable evidence tied to its corpus/profile/digests; superseding it requires a new execution and explicit evidence history, not silent overwrite or relabeling.

## Risks and Divergence

| Risk | Impact | Mitigation / stop condition |
|---|---|---|
| Provisional System Semantic Link source/corpus/support is incomplete, changes shape, or lacks frozen delivery evidence | Ghostagram could bind to unstable contracts, copy semantics, or invent a parallel validator | Stop before implementation; require exact delivered public/support locators, frozen digests, a passing System producer, Complete Evidence, and independently Accepted Validation |
| Existing generic metadata projection cannot represent an assigned Link/backreference public record | A Bridge production change would broaden the accepted allocation | Record the required case unsupported and return Plan/Target/Design to review; do not modify Bridge |
| Expected fixture fields are used as observations | Envelope would be relabeled System evidence | Require returned Bridge state and invocation counters for every pass |
| Presentation state contaminates canonical Link identity/freshness | Rebuildable UI data becomes semantic authority | Separate snapshots, recursive exclusion scans, and canonical before/after equality |
| Backreference projection becomes mutable or authoritative | Ghostagram could retarget/tombstone canonical Links | Consume only immutable System projection entries; expose no mutation/resolution path |
| Cross-repository test reference is machine-layout-dependent | Runner portability can drift | Use repository-relative project references only; stop if System does not expose a consumable neutral-support boundary |
| Dirty worktree overlaps planned paths | User or prior Feature work could be overwritten | Inspect exact diff before every edit and preserve all paths outside the Change Map |

No architecture divergence is introduced by this Design. A required production Bridge change, second authority, runtime/UI dependency, new persistence, mutation/resolution path, or copied conformance rule is a stop condition requiring architecture and Plan review.

## Review History

| Cycle | Reviewer | Decision | Recorded | Findings and disposition |
|---|---|---|---|---|
| 1 | elon-musk | REVISE | 2026-08-28T19:28:42.5266047-04:00 | The baseline incorrectly stated that no live System Semantic Link source existed. Repaired for cycle two by identifying the current provisional Core, Serialization, neutral-support, corpus, and profile paths while preserving the fail-closed distinction between in-progress files and a delivery-frozen, evidenced, independently validated System boundary. |
| 2 | elon-musk | Accepted | 2026-08-28T19:33:41.4505403-04:00 | Approved without conditions. The repaired current-state section now truthfully identifies provisional Semantic Link Core, Serialization, neutral-support, corpus, and profile files while withholding delivery claims until frozen digests, a passing producer result, Complete System Evidence, and independently Accepted System Validation exist. The Design preserves fixed `ghostagram` attribution, real Bridge full/delta projection, canonical Link/origin/ordered-role/revision observations, rebuildable non-authoritative backreference freshness/invalidation, presentation/private-field exclusion, explicit skew and unsupported outcomes, all twelve criterion mappings, and the no-second-authority/UI/browser/live-operation/persistence/mutation/resolution/relabel boundaries. |

## Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | dennis-ritchie (`design_ghostagram_f3`) |
| Approver | elon-musk |
| Decision | Accepted |
| Recorded | 2026-08-28T19:33:41.4505403-04:00 |
| Evidence | Cycle-two independent review approved without conditions. The current baseline is accurate about provisional System Link implementation while remaining fail-closed on frozen delivery evidence and Validation; exact prerequisite gates, accepted Plan/Target, fixed `ghostagram` neutral-support runner, real Bridge mapping, Link/origin/ordered-role/revision preservation, backreference freshness/invalidation, exclusion/skew behavior, AC-001 through AC-012 traceability, and explicit scope boundaries are implementation-ready and coherent. |
| Bypass reason | None |
