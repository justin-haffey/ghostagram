using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Bridge;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostworx.System.Graph;

namespace Ghostagram.Graph.Conformance;

public static class GhostagramBridgeHarness
{
    public static CaseObservation ObserveIdentity(JsonElement fixture)
    {
        var state = Create(fixture);
        var address = SemanticAddress.ForNode(state.Authority, state.Graph.Id, state.Node.Id, state.Graph.Version);
        state.Node.Set("authority", address.Authority.Value);
        state.Node.Set("semanticAddress", address.CanonicalText);
        var document = state.Projection.Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture());
        var node = document.Nodes.Single(item => item.Id == state.Node.Id.ToString());
        var projectedAddress = SemanticAddress.Parse(Property(node, "semanticAddress").GetString()!);
        Require(projectedAddress.LocalId == state.Node.Id.Value && document.DocumentId == state.Graph.Id.ToString(),
            "Projection changed canonical graph or node identity.");
        return Observation("IGraphDiagramProjection.Project", "success", "identity", [],
            ("authority", projectedAddress.Authority.Value),
            ("graphId", document.DocumentId),
            ("elementKind", projectedAddress.ElementKind.ToString().ToLowerInvariant()),
            ("localId", node.Id),
            ("revision", document.ExtensionData!["graphVersion"].GetInt64().ToString()));
    }

    public static CaseObservation ObserveDeterministicReplay(JsonElement fixture)
    {
        var state = Create(fixture);
        var snapshot = state.Graph.CaptureSnapshot();
        var presentation = state.Presentation.Capture();
        var first = state.Projection.Project(snapshot, presentation);
        var second = new GraphDiagramProjection().Project(snapshot, presentation);
        Require(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(first), JsonSerializer.SerializeToElement(second)),
            "Equivalent projection inputs produced different documents.");
        return Observation("IGraphDiagramProjection.Project", "success", "mutation", [],
            ("revision", first.ExtensionData!["graphVersion"].GetInt64().ToString()));
    }

    public static CaseObservation ObserveExpectedRevisionConflict(JsonElement fixture)
    {
        var state = CreateDefault();
        var expected = fixture.GetProperty("conflictExpectedRevision").GetInt64();
        var current = fixture.GetProperty("currentRevision").GetInt64();
        while (state.Graph.Version < current) state.Node.Rename($"Revision {state.Graph.Version + 1}");
        Require(state.Graph.Version == current && expected == current - 1,
            "The revision-conflict fixture does not describe the constructed Bridge state.");
        var adapter = new GraphDiagramCommandAdapter(state.Graph, state.Presentation, state.Projection);
        var graphBefore = state.Graph.Version;
        var presentationBefore = state.Presentation.Revision;
        var graphConflict = adapter.Apply(new(expected, presentationBefore, ImmutableArray<GhostagramOperation>.Empty));
        var diagramConflict = adapter.Apply(new(graphBefore, presentationBefore - 1, ImmutableArray<GhostagramOperation>.Empty));
        Require(!graphConflict.Accepted && graphConflict.Code == "GRAPH_VERSION_CONFLICT"
            && !diagramConflict.Accepted && diagramConflict.Code == "DIAGRAM_REVISION_CONFLICT"
            && state.Graph.Version == graphBefore && state.Presentation.Revision == presentationBefore,
            "A stale Bridge proposal was accepted or changed authoritative state.");
        return Observation("IGraphDiagramCommandAdapter.Apply", "revision-conflict", "conflict", ["revision-conflict"],
            ("expectedRevision", expected.ToString()),
            ("actualRevision", graphConflict.GraphVersion.ToString()),
            ("graphCode", graphConflict.Code),
            ("presentationCode", diagramConflict.Code));
    }

    public static CaseObservation ObserveFederationOrigin(JsonElement fixture)
    {
        var state = Create(fixture);
        var address = SemanticAddress.ForNode(state.Authority, state.Graph.Id, state.Node.Id);
        state.Node.Set("origin", address.CanonicalText);
        state.Node.Set("address", address.CanonicalText);
        var node = ProjectNode(state);
        var origin = SemanticAddress.Parse(Property(node, "origin").GetString()!);
        Require(origin.Authority == state.Authority && origin.LocalId == state.Node.Id.Value,
            "Projection did not preserve the System origin address.");
        return Observation("IGraphDiagramProjection.Project", "success", "federation", [],
            ("authority", origin.Authority.Value), ("address", origin.CanonicalText));
    }

    public static CaseObservation ObservePinnedRevision(JsonElement fixture)
    {
        var state = CreateDefault();
        var pinned = fixture.GetProperty("pinnedRevision").GetInt64();
        state.Node.Set("revision", pinned);
        var node = ProjectNode(state);
        Require(Property(node, "revision").GetInt64() == pinned, "Projection changed a pinned revision.");
        return Observation("IGraphDiagramProjection.Project", "success", "federation", [],
            ("revision", pinned.ToString()));
    }

    public static CaseObservation ObserveMirrorProjection(JsonElement fixture)
    {
        var state = Create(fixture);
        var address = SemanticAddress.ForNode(state.Authority, state.Graph.Id, state.Node.Id);
        state.Node.Set("origin", address.CanonicalText);
        var document = state.Projection.Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture());
        var node = document.Nodes.Single(item => item.Id == state.Node.Id.ToString());
        var origin = SemanticAddress.Parse(Property(node, "origin").GetString()!);
        Require(origin.LocalId.ToString("N") == node.Id, "Projection minted a second canonical identity.");
        return Observation("IGraphDiagramProjection.Project", "success", "federation", [],
            ("origin", origin.CanonicalText), ("projection", $"{document.DocumentId}:{node.Id}"));
    }

    public static BridgeVerification VerifyBridgeContracts()
    {
        var state = CreateDefault();
        var initialSnapshot = state.Graph.CaptureSnapshot();
        var initialPresentation = state.Presentation.Capture();
        var initial = state.Projection.Project(initialSnapshot, initialPresentation);
        var baseVersion = state.Graph.Version;
        state.Node.Rename("Renamed");
        var authoritative = state.Graph.CaptureSnapshot();
        var batch = state.Graph.ReadChangesSince(baseVersion).Single();
        var delta = new GraphDiagramDeltaProjector(state.Projection)
            .Project(batch, initial, authoritative, state.Presentation.Capture());
        Require(!delta.RequiresFullProjection && delta.BaseGraphVersion == baseVersion
            && delta.GraphVersion == authoritative.Version && delta.Operations.Any(),
            "A contiguous Bridge delta was not projected incrementally.");

        var mismatched = new GraphSnapshot(authoritative.GraphId, authoritative.Version + 1,
            authoritative.Nodes, authoritative.Relationships);
        var fallback = new GraphDiagramDeltaProjector(state.Projection)
            .Project(batch, initial, mismatched, state.Presentation.Capture());
        Require(fallback.RequiresFullProjection, "A skewed Bridge delta did not request full reprojection.");

        var graphBeforePresentation = state.Graph.CaptureSnapshot();
        var presentationCommit = state.Presentation.Execute(state.Presentation.Revision, editor =>
        {
            editor.SetNode(state.Node.Id, new(new(10, 20, 160, 80)));
            editor.SetViewport(new(5, 7, 1.25));
            editor.SetSelection([state.Node.Id.ToString()]);
            return true;
        });
        Require(presentationCommit.Accepted, "Presentation update was not accepted.");
        var full = state.Projection.Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture());
        var graphAfterPresentation = state.Graph.CaptureSnapshot();
        var semanticKeys = graphAfterPresentation.Nodes.SelectMany(node => node.Metadata.Keys).ToHashSet(StringComparer.Ordinal);
        Require(graphAfterPresentation.Version == graphBeforePresentation.Version
            && !semanticKeys.Contains("viewport") && !semanticKeys.Contains("selection")
            && full.ExtensionData!["presentationRevision"].GetInt64() == state.Presentation.Revision,
            "Presentation state entered semantic state or lost its independent revision.");

        var adapter = new GraphDiagramCommandAdapter(state.Graph, state.Presentation, state.Projection);
        var graphBeforeConflict = state.Graph.Version;
        var presentationBeforeConflict = state.Presentation.Revision;
        var conflict = adapter.Apply(new(graphBeforeConflict - 1, presentationBeforeConflict,
            ImmutableArray<GhostagramOperation>.Empty));
        Require(!conflict.Accepted && conflict.Code == "GRAPH_VERSION_CONFLICT"
            && state.Graph.Version == graphBeforeConflict && state.Presentation.Revision == presentationBeforeConflict,
            "Expected-revision conflict changed authoritative state.");

        return new(true, true, true, true);
    }

    private static FixtureState Create(JsonElement fixture)
    {
        var authority = new SemanticAuthority(fixture.GetProperty("authority").GetString()!);
        var nodeId = new NodeId(fixture.GetProperty("nodeId").GetGuid());
        var graph = new GraphStore("ghostagram-conformance", null, new GraphStoreOptions
        {
            Authority = authority,
            NodeRetention = GraphNodeRetentionMode.Strong
        });
        return new(graph, new FixtureNode(graph, nodeId), new GraphPresentationStore(), new GraphDiagramProjection(), authority);
    }

    private static FixtureState CreateDefault()
    {
        var graph = new GraphStore("ghostagram-conformance", null, new GraphStoreOptions
        {
            Authority = new SemanticAuthority("ghostworx.system"),
            NodeRetention = GraphNodeRetentionMode.Strong
        });
        return new(graph, new FixtureNode(graph, NodeId.New()), new GraphPresentationStore(), new GraphDiagramProjection(), graph.Options.Authority);
    }

    private static DiagramNode ProjectNode(FixtureState state) => state.Projection
        .Project(state.Graph.CaptureSnapshot(), state.Presentation.Capture())
        .Nodes.Single(item => item.Id == state.Node.Id.ToString());

    private static JsonElement Property(DiagramNode node, string id) =>
        node.Properties.Single(property => property.Id == id).Value
        ?? throw new InvalidDataException($"Projected property '{id}' had no value.");

    private static CaseObservation Observation(
        string seam,
        string outcome,
        string category,
        IReadOnlyList<string> codes,
        params (string Key, string? Value)[] fields) => new(
            seam,
            outcome,
            category,
            codes,
            fields.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            "Observed from returned Ghostagram Bridge state.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record FixtureState(
        GraphStore Graph,
        FixtureNode Node,
        GraphPresentationStore Presentation,
        GraphDiagramProjection Projection,
        SemanticAuthority Authority);

    private sealed class FixtureNode(GraphStore graph, NodeId id)
        : GraphNode(NodeKind.Define("Ghostworx.System.Graph", "Definition"), graph, "Fixture", id)
    {
        public void Set(string key, object? value) => SetMetadata(key, value);
        public void Rename(string value) => SetNodeName(value);
    }
}

public sealed record BridgeVerification(
    bool DeltaProjection,
    bool FullReprojectionFallback,
    bool PresentationExclusion,
    bool ExpectedRevisionConflict);
