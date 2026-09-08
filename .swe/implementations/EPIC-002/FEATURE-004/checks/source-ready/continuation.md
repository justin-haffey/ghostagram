# Ghostagram F004 continuation

This is a working checkpoint, not Complete Evidence or formal Validation.

- Child thread: `01a0795c-0f54-7df0-965e-9ebd3a11fda6`; parent: `01a0794b-7e3e-7800-8fca-82afa62d0e28`.
- Scope: Ghostagram EPIC-002/FEATURE-004 only, AC-007 and AC-009. Source changes stay in this repository. System and portfolio are read-only. No Goal operations, Git mutations, dependency changes, deployment or external operations.
- Shared build slot is currently held provisionally by SDK (latest parent handoff). Do not build, restore or run harnesses until the parent explicitly releases it and supplies the candidate fingerprint.
- Baseline checkout: `main`, HEAD `d3f2542c28acfed7ecf04f9c37a4e47241138f29`. Preserve all existing local edits and both diagnostic baseline directories. No source-phase build/test/restore has run.
- Original baseline: six ordinary harnesses passed; Execution failed on `DiagramNode` in semantic metadata. Graph corpus failed an exact-byte pin. The repaired Release baseline corrected only System corpus/schema line endings and build configuration: Graph 47 cases (10 passed/37 unsupported), Variable 12 (8/4), Links 13 (3/10); zero failed/missing. All original logs remain intact.

## Implemented source, awaiting build

Typed Graph semantic metadata and runtime-owner reference migration; private compiler sidecar with explicit public `GraphCompilationInput` handle; direct and two-step diagram paths preserve fingerprint/property/type/endpoint/guard behavior. Snapshot-only compilation remains semantic-only. No cache or CLR presentation object is placed in System metadata.

The handle freezes nested node presentation collections and extension dictionaries. Tests cover independent compiler instances, equivalent clones, interleaved inputs, source mutation, output mutation, duplicates, semantic metadata sensitivity, and retained existing parity assertions.

Bridge typed JSON projection preserves its previous generic structured-value omission. Command normalization occurs only in the outward runtime adapter. Conformance harness namespaces/project references and runtime Variable invalidation codec calls are updated. The benchmark uses typed snapshots/change batches and an explicit finite benchmark admission profile. README explains the explicit compilation handle and coordinated Release build flag.

Host persistence/cutover currently have provisional pure-codec/capture/materialization adaptations. **The host path is not complete:** current profiled context would change schema/custom-kind identity/bounds. Preserve source while awaiting the reviewed replacement seam. Regression source now requires schema-1 saves, custom-kind descriptor reload/edit, node/document extensions, complete history and history-unavailable-only fallback.

## Pending owning decisions

See [persistence findings](persistence-boundary-findings.md). Parent selected preservation, not an intentional persisted migration. The parent reports owner contract revision 3 Accepted by `elon-musk` at cycle 0. System is reconciling affected Targets and repairing Design revision 3 at cycle 1: outward local candidates remain separate from governed admitted snapshots/history, with full destination admission at every entry point. The exact System seam is not yet Accepted; do not infer its acceptance from the contract decision. After the System seam is Accepted, author a bounded Ghostagram Design revision 2, preserve revision-1 approval history, and obtain independent `elon-musk` acceptance before affected implementation.

Capacity proposal is unaccepted: optional trusted `GraphCompatibilityProfile` input to `GraphCompiler`, named General default 100,000 nodes/250,000 relationships, prior metadata limits. Explicitly disclose the new finite in-memory ceiling; persistence separately retains its original limits. Current provisional compiler `ConformanceSmall` cap and its 257-node rejection test must be reconciled after acceptance. Required revised tests: exact/+1/invalid smaller caller profile, real default >256 nodes/>1,024 edges, exact default-value assertions, and any default-only branch.

## Remaining sequence

1. Receive the parent’s accepted upstream seam/contract and authorize the local Design revision; do not reopen completed unrelated reviews or baselines.
2. Obtain local Design revision acceptance, then implement the exact persistence and capacity interfaces and reconcile tests/docs.
3. Receive shared build slot/final System candidate fingerprint. Build Release with `-p:ShouldUnsetParentConfigurationAndPlatform=false`; restore only as authorized for changed project references.
4. Run the seven ordinary harnesses and six Graph/Variable/Link conformance commands into a new F004 check directory; preserve original envelopes. Compare complete case outcomes and record source/output fingerprints and Release-owner matches.
5. Write schema-valid Complete `EVIDENCE.md` only after required checks pass. Independent local `swe-validate`/named approver produces `VALIDATION.md`; implementer does not self-validate. Return exact locators to parent; do not mark parent Goal complete.

The read-only mapper `/root/ghostagram_runtime_mapping` is available for bounded follow-up; it has changed no files or run builds.
