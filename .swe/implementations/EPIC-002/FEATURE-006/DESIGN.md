---
# swe:generated metadata sha256=417f963a40aba6616fdb3bae59d127116f4d31a24c553000437091d48fa90ec0
title: "Reproducible Composition Integration — Ghostagram Design"
artifact_type: "design"
id: "DESIGN-GHOSTAGRAM-F006"
status: "Accepted"
authority: "solution"
scope: "GHOSTAGRAM-F006-PROJECTION-AND-METADATA"
parent: "IMPL-PLAN-EPIC-002-FEATURE-006"
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
  implementation_plan:
    repository: "ghostworx"
    artifact_id: "IMPL-PLAN-EPIC-002-FEATURE-006"
    path: ".swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md"
owners:
  - "Ghostagram Design author (/root/ghostagram_f006_design)"
created: "2026-09-09"
updated: "2026-09-09"
template_version: "3.0.0"
# swe:end metadata
---

# Reproducible Composition Integration — Ghostagram Design

## Assignment and Boundaries

Assignment `GHOSTAGRAM-F006-PROJECTION-AND-METADATA` owns FEATURE-006 AC-001, AC-002, AC-006 and AC-007. This is preparation only: accepted Design and implementation-entry prerequisites precede any source/test change or final generation. System remains semantic/compiler/admission authority; Ghostagram projects admitted public facts and owns presentation and its separate existing local GraphWorkspace command/persistence seam. No new API, package, semantic adapter, dependency upgrade, runtime, activation, deployment or Git operation is allocated.

Governing inputs are [EPIC-002 revision 3](../../../../../../.swe/epics/002-declarative-composition-model/EPIC.md), the generated Feature/Plan links below, [composition contract revision 3](../../../../../../architecture/contracts/DECLARATIVE-COMPONENT-COMPOSITION.md), [Solution Target revision 3](../../../../architecture/SOLUTION-ARCHITECTURE.md), [Bridge Package revision 3](../../../../architecture/packages/Ghostagram.Bridge/PACKAGE-ARCHITECTURE.md), [projection Module revision 2](../../../../architecture/packages/Ghostagram.Bridge/modules/DeclarativeCompositionProjection/MODULE-ARCHITECTURE.md), and the independently [Accepted F006 reconciliation](../../../../architecture/reviews/EPIC-002-F006-TARGET-PLAN-RECONCILIATION.md). All Targets remain unchanged. Detailed proof treatment is appropriate because cross-repository provenance, persistence history and actual visible observation need explicit attribution.

## Current State

Prototype Mode is Off. Baseline Ghostagram HEAD is `b3b8d5b09a6b28aa205f85c939524c9f45ce3eec`; this identifies the baseline commit, not all working-tree bytes. Preserve dirty prototype STATE and journal and other workers' artifacts. Graph discovery did not cover the Composition runner, so its current source was read directly.

`tests/Ghostagram.Graph.Conformance/Composition/CompositionRunner.cs` already separates materialization and replay. `CompositionConsumerSession.Run` compares the external manifest digest and re-materialized canonical bytes, executes actual cases, writes projection-index.json, then invokes System ConsumerEnvelopeAdmission. Only successful admission writes consumer-envelope.json; a rejected candidate and diagnostics are retained separately. Exit zero additionally requires every observation passed and available. Local projection mode does not establish this chain.

`tests/Ghostagram.Layout.Verification/CompositionPreview.cs` accepts an actual projection result, invokes SvgDiagramExporter, and emits index.html, preview-provenance.json and (when applicable) diagram.svg. It explicitly leaves visibleInspection Pending. Fresh can render its document; Stale may show exact lastKnown Fresh with a stale label; Unsupported, Skewed and Failed cannot present a newly accepted diagram.

`tests/Ghostagram.Server.GraphWorkspace.Tests/Program.cs` imports schema-1 future.codec metadata, replaces it via DiagramOperations.Upsert and IGraphWorkspaceService.TryApply with expected graph/presentation revisions, saves retained old/new history, removes live state and reloads the replacement. Its descriptor and two-row node use 107px. This is unknown metadata, not transparency. Current `src/Ghostagram.Server/GraphWorkspaces/GraphWorkspaceService.cs:91` still passes `features: null`; consumed GraphStore's constructor names that argument `extensions`. The current-source failed build reported by the coordinator is not repaired by a later stale --no-build run.

## Proposed Design

1. After implementation gates open, make the ordinary compatibility correction `features: null` to `extensions: null` in GraphWorkspaceService.Create. Preserve the existing options, transaction, persistence and projection contracts. Any broader semantic/API repair returns to architecture review.
2. Before freeze, add only missing attributable test assertions/receipt detail in the existing suites. In the workspace fixture explicitly assert authoritative capture includes the accepted replacement and two property rows at the existing 107px minimum; retain old future.codec payload/extensions and new semantic-value payload/history observations before fixture cleanup. Never weaken an existing assertion to make the build pass.
3. Portfolio coordinates one frozen four-repository authored-source archive and manifest after approved repairs. Extract sibling-layout run-a/repos and run-b/repos independently from the same closure. No Git worktree or commit is needed. Each run owns its output directories; one Luna tester owns each shared build tree.
4. System produces and verifies actual producer inputs for each run. Ghostagram materializes its manifest, freezes its digest externally, replays the real consumer and obtains System admission. Only then resolve projection-index entries into preview inputs and generate/export/visibly inspect them.
5. Repeat the complete consumer and workspace proof in run-b. Compare stable source, fixture, manifest and projection payload identities/digests. Preserve raw run-scoped values and classify permitted differences explicitly; unclassified differences fail.

## Interfaces, Data, and Contracts

No public signature or storage migration changes. The four-repository closure must record baseline HEADs; explicit sorted authored source/config/test paths, lengths and SHA-256; added/modified/deleted paths relative to baseline (deletion has old blob and no current bytes); archive digest; and extraction/pre/post inventory comparisons that enumerate current path sets to detect additions/deletions. Include project/solution files, build/NuGet/SDK configuration, scripts, fixtures and every consumed source. Exclude .git, bin/obj and prior generated results/checks; any consumed artifact from excluded directories is a separately named frozen input. Reject escaping reparse links. Generated dependencies and outputs have separate manifests, never masquerading as authored source.

Record exact dotnet executable/SDK/runtime, OS/architecture, environment allowlist, command arrays/cwd, configuration, resolved package/dependency bytes, project-reference closure, build interval and DLL/PDB/deps/runtimeconfig hashes. No credentials enter manifests. A successful current-source build and unchanged dependency output hashes precede every DLL execution. Live-checkout drift is reported separately and does not change the frozen run silently.

The prepared invocation packet must resolve these slots before execution; unresolved or mismatched inputs block the check:

| Slot | Source and binding |
|---|---|
| ROOT | This run's extracted Ghostagram repository, sibling of frozen System/SDK/Server roots |
| C1, PRODUCER, E0, S0 | Actual passing System F006 producer export in the corresponding run: C1 normative corpus, admitted producer envelope, E0 diagnostic-source directory, S0 diagnostic corpus; include exact relative paths, SHA-256 inventories and System binding/export receipt |
| MANIFEST | ROOT/.swe/implementations/EPIC-002/FEATURE-006/results/run-a/materialize/consumer-manifest.json (run-b substitutes run-b) |
| DIGEST | Fresh MANIFEST canonical digest emitted by Materialize; independently calculate/check and freeze before replay; never a historical digest |
| OUTPUT | ROOT/.swe/implementations/EPIC-002/FEATURE-006/results/run-a/replay, new/empty; run-b separate |
| REVISION | The frozen Ghostagram baseline 40-hex HEAD, initially b3b8d5b09a6b28aa205f85c939524c9f45ce3eec, accompanied by immutable dirty manifest/archive digest; never append prose or falsely claim clean source |
| RESULT | OUTPUT/projection-index.json entry selected by caseId, with its path contained in OUTPUT and SHA-256 matching both index and admitted observation projectionSha256 |
| PREVIEW | ROOT/.swe/implementations/EPIC-002/FEATURE-006/results/run-a/preview/CASEID, create-new files; run-b separate |

Producer readiness means actual passing materialized/replayed System artifacts with matching corpus/binding/envelope hashes, under Accepted producer Design and F005 Validation. System Design approval alone is not artifact readiness; final System/portfolio F006 acceptance is not a prerequisite to generating Ghostagram proof. This avoids a cycle in joint F006 acceptance.

## Failure, Security, Observability, and Operations

Fail closed for absent F005 validation, producer input mismatch, manifest drift, failed case, unsupported required case, failed System admission, missing visible browser or build, source/dependency drift, or incomplete metadata/history proof. Retain every failed receipt and output without overwriting. No local candidate, historical final16 output, old count or stale binary becomes current evidence. Only public projection facts are exported: no VariableBinding values, private declarations, credentials or executable behavior. Keep original System diagnostics distinct from Ghostagram diagnostics.

Every command below has a 600-second timeout (build 900 seconds); visible browser observation has 300 seconds per status. Stop at the first failed prerequisite and retain timeout/exit/stdout/stderr. A repair changes the source closure and invalidates both runs; after approval freeze a new generation and rerun affected checks. Do not repeatedly retry unchanged failures. Tools unavailable to Luna produce Blocked coverage rather than a substitute screenshot or author-run check.

## Change Map

| Area or path | Change after gates | Owner |
|---|---|---|
| src/Ghostagram.Server/GraphWorkspaces/GraphWorkspaceService.cs | Existing named-argument compatibility repair only | Ghostagram implementer |
| tests/Ghostagram.Server.GraphWorkspace.Tests/Program.cs | Explicit capture/history/reload and 107px two-row assertions/attributable observations where absent | Ghostagram implementer |
| tests/Ghostagram.Graph.Conformance/Composition/CompositionConsumerSession.cs | Only necessary receipt attribution at existing admission seam; no new semantics | Ghostagram implementer |
| tests/Ghostagram.Layout.Verification/CompositionPreview.cs | Only necessary attribution at existing export seam; visible proof remains external observation | Ghostagram implementer |
| .swe/implementations/EPIC-002/FEATURE-006/ | Generation manifests, requests, immutable raw receipts/results, Evidence; independent Validation | Child owner / independent validator |
| README.md | Current proof and residual limits after actual observations | Ghostagram implementer |

No other product files are preauthorized by this Design; a necessary additional change requires a reviewed Design amendment. The present author writes only this Design and official generation scratch.

## Test and Evidence Plan

All execution is root-routed `$swe-test` using actual test-runner gpt-5.6-luna at medium. Authors implement assertions/repairs; independent validators judge coverage and request fresh checks. The installed Ghostagram start-server/stop-server and live collaboration/export skills apply if a later visible-browser session needs the server or live diagram; no server is started for Design preparation. Use owned isolated local state only.

Native command arrays below use executable dotnet and cwd ROOT. Substitute each slot into a separate argument, not an interpolated shell command. Historical F003/F004 request files in the Accepted Plan are syntax context only.

- Current-source build: ["build", "Ghostagram.slnx", "-c", "Release", "--no-restore", "-p:ShouldUnsetParentConfigurationAndPlatform=false"]. Serialize with System dependency builds; prerequisites require available recorded restored assets. Record actual referenced/copy-local System hashes and Release outputs. Do not silently restore/upgrade dependencies.
- Scoped workspace build: ["build", "tests/Ghostagram.Server.GraphWorkspace.Tests/Ghostagram.Server.GraphWorkspace.Tests.csproj", "-c", "Release", "--no-restore", "-p:BuildProjectReferences=false", "-p:ShouldUnsetParentConfigurationAndPlatform=false"], then ["tests/Ghostagram.Server.GraphWorkspace.Tests/bin/Release/net10.0/Ghostagram.Server.GraphWorkspace.Tests.dll"]. No runtime after a failed build.
- Materialize: ["tests/Ghostagram.Graph.Conformance/bin/Release/net10.0/Ghostagram.Graph.Conformance.dll", "--profile", "composition", "--corpus", C1, "--producer-envelope", PRODUCER, "--source-root", ROOT, "--consumer-manifest", MANIFEST, "--materialize-manifest", "--e0-source", E0, "--e0-corpus", S0].
- Replay: same materialize array without "--materialize-manifest", adding ["--manifest-digest", DIGEST, "--output", OUTPUT, "--source-revision", REVISION]. Require exit zero, consumer-envelope.json System admission and every required observation passed/available; projection-index existence alone is insufficient.
- Preview for each selected case: ["tests/Ghostagram.Layout.Verification/bin/Release/net10.0/Ghostagram.Layout.Verification.dll", "--composition-preview", RESULT, "--output", PREVIEW].

Receipt base R is `.swe/implementations/EPIC-002/FEATURE-006/checks/common-generation/`; each run/check has a fresh request/receipt/log directory and R/receipt.json is the aggregate index. Exact intervals, executor identity, consumed hashes and outputs bind every observation.

| Criterion | Required check and fixture | Actual participant/operation and expected observation | Timing | Receipt |
|---|---|---|---|---|
| AC-001 | F006-GEN-INVENTORY-REPEAT | Two independent extractions; full pre/post path/digest inventories, additions/deletions and separate dependency/build attribution; identical frozen authored closure | Before/after every command | R/run-a/inventory and R/run-b/inventory |
| AC-002 | F006-GEN-INVENTORY-REPEAT | Compare manifest, corpus/binding identities, projection payload SHA-256 and SVG digests by caseId. Retain raw envelopes; explicitly classify started/ended/correlation IDs, absolute run paths and derived envelope digest changes as run-scoped only when source schema/fields establish that derivation. Do not discard diagnostics/status/observations or normalize unexplained differences | After both runs before acceptance | R/reproduction |
| AC-006 | F006-GHOSTAGRAM-REPLAY-ADMISSION | CompositionRunner -> actual Project/Refresh/producer operations -> System ConsumerEnvelopeAdmission; complete final admitted envelope with exact external digest and generation bindings | After passing producer artifacts | R/run-a/replay and R/run-b/replay |
| AC-006 | F006-GHOSTAGRAM-PREVIEW-BROWSER | Select GRAM-NESTED-OPAQUE (3 nodes, 2 groups, one nested parent, one opaque component), GRAM-PUBLIC-EXPORTS, GRAM-IR-ANCHOR; SvgDiagramExporter and actual Luna visible browser show nested contents without clipping, exact status/revision/content digest, exported SVG and matching provenance | Only after admitted replay; both runs | R/run-a/browser and R/run-b/browser |
| AC-006 | F006-GHOSTAGRAM-PREVIEW-BROWSER status matrix | GRAM-SOURCE-STALE shows prior Fresh only with stale/full-reprojection label and prior revision; GRAM-UNSUPPORTED and GRAM-SKEWED preserve source diagnostics and no new diagram; GRAM-DIAGNOSTICS and GRAM-NO-RELABEL show Failed and never relabel prior success. Observe visible panel, SVG presence/absence and browser console/resources against admitted case results | After admitted replay; both runs | R/run-a/browser and R/run-b/browser |
| AC-006 | F006-GHOSTAGRAM-METADATA-GEOMETRY | Real TryApply expected-revision Upsert of future property; capture shows replacement; SaveAsync persisted graph preserves old future.codec value x=1 plus futureField and new semantic-value changed=true; document/node extensions retained; remove/load returns replacement. Two visible property rows require existing 107px geometry; explicitly assert minimum/row containment. No opacity-slider claim | Successful current-source scoped build first; both runs | R/run-a/metadata and R/run-b/metadata |
| AC-007 | F006-DOCS-VALIDATION | README and Evidence distinguish current passing proof, pending/failed obligations and historical waivers; independent local Validation binds raw receipts/common generation; portfolio independently checks continuity | After both complete proof sets | R/receipt.json and local EVIDENCE.md / VALIDATION.md |

Browser receipt must retain actual visible surface identity/URL, timestamp, screenshots captured by Luna's visible browser tool, resource/console findings, case/result/SVG/provenance hashes and observer identity. Generated HTML/SVG inspection alone does not satisfy it. The preview's generated visibleInspection=Pending remains historical generated data; a separate actual browser receipt establishes observation without rewriting the input artifact.

## Rollout, Compatibility, and Reversal

No production rollout or schema migration. Apply the ordinary source correction only after acceptance; freeze after all approved edits. Isolated tests use temporary persistence, not user workspaces. Preserve old schema-1 bytes/extensions/history and expected-revision conflict semantics. Reversal means reverting only owned edits on explicit later instruction and discarding owned disposable run state only when authorized; failed evidence remains. No Git action is part of this handoff.

## Risks and Divergence

Major, no deferral: replay admission, cross-repository attribution, persistence and visible rendering are mandatory early proof. Capacity corrections may change executable case inventory; derive it from current frozen native materialization rather than historic 176/470 counts. A missing current-source build, unresolved dependency, absent visible observer or unclassified reproduction difference remains a blocker. No architecture divergence is proposed; a new authority/API or changed semantic meaning requires upstream review.

## Risk and Prerequisites

```yaml
risk:
  class: "Major"
  rationale: "Cross-solution reproducibility, admission, persistence and actual browser evidence"
  policy_locator: "ghostworx / EPIC-002 / .swe/epics/002-declarative-composition-model/EPIC.md / revision 3; explicit auto-approve invocation"
  deferral_eligible: false
  confirmed_by: "architecture-reviewer (/root/f005_f006_gate_reviewer)"
dependencies:
  - assignment_id: "GWX-SYSTEM-F005-CANONICAL-BOUNDS"
    artifact: {repository: "ghostworx", artifact_id: "FEATURE-005", path: ".swe/epics/002-declarative-composition-model/features/005-bounded-immutable-composition/FEATURE.md", revision: "1"}
    criteria: ["AC-001", "AC-002", "AC-003", "AC-004", "AC-005"]
    entry_phase: "Implementation"
    requirement: "ValidatedBehavior"
  - assignment_id: "GWX-SYSTEM-F006-GENERATION"
    artifact: {repository: "ghostworx-system", artifact_id: "DESIGN-GWX-SYSTEM-F006", path: ".swe/implementations/EPIC-002/FEATURE-006/DESIGN.md"}
    criteria: ["AC-001", "AC-002", "AC-003", "AC-007"]
    entry_phase: "Implementation"
    requirement: "ApprovedContractOrDesign"
```

The [producer Design](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-006/DESIGN.md) is currently Draft/review pending; do not treat preparation as approval. Actual F005 independent Accepted local Validation at `ghostworx-system/.swe/implementations/EPIC-002/FEATURE-005/VALIDATION.md` must be read and its actual ID/generation/AC-001..005 passing receipts verified at entry; absence blocks implementation. Producer artifact readiness is additionally required immediately before replay, as specified above, without requiring final F006 portfolio acceptance. No local AC-006 is assigned to System's dependency.

## Review Packet

- Exact Design bytes are frozen once for root's independent review and root-routed Luna static checks; the coordinator records SHA-256 externally to avoid a circular fingerprint.
- Accepted reconciliation current SHA-256: 725E6CD5DD6D5EA42182B0EFF3808A23451BB50A3C892A486C6E67E9FB3E34CF. Its independent cycle-0 receipt is checks/target-plan-reconciliation/e2640389-884e-4618-a916-6da74813ded1/receipt.json (4 cases, 31 checks); this is architecture correspondence, not Design or runtime proof.
- Policy: explicit auto-approve without force; root assigns independent architecture-reviewer. Author cannot accept this Design.
- Review-cycle history is this section plus root's independent review record; cycle 0, zero Design repair cycles consumed. Upstream Plan cycle 2 remains exhausted and is not reopened.
- Static checks requested through root: generated metadata integrity, exact IDs/dual locators and reachable links, approval/lifecycle consistency, scope, prerequisite criteria, generation and test matrix. No executable check has been run by this author. Correspondence must be verified before acceptance.

## Approval Record

| Field | Value |
|---|---|
| Mode | auto-approve |
| Author | Ghostagram Design author (/root/ghostagram_f006_design) |
| Approver | architecture-reviewer (`/root/f005_f006_gate_reviewer`) |
| Decision | Accepted — cycle 0 |
| Recorded | `2026-09-09T21:04:10Z` |
| Evidence | [Independent Design review](reviews/DESIGN-REVIEW.md); frozen Design SHA-256 `B4C4CD13DF1A062D852808272082D20346EE5ADDB2BABC7B9371DF73514462CF`; bound Luna receipt `checks/design-static/2b0d79cd-96a6-4626-9159-93b584a9ab1e/receipt.json` |
| Bypass reason | None; force not authorized |

<!-- swe:generated traceability sha256=50079911b99fed6d3f56adedef053ff7a489e179a0fa389abda156ac025dfaad -->
## Generated Traceability

Mechanical index only; criterion judgments and approvals remain independently authored.

| Criterion | Owner | Expected evidence locator |
|---|---|---|
| AC-001 | GHOSTAGRAM-F006-PROJECTION-AND-METADATA | .swe/implementations/EPIC-002/FEATURE-006/EVIDENCE.md |
| AC-002 | GHOSTAGRAM-F006-PROJECTION-AND-METADATA | .swe/implementations/EPIC-002/FEATURE-006/EVIDENCE.md |
| AC-006 | GHOSTAGRAM-F006-PROJECTION-AND-METADATA | .swe/implementations/EPIC-002/FEATURE-006/EVIDENCE.md |
| AC-007 | GHOSTAGRAM-F006-PROJECTION-AND-METADATA | .swe/implementations/EPIC-002/FEATURE-006/EVIDENCE.md |

| Assignment | Repository | Local artifact path | Criteria |
|---|---|---|---|

Upstream locator: IMPL-PLAN-EPIC-002-FEATURE-006 in ghostworx, .swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md
- [Accepted Feature](<../../../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/FEATURE.md>)
- [Accepted Plan](<../../../../../../.swe/epics/002-declarative-composition-model/features/006-reproducible-composition-integration/IMPLEMENTATION-PLAN.md>)
<!-- swe:end traceability -->

## Current System Corpus Amendment — revision F006-current-1, Accepted

This narrow technical amendment follows the explicit user early-development/current-only disposition; compatibility is deferred to the portfolio gate before first stable release or external adoption. It consumes `ghostworx-system` [DESIGN-GWX-SYSTEM-F005, F005-current-1](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-005/DESIGN.md) and [DESIGN-GWX-SYSTEM-F006, F006-current-1](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-006/DESIGN.md). Their canonical paths are `.swe/implementations/EPIC-002/FEATURE-005/DESIGN.md` and `.swe/implementations/EPIC-002/FEATURE-006/DESIGN.md` in that repository. F005-current-1 and F006-current-1 are Accepted. This amendment is **Accepted / Accepted independent technical review**; earlier Accepted decisions apply only to their recorded bytes. The human scope disposition does not reset review cycles, confer self-approval or imply force.

Upon independent acceptance, this section supersedes only the earlier mandatory C1/E0/S0 input coupling and associated native argument arrays. Consume the corresponding run's complete current `corpus/`, exact producer envelope, generation binding, current materialization report and passing producer/persisted-reader receipts. Verify the transitive manifest, closed registry, candidate/expectation/prerequisite, schema/profile/compiler/contract and envelope hashes through current System admission. Do not invent empty E0/S0 directories, aliases or historical anchors. Counts derive from the frozen current manifests; no old count or bootstrap is required merely to preserve superseded tooling versions.

The actual System `current-command-manifest.json` must bind validated F005 source/entrypoint/DLL hashes and exact current materializer/producer/persisted-reader arrays. Separately freeze this consumer's supported current native arrays, entrypoint/source/DLL hashes, exact input/output paths and expected observations in its pre-execution request. System command syntax is not consumer syntax. Current CLI entry names/options remain implementation facts to resolve before dispatch: the old E0/S0 arrays below are historical context, not fallback commands. A missing current input mode or incompatible native option blocks execution pending a bounded existing-seam implementation and attributable check; this amendment invents no replacement CLI. Never silently drop arguments and assume equivalent admission.

All other Design requirements remain: two fresh isolated builds/runs from the same four-repository source archive; full additions/modifications/deletions and no-drift inventories; separate authorized offline dependency/output bindings; raw retention and strict classified reproduction comparisons; all assigned F006 criteria; Major/no deferral; independent local Design acceptance and actual Accepted F005 AC-001 through AC-005 ValidatedBehavior before implementation. Producer readiness requires fresh passing current System artifacts, not merely an approved Design and not circular final all-consumer F006 acceptance. No public semantics, security, size limits, cancellation, persistence, browser or consumer-authority waiver is granted.
### Ghostagram-specific supersession and preserved proof

Replace the C1/PRODUCER/E0/S0 input-table row and materialize/replay arrays with the current export contract and frozen supported native arrays above. Preserve current consumer-manifest materialization, independent external digest calculation, actual Project/Refresh operations, System consumer-envelope admission and complete projection-index/result digest binding. Existing references saying the System Design is wholly absent/Draft are historical: its earlier revision was accepted, but the current-route amendment must be independently accepted and the consumed generation validated before dispatch.

A necessary current-input adaptation is limited to existing internal Composition consumer option/intake/session seams under `tests/Ghostagram.Graph.Conformance/Composition/`, including `CompositionConsumerSession.cs`; no public model, compiler, projection meaning or production admission changes are authorized. This narrow addition supersedes the Change Map's exclusion only for those internal corpus/command-binding seams. Freeze actual changed files and obtain the required attributable checks before replay.

All AC-006 observations remain unchanged: GRAM-NESTED-OPAQUE, GRAM-PUBLIC-EXPORTS and GRAM-IR-ANCHOR; Fresh/Stale/Unsupported/Skewed/Failed and no-relabel behavior; actual visible browser with console/resources and matched SVG/provenance; metadata replacement/capture/save/history/reload preserving extensions and future values; two property rows within existing 107px geometry. If current native materialization renames a selected fixture, independently review an exact semantic-equivalence mapping before execution. Generated preview or index existence never substitutes for admitted replay or visible observation. Preserve current metadata/history compatibility; the removed compatibility premise concerns superseded System tooling corpus generations only.

Review history remains Accepted cycle 0 with zero Design repair cycles consumed; upstream exhausted cycles remain unchanged. Amendment author: `/root/system_f006_design`; independent decision Accepted by architecture-reviewer (`/root/f006_current_design_review`) on `2026-09-09T22:50:46Z`, recorded in [DESIGN-REVIEW](reviews/DESIGN-REVIEW.md) against frozen SHA-256 `DD7E0F57960A822B4F865F52C7C7D7FCABAE7F5B0FA99C6207AFAD668D282B50` and the bound Luna receipt `C:/Users/justin/Source/ghostworx/.swe/checks/epic002-f006-current-only-20260909/results/1cfd7cac-4613-4eea-9f0d-684f44edc902/receipt.json` (69/69, no source drift or missing coverage). No code, server startup, browser action or runtime proof is claimed.
