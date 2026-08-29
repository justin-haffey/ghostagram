using System.Collections.Immutable;
using System.Text.Json;
using Ghostagram.Bridge;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostworx.System.Graph;
using Ghostworx.System.Graph.Serialization;

var hierarchyKind = NodeKind.Collection;
var processKind = NodeKind.Other;
var modelKind = NodeKind.Metadata;
var graph = new GraphStore("cutover", features: null, options: new GraphStoreOptions { NodeRetention = GraphNodeRetentionMode.Strong });
var hierarchy = new TestNode(hierarchyKind, graph, "Order workflow");
var intake = new TestNode(processKind, graph, "Intake");
var approve = new TestNode(processKind, graph, "Approve");
var order = new TestNode(modelKind, graph, "Order");
var audit = new TestNode(modelKind, graph, "Audit record");
intake.Set("lane", "operations");
order.Set("schemaVersion", 3L);
graph.Connect(hierarchy, intake, RelationshipKind.Contains);
graph.Connect(hierarchy, approve, RelationshipKind.Contains);
graph.Connect(intake, approve, RelationshipKind.DependsOn, "then");
graph.Connect(order, intake, RelationshipKind.Describes, "input");

var processDescriptor = new NodeTypeDescriptor("cutover.process", 1, "Process", "Cutover", "mdi:workflow", 220, 120,
    properties: [new("lane", "Lane", DiagramPropertyTypes.String)], rendererKey: "cutover.process", rendererVersion: 1);
var modelDescriptor = new NodeTypeDescriptor("cutover.model", 1, "Model", "Cutover", "mdi:database", 200, 100,
    properties: [new("schemaVersion", "Schema version", DiagramPropertyTypes.Integer)]);
var descriptors = new NodeKindDescriptorRegistry([
    KeyValuePair.Create(processKind, processDescriptor),
    KeyValuePair.Create(modelKind, modelDescriptor)
]);
var presentation = new GraphPresentationStore();
var initialPresentation = presentation.Execute(0, editor =>
{
    editor.SetNode(hierarchy.Id, new(new(40, 40, 760, 360)));
    editor.SetNode(intake.Id, new(new(120, 130, 220, 120)));
    editor.SetNode(approve.Id, new(new(440, 130, 220, 120)));
    editor.SetNode(order.Id, new(new(120, 500, 200, 100)));
    editor.SetNode(audit.Id, new(new(500, 500, 200, 100)));
    editor.SetGroup(hierarchy.Id, new(new(64, 72, 680, 260)));
    editor.SetViewport(new(18, 24, 0.9));
    editor.SetSelection([GraphDiagramIds.Node(intake.Id)]);
    return true;
});
Assert(initialPresentation.Accepted, "Initial presentation commit failed.");

var projection = new GraphDiagramProjection(new DefaultNodePresentationMapper(descriptors));
var initial = projection.Project(graph.CaptureSnapshot(), presentation.Capture());
Assert(initial.Nodes.Any(node => node.Id == hierarchy.Id.ToString()) &&
       initial.Nodes.Any(node => node.TypeId == "cutover.process") &&
       initial.Nodes.Any(node => node.TypeId == "cutover.model"),
    "Hierarchy, process, and model nodes were not projected.");
Assert(initial.Groups.Single().Id == GraphDiagramIds.Group(hierarchy.Id), "Contains hierarchy was not projected as a stable group.");

var adapter = new GraphDiagramCommandAdapter(graph, presentation, projection, descriptors);
var intakeProposal = initial.Nodes.Single(node => node.Id == intake.Id.ToString()) with
{
    Label = "Validate intake",
    X = 170,
    Y = 155,
    Properties = [new("lane", "Lane", DiagramPropertyTypes.String, JsonSerializer.SerializeToElement("quality"))]
};
var edit = adapter.Apply(new(graph.Version, presentation.Revision,
    ImmutableArray.Create(DiagramOperations.Upsert(intakeProposal), DiagramOperations.SetViewport(new(30, 35, 1.1)), DiagramOperations.Select([intake.Id.ToString(), approve.Id.ToString()]))));
Assert(edit.Accepted && edit.GraphChanges is not null, "Combined semantic and presentation proposal was rejected.");
Assert(graph.CaptureSnapshot().Nodes.Single(node => node.Id == intake.Id).NodeName == "Validate intake" &&
       presentation.Capture().Nodes[intake.Id].Bounds.X == 170 && presentation.Capture().Viewport.Zoom == 1.1,
    "Semantic and presentation ownership were not committed to their respective stores.");

var graphConflict = adapter.Apply(new(edit.GraphVersion - 1, edit.DiagramRevision, ImmutableArray.Create(DiagramOperations.Select([]))));
Assert(!graphConflict.Accepted && graphConflict.Code == "GRAPH_VERSION_CONFLICT" && graphConflict.AuthoritativeDocument.Nodes.Any(node => node.Label == "Validate intake"),
    "Stale graph proposal did not return authoritative recovery state.");
var diagramConflict = adapter.Apply(new(graph.Version, edit.DiagramRevision - 1, ImmutableArray.Create(DiagramOperations.Select([]))));
Assert(!diagramConflict.Accepted && diagramConflict.Code == "DIAGRAM_REVISION_CONFLICT" && diagramConflict.DiagramRevision == presentation.Revision,
    "Stale presentation proposal did not return the current revision.");

var temporaryEdgeId = "browser-edge-temp";
var edgeProposal = new DiagramEdge(
    temporaryEdgeId,
    GraphDiagramIds.OutputPort(approve.Id),
    GraphDiagramIds.InputPort(audit.Id),
    "records",
    Type: RelationshipKind.DependsOn.QualifiedName);
var edgeEdit = adapter.Apply(new(graph.Version, presentation.Revision, ImmutableArray.Create(DiagramOperations.Upsert(edgeProposal))));
Assert(edgeEdit.Accepted && edgeEdit.GraphChanges is not null, "Browser-created semantic edge was rejected.");
var edgeChanges = edgeEdit.GraphChanges ?? throw new InvalidOperationException("Accepted edge proposal did not return graph changes.");
var connected = edgeChanges.Changes.Single(change => change.Kind == GraphChangeKind.RelationshipConnected).Relationship
    ?? throw new InvalidOperationException("Accepted edge proposal did not expose its authoritative relationship.");
var authoritativeEdgeId = connected.Id.ToString();
Assert(authoritativeEdgeId != temporaryEdgeId && edgeEdit.AuthoritativeDocument.Edges.Any(edge => edge.Id == authoritativeEdgeId) &&
       edgeEdit.AuthoritativeDocument.Edges.All(edge => edge.Id != temporaryEdgeId),
    "Authoritative recovery did not replace the browser temporary edge id.");

var authoritativeEdge = edgeEdit.AuthoritativeDocument.Edges.Single(edge => edge.Id == authoritativeEdgeId) with
{
    Waypoints = [new(510, 310), new(560, 430)]
};
var waypointEdit = adapter.Apply(new(graph.Version, presentation.Revision, ImmutableArray.Create(DiagramOperations.Upsert(authoritativeEdge))));
Assert(waypointEdit.Accepted && presentation.Capture().EdgeWaypoints[connected.Id].Count == 2,
    "Presentation for the recovered authoritative edge did not commit.");

var beforePersistence = graph.CaptureSnapshot();
var beforePresentation = presentation.Capture();
var serializer = new GraphJsonSerializer();
var presentationSerializer = new GraphPresentationJsonSerializer();
var exchange = new GraphExchangeContext(
    graph.Options.Authority,
    new GraphOperationContext(GraphCompatibilityProfile.ConformanceSmall, DateTimeOffset.UtcNow.AddMinutes(1)));
var persistedResult = serializer.Serialize(graph, exchange);
Assert(persistedResult.IsSuccess, persistedResult.Outcome?.Code.Value ?? "Profiled graph serialization failed.");
var persisted = persistedResult.Value;
using (var persistedDocument = JsonDocument.Parse(persisted))
{
    var root = persistedDocument.RootElement;
    Assert(root.GetProperty("schemaVersion").GetInt32() == 2 &&
           root.GetProperty("contractVersion").GetString() == GraphContractVersion.Current.CanonicalText &&
           root.GetProperty("originAuthority").GetString() == graph.Options.Authority.Value,
        "Graph serialization did not use the accepted schema, contract version, or semantic authority.");
}
var persistedPresentation = presentationSerializer.Serialize(beforePresentation);
var restoredResult = serializer.Deserialize(persisted, exchange);
Assert(restoredResult.IsSuccess, restoredResult.Outcome?.Code.Value ?? "Profiled graph deserialization failed.");
var restoredGraph = restoredResult.Value;
var restored = restoredGraph.CaptureSnapshot();
var restoredPresentation = presentationSerializer.Deserialize(persistedPresentation);
var reserializedResult = serializer.Serialize(restoredGraph, exchange);
Assert(reserializedResult.IsSuccess && reserializedResult.Value == persisted &&
       restored.GraphId == beforePersistence.GraphId && restored.Version == beforePersistence.Version,
    "Profiled graph round-trip changed canonical schema-v2 bytes, graph identity, or revision.");
Assert(presentationSerializer.Serialize(restoredPresentation) == persistedPresentation,
    "Presentation sidecar serialization was not deterministic across reload.");
var restoredDocument = projection.Project(restored, restoredPresentation);
Assert(StableProjectionEquivalent(waypointEdit.AuthoritativeDocument, restoredDocument),
    "Reloaded semantics plus presentation sidecar did not reproduce stable diagram identities and geometry.");
Assert(restoredDocument.Edges.Single(edge => edge.Id == authoritativeEdgeId).Waypoints?.Count == 2,
    "Recovered edge presentation was not retained across the independent sidecar round trip.");

var compiler = (IGraphSnapshotCompiler)new GraphCompiler(new NodeTypeRegistry([]));
var compilation = compiler.Compile(restored, new(GraphCompileProfile.DagOnly));
Assert(compilation.Succeeded && compilation.Graph is not null, "Graph-native execution compilation failed after reload.");
var compiledGraph = compilation.Graph ?? throw new InvalidOperationException("Successful compilation returned no graph.");
Assert(compiledGraph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal)
       .SetEquals(restored.Nodes.Select(node => node.Id.ToString())), "Compiled graph lost stable semantic node ids.");
Assert(compiledGraph.Edges.Any(edge => edge.EdgeId == authoritativeEdgeId), "Compiled graph omitted the recovered browser-created relationship.");

Console.WriteLine("PASS representative hierarchy/process/model projection");
Console.WriteLine("PASS semantic and presentation proposals with conflict recovery");
Console.WriteLine($"PASS authoritative browser edge recovery {temporaryEdgeId} -> {authoritativeEdgeId}");
Console.WriteLine("PASS graph serialization and presentation projection round trip");
Console.WriteLine("PASS versioned presentation sidecar persistence and reload");
Console.WriteLine($"PASS graph-native compilation fingerprint {compiledGraph.PlanFingerprint}");
Console.WriteLine("Ghostagram cutover tests passed.");

static bool StableProjectionEquivalent(DiagramDocument left, DiagramDocument right) =>
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Nodes.OrderBy(node => node.Id)), JsonSerializer.SerializeToElement(right.Nodes.OrderBy(node => node.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Ports.OrderBy(port => port.Id)), JsonSerializer.SerializeToElement(right.Ports.OrderBy(port => port.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Edges.OrderBy(edge => edge.Id)), JsonSerializer.SerializeToElement(right.Edges.OrderBy(edge => edge.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Groups.OrderBy(group => group.Id)), JsonSerializer.SerializeToElement(right.Groups.OrderBy(group => group.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Viewport), JsonSerializer.SerializeToElement(right.Viewport));

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class TestNode(NodeKind kind, IGraph graph, string name) : GraphNode(kind, graph, name)
{
    public void Set(string key, object? value) => SetMetadata(key, value);
}
