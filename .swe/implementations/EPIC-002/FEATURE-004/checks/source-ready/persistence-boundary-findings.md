# F004 Ghostagram persistence boundary findings

Recorded 2026-09-07. Status: unresolved owner/reviewer disposition, not delivery Validation.

Scope: accepted [Ghostagram F004 Design](../../DESIGN.md), AC-007/AC-009. These findings were obtained through read-only comparison with System commit `b498a215db63f9dbd85a110c23203f1e55f8f87d` and the current F004 candidate. No profile amendment is accepted by this note.

## Verified baseline

The host called `GraphJsonSerializer.SerializeGraph(graph)` and `DeserializeGraph(json, StoreOptions())`. At the baseline System commit, `src/Ghostworx.System.Graph.Serialization/GraphJsonSerializer.cs:50` saved schema 1 without portable profile/authority/vocabulary assignment. Lines 74–78 included retained history when `ReadChangesSince(0)` succeeded and omitted it only for `GraphHistoryUnavailableException`. Lines 85–96 constructed the schema-1 document. `GraphMigrations.cs:68` kept legacy documents at schema 1 in the no-context path.

`GraphSerializationRegistry.cs:50` returned legacy `NodeKind.Define(namespace,name)` for unknown symbols without vocabulary fields. This preserved the exact kind used by the host's descriptor and port registries. `GraphSerializationDtos.cs:156` defaulted to 4 MiB input, 100,000 nodes, 250,000 relationships, 100,000 change batches, and 1,024 metadata entries/extensions. The existing Cutover harness used the separate explicitly profiled schema-2 API; the host did not.

## Current candidate implications

The new public codec always takes `GraphExchangeContext`; migration to schema 2 requires an explicit `LegacyAuthorityAssignment`. `GraphDocumentJson.ApplyVocabularyFacts` requires an admitted namespace for every symbol. `ConformanceSmall` accepts only the built-in Graph vocabulary and admits 256 nodes/1,024 relationships. It cannot admit the host's existing `Ghostagram.NodeType` and `Ghostagram.Bridge` namespaces.

Adding trusted vocabulary identities would emit schema-2 bytes with an actual profile ID/version and governed custom symbols. Current internal `GraphSerializationRegistry` resolves those custom symbols to governed `NodeKind`, whose record equality includes vocabulary identity. The host's existing `GraphWorkspaceNodeKinds.For` returns an ungoverned kind. Exact descriptor/port registry lookup can therefore change after reload. `GraphStore` materialization rejects a factory that changes the admitted kind, so a factory is not a compatibility workaround.

A trusted host profile is an available System mechanism, but its identity, admitted vocabularies, effective bounds, and governed-kind migration require an explicit owner/reviewer decision. Vocabulary must not be admitted merely because an input document names it. Source changes for this policy remain held.

## Corrections already scoped

Local host capture now requests complete history, retrying without history only for `HistoryUnavailable`. The host supplies its existing authority explicitly for legacy migration; external legacy graph identities without explicit assignments still fail. A fixed schema-1 regression fixture and available-history assertion have been added to the host harness source. They have not yet run because the parent retains the shared build slot.

The diagram compilation adapter's provisional `ConformanceSmall` admission also introduces a 256-node cap absent from its old graph-count behavior. This remains a separate unresolved finite-admission choice; small passing tests would not establish capacity parity.

## Independent context interpretation

The parent relayed independent `elon-musk` implementation guidance: preserve two-step diagram compilation through an explicit immutable Ghostagram-owned input handle, with private typed sidecar data alongside the portable snapshot. Direct and two-step diagram paths must retain fingerprints, properties, types, endpoints and inferred guards; snapshot-only compilation consumes semantic facts only. This guidance is implemented without hidden caches. It is not formal delivery Validation or an approval of the unresolved persistence policy above.
