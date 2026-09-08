# Observed F004 source decisions pending execution and review

Run: PROTOTYPE-RUN-20260907T094049Z. This note records the unbuilt source candidate for prototype backtracking; it does not amend or approve DESIGN-EPIC-002-FEATURE-004-GHOSTAGRAM revision 1.

## Owner and boundary

System Graph contract revision 3 and System F004 Design revision 3 separate GraphLocal structural candidates from fully admitted GraphSnapshot/GraphDocument records. Ghostagram Bridge now consumes GraphLocalSnapshot/GraphLocalChangeBatch returned by the actual runtime. Diagram compilation constructs a local snapshot; the governed compile overload explicitly uses GraphLocalInspection.FromSnapshot. No local-to-governed cast, wrapper or invented vocabulary/profile is used. Cutover tooling retains its existing explicit governed wire round trip.

The host uses IGraphLocalDocumentCodec EncodeLocal/DecodeLocal, GraphStoreDocumentMapper.CaptureLocal and GraphStoreMaterializer.MaterializeLocal with an explicit empty strong-retention target and node factory. The local schema stays 1. Complete history is included when available; only history-unavailable permits snapshot-only fallback. Custom node kinds, qualified tuples when already supplied and opaque extensions remain owner-controlled data. Historical null governance fields and empty arrays stay in the serialized shape.

## Local capacity and values

GraphCompilationLimits.General supplies GraphLocalLimits with 100,000 nodes and 250,000 relationships. Direct diagram, local snapshot, governed inspection and explicit handle compilation apply the receiving compiler's finite policy; count overflow returns GRAPH_ADMISSION_REJECTED without a partial result. All six local limit values must be positive and below int.MaxValue. Metadata entry counts apply to snapshots; MaximumInputBytes, MaximumChangeBatches and MaximumExtensions are codec/history fields with no corresponding byte stream/history/extension aggregate in the diagram compiler. Their numeric validity is checked but no serialization-byte ceiling is claimed for in-memory diagrams.

The System-owned historical GraphLegacyMetadataPolicy supplies metadata key 256 UTF-8 bytes, typed scalar 64 KiB, value 256 KiB, depth 16 and collection-item 1024 bounds. GraphValueAdmission validates typed values; the pure projection does not normalize arbitrary CLR objects. Unsupported fingerprint kinds still produce explicit diagnostics. Compiler context stores detached node/property JSON and immutable scalar port facts. Returned node presentation objects are cloned, and context reuse needs no compiler-local cache.

## Verification still required

Static project-reference resolution and git diff --check pass. No source-phase build/test/restore has run. Seven ordinary suites plus existing Graph, Variable and Links conformance test/export pairs must run against one coordinated System fingerprint after parent releases the build slot. New source tests cover default capacity above 256 nodes/1024 edges, exact/+1/invalid policies, receiving-handle limits, custom-kind edit/reload, extension retention and history fallback. Results are not yet known.

Reconstruct a truthful Draft successor Design from execution evidence, preserve Accepted revision 1 and accepted Target2 archives, and obtain independent named elon-musk review and independent local Validation. F003 source/evidence remains separately attributable.
