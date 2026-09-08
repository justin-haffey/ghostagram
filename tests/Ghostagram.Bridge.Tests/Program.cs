using Ghostworx.System.Graph.Serialization;
using Ghostworx.System.Graph.Runtime;
using System.Collections.Immutable;
using System.Text.Json;
using System.Xml.Linq;
using Ghostagram.Bridge;
using Ghostagram.Contracts;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostworx.System.Graph;

if (args.SequenceEqual(new[] { "--profile", "composition" }))
{
    Ghostagram.Bridge.Tests.DeclarativeComposition.ProjectionFoundationTests.Run();
    Ghostagram.Bridge.Tests.DeclarativeComposition.ProjectionBehaviorTests.Run();
    Ghostagram.Bridge.Tests.DeclarativeComposition.OwnerDiagnosticProjectionTests.Run();
    Ghostagram.Bridge.Tests.DeclarativeComposition.ProjectionBoundaryCases.Run();
    Ghostagram.Bridge.Tests.DeclarativeComposition.ProjectionOutputSizeCases.Run();
    Console.WriteLine("Composition foundation and projection behavior tests passed.");
    return;
}

var kind = NodeKind.Define("Tests", "Task");
var graph = new GraphStore("bridge-tests", features: null, options: new GraphStoreOptions { NodeRetention = GraphNodeRetentionMode.Strong });
var parent = new TestNode(kind, graph, "Parent");
var child = new TestNode(kind, graph, "Child");
child.Set("priority", 3L);
child.Set("obsolete", "remove-me");
parent.Set("structured", new Dictionary<string, object?> { ["nested"] = "accepted-semantic-value" });
var dependency = graph.Connect(parent, child, RelationshipKind.DependsOn, "blocks");
var contains = graph.Connect(parent, child, RelationshipKind.Contains);

var descriptor = new NodeTypeDescriptor("tests.task", 2, "Task", "Tests", "mdi:check", 220, 120,
    properties: [new("priority", "Priority", DiagramPropertyTypes.Integer), new("code", "Code", DiagramPropertyTypes.String)],
    rendererKey: "tests.task", rendererVersion: 1);
var registry = new NodeKindDescriptorRegistry([KeyValuePair.Create(kind, descriptor)]);
var presentation = new GraphPresentationStore();
var initialPresentation = presentation.Execute(0, editor =>
{
    editor.SetNode(parent.Id, new(new(100, 120, 220, 120)));
    editor.SetNode(child.Id, new(new(420, 180, 220, 120)));
    editor.SetGroup(parent.Id, new(new(72, 72, 620, 300), Collapsed: false));
    editor.SetWaypoints(dependency.Id, [new(300, 140), new(360, 220)]);
    editor.SetViewport(new(15, 25, 1.25));
    editor.SetSelection([GraphDiagramIds.Node(child.Id)]);
    return true;
});
Assert(initialPresentation.Accepted, "Presentation setup commits.");

var projection = new GraphDiagramProjection(new DefaultNodePresentationMapper(registry));
var snapshot = graph.CaptureSnapshot();
var document = projection.Project(snapshot, presentation.Capture());
var parentDiagram = document.Nodes.Single(node => node.Id == parent.Id.ToString());
var childDiagram = document.Nodes.Single(node => node.Id == child.Id.ToString());
Assert(parentDiagram.Id == parent.Id.ToString() && document.Edges.Any(edge => edge.Id == dependency.Id.ToString()), "Projection uses stable graph node and edge ids, never names.");
Assert(document.Ports.Any(port => port.Id == GraphDiagramIds.InputPort(parent.Id)) && document.Ports.Any(port => port.Id == GraphDiagramIds.OutputPort(parent.Id)), "Projection creates deterministic synthetic input/output ports.");
Assert(parentDiagram.TypeId == descriptor.TypeId && parentDiagram.TypeVersion == 2 && parentDiagram.RendererKey == "tests.task", "NodeKind registry applies kind-specific descriptor and renderer data.");
Assert(document.ExtensionData!["projectionDiagnostics"].EnumerateArray().Any(item => item.GetProperty("metadataKey").GetString() == "structured") &&
       parentDiagram.Properties.All(property => property.Id != "structured"), "Accepted structured semantic metadata without a diagram projection is omitted with a diagnostic rather than stringified.");
Assert(parentDiagram.Properties.Single(property => property.Id == "priority").Value is null && childDiagram.Properties.Single(property => property.Id == "priority").Value!.Value.GetInt64() == 3, "Typed descriptors map registered metadata with defaults preserved.");
Assert(document.Groups.Any(group => group.Id == GraphDiagramIds.Group(parent.Id)) && childDiagram.GroupId == GraphDiagramIds.Group(parent.Id), "Contains relationships project to hierarchy groups and membership.");
Assert(document.Edges.Single(edge => edge.Id == dependency.Id.ToString()).Waypoints!.Count == 2 && document.Viewport.Zoom == 1.25 && document.Selection.SequenceEqual([child.Id.ToString()]), "Presentation sidecar round-trips waypoints, viewport, and selection.");

var descriptorWithPorts = new NodeTypeDescriptor("tests.profiled", 1, "Profiled", "Tests", ports:
[
    new("descriptor-in", "target", "typed", Label: "Descriptor in", Order: 0, Anchor: "left"),
    new("descriptor-out", "source", "typed", Label: "Descriptor out", Order: 1, Anchor: "right")
]);
var descriptorPortProjection = new GraphDiagramProjection(new DefaultNodePresentationMapper(
    new NodeKindDescriptorRegistry([KeyValuePair.Create(kind, descriptorWithPorts)])));
var descriptorPortDocument = descriptorPortProjection.Project(snapshot, presentation.Capture());
Assert(descriptorPortDocument.Ports.Any(port => port.Id == GraphDiagramIds.Port(parent.Id, "descriptor-in")) &&
       descriptorPortDocument.Ports.Any(port => port.Id == GraphDiagramIds.Port(parent.Id, "descriptor-out")),
    "Typed NodeKind descriptors project their declared ports before the generic synthetic fallback.");

var explicitPorts = new NodePortPresentationProfileRegistry([new TestPortProfile(kind)]);
var explicitEdges = new RelationshipPresentationProfileRegistry([new TestRelationshipProfile(RelationshipKind.DependsOn)]);
var profiledProjection = new GraphDiagramProjection(
    new DefaultNodePresentationMapper(new NodeKindDescriptorRegistry([KeyValuePair.Create(kind, descriptorWithPorts)]), explicitPorts),
    new DefaultRelationshipPresentationMapper(explicitEdges));
var profiled = profiledProjection.Project(snapshot, presentation.Capture());
Assert(profiled.Ports.Any(port => port.Id == GraphDiagramIds.Port(parent.Id, "receive")) &&
       profiled.Ports.All(port => !port.Id.EndsWith(":descriptor-in", StringComparison.Ordinal)),
    "An explicit kind-specific port profile overrides descriptor and generic ports.");
var profiledDependency = profiled.Edges.Single(edge => edge.Id == dependency.Id.ToString());
Assert(profiledDependency.SourcePortId == GraphDiagramIds.Port(parent.Id, "send") &&
       profiledDependency.TargetPortId == GraphDiagramIds.Port(child.Id, "receive") &&
       profiledDependency.Connector == "bezier" && profiledDependency.Type == "tests.depends-profile",
    "An explicit relationship-kind profile overrides generic edge ports and presentation while preserving the stable EdgeId.");

var genericProjection = new GraphDiagramProjection();
var generic = genericProjection.Project(snapshot, presentation.Capture());
Assert(generic.Nodes.Single(node => node.Id == child.Id.ToString()).Properties.Any(property => property.Id == "kind"), "Unregistered kinds use the generic semantic mapper.");

var current = document;
var baseVersion = graph.Version;
child.RenameTo("Child renamed");
var renamedSnapshot = graph.CaptureSnapshot();
var changeBatch = graph.ReadChangesSince(baseVersion).Single();
var delta = new GraphDiagramDeltaProjector(projection).Project(changeBatch, current, renamedSnapshot, presentation.Capture());
var deltaApplied = Apply(current, delta.Operations);
var fullAfterRename = projection.Project(renamedSnapshot, presentation.Capture());
Assert(Equivalent(deltaApplied, fullAfterRename), "Applying the projected graph delta is equivalent to a fresh full projection.");
Assert(delta.BaseGraphVersion == baseVersion && delta.GraphVersion == graph.Version && delta.Operations.Any(operation => operation.Type == "node.upsert" && operation.Id == child.Id.ToString()), "Delta batch preserves graph versions and updates the changed stable node.");
Assert(delta.Metadata!["graphVersion"].GetInt64() == graph.Version && delta.Metadata["presentationRevision"].GetInt64() == presentation.Revision,
    "Delta results carry authoritative graph and presentation version metadata.");

var deltaCurrent = fullAfterRename;
var addBase = graph.Version;
var transient = new TestNode(kind, graph, "Transient");
var addBatch = graph.ReadChangesSince(addBase).Single();
var addSnapshot = graph.CaptureSnapshot();
var addDelta = new GraphDiagramDeltaProjector(projection).Project(addBatch, deltaCurrent, addSnapshot, presentation.Capture());
deltaCurrent = Apply(deltaCurrent, addDelta.Operations);
Assert(!addDelta.RequiresFullProjection && Equivalent(deltaCurrent, projection.Project(addSnapshot, presentation.Capture())) &&
       addDelta.Operations.Any(operation => operation.Type == "node.upsert" && operation.Id == transient.Id.ToString()) &&
       addDelta.Operations.Count(operation => operation.Type == "port.upsert" && operation.Id!.StartsWith($"{transient.Id}:", StringComparison.Ordinal)) == 2,
    "Node registration projects an equivalent incremental node and port batch.");

var edgeBase = graph.Version;
var transientEdge = graph.Connect(transient, parent, RelationshipKind.DependsOn, "temporary");
var edgeBatch = graph.ReadChangesSince(edgeBase).Single();
var edgeSnapshot = graph.CaptureSnapshot();
var edgeDelta = new GraphDiagramDeltaProjector(projection).Project(edgeBatch, deltaCurrent, edgeSnapshot, presentation.Capture());
deltaCurrent = Apply(deltaCurrent, edgeDelta.Operations);
Assert(!edgeDelta.RequiresFullProjection && Equivalent(deltaCurrent, projection.Project(edgeSnapshot, presentation.Capture())) &&
       edgeDelta.Operations.Single(operation => operation.Type == "edge.upsert").Id == transientEdge.Id.ToString(),
    "Relationship connection projects an equivalent incremental edge batch.");

var edgeRemoveBase = graph.Version;
graph.Disconnect(transientEdge.Id);
var edgeRemoveBatch = graph.ReadChangesSince(edgeRemoveBase).Single();
var edgeRemoveSnapshot = graph.CaptureSnapshot();
var edgeRemoveDelta = new GraphDiagramDeltaProjector(projection).Project(edgeRemoveBatch, deltaCurrent, edgeRemoveSnapshot, presentation.Capture());
deltaCurrent = Apply(deltaCurrent, edgeRemoveDelta.Operations);
Assert(!edgeRemoveDelta.RequiresFullProjection && Equivalent(deltaCurrent, projection.Project(edgeRemoveSnapshot, presentation.Capture())) &&
       edgeRemoveDelta.Operations.Single(operation => operation.Type == "edge.remove").Id == transientEdge.Id.ToString(),
    "Relationship disconnection projects an equivalent incremental edge removal.");

var containsBase = graph.Version;
var transientContainment = graph.Connect(parent, transient, RelationshipKind.Contains);
var containsBatch = graph.ReadChangesSince(containsBase).Single();
var containsSnapshot = graph.CaptureSnapshot();
var containsDelta = new GraphDiagramDeltaProjector(projection).Project(containsBatch, deltaCurrent, containsSnapshot, presentation.Capture());
var containsRecovered = projection.Project(containsSnapshot, presentation.Capture());
Assert(containsDelta.RequiresFullProjection &&
       containsRecovered.Nodes.Single(node => node.Id == transient.Id.ToString()).GroupId == GraphDiagramIds.Group(parent.Id) &&
       containsRecovered.Groups.Any(group => group.Id == GraphDiagramIds.Group(parent.Id)),
    "Containment changes explicitly request authoritative full-projection recovery because they can reshape groups and memberships.");
deltaCurrent = containsRecovered;

var containsRemoveBase = graph.Version;
graph.Disconnect(transientContainment.Id);
var containsRemoveBatch = graph.ReadChangesSince(containsRemoveBase).Single();
var containsRemoveSnapshot = graph.CaptureSnapshot();
var containsRemoveDelta = new GraphDiagramDeltaProjector(projection).Project(containsRemoveBatch, deltaCurrent, containsRemoveSnapshot, presentation.Capture());
var containsRemoveRecovered = projection.Project(containsRemoveSnapshot, presentation.Capture());
Assert(containsRemoveDelta.RequiresFullProjection &&
       containsRemoveRecovered.Nodes.Single(node => node.Id == transient.Id.ToString()).GroupId is null,
    "Containment removal also requests authoritative full-projection recovery and clears membership.");
deltaCurrent = containsRemoveRecovered;

var removeBase = graph.Version;
graph.Unregister(transient.Id);
var removeBatch = graph.ReadChangesSince(removeBase).Single();
var removeSnapshot = graph.CaptureSnapshot();
var removeDelta = new GraphDiagramDeltaProjector(projection).Project(removeBatch, deltaCurrent, removeSnapshot, presentation.Capture());
deltaCurrent = Apply(deltaCurrent, removeDelta.Operations);
Assert(!removeDelta.RequiresFullProjection && Equivalent(deltaCurrent, projection.Project(removeSnapshot, presentation.Capture())) &&
       removeDelta.Operations.Any(operation => operation.Type == "node.remove") && removeDelta.Operations.Count(operation => operation.Type == "port.remove") == 2,
    "Node removal projects equivalent node and port removals.");

var presentationSerializer = new GraphPresentationJsonSerializer();
var presentationJson = presentationSerializer.Serialize(presentation.Capture());
var restoredPresentation = presentationSerializer.Deserialize(presentationJson);
Assert(presentationSerializer.Serialize(restoredPresentation) == presentationJson &&
       restoredPresentation.Nodes[child.Id].Bounds.X == presentation.Capture().Nodes[child.Id].Bounds.X &&
       restoredPresentation.EdgeWaypoints[dependency.Id].Count == 2 &&
       restoredPresentation.Selection.SequenceEqual(presentation.Capture().Selection),
    "The versioned presentation sidecar has a deterministic durable JSON round trip.");
AssertThrows<GraphPresentationSerializationException>(() => presentationSerializer.Deserialize(presentationJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":99", StringComparison.Ordinal)),
    "Future presentation schema versions fail closed.");

var orphanNode = NodeId.New();
var orphanEdge = EdgeId.New();
var orphanCommit = presentation.Execute(presentation.Revision, editor =>
{
    editor.SetNode(orphanNode, new(new(1, 1, 10, 10)));
    editor.SetGroup(orphanNode, new(new(1, 1, 10, 10)));
    editor.SetWaypoints(orphanEdge, [new(1, 1)]);
    editor.SetSelection([orphanNode.ToString(), child.Id.ToString()]);
    return true;
});
var reconcile = presentation.Reconcile(renamedSnapshot, OrphanHandling.Remove, orphanCommit.Revision);
Assert(reconcile.Accepted && reconcile.RemovedNodes == 1 && reconcile.RemovedGroups == 1 && reconcile.RemovedEdges == 1 && reconcile.RemovedSelections == 1, "Orphan reconciliation removes stale graph-keyed presentation state.");

var adapter = new GraphDiagramCommandAdapter(graph, presentation, projection, registry);
var authoritativeBefore = projection.Project(graph.CaptureSnapshot(), presentation.Capture());
var proposedChild = authoritativeBefore.Nodes.Single(node => node.Id == child.Id.ToString()) with
{
    Label = "Command rename",
    X = 510,
    Y = 260,
    Properties = [new("priority", "Priority", DiagramPropertyTypes.Integer, JsonSerializer.SerializeToElement(7L)),
        new("code", "Code", DiagramPropertyTypes.String, JsonSerializer.SerializeToElement("2026-08-19T12:30:00Z"))]
};
var accepted = adapter.Apply(new(graph.Version, presentation.Revision, [DiagramOperations.Upsert(proposedChild)]));
Assert(accepted.Accepted && accepted.GraphChanges is not null && accepted.AuthoritativeDocument.Nodes.Single(node => node.Id == child.Id.ToString()).Label == "Command rename", "Browser node proposals atomically mutate semantic name/metadata and return authoritative recovery state.");
Assert(graph.CaptureSnapshot().Nodes.Single(node => node.Id == child.Id).Metadata["priority"].GetScalar<long>() == 7 && presentation.Capture().Nodes[child.Id].Bounds.X == 510, "Accepted command preserves semantic and presentation ownership in their respective stores.");
var replacedMetadata = graph.CaptureSnapshot().Nodes.Single(node => node.Id == child.Id).Metadata;
Assert(!replacedMetadata.ContainsKey("obsolete") && replacedMetadata["code"].Kind == GraphSemanticValueKind.String && replacedMetadata["code"].GetScalar<string>() == "2026-08-19T12:30:00Z",
    "Node upsert replaces the full projected metadata set and preserves date-like strings as declared strings.");

var beforeInvalidGraphVersion = graph.Version;
var beforeInvalidPresentationRevision = presentation.Revision;
var invalidGeometry = adapter.Apply(new(graph.Version, presentation.Revision,
    [DiagramOperations.Upsert(proposedChild with { Width = 0 })]));
Assert(!invalidGeometry.Accepted && invalidGeometry.Code == "INVALID_PROPOSAL" && graph.Version == beforeInvalidGraphVersion && presentation.Revision == beforeInvalidPresentationRevision,
    "Invalid geometry fails before either authoritative store commits.");
var invalidSelection = adapter.Apply(new(graph.Version, presentation.Revision, [DiagramOperations.Select(["missing-selection-id"])]));
Assert(!invalidSelection.Accepted && invalidSelection.Code == "INVALID_PROPOSAL", "Selection must refer to authoritative graph-backed state.");

var graphConflict = adapter.Apply(new(accepted.GraphVersion - 1, accepted.DiagramRevision, [DiagramOperations.Select([])]));
Assert(!graphConflict.Accepted && graphConflict.Code == "GRAPH_VERSION_CONFLICT" && graphConflict.AuthoritativeDocument.Nodes.Any(node => node.Label == "Command rename"), "Stale graph commands return a clear conflict with authoritative full projection recovery.");
var diagramConflict = adapter.Apply(new(graph.Version, accepted.DiagramRevision - 1, [DiagramOperations.Select([])]));
Assert(!diagramConflict.Accepted && diagramConflict.Code == "DIAGRAM_REVISION_CONFLICT" && diagramConflict.DiagramRevision == presentation.Revision, "Stale diagram commands return a clear presentation conflict and recovery revision.");

var newEdge = new DiagramEdge("browser-temp", GraphDiagramIds.OutputPort(child.Id), GraphDiagramIds.InputPort(parent.Id),
    "references", Type: RelationshipKind.References.QualifiedName, Waypoints: [new(620, 310), new(260, 330)]);
var createdEdge = adapter.Apply(new(graph.Version, presentation.Revision, [DiagramOperations.Upsert(newEdge)]));
var authoritativeCreatedEdge = createdEdge.GraphChanges!.Changes.Single(change => change.Kind == GraphChangeKind.RelationshipConnected).Relationship!.Value;
Assert(createdEdge.Accepted && createdEdge.AuthoritativeDocument.Edges.Single(edge => edge.Id == authoritativeCreatedEdge.Id.ToString()).Waypoints!.Count == 2,
    "New-edge waypoints are atomically remapped from the temporary browser id to the authoritative EdgeId.");

var bridgeProject = XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Ghostagram.Bridge", "Ghostagram.Bridge.csproj")));
var bridgeReferences = bridgeProject.Descendants("ProjectReference").Select(element => (string?)element.Attribute("Include") ?? "").ToArray();
Assert(bridgeReferences.Any(reference => reference.Contains("Ghostworx.System.Core", StringComparison.Ordinal)) && bridgeReferences.Any(reference => reference.Contains("Ghostagram.Core", StringComparison.Ordinal)), "Bridge is the only integration layer and references both systems.");
var systemProject = XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "ghostworx-system", "src", "Ghostworx.System.Core", "Ghostworx.System.Core.csproj")));
Assert(!systemProject.Descendants("ProjectReference").Any(element => ((string?)element.Attribute("Include"))?.Contains("Ghostagram", StringComparison.OrdinalIgnoreCase) == true), "Ghostworx.System.Core never references Ghostagram.");

Console.WriteLine("Ghostagram.Bridge tests passed.");

static DiagramDocument Apply(DiagramDocument document, IEnumerable<GhostagramOperation> operations)
{
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var nodes = document.Nodes.ToDictionary(node => node.Id);
    var ports = document.Ports.ToDictionary(port => port.Id);
    var edges = document.Edges.ToDictionary(edge => edge.Id);
    var groups = document.Groups.ToDictionary(group => group.Id);
    var selection = document.Selection.ToArray();
    var viewport = document.Viewport;
    foreach (var operation in operations)
    {
        switch (operation.Type)
        {
            case "node.upsert": { var value = operation.Value.Deserialize<DiagramNode>(jsonOptions)!; nodes[value.Id] = value; break; }
            case "port.upsert": { var value = operation.Value.Deserialize<DiagramPort>(jsonOptions)!; ports[value.Id] = value; break; }
            case "edge.upsert": { var value = operation.Value.Deserialize<DiagramEdge>(jsonOptions)!; edges[value.Id] = value; break; }
            case "group.upsert": { var value = operation.Value.Deserialize<DiagramGroup>(jsonOptions)!; groups[value.Id] = value; break; }
            case "node.remove": nodes.Remove(operation.Id!); break;
            case "port.remove": ports.Remove(operation.Id!); break;
            case "edge.remove": edges.Remove(operation.Id!); break;
            case "group.remove": groups.Remove(operation.Id!); break;
            case "selection.replace": selection = operation.Value.GetProperty("ids").EnumerateArray().Select(item => item.GetString()!).ToArray(); break;
            case "viewport.set": viewport = operation.Value.Deserialize<DiagramViewport>(jsonOptions)!; break;
        }
    }
    return document with { Nodes = nodes.Values.OrderBy(node => node.Id).ToArray(), Ports = ports.Values.OrderBy(port => port.Id).ToArray(), Edges = edges.Values.OrderBy(edge => edge.Id).ToArray(), Groups = groups.Values.OrderBy(group => group.Id).ToArray(), Selection = selection, Viewport = viewport };
}

static bool Equivalent(DiagramDocument left, DiagramDocument right) =>
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Nodes.OrderBy(node => node.Id)), JsonSerializer.SerializeToElement(right.Nodes.OrderBy(node => node.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Ports.OrderBy(port => port.Id)), JsonSerializer.SerializeToElement(right.Ports.OrderBy(port => port.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Edges.OrderBy(edge => edge.Id)), JsonSerializer.SerializeToElement(right.Edges.OrderBy(edge => edge.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Groups.OrderBy(group => group.Id)), JsonSerializer.SerializeToElement(right.Groups.OrderBy(group => group.Id))) &&
    JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Viewport), JsonSerializer.SerializeToElement(right.Viewport)) &&
    left.Selection.SequenceEqual(right.Selection);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

sealed class TestNode(NodeKind kind, IGraph graph, string name) : GraphNode(kind, graph, name)
{
    public void RenameTo(string name) => SetNodeName(name);
    public void Set(string key, object? value) => SetMetadata(key, value);
}

sealed class TestPortProfile(NodeKind kind) : INodePortPresentationProfile
{
    public NodeKind Kind { get; } = kind;
    public IReadOnlyList<DiagramPort> Map(GraphLocalNodeSnapshot node, DiagramNode projectedNode) =>
    [
        new(GraphDiagramIds.Port(node.Id, "receive"), projectedNode.Id, "target", "profile", Anchor: "left", Label: "Receive"),
        new(GraphDiagramIds.Port(node.Id, "send"), projectedNode.Id, "source", "profile", Anchor: "right", Label: "Send")
    ];
}

sealed class TestRelationshipProfile(RelationshipKind kind) : IRelationshipPresentationProfile
{
    public RelationshipKind Kind { get; } = kind;
    public DiagramEdge Map(GraphLocalRelationshipSnapshot relationship, GraphPresentationSnapshot presentation,
        IReadOnlyList<DiagramPort> sourcePorts, IReadOnlyList<DiagramPort> targetPorts) => new(
        GraphDiagramIds.Edge(relationship.Relationship.Id),
        GraphDiagramIds.Port(relationship.Relationship.Source, "send"),
        GraphDiagramIds.Port(relationship.Relationship.Target, "receive"),
        relationship.Relationship.Label,
        Connector: "bezier",
        Type: "tests.depends-profile",
        Waypoints: presentation.EdgeWaypoints.GetValueOrDefault(relationship.Relationship.Id) ?? []);
}
