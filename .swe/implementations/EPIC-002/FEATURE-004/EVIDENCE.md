---
title: "Layered System Definition Foundation — Ghostagram Evidence"
artifact_type: "implementation_evidence"
id: "EVIDENCE-EPIC-002-FEATURE-004-GHOSTAGRAM"
status: "Complete"
authority: "solution"
scope: "ghostagram"
parent: "DESIGN-EPIC-002-FEATURE-004-GHOSTAGRAM"
upstream:
  repository: "ghostagram"
  artifact_id: "DESIGN-EPIC-002-FEATURE-004-GHOSTAGRAM"
  path: ".swe/implementations/EPIC-002/FEATURE-004/DESIGN.md"
  revision: "2"
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
    revision: "3"
owners:
  - "ghostagram_bridge_implementation"
created: "2026-09-07"
updated: "2026-09-07"
revision: "1"
template_version: "2.0.0"
---

# Implementation Evidence

## Change Summary

Ghostagram's assigned F004 AC-007/AC-009 work is implemented and verified against the frozen System F004 generation. Existing Bridge projection, local execution, host persistence and Graph/Variable/Links conformance behavior pass after direct ownership/API migration. This Complete evidence records execution; it does not accept the Accepted [Design revision 2](DESIGN.md), Accepted Target revision 3 or the Feature. Independent Target/Design review has accepted the recorded successors; local Validation remains separate.

Diagram compilation now carries detached local context in `GraphCompilationInput` alongside a separate System `GraphLocalSnapshot`. Native local and explicit governed-inspection overloads remain distinct. Host persistence uses the local schema-1 codec/capture/materialization seam with explicit target retention and history-unavailable-only fallback. Bridge-created nodes inherit System transactional `GraphNode`, fixing the observed repeat-mutation rollback gap.

## Changed Paths and Tested Generation

- [Bridge contracts and projection](../../../../src/Ghostagram.Bridge/BridgeContracts.cs), [projection mapper](../../../../src/Ghostagram.Bridge/Projection.cs) and [command adapter](../../../../src/Ghostagram.Bridge/CommandAdapter.cs): local snapshots/batches, typed metadata and runtime transactional node integration.
- [GraphCompiler](../../../../src/Ghostagram.Execution/GraphCompiler.cs), [execution contracts](../../../../src/Ghostagram.Execution/ExecutionContracts.cs) and [execution tests](../../../../tests/Ghostagram.Execution.Tests/Program.cs): explicit immutable input, semantic-only path, finite capacity and retained parity.
- [Workspace host](../../../../src/Ghostagram.Server/GraphWorkspaces/GraphWorkspaceService.cs) and [host tests](../../../../tests/Ghostagram.Server.GraphWorkspace.Tests/Program.cs): schema-1 custom kinds, exact null/empty governance shape, extensions, edit/restart and expired-history fallback.
- Project references, existing conformance harnesses, Cutover/Bridge tests, benchmark and [README](../../../../README.md) directly follow the actual owner APIs.

[Source manifest](checks/candidate-release/source-manifest.json) and [source archive](checks/candidate-release/source.zip) capture 138 source/config/test files at Ghostagram HEAD `d3f2542c28acfed7ecf04f9c37a4e47241138f29` plus recorded local changes. Source fingerprint: `41a988f64d4c7ed22590ef1ddc51c131b85d4675b82f5db6ed45f43a59fb470d`; archive SHA-256: `8b0cb2995a2c02a2cc83ebf35d0c30b628f17f701505ddd3ce2b4e077857e6fd`. F003's three foundation source files and separate test dispatch were present in this snapshot; they are explicitly excluded from F004 acceptance coverage.

System input is archived [generation f004-generation-20260907T100038Z](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-004/checks/f004-generation-20260907T100038Z/source-manifest.json), source fingerprint `7d0b1b09c9300ee8305a204531fff730ed148f488097ee50c2bc59fdef135eb1`. [Owner-output baseline](checks/candidate-release/system-owner-baseline.json) records its 15 production DLL hashes; the owner manifest SHA-256 is `90f061e4b6aab6136e26b48b700b9673e994cbf88cd8cf0178fb0201b8240ce8`. System subsequently began newer unbuilt F001 source work; that live source is not the tested F004 generation.

Accepted System Design revision 3 content SHA-256: `563711439a2e5f15810d72b622bfb82cd8c0cc1f5f7a241ec7235180a1d69432`; this supplements the corrected traceability locator above.

## Verification

| Check | Command or method | Result | Evidence |
|---|---|---|---|
| Existing dependency restore | Child-only solution, `dotnet restore --no-dependencies --packages C:/Users/justin/.nuget/packages --source <empty local feed> -p:NuGetAudit=false -p:BuildProjectReferences=false` | Pass; cached exact dependencies, no downloads/version changes/System project restore | [Restore log](checks/candidate-release/restore-03-existing-user-cache.log) |
| All 18 local projects | Ordered `dotnet build <project> -c Release --no-restore -p:BuildProjectReferences=false -p:ShouldUnsetParentConfigurationAndPlatform=false` | Pass; local source built against frozen System outputs | `checks/candidate-release/build-ordered-*.log` and final `build-repair2-*.log` |
| Seven ordinary suites | `dotnet tests/<suite>/bin/Release/net10.0/<suite>.dll` | Core, Bridge, Execution, Cutover, Persistence, Server.GraphWorkspace and Layout.Verification pass | [Exact commands and final exits](checks/candidate-release/final/ordinary-results.json) |
| Focused F004 regressions | Execution and host ordinary suite assertions | Pass: lossless independent/interleaved/mutation-safe handles; pure typed metadata; exact/+1/invalid and above-small-profile capacity; receiving compiler policy; schema-1 custom-kind/extension preservation; complete and unavailable history paths | [Execution output](checks/candidate-release/final/Ghostagram.Execution.Tests.stdout.log), [host output](checks/candidate-release/final/Ghostagram.Server.GraphWorkspace.Tests.stdout.log) |
| Existing Graph/Variable/Links test/export pairs | Exact six commands in runner result | All six pass | [Commands/exits](checks/candidate-release/conformance/ordinary-results.json) |
| Existing case behavior | Compare repaired-baseline case records and final real envelopes | Graph 47: 10 pass/37 unsupported; Variable 12: 8/4; Links 13: 3/10. Zero failed/missing; no case status loss | [Case comparison](checks/candidate-release/case-comparison.json), [Graph](checks/candidate-release/conformance/graph-result.json), [Variable](checks/candidate-release/conformance/variable-result.json), [Links](checks/candidate-release/conformance/links-result.json) |
| Output ownership | SHA-256 comparison after final checks | All 15 frozen System owner outputs unchanged; all 72 copied System DLLs match their respective recorded production or conformance-support owners | [Output verification](checks/candidate-release/system-output-verification.json) |
| Baseline preservation | SHA-256 against earlier recorded originals | All 34 original baseline files preserved | [Preservation check](checks/candidate-release/original-baseline-preservation.json) |
| Source/document checks | `git diff --check`, project/XML reference resolution and static artifact/link/ID checks | Pass; no YAML parser claim | [Verification summary](checks/candidate-release/verification-summary.json) |

Variable and Links case records are equal after JSON parsing. Three Graph observation fields differ only in newly generated per-run graph GUIDs; authority, local node IDs, revisions, seams and case outcomes remain unchanged. Raw differences are preserved, not suppressed. The legacy baseline unsupported observations remain unsupported; no new producer participation is claimed.

The separate F003 foundation branch and benchmark also pass. Foundation coverage proves only copied anchors, sidecar admission, identity encoding and freshness comparison. It does not prove Composition hierarchy projection, full closed result mapping, visible verification or consumer envelopes. Those remain F003 obligations.

## Acceptance Coverage

| Criterion | Implementer observation | Evidence |
|---|---|---|
| AC-007, Ghostagram assignment only | Pass: direct layered references, pure typed/local projection, explicit runtime materialization and transaction owner, preserved compilation context | Source archive, 18 local builds, ordinary suites and DLL verification |
| AC-009, Ghostagram assignment only | Pass: all prior passing observations retained; original Execution failure repaired; existing profile totals and all case statuses preserved | Final ordinary commands, actual envelopes, raw case comparison and preserved baselines |

## Design and Architecture Deviations

Accepted Design revision 1 assumed the earlier admitted snapshot/codec seam. System contract/Design revision 3 introduced the separate local family to preserve existing unprofiled behavior without inventing admission. Prototype run `PROTOTYPE-RUN-20260907T094049Z` authorized implementation; Accepted Design revision 2 and Accepted Solution/Bridge Target revision 3 now record exact local APIs, General capacity, receiver limits, codec-only bound applicability, historical schema shape and rollback ownership. [Implementation notes](checks/prototype-source-local/implementation-notes.md) retain the pre-execution reasoning. No retrospective acceptance or architecture promotion is claimed.

Initial candidate logs remain: stale-assets build, blocked default-cache/network restore, first Execution parity assertion and the history-ring failure. The former was corrected by passing lossless `Input` and separately testing semantic-only compilation. The latter exposed Bridge's handmade `IMutableNode` lacking System rollback; inheriting `GraphNode` fixed repeated mutation without weakening the history test. Owned failing harnesses hung after unhandled exceptions and were terminated by their recorded process handles; final passing checks have native zero exits and no forced termination.

## Residual Risk and Follow-up

- Independent elon-musk accepted Target3 at 2026-09-07T10:23:15Z and Design2 at 2026-09-07T10:27:51Z, both cycle 0 without conditions. Independent local `VALIDATION.md` is still required; no self-validation was authored.
- Prototype mode and its run remain parent-owned. Source work for F003 may proceed; F004 evidence binds to the archived source and frozen System generation above.
- No browser-based Composition verification occurred in F004; it belongs to F003. The existing Layout.Verification suite did run.
- PyYAML is unavailable. YAML headers and locators are checked structurally; no external YAML parser ran. No Git mutation, dependency version change, release or deployment occurred.
