---
title: "Graph-Native Variable System — Ghostagram Design"
artifact_type: "design"
id: "DESIGN-EPIC-001-FEATURE-002-GHOSTAGRAM"
status: "Accepted"
authority: "solution"
scope: "ghostagram"
parent: "IMPL-PLAN-EPIC-001-FEATURE-002"
upstream:
  repository: "ghostworx"
  artifact_id: "IMPL-PLAN-EPIC-001-FEATURE-002"
  path: ".swe/epics/001-semantic-system-foundation/features/002-graph-native-variable-system/IMPLEMENTATION-PLAN.md"
  revision: "None"
traceability:
  epic:
    repository: "ghostworx"
    artifact_id: "EPIC-001"
    path: ".swe/epics/001-semantic-system-foundation/EPIC.md"
    revision: "None"
  feature:
    repository: "ghostworx"
    artifact_id: "FEATURE-002"
    path: ".swe/epics/001-semantic-system-foundation/features/002-graph-native-variable-system/FEATURE.md"
    revision: "None"
  implementation_plan:
    repository: "ghostworx"
    artifact_id: "IMPL-PLAN-EPIC-001-FEATURE-002"
    path: ".swe/epics/001-semantic-system-foundation/features/002-graph-native-variable-system/IMPLEMENTATION-PLAN.md"
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
owners:
  - "dennis-ritchie (Ghostagram Design author)"
created: "2026-08-28"
updated: "2026-08-28"
template_version: "2.0.0"
---

# Graph-Native Variable System — Ghostagram Design

## Assignment and Boundaries

This Design implements only the bounded `ghostagram` consumer-conformance assignment from the accepted [FEATURE-002 Plan](../../../../../../.swe/epics/001-semantic-system-foundation/features/002-graph-native-variable-system/IMPLEMENTATION-PLAN.md). It extends the accepted [Ghostagram Solution Target](../../../../architecture/SOLUTION-ARCHITECTURE.md) and the existing FEATURE-001 test/tooling adapter through the real `Ghostagram.Bridge` projection seam. It consumes System-owned Variable records and reports actual Ghostagram observations; it does not implement or reinterpret Variable semantics.

The hard prerequisite is closed by the independently accepted [FEATURE-001 portfolio Validation](../../../../../../.swe/epics/001-semantic-system-foundation/features/001-trusted-semantic-graph-foundation/VALIDATION.md). Ghostagram's accepted local [FEATURE-001 Validation](../FEATURE-001/VALIDATION.md) supplies the reusable runner and real-seam baseline. FEATURE-002 implementation still waits for the System owner to deliver the accepted public Variable contracts, immutable corpus, schemas, digests, and reusable test/tooling validation boundary described by the accepted [System FEATURE-002 Design](../../../../../../repos/ghostworx-system/.swe/implementations/EPIC-001/FEATURE-002/DESIGN.md).

### Assigned outcome

- Execute every corpus case assigned to participant `ghostagram` through the existing full-projection or delta-projection boundary, or through the System-owned corpus/envelope compatibility gate when the case must fail before projection.
- Observe canonical Variable identity, live or pinned reference mode, requested and effective revision, bounded public provenance, invalidation/freshness, and unsupported version skew without resolving a Variable.
- Prove no protected locator, credential, provider configuration, resolved payload, protected tenant state, or provider exception enters the projected `DiagramDocument`, presentation sidecar, console output, or result envelope.
- Emit one schema-, digest-, and invariant-valid envelope with fixed participant identity `ghostagram`, separate from FEATURE-001 graph evidence.
- Preserve every non-assigned manifest case as an explicit `unsupported` result and fail when a required Ghostagram case has no approved mapping.

### Explicit exclusions

- Variable Definition, Reference, type, scope, alias, revision, Binding, resolution, snapshot, provenance, invalidation, failure, compatibility, migration, or schema meaning.
- Provider invocation, protected Binding read/write or registry, operational cache, authorization, credentials, retry/failover, runtime activation, Port dispatch, or composition-time resolution.
- Production `Ghostagram.Bridge` API changes, UI/browser behavior, JavaScript, Blazor Laboratory, SignalR, Server runtime Projection or Control Ports, live resolution, persistence, deployment, publication, release, or architecture promotion.
- Relabeling a System producer/provider/composition result, copying a System expected result, or treating adapter reuse as FEATURE-002 evidence without actual execution.

## Current State

| Area | Verified current fact | Design consequence |
|---|---|---|
| FEATURE-001 gate | Portfolio Validation is `Accepted`; all System and consumer envelopes share the frozen graph corpus and have independent accepted local Validation | FEATURE-002 Design and later source entry are no longer blocked by FEATURE-001 |
| Reusable runner | `tests/Ghostagram.Graph.Conformance` fixes participant `ghostagram`, validates a System-owned corpus/schema, writes atomically, and dispatches through `Feature001CaseAdapter` | Preserve its FEATURE-001 CLI and behavior; add a feature-specific dispatch path rather than a second runner |
| Reusable tests | `tests/Ghostagram.Graph.Conformance.Tests` verifies attribution, exact case partition, Bridge behavior, schema, and invariants | Extend this executable with FEATURE-002-only tests and keep FEATURE-001 assertions intact |
| Full projection | `IGraphDiagramProjection.Project` projects graph snapshots and serializable public metadata into rebuildable diagram nodes while recording graph and presentation revisions | Project only System-validated public Variable records and inspect the returned document |
| Freshness | `IGraphDiagramDeltaProjector.Project` advances contiguous graph changes or requires full projection for an incompatible path/version | Model invalidation as a public fact plus authoritative graph change; never invent freshness or resolve eagerly |
| Presentation ownership | `GraphPresentationSnapshot` is separately revisioned from System graph state | Variable identity, mode, revision, provenance, invalidation, and locator data never enter presentation state |
| Variable delivery | The System FEATURE-002 Design is Accepted, but current live source has no Variable implementation or frozen Variable corpus | Implementation preflight must stop until exact delivered files and a passing producer corpus are available |
| Worktree | Existing documentation, renderer, tests, scaffold, architecture, FEATURE-001 adapter/evidence, and bug artifacts are dirty or untracked | Implementation edits only the paths in the Change Map and preserves all unrelated work |

## Proposed Design

### Reusable command boundary

`Program.cs` remains the single non-deployable entry point. Existing FEATURE-001 arguments retain their current meaning. A `--feature feature-002` discriminator selects `Feature002RunnerOptions`, requiring `--fixtures`, `--result`, and `--participant-version`; participant identity is never accepted from the caller. The FEATURE-002 path uses the System-owned Variable manifest and result contract, and writes exactly:

` .swe/implementations/EPIC-001/FEATURE-002/results/ghostagram-variable-v1.json `

The surrounding spaces above are prose delimiters; the path itself is `.swe/implementations/EPIC-001/FEATURE-002/results/ghostagram-variable-v1.json`.

`Feature002ConformanceRunner` performs this sequence:

1. Resolve the fixture root, manifest, schemas, and every fixture beneath the declared root; reject absolute paths and traversal.
2. Invoke the delivered System-owned Variable corpus loader, canonical digest checks, finite-profile checks, and result-envelope support. Do not call a System producer, provider, composition participant, or resolution implementation.
3. Verify the manifest advertises participant `ghostagram` and derive the required Ghostagram partition from its immutable `requiredParticipantKinds` and operation fields.
4. Dispatch each assigned operation through `Feature002CaseAdapter`; dispatch every other manifest case to one explicit unsupported result.
5. Validate exact manifest coverage, public-field allowlists, summary counts, canonical encoding, digests, and the System-owned result schema before replacing the output atomically.
6. Return zero only when every assigned case passes, every non-assigned case is explicitly unsupported, and no case is failed, missing, malformed, or privately contaminated.

The fixed envelope participant is `ghostagram`; runner/source versions identify Ghostagram code. A System envelope may be used only as an input-contract compatibility check and can never be copied or relabeled.

### Feature-specific projection mapping

`Feature002CaseAdapter` owns no Variable rules. It deserializes only through delivered System public Variable APIs and selects one closed handler by manifest operation. Each handler calls `Feature002BridgeHarness`, which creates an in-memory System-authority graph from the validated public fixture, captures the authoritative snapshot, invokes the existing Bridge seam, and reads observations only from the returned `DiagramDocument`, `GraphDiagramOperationBatch`, graph snapshot, or presentation snapshot.

| Mapping category | Actual seam and observation | Required assertions |
|---|---|---|
| Public Definition/Reference projection | `IGraphDiagramProjection.Project` | Canonical identity is unchanged; live/pinned mode is explicit; pinned requested revision is exact; type/scope facts remain public System values |
| Public snapshot/provenance projection | `IGraphDiagramProjection.Project` | Requested/effective identity and revision, bounded provider/result identity, derivation, observation time, and freshness are preserved; protected fields are absent |
| Invalidation/freshness | `IGraphDiagramDeltaProjector.Project`, followed by full projection only when requested | Publisher token, affected identity/revision relation, and change category are observable; contiguous changes advance, gaps/skew cannot claim freshness, and no eager resolution occurs |
| Protected-locator exclusion | Full projection plus recursive public-output scan | No protected locator key/value or other forbidden field reaches diagram, presentation, diagnostic, console, or envelope data |
| No implicit resolution | Projection with a zero-count resolution probe and dependency/reference fitness checks | Projection and invalidation consume public records only; resolution/provider invocation count remains zero |
| Contract/profile skew | System-owned compatibility gate before any Bridge call | Unsupported contract/profile is explicit and stable; projection invocation count is zero |

The harness may translate public System record properties into existing Graph metadata only through delivered System serializers/adapters and canonical values. It may not parse expected outcomes into replacement semantics, fabricate a protected record, or add a Variable-specific production mapper to Bridge. If the delivered public record cannot reach an existing Bridge seam without a production API change, that case is unsupported and the Plan/Target returns to review.

### Exact partition and fail-closed rules

The immutable manifest is authoritative for case inventory, but not for expanding Ghostagram's architecture. Partitioning is deterministic:

- `assigned`: `requiredParticipantKinds` contains `ghostagram` and the operation belongs to one of the six approved mapping categories above;
- `unsupported`: `requiredParticipantKinds` does not contain `ghostagram`; emit `status=unsupported`, the exact operation, and a bounded reason;
- `design-drift failure`: `requiredParticipantKinds` contains `ghostagram` but the operation has no closed handler, or an operation claims provider, Binding registry, cache, authorization, composition, UI, runtime, persistence, or deployment behavior;
- `missing`: any manifest case lacks exactly one result;
- `failed`: an executed observation disagrees with the frozen public outcome/category or violates System envelope invariants.

Tests freeze the delivered assigned case IDs and operation names in an explicit allowlist after verifying them against the immutable manifest and its digest. A later manifest changes that set only through a reviewed compatible corpus revision and Design update; it never silently activates a new handler. Every case appears exactly once, and `assigned + unsupported = manifest total` for an eligible Ghostagram envelope.

### State and failure semantics

- Each case receives a fresh in-memory graph and presentation store; state never crosses cases.
- Before/after graph and presentation snapshots prove failure, skew, unsupported, invalidation-gap, and locator-exclusion cases do not mutate canonical or presentation state.
- A delta gap or unsupported Bridge path requests authoritative full reprojection or remains unsupported; stale output is never labeled fresh.
- Public diagnostics are bounded, stable, and redacted. Absolute paths, user/machine names, raw exceptions, protected locators, payloads, credentials, and tenant/provider policy never enter the result.
- Malformed corpus, digest mismatch, unknown field, invalid canonical bytes, deadline expiry, schema failure, or result-invariant failure returns non-zero and leaves any prior valid result unchanged.

## Interfaces, Data, and Contracts

- Normative meaning remains in the accepted [Variable Definition and Resolution contract](../../../../../../architecture/contracts/VARIABLE-DEFINITION-AND-RESOLUTION.md) and [ADR-002](../../../../../../architecture/decisions/ADR-002-variable-binding-consistency-caching-invalidation.md).
- Test/tooling dependencies may point to delivered System Variable Contracts, Serialization, and reusable Variable conformance support. No Ghostagram production project references System conformance tooling.
- If the delivered System conformance project does not expose a reusable corpus/envelope support boundary, implementation stops for contract/Design review; Ghostagram does not copy the validator or invoke a producer to manufacture its result.
- Public Variable records remain immutable inputs. `DiagramDocument` is rebuildable projection output, and `GraphPresentationSnapshot` remains Ghostagram-owned sidecar state.
- The child envelope is copied byte-identically by the System integration owner to its required `results/ghostagram/variable-v1.json` intake locator; Ghostagram owns the source envelope and its Evidence.

## Failure, Security, Observability, and Operations

- The runner is local, non-interactive, and network-free. It opens no listener and needs no browser, Server process, credential, provider, protected registry, production data, or deployment resource.
- Console output contains a concise participant/corpus/digest/count summary and the repository-relative result path. Detailed public observations remain in the envelope.
- Unknown or unsupported skew is a stable public result, not a fallback. A required unsupported mapping blocks the child assignment rather than becoming a pass.
- Locator and leakage tests use sentinel comparison only inside the test process; sentinel values are never serialized into Evidence or envelope output.
- Windows Application Control unavailability is recorded as unavailable validation; implementation may use the signed `dotnet <dll>` host but must not alter system policy or trust settings.

## Change Map

| Area or path | Change | Owner |
|---|---|---|
| `tests/Ghostagram.Graph.Conformance/Program.cs` | Preserve FEATURE-001 behavior and dispatch `--feature feature-002` to the new runner | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/GhostagramConformanceRunner.cs` | Extract only shared command/output mechanics needed by both Feature paths without changing FEATURE-001 results | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Feature002ConformanceRunner.cs` | FEATURE-002 options, fixed participant, System corpus/envelope support, partition, validation, and atomic output | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Feature002CaseAdapter.cs` | Closed assigned-operation dispatch and explicit unsupported mapping | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Feature002BridgeHarness.cs` | Actual projection/delta observations, freshness, locator exclusion, and no-resolution probe | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance/Ghostagram.Graph.Conformance.csproj` | Add only delivered System Variable test/tooling references needed for public records and conformance support | Ghostagram conformance tooling |
| `tests/Ghostagram.Graph.Conformance.Tests/Program.cs` | Preserve FEATURE-001 tests and invoke FEATURE-002 verification | Ghostagram conformance tests |
| `tests/Ghostagram.Graph.Conformance.Tests/Feature002Tests.cs` | Assigned/unsupported partition, real seam, attribution, leakage, invalidation, no-resolution, skew, digest, schema, and repeat-run tests | Ghostagram conformance tests |
| `tests/Ghostagram.Graph.Conformance.Tests/Ghostagram.Graph.Conformance.Tests.csproj` | Add no production dependency; consume the runner project only | Ghostagram conformance tests |
| `.swe/implementations/EPIC-001/FEATURE-002/results/ghostagram-variable-v1.json` | Generated attributable FEATURE-002 envelope | Ghostagram conformance tooling |
| `.swe/implementations/EPIC-001/FEATURE-002/EVIDENCE.md` | Complete commands, versions, prerequisite locators, observations, partition, exclusions, and unresolved behavior | Ghostagram implementation owner |

No other file is authorized. In particular, implementation does not edit `src/Ghostagram.Bridge`, `src/Ghostagram.Blazor`, `src/Ghostagram.Server`, JavaScript, existing dirty documentation/tests, portfolio artifacts, System source, architecture, or local Validation.

## Test and Evidence Plan

### Planned commands

From `repos/ghostagram` after the System owner delivers the frozen Variable artifacts:

```powershell
dotnet restore .\Ghostagram.slnx
dotnet build .\Ghostagram.slnx -c Release --no-restore
dotnet .\tests\Ghostagram.Graph.Conformance.Tests\bin\Release\net10.0\Ghostagram.Graph.Conformance.Tests.dll --feature feature-002 --fixtures ..\ghostworx-system\tests\Ghostworx.System.Variable.Conformance\Fixtures\variable\v1
dotnet .\tests\Ghostagram.Graph.Conformance\bin\Release\net10.0\Ghostagram.Graph.Conformance.dll --feature feature-002 --fixtures ..\ghostworx-system\tests\Ghostworx.System.Variable.Conformance\Fixtures\variable\v1 --result .\.swe\implementations\EPIC-001\FEATURE-002\results\ghostagram-variable-v1.json --participant-version <source-revision>
dotnet .\tests\Ghostagram.Bridge.Tests\bin\Release\net10.0\Ghostagram.Bridge.Tests.dll
dotnet .\tests\Ghostagram.Cutover.Tests\bin\Release\net10.0\Ghostagram.Cutover.Tests.dll
git diff --check -- .\tests\Ghostagram.Graph.Conformance .\tests\Ghostagram.Graph.Conformance.Tests .\.swe\implementations\EPIC-001\FEATURE-002
```

The implementer also runs the delivered System verifier in verify-only mode against the unchanged child envelope and records its exact command/result. A policy-blocked apphost may be replaced with `dotnet <dll>` as shown; no policy mutation is authorized. `<source-revision>` is a command placeholder resolved to the exact Ghostagram revision or `None` during implementation, not a literal artifact value.

### Criterion traceability

| Criterion | Ghostagram verification | Planned evidence location |
|---|---|---|
| AC-001 | Project a System-owned public Definition plus live and pinned References and inspect canonical identity/mode without a provider or runtime host | `EVIDENCE.md#ac-001`; child envelope identity/reference cases |
| AC-002 | Recursively scan public fixture projection, presentation, diagnostics, console, and envelope for forbidden payload/provider/locator/credential fields | `EVIDENCE.md#ac-002`; leakage cases and scan output |
| AC-003 | Observe live reference identity, effective revision, immutable public provenance, and explicit public outcome without invoking resolution | `EVIDENCE.md#ac-003`; live projection cases |
| AC-004 | Observe exact pinned requested/effective revision and prove no live/newer/alias/cache substitution in the projected public record | `EVIDENCE.md#ac-004`; pinned projection cases |
| AC-005 | Preserve stable public failure categories where assigned and prove failed/skewed/unsupported cases leave graph and presentation snapshots unchanged | `EVIDENCE.md#ac-005`; state-isolation matrix |
| AC-006 | Preserve identity, mode, revision, type, scope, and public provenance through System-deserialized records; reject unsupported contract/profile before projection | `EVIDENCE.md#ac-006`; compatibility/skew cases |
| AC-007 | Apply public invalidation/change facts through delta projection, verify token/revision freshness or full-reprojection fallback, retain prior observations, and record zero eager resolutions | `EVIDENCE.md#ac-007`; invalidation/freshness cases |
| AC-008 | Produce the fixed `ghostagram` envelope through actual Bridge execution; assert no Binding misuse, locator leakage, implicit resolution, semantic duplication, or relabeled producer data | `EVIDENCE.md#ac-008`; `results/ghostagram-variable-v1.json` |
| AC-009 | Separate FEATURE-002 identity/reference, live, pinned, failure, provenance, invalidation, security, skew, unsupported, and unresolved results from FEATURE-001 Graph evidence | `EVIDENCE.md#ac-009`; evidence matrix |
| AC-010 | Enforce delivered finite corpus/output bounds and deadlines in support; mark provider/cache/dependency/federation work outside the Bridge allocation explicitly unsupported | `EVIDENCE.md#ac-010`; boundary and unsupported partition results |

Evidence records the accepted Feature, Plan, Target, Design, FEATURE-001 portfolio Validation, source revision, environment, exact corpus/manifest/schema/profile/digest identities, commands and exits, assigned/unsupported/failed/missing counts, actual seam per passing case, child envelope digest, System verify-only result, preserved dirty paths, and unresolved Target behavior. It is `Complete` and does not validate itself.

## Rollout, Compatibility, and Reversal

1. Accept this Design independently before any source/test/result/Evidence change.
2. At implementation entry, inventory the dirty worktree and verify the FEATURE-001 Validation locator plus delivered System Variable projects, immutable manifest/schemas, producer result, and reusable support boundary.
3. Add the FEATURE-002 mapping behind an explicit feature discriminator while rerunning the unchanged FEATURE-001 tests and envelope checks.
4. Freeze the exact Ghostagram assigned-case allowlist against the delivered manifest digest, then run the child suite twice and compare equivalent status/category/diagnostic/public fields.
5. Emit the child envelope and Complete Evidence; an independent solution validator decides the local result before portfolio intake.

There is no production rollout or data migration. Before acceptance by another consumer, reversal removes the FEATURE-002-only files and dispatcher branch while retaining FEATURE-001 behavior. After the result contract is consumed, changes require compatible corpus/schema evolution and review; rewriting an envelope or relabeling another participant is never a rollback.

## Risks and Divergence

- The System Variable implementation and frozen corpus are not yet present. This does not block Design review, but it blocks implementation beyond read-only preflight until the exact portable contracts and support boundary exist.
- The accepted System Design currently places Variable conformance support in an executable test project rather than a separately named support library. If its delivered public surface cannot be reused safely from Ghostagram tooling, stop for Design/architecture review instead of copying validation or semantic rules.
- Some required corpus behavior may not be representable through the current generic Bridge metadata projection. Such a case remains explicit unsupported and returns to Plan/Target review; this Design authorizes no production Bridge extension.
- Direct sibling project references remain tooling-local and machine-layout-sensitive. Evidence records the exact layout and does not claim package publication or release portability.
- Application Control may block unsigned workspace apphosts. Use the signed .NET host when available and report an unresolved policy block; never bypass policy.
- No architecture contradiction is presently known. Authority, dependency direction, public/protected boundaries, existing Bridge APIs, and all UI/runtime exclusions remain unchanged.

## Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | dennis-ritchie (`ghostagram_architecture`, Ghostagram Design author) |
| Approver | elon-musk |
| Decision | Accepted |
| Recorded | 2026-08-28T15:44:01.4632361-04:00 |
| Evidence | The Design reuses existing Ghostagram.Bridge full and delta projection seams through tooling-only mappings, with fixed participant ghostagram and separate FEATURE-002 envelope. It performs no resolution, production Bridge change, UI or browser work, SignalR integration, Server runtime projection, persistence, or deployment. Assigned cases must reach an approved Bridge handler; absent mappings fail while non-assigned cases are explicit unsupported. AC-001 through AC-010 and links pass; System results cannot be copied or relabeled. |
| Bypass reason | None |
