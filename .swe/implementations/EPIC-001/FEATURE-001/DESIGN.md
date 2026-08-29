---
title: "Ghostagram Trusted Semantic Graph Conformance Adapter Design"
artifact_type: "design"
id: "DESIGN-EPIC-001-FEATURE-001-GHOSTAGRAM"
status: "Accepted"
authority: "solution"
scope: "ghostagram FEATURE-001 assignment"
parent: "IMPL-PLAN-EPIC-001-FEATURE-001"
upstream:
  repository: "ghostworx"
  artifact_id: "IMPL-PLAN-EPIC-001-FEATURE-001"
  path: ".swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/IMPLEMENTATION-PLAN.md"
  revision: "None"
traceability:
  epic:
    repository: "ghostworx"
    artifact_id: "EPIC-001"
    path: ".swe/epics/001-semantic-system-foundation/EPIC.md"
    revision: "None"
  feature:
    repository: "ghostworx"
    artifact_id: "FEATURE-001"
    path: ".swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/FEATURE.md"
    revision: "None"
  implementation_plan:
    repository: "ghostworx"
    artifact_id: "IMPL-PLAN-EPIC-001-FEATURE-001"
    path: ".swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/IMPLEMENTATION-PLAN.md"
    revision: "None"
owners:
  - "dennis-ritchie (delegated Ghostagram Design author)"
created: "2026-08-28"
updated: "2026-08-28"
template_version: "2.0.0"
---

# Ghostagram Trusted Semantic Graph Conformance Adapter Design

## 1. Assignment and Boundaries

This Design implements only the `ghostagram` assignment in the accepted [FEATURE-001 Implementation Plan](../../../../../../.swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/IMPLEMENTATION-PLAN.md) through the accepted [Ghostagram Solution Target](../../../../architecture/SOLUTION-ARCHITECTURE.md). It adds a non-deployable Ghostagram-owned runner that consumes the frozen System corpus and shared conformance support, invokes the real `Ghostagram.Bridge` projection, delta, command, and presentation contracts, and writes one attributable Ghostagram result envelope.

The runner reports actual returned Bridge state. It must not execute the System producer runner, import a producer result envelope, copy expected outcomes into observations, or change `Participant` on another participant's result. `Participant` is fixed to `ghostagram`; a supported result is constructible only from a case handler that records the invoked Bridge seam and its returned document, batch, command result, graph version, or presentation revision.

In scope:

- FEATURE-001 adapter mapping, test/tooling projects, repository-native commands, one result envelope, `EVIDENCE.md`, and later independent local Validation.
- The public `IGraphDiagramProjection`, `IGraphDiagramDeltaProjector`, `IGraphDiagramCommandAdapter`, `GraphPresentationSnapshot`, and System conformance-support APIs.
- Ephemeral System graph and Ghostagram presentation state created inside each case; no persisted or shared runtime state.
- Explicit unsupported results for every corpus operation without an allocated Bridge behavior.

Out of scope:

- UI or browser acceptance, Blazor Laboratory behavior, JavaScript rendering, MCP/HTTP, SignalR, and the Server-owned live Projection or Control Ports.
- Persistence, operational mutation, provider/runtime state, protected fields, credentials, endpoints, deployment, package publication, or architecture promotion.
- New production APIs or changes to `Ghostagram.Bridge`, System semantic rules, corpus meaning, result schema, or shared invariants.
- FEATURE-002 Variables and FEATURE-003 Semantic Links; reuse of runner plumbing is not evidence for either Feature.

## 2. Governing Contracts and Current State

- [FEATURE-001](../../../../../../.swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/FEATURE.md) is Accepted and retains AC-001 through AC-008.
- The amended [Implementation Plan](../../../../../../.swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/IMPLEMENTATION-PLAN.md) is Accepted and allocates only the existing Bridge seams plus test/tooling and Feature-local evidence.
- [ARCH-SOLUTION-GHOSTAGRAM](../../../../architecture/SOLUTION-ARCHITECTURE.md) is `Target`, independently approved, and has independently Accepted post-Plan reconciliation.
- [CONTRACT-SEMANTIC-GRAPH-FEDERATION](../../../../../../architecture/contracts/SEMANTIC-GRAPH-AND-FEDERATION.md) and [ADR-001](../../../../../../architecture/decisions/ADR-001-semantic-authority-federation-vocabulary.md) keep System as semantic authority and Ghostagram as a projection/change-proposal consumer.

Current source supplies the required real seam:

| Contract | Observed behavior used by the runner |
|---|---|
| `GraphDiagramProjection.Project` | Deterministically projects System snapshots; returns the graph ID, graph version, presentation revision, canonical node/edge IDs, portable metadata it can serialize, and explicit diagnostics for omitted unsupported metadata. |
| `GraphDiagramDeltaProjector.Project` | Returns operations for a direct compatible change batch or `RequiresFullProjection = true` when the authoritative version or projector shape is not safe for incremental use. |
| `GraphDiagramCommandAdapter.Apply` | Checks expected graph and diagram revisions separately, commits through a System graph transaction, returns `GRAPH_VERSION_CONFLICT`, `DIAGRAM_REVISION_CONFLICT`, `INVALID_PROPOSAL`, or `OK`, and always returns an authoritative projection. |
| `GraphPresentationStore` | Owns separately revisioned, copied presentation state for node/group bounds, waypoints, viewport, and selection. |

Existing Bridge and Cutover executable tests prove the seam is callable, but they do not emit the System-owned conformance schema and cannot be relabeled as Ghostagram participant evidence.

## 3. Proposed Design

### 3.1 Physical placement

Add two test/tooling-only .NET 10 executable projects:

```text
tests/
  Ghostagram.Graph.Conformance/
    Ghostagram.Graph.Conformance.csproj
    Program.cs
    GhostagramConformanceRunner.cs
    GhostagramBridgeHarness.cs
    Feature001CaseAdapter.cs
  Ghostagram.Graph.Conformance.Tests/
    Ghostagram.Graph.Conformance.Tests.csproj
    Program.cs
```

`Ghostagram.Graph.Conformance` references `src/Ghostagram.Bridge/Ghostagram.Bridge.csproj` and the sibling System checkout's `tests/Ghostworx.System.Graph.Conformance.Support/Ghostworx.System.Graph.Conformance.Support.csproj`. The test project references the runner project. Both are added to `Ghostagram.slnx`. These references are tooling-only; no production project gains a test or cross-repository conformance dependency.

### 3.2 Components

| Component | Single responsibility |
|---|---|
| `Program` | Parse required paths and participant version, invoke the runner, print a bounded summary, and return the defined exit code. |
| `GhostagramConformanceRunner` | Load and verify the System corpus, enumerate every manifest case once, assemble the fixed-participant envelope, validate it, and write it only after validation. |
| `Feature001CaseAdapter` | Dispatch the exact FEATURE-001 operation allowlist to a Bridge harness; produce an explicit shared-schema `unsupported` result for every other operation. |
| `GhostagramBridgeHarness` | Create isolated System graph/presentation state, call one named public Bridge seam, and return a typed observation containing only values obtained from inputs plus the Bridge result. |

The adapter owns no semantic validator. Expected outcome/category/diagnostic comparison and envelope invariants remain in the System conformance support. Case handlers may prepare fixture-driven System inputs and compare returned Ghostagram state with those inputs, but they may not manufacture a successful observation from manifest expectations.

### 3.3 Command interface

The runner accepts only:

```text
--corpus <directory-containing-manifest.json>
--schema <conformance-result.schema.json>
--output <result-json-path>
--participant-version <source-revision-or-working-tree-identifier>
```

`Participant` is not a command option and is always `ghostagram`. `RunnerVersion` is a source constant. Paths are normalized before use; the shared `ConformanceCorpus.Load` performs manifest, fixture containment, digest, and invariant checks. The schema path must identify a regular file. The output is serialized canonically, validated against manifest invariants and the supplied schema, serialized a second time to prove stable bytes, then written to a same-directory temporary file and atomically replaced. A validation failure leaves no eligible final result.

Exit codes are `0` when all Ghostagram-assigned cases pass with no missing/failed cases, even though non-assigned cases are explicitly unsupported; `1` for an assigned failed, unsupported, or missing case; and `2` for invalid arguments, corpus/profile/schema/invariant failure, I/O failure, or an unhandled runner failure.

## 4. FEATURE-001 Case Mapping

The frozen corpus currently contains 47 cases. The following ten are the complete Ghostagram allowlist. Each handler must invoke the named real seam or shared support probe and record the returned values named below.

| Corpus case | Execution | Required actual observations |
|---|---|---|
| `identity.address-roundtrip` | Project an ephemeral System graph built from the identity fixture through `IGraphDiagramProjection.Project`. | Returned document graph ID, projected canonical node ID, projected authority metadata, element kind, local ID, and returned `graphVersion`; IDs must equal the System snapshot rather than a Ghostagram-generated alias. |
| `mutation.deterministic-replay` | Project the same immutable snapshot and presentation snapshot through two fresh `GraphDiagramProjection` instances. | Canonical equality of returned documents and the returned graph revision. |
| `mutation.expected-revision-conflict` | Submit one stale graph-version command and one stale presentation-revision command through `IGraphDiagramCommandAdapter.Apply`. | Both rejection codes, expected/actual revisions, unchanged graph and presentation revisions, and returned authoritative documents. The envelope case passes only when neither attempt commits. |
| `federation.origin-preserved` | Project fixture origin/address values carried by the System snapshot as portable metadata. | Origin authority and address read back from the returned diagram node; no endpoint field. |
| `federation.pinned-revision` | Project a pinned-revision fixture value through the same projection seam. | Pinned revision read from the returned diagram node and unchanged graph revision from document extension data. |
| `federation.mirror-projection-distinct` | Project an origin-qualified System identity into a Ghostagram document whose document/node identity remains a projection key. | Origin value, projection identifier, and equality of the projected node key to the canonical System node ID; no minted semantic identity. |
| `consumer.contract-skew` | Ask the Ghostagram runner to load the fixture's unknown contract before any Bridge call. | Actual rejection category/code and unknown contract value from the runner boundary; no best-effort Bridge execution. |
| `consumer.profile-skew` | Ask the Ghostagram runner to load the fixture's unknown profile before any Bridge call. | Actual rejection category/code and unknown profile value from the runner boundary. |
| `corpus.duplicate-case-rejected` | Validate an in-memory manifest probe containing the declared duplicate through `ConformanceInvariants`. | Actual duplicate-case rejection and case ID; the production manifest is not modified. |
| `result.private-field-rejected` | Validate an in-memory result probe containing one forbidden field through the shared result invariants/schema, then discard it. | Actual private-field rejection and case ID; only the clean final envelope may be written. |

The following 37 operations have no allocated Bridge behavior and must be emitted as `status = unsupported`, `observedOutcome = unsupported-operation`, `observedCategory = unsupported`, with observed fields `operation=<manifest operation>` and `supportStatus=unsupported`:

- Vocabulary authority: `vocabulary.governed`, `vocabulary.legacy-rejected`.
- System cancellation/snapshot/history/redaction: `mutation.cancelled-atomic`, `snapshot.deep-immutable`, `history.exact-contiguous`, `history.gap`, `diagnostics.public-redaction`.
- System exchange/migration/extensions: `schema.v2-golden-roundtrip`, `migration.v0-v1-v2`, `migration.authority-required`, `extensions.compatible-preserved`, `extensions.incompatible-rejected`.
- Federation resolution/host behavior: `federation.unknown-authority`, `federation.unavailable`, `federation.partial`, `federation.forbidden-redacted`, `federation.cycle`, `federation.deadline`, `federation.cancelled`.
- System profile bounds: `boundary.graph-exact`, `boundary.graph-over`, `boundary.traversal-exact`, `boundary.traversal-over`, `boundary.batch-exact`, `boundary.batch-over`, `boundary.diagnostics-exact`, `boundary.diagnostics-over`, `boundary.semantic-value-exact`, `boundary.semantic-value-over`, `boundary.document-exact`, `boundary.document-over`, `boundary.extension-exact`, `boundary.extension-over`, `boundary.federation-exact`, `boundary.federation-over`, `boundary.resource-unavailable`.
- Separate System composition participant: `composition.graph-profile`.

Unsupported does not mean passed and cannot satisfy another participant's obligation. Adding support for one of these cases requires an existing allocated Bridge behavior; otherwise the Plan or architecture must be amended before implementation.

## 5. Bridge-Specific Observations Beyond Manifest Cases

The runner test suite must separately prove the Plan's Bridge obligations that are not honest one-to-one mappings of a corpus operation:

1. A contiguous `GraphChangeBatch` sent through `IGraphDiagramDeltaProjector.Project` returns the exact base/version pair and an incremental operation batch.
2. A version mismatch returns `RequiresFullProjection = true`; rebuilding through `IGraphDiagramProjection.Project` yields the authoritative graph and presentation revisions.
3. Presentation-only bounds, waypoints, viewport, selection, and `presentationRevision` change only the presentation sidecar. Captured System snapshots and portable semantic metadata do not acquire those fields.
4. A successful expected-revision proposal returns one committed graph change batch and a fresh authoritative document; a graph or diagram conflict leaves both authorities unchanged.
5. Projected canonical node and edge IDs, origin metadata, and pinned revision remain stable across full reprojection; mirror/projection possession does not create a second System identity.

These checks are named in `EVIDENCE.md` with exact command and result locators. They supplement rather than alter the 47-case envelope.

## 6. Data, Failure, Security, and Observability

Each case gets a fresh graph, presentation store, projector, delta projector, and command adapter. No case may depend on execution order. Fixture content is untrusted until shared corpus validation completes. Case detail is bounded by the shared invariant; console output contains case IDs and public categories only, never raw fixture bodies, exceptions, paths, or private values.

Failure handling is fail-closed:

- A supported handler that throws or disagrees with the manifest becomes `failed`; it is never downgraded to `unsupported`.
- A manifest operation absent from the exact allowlist becomes `unsupported`; an unknown allowlisted dispatch is a runner error.
- Missing, duplicate, malformed, digest-skewed, schema-invalid, or private-field-bearing results cannot be evidence.
- Cancellation/deadline and System profile-limit operations remain unsupported because the synchronous Bridge seam owns no such contract; the runner does not emulate them.
- Output directories may be created, but an existing result is replaced only after complete validation. No other file or external system is mutated.

The final result location is `.swe/implementations/EPIC-001/FEATURE-001/results/ghostagram-feature-001.json`. `EVIDENCE.md` will record the exact corpus/profile/schema versions and digest, participant and runner versions, environment, commands, ten assigned results, 37 unsupported results, Bridge-specific test results, failures/skips, and dirty-worktree boundary. It remains evidence, not approval.

## 7. Change Map

| Path | Planned change |
|---|---|
| `Ghostagram.slnx` | Register the two non-deployable conformance projects. |
| `tests/Ghostagram.Graph.Conformance/Ghostagram.Graph.Conformance.csproj` | Reference Bridge and System conformance support; copy no corpus or semantic implementation. |
| `tests/Ghostagram.Graph.Conformance/Program.cs` | CLI entry point and exit-code boundary. |
| `tests/Ghostagram.Graph.Conformance/GhostagramConformanceRunner.cs` | Corpus lifecycle, envelope assembly/validation, deterministic serialization, and guarded write. |
| `tests/Ghostagram.Graph.Conformance/GhostagramBridgeHarness.cs` | Isolated construction and actual calls to projection, delta, command, and presentation seams. |
| `tests/Ghostagram.Graph.Conformance/Feature001CaseAdapter.cs` | Exact ten-case allowlist and explicit unsupported fallback for the other 37 manifest operations. |
| `tests/Ghostagram.Graph.Conformance.Tests/Ghostagram.Graph.Conformance.Tests.csproj` | Executable contract-test project. |
| `tests/Ghostagram.Graph.Conformance.Tests/Program.cs` | Runner, seam, failure, provenance, mapping, output, and exclusion tests. |
| `.swe/implementations/EPIC-001/FEATURE-001/results/ghostagram-feature-001.json` | Generated, schema- and invariant-valid Ghostagram envelope. |
| `.swe/implementations/EPIC-001/FEATURE-001/EVIDENCE.md` | Implementation-owned commands, results, traceability, observations, unsupported cases, and risks. |

No production `src/`, existing test source, UI/browser asset, server, persistence, or unrelated dirty file is in the change map.

## 8. Test and Evidence Plan

The new executable test project must verify:

- all 47 manifest case IDs are dispatched exactly once; the allowlist is exactly the ten cases above and the unsupported list is exactly the other 37;
- every passed case was created from an actual typed Bridge/support observation and contains its seam identifier; prebuilt producer results cannot enter envelope assembly;
- projection determinism, identity/origin/pinned-revision preservation, mirror/projection distinction, separate graph/presentation revisions, incremental delta, full-reprojection fallback, expected-graph conflict, expected-presentation conflict, atomic no-change on rejection, and presentation-field exclusion;
- contract/profile skew, duplicate-case, and private-field probes fail through shared support with the required public category/code;
- envelope manifest invariants, JSON schema, stable serialization, fixed `ghostagram` participant, exact corpus digest, and guarded output behavior;
- assigned failure/unsupported/missing cases return exit code `1`; invalid input/result returns `2`; a valid ten-pass/37-unsupported run returns `0`.

Implementation verification commands, run from the Ghostagram repository root, are:

```powershell
dotnet restore .\Ghostagram.slnx
dotnet build .\Ghostagram.slnx -c Release --no-restore
dotnet run --project .\tests\Ghostagram.Bridge.Tests\Ghostagram.Bridge.Tests.csproj -c Release --no-build
dotnet run --project .\tests\Ghostagram.Cutover.Tests\Ghostagram.Cutover.Tests.csproj -c Release --no-build
dotnet run --project .\tests\Ghostagram.Graph.Conformance.Tests\Ghostagram.Graph.Conformance.Tests.csproj -c Release --no-build
dotnet run --project .\tests\Ghostagram.Graph.Conformance\Ghostagram.Graph.Conformance.csproj -c Release --no-build -- --corpus ..\ghostworx-system\tests\Ghostworx.System.Graph.Conformance\Fixtures\semantic-graph\v1 --schema ..\ghostworx-system\tests\Ghostworx.System.Graph.Conformance\Schemas\conformance-result.schema.json --output .\.swe\implementations\EPIC-001\FEATURE-001\results\ghostagram-feature-001.json --participant-version <source-revision-or-working-tree-identifier>
git diff --check
```

The implementation must replace the participant-version placeholder with observed source state and record the exact command in `EVIDENCE.md`. No browser/live test is required or claimed.

## 9. Acceptance-Criteria Traceability

| Criterion | Ghostagram-local evidence responsibility |
|---|---|
| AC-001 | `identity.address-roundtrip` plus actual projection of System-owned graph/node identity; broader System construction, validation, traversal, serialization, and exchange remain unsupported here. |
| AC-002 | Deterministic projection replay and expected graph/presentation revision conflict with no partial state. |
| AC-003 | Separate graph/presentation revisions, authoritative reprojection, and explicit unsupported System snapshot/history/redaction cases. |
| AC-004 | Contract/profile skew is rejected before projection; System serialization, migration, and extension operations are explicit unsupported cases. |
| AC-005 | Origin, pinned revision, and mirror/projection distinction are observed through the actual projection seam without minted identity. |
| AC-006 | Attributable fixed-participant envelope, real seam provenance, skew, duplicate-case and private-field rejection, exact unsupported reporting, and no relabeled System output. |
| AC-007 | Feature-local `EVIDENCE.md` separates unchanged Bridge baseline, FEATURE-001 adapter/test changes, unresolved unsupported behavior, and excluded FEATURE-002/003 work. |
| AC-008 | The runner consumes the finite profile and stays bounded to the manifest; Bridge-unowned semantic bounds, cancellation, deadline, and capacity cases remain explicit unsupported results rather than copied enforcement. |

System and portfolio owners retain final AC accountability. This Design claims only the bounded Ghostagram participation allocated by the Plan.

## 10. Compatibility, Rollout, and Reversal

The change is additive and non-deployable. Existing public Bridge APIs and production build behavior remain compatible. The direct sibling test reference matches the current portfolio checkout layout; a standalone Ghostagram checkout must be supplied the exact System support/corpus at the recorded relative layout or through a separately governed distribution mechanism.

Reversal removes the two new projects, their solution entries, and generated Feature-local result/evidence files. It does not require data migration or production rollback. Implementation pauses and returns to Design/architecture review if the frozen corpus cannot be consumed through shared support, an assigned case needs a production Bridge change, or any observation requires copied System semantics.

## 11. Risks and Known Limits

- The direct sibling project reference is intentionally test/tooling-only and couples local execution to the portfolio checkout layout.
- Thirty-seven corpus operations are outside the allocated Ghostagram seam. Their explicit unsupported state is truthful and does not substitute for System, Server, SDK, federation-adapter, or composition evidence.
- The existing Bridge preserves origin/pinned revision only when supplied as portable System metadata; it does not resolve federation references or become their authority.
- No UI/browser, live Projection Port, SignalR, persistence, deployment, or runtime behavior will be tested by this delivery.

No accepted Plan/Target divergence is known. Any discovered production API requirement is a blocker, not implicit Design authority.

## 12. Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | dennis-ritchie (`ghostagram_architecture`, delegated Ghostagram Design author) |
| Approver | elon-musk |
| Decision | Accepted |
| Recorded | 2026-08-28T14:10:53.5028977-04:00 |
| Evidence | The Design traces the accepted Plan and Target, uses verified Bridge projection, delta, command, and presentation seams, fixes participant identity, prohibits producer invocation and relabeling, partitions all 47 cases as 10 executed plus 37 explicit unsupported, adds Bridge-specific freshness, reprojection, and conflict tests, keeps dependencies tooling-only and inward, and excludes production, runtime, UI, persistence, deployment, and unrelated dirty work. AC-001 through AC-008 are preserved exactly once; locators and static hygiene pass. |
| Bypass reason | None |
