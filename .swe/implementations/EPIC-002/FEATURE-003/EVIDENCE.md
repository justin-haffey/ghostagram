---
title: "Read-Only Composition Projection — Ghostagram Evidence"
artifact_type: "implementation_evidence"
id: "EVIDENCE-EPIC-002-FEATURE-003-GHOSTAGRAM"
status: "InProgress"
authority: "solution"
scope: "ghostagram"
parent: "DESIGN-EPIC-002-FEATURE-003-GHOSTAGRAM"
upstream:
  repository: "ghostagram"
  artifact_id: "DESIGN-EPIC-002-FEATURE-003-GHOSTAGRAM"
  path: ".swe/implementations/EPIC-002/FEATURE-003/DESIGN.md"
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
owners:
  - "Ghostagram child implementation coordinator"
created: "2026-09-07"
updated: "2026-09-07"
revision: "1"
template_version: "2.0.0"
---

# Implementation Evidence

## Current Evidence Status

F003 source and local projection behavior are implemented and pass provisional integration checks. This evidence remains **InProgress** because the corrected complete producer corpus and producer envelope, final combined System generation, producer-linked consumer manifest/envelope, actual renderer previews with visible inspection, and independent local Validation do not yet exist.

[Design revision 2](DESIGN.md) was independently Accepted by the user-named `elon-musk` at `2026-09-07T12:01:36Z`, cycle 0 without conditions. The exact reviewed bytes and decision are retained under [the Design review packet](checks/design-revision-002-review/). Design acceptance does not approve this implementation or any criterion.

## Implemented Boundary

- [DeclarativeCompositionProjection](../../../../src/Ghostagram.Bridge/DeclarativeCompositionProjection/) maps only accepted compiler or verified IR `PublicView` data into immutable Ghostagram diagram state. It retains hierarchy, opaque boundaries, public facts, inert declared surfaces, exact source identity, content identity, provenance, ordered reference provenance, compatibility, lineage and source diagnostics.
- `Project` and `Refresh` return the closed `Fresh | Stale | Unsupported | Skewed | Failed` result family. Only Fresh carries a newly accepted diagram. Refresh permits presentation-only deltas over an equal source anchor; semantic discontinuity requires full reprojection.
- Actual rejected Compile, Verify, Decode, Encode, Migrate, Inspect, Negotiate and GetExact result types have diagnostic-preserving projection paths. Successful wire records require a separately executed actual Verify before projection. Successful semantic results without an IR PublicView are recorded as non-projectable only after exact owner observation checks pass.
- [CompositionOwnerReplay](../../../../tests/Ghostagram.Graph.Conformance/Composition/CompositionOwnerReplay.cs) uses the System test-only execution driver and shared result observer. It retains exact fixture/profile/context/checkpoint inputs, actual prerequisites, operation/schema/status, ordered diagnostics, required observed fields and original producer result bytes before a separate consumer Project call.
- [Composition consumer tooling](../../../../tests/Ghostagram.Graph.Conformance/Composition/) materializes a concrete manifest before consumer execution, binds local source/applicability bytes, preserves actual per-case projection JSON, and delegates candidate admission to the System-owned consumer-envelope implementation.
- [CompositionPreview](../../../../tests/Ghostagram.Layout.Verification/CompositionPreview.cs) accepts actual result JSON and routes its DiagramDocument through the existing `SvgDiagramExporter`. Fresh renders a diagram; Stale renders only explicitly last-known output; failure statuses render a status panel without a new diagram.

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

- System must finish and freeze the corrected complete inventory/corpus, producer envelope and final combined generation. The historical partial/407-case artifacts cannot close F003.
- Ghostagram must build and rerun local checks against that exact generation with owner/copy verification.
- The producer-linked runner must materialize and externally freeze its concrete consumer-manifest digest before Project/Refresh execution, execute every required case, and produce a System-admitted consumer envelope with no failed, missing or unsupported required case.
- Actual preview outputs for Fresh nested/opaque and inert surfaces, Failed diagnostics, Stale, Unsupported and Skewed states must be visibly inspected and recorded. Static SVG cannot prove a future interactive host.
- Final source/result fingerprints, requirement mapping and residual risks must replace this provisional record. Evidence status then may become Complete.
- An independent solution validator must author `VALIDATION.md`; the implementer does not validate its own work. Portfolio Feature acceptance and architecture promotion remain separate.

No deployment, publication, dependency-version change, Git mutation, production-data change or architecture promotion occurred.
