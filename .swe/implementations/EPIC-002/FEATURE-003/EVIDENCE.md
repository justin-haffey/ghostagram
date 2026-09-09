---
title: "Read-Only Composition Projection — Ghostagram Evidence"
artifact_type: "implementation_evidence"
id: "EVIDENCE-EPIC-002-FEATURE-003-GHOSTAGRAM"
status: "Complete"
authority: "solution"
scope: "ghostagram"
parent: "DESIGN-EPIC-002-FEATURE-003-GHOSTAGRAM"
upstream:
  repository: "ghostagram"
  artifact_id: "DESIGN-EPIC-002-FEATURE-003-GHOSTAGRAM"
  path: ".swe/implementations/EPIC-002/FEATURE-003/DESIGN.md"
  revision: "3"
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
owners:
  - "Ghostagram child implementation coordinator"
created: "2026-09-07"
updated: "2026-09-08"
revision: "5"
template_version: "3.0.0"
---

# Implementation Evidence

## Delivery Progress

```yaml
delivery_progress:
  implementation: Complete
  verification: Complete
  acceptance: Accepted
  source_generation: "ghostagram:d94c70adfd5579bd0fcc3dc52309fd9a5bd3dc35+uncommitted-source-batch"
  acceptance_locator: ".swe/implementations/EPIC-002/FEATURE-003/VALIDATION.md"
  pending_obligations: []
```

## Accepted Prototype Closure — 2026-09-08

This section supersedes the operational disposition below while preserving every attempted receipt. Independent `elon-musk` accepted the local prototype delivery at `2026-09-08T23:44:33Z`. The accepted evidence is the 176-case manifest materialization and the first replay's **176/176 passing projection assertions**, including the two representative final16 producer cases, all 15 named projection scenarios, and the complete local boundary/profile/context/output-size inventory.

Justin's explicit finish-and-approve direction closes three observations as owner-waived prototype risks: final consumer-envelope admission, focused exporter/visual verification, and clean build-receipt attribution. They are not reported as passes. The first replay's candidate admission failed only because its supplied source revision contained a non-hex suffix; later exact-hex attempts were blocked by path-expansion, shared-output locking, or an execution with no output before the owned child was stopped. No admitted consumer envelope or final preview exists. The independent decision and limits are recorded in [Validation](VALIDATION.md).

## Current Evidence Status

F003 source and local projection behavior are implemented through the current source batch. Accepted Design revision 3 removed duplicate full producer replay while retaining two representative producer cases, all 15 named local scenarios, the complete local boundary/profile/context/output-size inventory, and complete final16 producer binding. Final16 materialization passes for all 176 consumer cases. The owner-waived gaps above remain explicit and limit this acceptance to prototype closure.

[Design revision 3](DESIGN.md) was independently Accepted by the user-named `elon-musk` at `2026-09-08T23:03:16Z` with one satisfied condition: `LoadProducer` applies the 4,194,304-byte transport cap only after the corpus is admitted and confirmed normative. Revision 2 remains preserved under [its Design review packet](checks/design-revision-002-review/). Design acceptance does not approve this implementation or any criterion.

## Implemented Boundary

- [DeclarativeCompositionProjection](../../../../src/Ghostagram.Bridge/DeclarativeCompositionProjection/) maps only accepted compiler or verified IR `PublicView` data into immutable Ghostagram diagram state. It retains hierarchy, opaque boundaries, public facts, inert declared surfaces, exact source identity, content identity, provenance, ordered reference provenance, compatibility, lineage and source diagnostics.
- `Project` and `Refresh` return the closed `Fresh | Stale | Unsupported | Skewed | Failed` result family. Only Fresh carries a newly accepted diagram. Refresh permits presentation-only deltas over an equal source anchor; semantic discontinuity requires full reprojection.
- Actual rejected Compile, Verify, Decode, Encode, Migrate, Inspect, Negotiate and GetExact result types have diagnostic-preserving projection paths. Successful wire records require a separately executed actual Verify before projection. Successful semantic results without an IR PublicView are recorded as non-projectable only after exact owner observation checks pass.
- [CompositionOwnerReplay](../../../../tests/Ghostagram.Graph.Conformance/Composition/CompositionOwnerReplay.cs) uses the System test-only execution driver and shared result observer. It retains exact fixture/profile/context/checkpoint inputs, actual prerequisites, operation/schema/status, ordered diagnostics, required observed fields and original producer result bytes before a separate consumer Project call.
- [Composition consumer tooling](../../../../tests/Ghostagram.Graph.Conformance/Composition/) materializes a concrete manifest before consumer execution, binds local source/applicability bytes, preserves actual per-case projection JSON, and delegates candidate admission to the System-owned consumer-envelope implementation.
- Producer-linked inventory and final envelope publication require System-owned `ConformancePurpose.NormativeConformance` on the admitted corpus, producer envelope, and consumer envelope. Diagnostic-seed and unrecognized inputs cannot be promoted into final Ghostagram evidence, and every producer-linked run requires an explicit source revision.
- Bound-C1 execution accepts only the paired explicit CLI arguments `--e0-source` and `--e0-corpus`. It acquires the System-owned `AcquiredDiagnosticResultSource`, loads the C1 catalog through the owner `ICorpusPublishedResultSource` seam, creates admission input through `CorpusArtifactCatalog.CreateAdmissionRequest()`, and holds/rechecks that acquisition across corpus and producer admission, owner replay, consumer projection and final publication. Omitting both arguments preserves the unbound corpus path; supplying only one fails before corpus loading.
- Owner replay preserves the System `Unsupported` exchange status as `CompositionResultStatus.Unsupported`; it does not relabel an explicit unsupported tuple as a generic rejection.
- [CompositionPreview](../../../../tests/Ghostagram.Layout.Verification/CompositionPreview.cs) accepts actual result JSON and routes its DiagramDocument through the existing `SvgDiagramExporter`. Fresh renders its accepted diagram; Stale renders only an exact prior Fresh result and records that result's separate presentation revision and source content digest; failure statuses render a status panel without a new diagram.

## Current Source Batch — 2026-09-08

| Path | Implemented change | Execution state |
|---|---|---|
| `tests/Ghostagram.Graph.Conformance/Composition/CompositionConsumerSession.cs` | Loads unbound or explicit bound-C1 catalogs through the owner APIs, rechecks the held E0 source around admission, enforces normative purpose before replay/output, requires an attributable source revision, and refuses to publish a non-normative admitted consumer envelope | Revision-3 build and 176-case materialization passed; corrected replay blocked on missing shared C1 input |
| `tests/Ghostagram.Graph.Conformance/Composition/ProducerProjectionCases.cs` | Verifies the complete admitted producer case inventory, then binds the two accepted Design-representative final16 cases to the consumer Project seam | Both representative cases executed in the first replay; final envelope admission failed because that attempt supplied an invalid source revision |
| `tests/Ghostagram.Graph.Conformance/Composition/CompositionConsumerProtocol.cs` | Makes source revision mandatory in the candidate contract | The first replay proved the owner also requires exactly 40 hexadecimal characters; corrected invocation did not launch because C1 changed |
| `tests/Ghostagram.Graph.Conformance/Composition/CompositionRunner.cs` | Requires `--source-revision`, rejects unpaired `--e0-source`/`--e0-corpus`, and holds the acquired E0 lifetime through manifest materialization or replay/publication | Bound final16 materialization passed with unchanged inputs; corrected replay was blocked before launch on a missing C1 prerequisite file |
| `tests/Ghostagram.Graph.Conformance/Composition/CompositionOwnerReplay.cs` | Maps the owner `CompositionExchangeStatus.Unsupported` without semantic relabeling | Two representative final16 cases executed in the rejected first replay; admitted publication remains unobserved |
| `tests/Ghostagram.Layout.Verification/CompositionPreview.cs` | Distinguishes result revision from rendered Fresh/last-known revision and records the rendered source content digest | No admitted final16 replay output exists; renderer execution and visible inspection remain blocked |

The final16 observations below supersede the earlier statement that this batch was unexecuted. They do not make the provisional receipts reusable for the changed source generation.

## Final16 Attempted Verification — 2026-09-08

The independent `$swe-test` executor was `test-runner` on `gpt-5.6-luna` at medium reasoning. System final16 reported `470/470` owner cases before this child run. Ghostagram used C1 corpus `final16-seed-20260908-1/C1`, E0 source `final16-seed-20260908-1/E0`, S0 corpus `final16-seed-20260908-1/S0`, producer replay envelope SHA256 `3c620ca70b041033cc240f129ee907b6a571fcc5a0fe8c83343c5e8793e05cf1`, and binding SHA256 `9e2d7f0612c6ceff0f9bc5ab5153d13f182f81b4ffb47f78ecd5f025b69366ea`. The E0 source digest is `f60da481e545e5a5085cb08099e929ee5f8a98d50122d8724955e0f55e7e552c`; its owner receipt explicitly classifies `finalNormativeEvidence` as `false`.

| Phase | Exact observation | Evidence |
|---|---|---|
| Initial scoped run | Bridge build, Bridge.Tests build, and Bridge native execution passed with exit 0. Conformance build failed with `CS0103` for the missing `ConformancePurposeRegistry` namespace import. The receipt is `Blocked` and non-reusable because captured inputs changed during execution. | [Receipt `da97130d-31d3-4aa8-bd7d-7f89acdf161f`](checks/final16-20260908-r1/da97130d-31d3-4aa8-bd7d-7f89acdf161f/receipt.json) |
| Repaired conformance build | After importing `Ghostworx.System.Composition.Conformance`, the conformance build passed with exit 0. The receipt remains `Blocked` and non-reusable because captured inputs changed during execution. | [Receipt `35a83570-bddd-476b-9595-ab77bb5d4c8f`](checks/final16-20260908-r2/35a83570-bddd-476b-9595-ab77bb5d4c8f/receipt.json) |
| Bound manifest materialization | The requested bound run stalled during E0/C1 acquisition and was terminated after sustained no progress. It produced no runner receipt, consumer manifest, admitted consumer envelope, projection index, replay output, or case counts. Replay and preview generation therefore did not run. | [Materialization request](checks/final16-20260908-r3/materialize.request.json); preserved executor request under `checks/final16-20260908-r3/c41dd0ed-5763-4698-8153-0533ff359baa/` |

No browser or full-size visible preview inspection was run because the bound run produced no final16 result input. No acceptance criterion is claimed complete from this attempted verification, and no implementation acceptance is implied by the earlier Design approval.

## Revision-3 Final16 Verification — 176-Case Manifest

The accepted revision-3 inventory contains two representative final16 producer-linked Project cases, all 15 named `GRAM-*` projection scenarios, and the complete local boundary/profile/context/output-size cases, for 176 total entries. It remains bound to the complete admitted final16 producer corpus and envelope rather than treating the two representative calls as exhaustive owner evidence.

| Phase | Exact observation | Evidence |
|---|---|---|
| Conformance build | Native exit 0. Stdout SHA256 `3958d6a82424a0b72948e920bcd705e15b5e318afae72c788720048d08731991`. The receipt envelope is `Blocked` and non-reusable because repository inputs changed while execution artifacts were written. | [Receipt `3ba41c81-61db-4d90-954d-a5fe7cb09f57`](checks/final16-17case-20260908-r1/3ba41c81-61db-4d90-954d-a5fe7cb09f57/receipt.json) |
| Bound materialization | Passed with unchanged inputs and reusable receipt. The manifest contains 176 cases; canonical manifest digest `24583d072383dff3da34f8ad5f7b1e9c41309b593eefb4f993af157cd07b6012`; file/observation SHA256 `3196ddfc5f7bae55097c3bc0f817a91efca61f25a2db6b6d97744b9809ac418`. | [Manifest](checks/final16-17case-20260908-r1/materialize/consumer-manifest.json) and [receipt `85378cdb-69ee-452c-bf88-d484e727482b`](checks/final16-17case-20260908-r1/materialize/85378cdb-69ee-452c-bf88-d484e727482b/receipt.json) |
| First 176-case replay | All 176 consumer observations executed and were placed in the passed partition. Projection statuses were Fresh 42, Failed 84, Stale 1, Unsupported 1, Skewed 1, with 47 sidecar-admission cases having no projection path. System rejected final candidate admission with `CMP-ENVELOPE-INVALID` because the supplied source revision appended a non-hex suffix. Projection-index SHA256 `36f670c533451b6c06fa8bbeb6eac7b5226756be11c81f394630c1e8c697e1f4`; unadmitted-candidate SHA256 `228533f5fa5ff7690ee94cf7a75155b60af8331eb23cd3dbf7ada812a66b5956`; diagnostic SHA256 `221b2a8f8679b15e3ab0aedda6a73e9286f5ff173868734be456dcaa4c8fdbdd`. | [Failed replay receipt `1d161bf0-77a5-407c-8d34-bf4cf2329c16`](checks/final16-17case-20260908-r1/replay/1d161bf0-77a5-407c-8d34-bf4cf2329c16/receipt.json) and [rejected outputs](checks/final16-17case-20260908-r1/replay/output/) |
| First corrected replay preflight | The retry supplied exact 40-hex revision `d94c70adfd5579bd0fcc3dc52309fd9a5bd3dc35`. It was blocked before native launch because `C1/prerequisites/d2141bd138a01b80f7c1bacee782aaa965956e65be023e25d16af23e94ac87b1/candidates/COMP-DET-UNSUPPORTED-COMPILER-PROFILE.json` was unavailable. No attempt, envelope, replay output, or preview was produced. | [Blocked receipt `b53c3484-ba92-469d-8c73-ae31a402e5d0`](checks/final16-17case-20260908-r2/replay/b53c3484-ba92-469d-8c73-ae31a402e5d0/receipt.json) |
| Restored-input retry | After the owner reported the exact C1 prerequisite restored, the same exact-hex replay again blocked before native launch on that missing path. | [Blocked receipt `4a047137-e0e8-4e5f-b7b0-fc20081948fc`](checks/final16-17case-20260908-r3/replay/4a047137-e0e8-4e5f-b7b0-fc20081948fc/receipt.json) |
| Stable-slot retry | After shared-output work was reported complete, the bounded final retry again found the path unavailable during preflight; its immediate post-run `Test-Path` found the same path present. This confirms a racing or otherwise unstable owner input view. No native attempt ran. | [Blocked receipt `efb1c71a-8f94-45d6-8778-5083fe46cb68`](checks/final16-17case-20260908-r4/replay/efb1c71a-8f94-45d6-8778-5083fe46cb68/receipt.json) |

The successful materialization establishes a concrete, frozen consumer inventory. The first replay's outputs are rejected-candidate diagnostics and cannot support Feature acceptance or visible-delivery closure. The corrected replay requires the System owner to provide a stable, fully attributable final16 C1 view for the complete fingerprint-and-execution interval. The bounded retries are exhausted for this handoff; no further Ghostagram source repair is indicated by any corrected replay receipt.

## Provisional Tested Generation

The local checks used System `provisional-20260907T120942Z`, explicitly classified by its owner as a provisional API generation with `31/32` composition groups passing. It is not the final combined generation or Feature acceptance evidence.

| Anchor | Value |
|---|---|
| Owner manifest | SHA256 `d716b81b89c9cc5152e4a3b577ee5381056f52f3b88981b8ceacc236b92bb273`; 25 recorded outputs |
| Owner source | Fingerprint `5a70b636b93f11b5a85eea82ffccf1900e3292fc26ce260f690634b159e657da`; archive SHA256 `00591636d04d2acc04ff5f997f792c08ec288e4d3d2c670aeea3ef75541c4c68` |
| Nested support supplement | SHA256 `2635a9b6b75a8e6027a8d58265a90e72bb6df9415eb3454bcd25b9d3ec182944`; exact immutable-manifest/fingerprint/project binding |
| Final local phase source | Fingerprint `469a7d0ddf1d1390344da560c082772a3f827d06926e207e4f57a7ba1d6decfe`; archive SHA256 `bc313adc5e3d1179327826b254c92f4fb0756a7a56c21fe0cdb72845ec552a11` |
| F004 owner repair Validation | System isolated cycle 3 Accepted record SHA256 `8ffbfe44fc3369115fc65aa7d9e74e0c785c2f77e34267530513f776ad7e1754`; final combined consumer validation remains separate |

## Provisional Verification

| Check | Result | Evidence |
|---|---|---|
| Child-only cached restore and scoped Release build | Pass: Ghostagram.Bridge, Bridge.Tests, Graph.Conformance and Layout.Verification; no System build or dependency-version change | [Initial build phase](checks/source-integration/provisional-120942-build-01/) and final fixture rebuild phases `provisional-120942-fixture-build-01` through `-03` |
| Composition module/test mode | Pass after three preserved test-fixture corrections; actual compiler/projector/exchange/migration paths completed with native zero exit | [Passing phase](checks/source-integration/provisional-120942-composition-04/) |
| Local projection inventory | Pass `174/174`; 128 actual projection JSON files plus index; native zero exit, no forced termination | [Local projection phase](checks/source-integration/provisional-120942-local-projections-02/) and [index](checks/source-integration/provisional-120942-local-projections-02/projections/local-projection-index.json) SHA256 `da4e38761b73e69f0413b243c6c5e3ae48026521936a37079882240ef3f93e2` |
| Owner/copy identity | Pass: 26 owner rows and 16 copied System DLLs match exactly, including the separately attested nested support output | `owners-before.json`, `owners-after.json` and `copies-local-projections.json` in the passing local phase |
| Source/static integrity | Pass: source archive/manifest captured per phase, affected project XML parses, reference paths and `git diff --check`; only line-ending warnings | Passing phase source records and source-integration continuation |

The fixture corrections preserve the failures that exposed them. `provisional-120942-composition-01` records unpinned local Definition reference skew. `-02` records incorrectly pinned VariableType/Scope/Reference identifiers. `-03` records a changed root Definition revision whose pinned identity still named the old revision. The final fixture pins composition identities at revision 1 by default, leaves VariableType/Scope/Reference identities canonical and unpinned, and changes only Definition identities with the supplied source revision. `provisional-120942-local-projections-01` records the original audit failure for the owner-manifest omission; the harness now accepts only the exact validated supplement and still checks the immutable original manifest.

## Provisional Criterion Coverage

| Criterion | Provisional observation | Remaining final evidence |
|---|---|---|
| AC-001 | `GRAM-NESTED-OPAQUE` and `GRAM-PUBLIC-EXPORTS` pass against actual compiler PublicView hierarchy | Complete producer-linked fixtures and visible nested/opaque inspection |
| AC-002 | Actual rejected owner results preserve exact diagnostics and never create Fresh output | Corrected complete producer corpus/envelope and every applicable rejected producer case |
| AC-003 | IR/content/profile/provenance/reference-provenance anchors, no mutation and public-only facts pass locally | Final generation/source anchors and producer-bound consumer envelope |
| AC-004 | `GRAM-INERT-SURFACES` passes for all four display-only, nonconnectable descriptor kinds; no diagram ports/edges/editor/control path | Actual SVG/panel inspection and final source/API audit |
| AC-005 | Same-source presentation delta, semantic-source Stale and explicit full Project recovery pass | Final-generation actual JSON and visible stale labeling |
| AC-006 | All five result arms and local supported/unsupported/skew/failure paths pass | Complete concrete consumer manifest and admitted producer-linked envelope |
| AC-007 | 157 local boundary cases plus two serialized-result byte cases pass, including exact/+1/invalid, cancellation, deadline, collision, privacy and nonmutation | Corrected complete source/profile fixture execution and independent Validation |

## Prepared Visual Inputs

The provisional passing phase contains actual result inputs ready for the existing renderer. These are preparation evidence only; no renderer command or visible inspection is claimed while System owns the build slot.

| Case | Status | Input SHA256 |
|---|---|---|
| `GRAM-NESTED-OPAQUE` | Fresh | `c0f2fd25064875070fcd7da7bc14431f6377e50ac265946fcb6335f023f45e56` |
| `GRAM-INERT-SURFACES` | Fresh | `c0f2fd25064875070fcd7da7bc14431f6377e50ac265946fcb6335f023f45e56` |
| `GRAM-DIAGNOSTICS` | Failed | `f0c1aee24f7a13ad07479b9f018c83fc9d1bbcedfe39f636948878f621c00699` |
| `GRAM-SOURCE-STALE` | Stale | `4d79e0c895cf32e47b7aefa222f080d134071e3265dc1f2cc9629391b9f79404` |
| `GRAM-UNSUPPORTED` | Unsupported | `b932d13af1cc925ca10743ef5e035cc94c2e5688bc66de928571a5262b7fd4f0` |
| `GRAM-SKEWED` | Skewed | `0e32919b91e559043e45c32d86c90fc36edd44dc8499ff32e3abfa7930ee63d4` |

Each future preview must use `dotnet tests/Ghostagram.Layout.Verification/bin/Release/net10.0/Ghostagram.Layout.Verification.dll --composition-preview <actual-json> --output <fresh-directory>`, retain its unmodified renderer SVG/status panel/provenance, and receive full-size visible inspection. Final evidence must use actual outputs from the final combined generation rather than relabel these provisional inputs.

## Remaining Delivery Gaps

- System final16 supplied the complete owner corpus and producer envelope, and Ghostagram froze a reusable 176-case manifest. The shared C1 prerequisite tree changed before the corrected replay, so the owner must restore that exact tree or issue another stable, fully attributable slot.
- Ghostagram still requires a reusable corrected replay receipt against a frozen owner generation, including owner/copy verification.
- The producer-linked runner must replay the already frozen manifest with the exact 40-hex source revision and produce a System-admitted consumer envelope with no failed, missing or unsupported required case.
- Actual preview outputs for Fresh nested/opaque and inert surfaces, Failed diagnostics, Stale, Unsupported and Skewed states must be visibly inspected and recorded. Static SVG cannot prove a future interactive host.
- Final source/result fingerprints, requirement mapping and residual risks must replace this blocked record. Evidence status then may become Complete.
- An independent solution validator must author `VALIDATION.md`; the implementer does not validate its own work. Portfolio Feature acceptance and architecture promotion remain separate.

No deployment, publication, dependency-version change, Git mutation, production-data change or architecture promotion occurred.
