# Ghostworx graph migration

Ghostagram is a projection and editing surface for `Ghostworx.System.Graph`. The system graph owns semantic node and relationship identity, kinds, names, metadata, graph version, and change history. Ghostagram owns diagram bounds, groups, waypoints, viewport, selection, and presentation revision. `Ghostagram.Bridge` is the only layer that depends on both models; `Ghostworx.System` must never reference Ghostagram.

## Adoption path

1. Store application semantics in a `GraphStore` and persist them with `Ghostworx.System.Graph.Serialization.GraphJsonSerializer`.
2. Keep `GraphPresentationSnapshot` as an independently persisted sidecar keyed by stable `NodeId` and `EdgeId` values. Names are display text, never diagram identity.
3. Produce an editor document with `GraphDiagramProjection.Project`. Apply ordered `GraphChangeBatch` values through `GraphDiagramDeltaProjector`; request a full projection whenever history is unavailable or the delta says recovery is required.
4. Send browser proposals through `GraphDiagramCommandAdapter` with both the expected graph version and presentation revision. On either conflict, discard optimistic state and use the returned authoritative document.
5. Compile execution from `GraphSnapshot` through `IGraphSnapshotCompiler`. Treat `DiagramDocument` as an editor projection, not the semantic source of truth.

Browser-created edges initially carry a client-local ID because `IGraphTransaction.Connect` does not choose an `EdgeId`. After an accepted proposal, read the `RelationshipConnected` change or the returned authoritative document and replace the temporary ID with `EdgeId.ToString()`. Apply waypoints only after that recovery. The bridge deliberately does not persist the temporary ID.

## Custom renderers and export

Register trusted browser body renderers with `registerNodeRenderer(key, version, lifecycle)`. Resolution is an exact key/version match; missing, incompatible, or failing renderers fall back to the standard body. Ghostagram continues to own outer geometry, title, accessibility, selection, ports, and handles. A renderer may provide lifecycle hooks (`mount`, `update`, `measure`, `dispose`) and a deterministic SVG hook, but persisted documents contain only `rendererKey` and a positive `rendererVersion`, never executable code or markup.

Server SVG export uses trusted `INodeSvgBodyRenderer` implementations registered in `NodeSvgRendererRegistry`. Its exact-version lookup and escaping-only `NodeSvgWriter` mirror the browser fallback boundary. Capability discovery reports the registrations available in each runtime; browser and server registration lists should be checked before relying on custom output.

## Compatibility and removal gates

`IGraphCompiler.Compile(DiagramDocument, ...)` is a temporary compatibility adapter. Remove it only after all persisted documents and execution callers use graph snapshots and tests prove semantic persistence, graph-to-diagram round-trip, browser conflict/recovery, layout/presentation, and execution-plan parity. The package-free `Ghostagram.Cutover.Tests` executable is the in-repository baseline for those gates.

QuickGraph and any wrappers built around it belong to a separate project and are outside this migration's scope. This repository makes no deprecation, compatibility, migration, or removal decision for that project and does not modify its checkout.

## Verification

```powershell
dotnet build .\Ghostagram.slnx -c Release
dotnet run --project .\tests\Ghostagram.Cutover.Tests\Ghostagram.Cutover.Tests.csproj -c Release
dotnet run --project .\benchmarks\Ghostagram.Bridge.Benchmarks\Ghostagram.Bridge.Benchmarks.csproj -c Release -- --repetitions 5
npm test
npm run benchmark
```

Visible browser verification remains a release gate for renderer lifecycle, interactive controls, selection/drag/ports, fallback rendering, and browser SVG output. Compare browser and server SVGs for stable geometry and escaping; unit and executable tests do not replace that visual check.

Current comparative measurements and reproduction commands are retained in `benchmarks/BASELINE.md`.
