---
title: "Layered System Definition Foundation — Ghostagram Corrective Evidence"
artifact_type: "implementation_evidence"
id: "EVIDENCE-EPIC-002-FEATURE-004-GHOSTAGRAM-CORRECTION-1"
status: "InProgress"
authority: "solution"
scope: "ghostagram corrective compatibility with System cycle 3"
parent: "DESIGN-EPIC-002-FEATURE-004-GHOSTAGRAM"
upstream:
  repository: "ghostagram"
  artifact_id: "EVIDENCE-EPIC-002-FEATURE-004-GHOSTAGRAM"
  path: ".swe/implementations/EPIC-002/FEATURE-004/EVIDENCE.md"
  revision: "1"
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
  system_validation:
    repository: "ghostworx-system"
    artifact_id: "VALIDATION-EPIC-002-FEATURE-004-GHOSTWORX-SYSTEM"
    path: ".swe/implementations/EPIC-002/FEATURE-004/VALIDATION.md"
    revision: "4"
owners:
  - "Ghostagram child implementation coordinator"
created: "2026-09-07"
updated: "2026-09-07"
revision: "1"
template_version: "2.0.0"
---

# Corrective Implementation Evidence

## Current Status

This corrective record is **InProgress**. It preserves the original [Complete implementation Evidence](EVIDENCE.md) and independent [Accepted local Validation with its confirmed-upstream-defect addendum](VALIDATION.md) byte-for-byte. Those artifacts truthfully describe the earlier frozen generation and are not relabeled as evidence for the corrected System generation.

The System owner has independently Accepted the human-authorized F004 repair cycle 3. Archive-only comparison establishes exact production-source correspondence between that accepted isolated candidate and the later `provisional-20260907T120942Z` System generation used by Ghostagram's provisional F003 checks. The existing Ghostagram checks do not execute the F004 assignment broadly enough to close corrective AC-007 or AC-009, so this record remains open pending a bounded same-generation rerun and independent corrective Validation.

## Preserved Decision History

| Artifact | Disposition | SHA256 |
|---|---|---|
| [Original Ghostagram Evidence](EVIDENCE.md) | Complete for the earlier `f004-generation-20260907T100038Z` only | `c437565afefaa108f72db830d5dea0d20fef9fa2f3303d9800d49b68cf6dbccc` |
| [Original Ghostagram Validation](VALIDATION.md) | Accepted local AC-007/AC-009 for that earlier generation; post-decision addendum preserves the confirmed upstream defect and requires corrected-generation checks | `79df10793ce7231ed1c29ffcdd764522a5c80ed54a7813fd414a4cedb7e83c7c` |
| [System cycle-3 correction Evidence](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-004/EVIDENCE.correction-3.md) | Complete | `d05a94be7cbd20b5c3c6c4c302b36bc09ea023c3a94d0c60c7a3932f0d86e4e0` |
| [System Validation](../../../../../ghostworx-system/.swe/implementations/EPIC-002/FEATURE-004/VALIDATION.md) | Cycle 3 Accepted at `2026-09-07T12:22:59Z` by independent `elon-musk` | `8ffbfe44fc3369115fc65aa7d9e74e0c785c2f77e34267530513f776ad7e1754` |

No prior rejection, review cycle or defect record is superseded by this successor Evidence.

## Corrected System Correspondence

[The machine-readable comparison](checks/corrective-120942/source-correspondence.json) binds:

- accepted cycle-3 source fingerprint `63d4bfb69171c971e53518ab87def8aad6bf0d111606e7aed07cd0c0c47dd06f` and owner-manifest SHA256 `db826d492ab695bac7017704f52267cb47c376ddc0abc6fd030e8c7b21768481`;
- provisional combined source fingerprint `5a70b636b93f11b5a85eea82ffccf1900e3292fc26ce260f690634b159e657da` and owner-manifest SHA256 `d716b81b89c9cc5152e4a3b577ee5381056f52f3b88981b8ceacc236b92bb273`;
- all 163 F004-relevant production entries as byte-identical; and
- 33 of 34 selected F004 test entries as byte-identical.

The sole selected test-source difference is `OpaqueMetadataEditTests.cs`. The `120942Z` archive predates the final cycle-3 test-only correction that asserts the actual `BatchCommitted` marker and models runtime semantic null as absent history `NewValue`. The governing production repair in `GraphDocumentJson.cs` and the test dispatch in `Program.cs` are byte-identical. The accepted cycle-3 Validation preserves both first-attempt failures and final passing logs and explicitly classifies this final change as test-only.

The two owner manifests are deliberately not treated as byte-equivalent binary generations: their DLL hashes differ. The later manifest is independently anchored and was the actual input to the Ghostagram provisional checks. Source correspondence supports compatibility reasoning; it does not substitute for consumer execution.

## Actual `120942Z` Ghostagram Observations

The F003 phase `provisional-120942-local-projections-02` built and executed against the exact later owner outputs. Its generation receipt SHA256 is `68dfb484116cd06f8a9f2fd63a9d65a4e8e9cdedb37f6742011c0dc98943ad9c`; command record SHA256 is `5d91a82600b40ab38130e15ff96109b0555ca381b41ad8b960bb4f6e507fdf49`; copy verification SHA256 is `3ee2bbb9c50c05004ef6878db805937c68448367526ffc2c8561e830f50bad67`. It reports 26 exact owner rows, 16 exact copied System DLLs and `174/174` local composition projection cases passing.

That result provides a real same-generation Bridge and consumer-harness compatibility observation. It is F003 composition projection evidence. It is not evidence that the F004 execution, persistence, cutover, Graph, Variable or Semantic Link boundaries passed against this generation.

## Actual `132448Z` Corrective Run

[Corrective phase `corrective-132448-build-test-01`](checks/corrective-132448-build-test-01/) executed against System `provisional-20260907T132448Z`, the built/tested 12-operation owner generation with System source fingerprint `9fcf09c46233cf03bfc60f081c000d2615fef5a66f47391ec4f9901a45f4bb4a` and 27-output owner-manifest SHA256 `86837e727c9decfcf914b29710fbeaa341f643ed81ea223faac1f1d34a84c370`.

[The exact accepted-cycle-3 comparison](checks/corrective-120942/source-correspondence-132448.json), SHA256 `5c4d37f9945f2d13eb31283fe64ce6c8132795639279b9b5fcb580e473ebf969`, finds all 163 selected F004 production files and all 34 selected F004 test files byte-identical. Unlike the earlier `120942Z` observation, this generation contains the final accepted cycle-3 test correction as well as the same production repair.

| Check | Result | Evidence |
|---|---|---|
| Scoped child Release build | Pass: Contracts, Core, Execution, Bridge, Server, Execution.Tests, Graph.Conformance, Graph.Conformance.Tests and Server.GraphWorkspace.Tests; all used `--no-restore` and `BuildProjectReferences=false` | [Commands](checks/corrective-132448-build-test-01/commands.json) SHA256 `aa2b278099bf38917baf83b58914ad6a8b4088d46b0cf2c9d089122e2b8074f6` |
| Affected behavior suites | Pass: `Ghostagram.Execution.Tests` and `Ghostagram.Server.GraphWorkspace.Tests`, native zero exits | Same command record and raw stdout/stderr logs |
| Existing Graph/Variable/Link profiles | Pass: each test and persistent envelope command, native zero exits | `graph-result.json`, `variable-result.json`, `links-result.json` and raw logs in the corrective phase |
| Prior accepted case behavior | Pass after normalizing only the same three documented run-generated Graph IDs: Graph 47 with 10 pass/37 unsupported; Variable 12 with 8/4; Links 13 with 3/10; no missing, added or changed cases | [Case comparison](checks/corrective-132448-build-test-01/case-comparison.json) SHA256 `555699a855def336fb5d1b23ada818faa8012df8d7a46504a37f23da40864885` |
| Owner/copy identity | Pass: exact 27 owners before and after; per-command staged copies and pre/post copy hashes match the issued owner manifest; no forced termination | `owners-before.json` and `owners-after.json`, identical SHA256 `d992cacc41b4410fe9efaad3ba40aff1bf1a10daaede5c9e474e11066ef1b039`, plus `staged-copies-*` and `copies-*-after.json` |
| Child source identity | Pass during the phase: fingerprint `586af1e2eb633a883e27693f1e5b735d1e120abc7e2ce053a8a601bc83986135`; archive SHA256 `72f787f428904540aa8d7bb5df99841d91ad32f325e714d0278739737794d9a9`; post-run source rows equal the captured manifest | [Generation](checks/corrective-132448-build-test-01/generation.json) SHA256 `794df3e539e1e5f632572dc74baddfdf48cb03880a8671425407aa9c4fc637ad` |

This run closes corrected-owner compatibility for the existing execution, workspace and conformance behavior. The execution suite's concrete F004 locators are `tests/Ghostagram.Execution.Tests/Program.cs:122-194`: distinct local snapshot input, direct materialization-compatible compilation, retained typed metadata, unsupported metadata rejection and relationship metadata fingerprinting. The workspace suite's executed locators are `tests/Ghostagram.Server.GraphWorkspace.Tests/Program.cs:91-178`: schema-1 capture, edit/save, restart, extension retention, complete history and unavailable-history fallback.

## One Remaining Focused Consumer Check

Inspection after the passing run found that those workspace assertions did not replace an imported unknown opaque metadata value at the same key. [A focused assertion](../../../../tests/Ghostagram.Server.GraphWorkspace.Tests/Program.cs) now imports `future.codec@1.0.0`, replaces `future` with an Object through the real diagram command adapter, and checks capture/persisted old/new history plus reload. Its current source SHA256 is `78fb64efae60f6373b47307c26b45080723c372bdc4754da5b1e0c8a2f87562a`; the executed phase contained predecessor SHA256 `27ca4786c3118554e7229c36efaeedd3270aa388ef6c91f9f59cb7595de650dc`.

That focused test source is implemented but intentionally **not claimed as executed**. The parent requested wrap-up before another slot. The only remaining F004 execution is a scoped build of `Ghostagram.Server.GraphWorkspace.Tests` and its native suite against one exact issued System generation, with the same pre/post owner/copy/source receipts. No broader campaign is required. Corrective Evidence remains InProgress until that check passes.

### Final bounded attempt

[Phase `corrective-134751-opaque-workspace-01`](checks/corrective-134751-opaque-workspace-01/) used System `provisional-20260907T134751Z`, owner-manifest SHA256 `5305b582b82e42e60a24a3a09c12dae0bfbff10a8af9c28ac6199ce96b60c74a`. The one scoped `Ghostagram.Server.GraphWorkspace.Tests` build passed. The one native suite then exited nonzero before reaching the new opaque assertion: adding the second visible descriptor property made the existing 100px test node violate `NodeTypeDescriptor`'s 107px minimum height. The exact exception is preserved in `Ghostagram.Server.GraphWorkspace.Tests.stderr.log`, SHA256 `1a8608cb2115bd7caaf72dbc1cae7bc390817ea1d253f92b335c08bb01c94add`.

The command receipt SHA256 is `8ab0e8c6d3a7498f1c7d12566e1b6a4abef11052443fece7b8ebe852520d676c`. Child source fingerprint is `f1452d23053a8e7b725a3e1ccd6303083b58d2bbc23d530ca3128fbb8f9fbfcf`; source archive SHA256 is `4b9e944c954fc2aabdac2bbc0d26f4484764339872a22589808ab7c36fdb2bb4`. Exact 27-owner before/after receipts are identical at SHA256 `c06870847fbe23b580b0fe86cb2047db89fdbaab6379a0771f11abe51df35b85`. There was no timeout, forced termination, System build or restore.

Per the bounded wrap-up instruction, no repair or second execution was attempted. The focused opaque path therefore remains unobserved, corrective Evidence remains InProgress, and independent corrective Validation is not yet eligible. The concrete next repair is limited to making the descriptor/test node height satisfy its existing minimum, followed by the same one-project build and one-suite execution under a newly granted exact-generation slot.

## Required Corrective Checks

The `132448Z` phase completed the broader corrective run. The remaining completion gate is limited to:

1. build `Ghostagram.Server.GraphWorkspace.Tests` with referenced System projects disabled against the issued generation;
2. execute that one native suite and retain its opaque replacement/capture/persist/reload/history assertions; and
3. capture exact System owner/copy and Ghostagram source identity before and after.

The existing `174/174` F003 local result does not substitute for this focused F004 path. Final corrective Evidence must name the exact System generation used for the final assertion and relate any source-only delta to the completed `132448Z` phase without mixing owner generations inside an execution phase.

## Validation Gate

After the bounded rerun passes, this artifact may become Complete and must be sent to the existing independent user-named `elon-musk` validator for a corrective AC-007/AC-009 decision. The original Accepted Validation and its addendum remain unchanged. Portfolio integration and architecture promotion remain separate decisions.

No build, restore, binary execution, dependency-version change, Git mutation, deployment, release or external operation was performed while creating this source-only corrective record.
