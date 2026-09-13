using System.Text.Json;
using Ghostworx.System.Graph.Runtime;
using System.Collections.Immutable;
using Ghostagram.Bridge;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostagram.Server.GraphWorkspaces;
using Ghostworx.System.Graph;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

var descriptor = new NodeTypeDescriptor(
    "server.task",
    1,
    "Task",
    "Server tests",
    width: 220,
    height: 107,
    properties:
    [
        new("owner", "Owner", DiagramPropertyTypes.String),
        new("future", "Future", DiagramPropertyTypes.Json)
    ]);
var nodeTypes = new NodeTypeRegistry([new NodeSetDescriptor("server-tests", "Server tests", [descriptor])]);
AssertThrows<ArgumentOutOfRangeException>(() => new NodeTypeDescriptor(
    "server.task.too-short", 1, "Task", "Server tests", width: 220, height: 106,
    properties: descriptor.Properties),
    "The real descriptor rejects 106px for the text and JSON property rows; their minimum is 107px.");
var storageDirectory = Path.Combine(Path.GetTempPath(), "ghostagram-graph-workspace-tests", Guid.NewGuid().ToString("N"));

var services = new ServiceCollection();
services.AddSingleton<INodeTypeRegistry>(nodeTypes);
services.AddGraphWorkspaceBridge(options => { options.MaximumWorkspaces = 2; options.StorageDirectory = storageDirectory; });
await using var provider = services.BuildServiceProvider();
var workspaces = provider.GetRequiredService<IGraphWorkspaceService>();

var created = workspaces.Create("orders");
Assert(created.GraphVersion == 0 && created.DiagramRevision == 0 && created.Document.Nodes.Count == 1 &&
       created.Document.Nodes.Single().TypeId == NodeKind.Graph.QualifiedName,
    "A new workspace must expose only its authoritative graph root.");

var firstId = NodeId.New();
var secondId = NodeId.New();
var first = new DiagramNode(firstId.ToString(), 80, 100, 220, 107, "Receive order", TypeId: descriptor.TypeId,
    TypeVersion: descriptor.Version, Properties: [new("owner", "Owner", Value: System.Text.Json.JsonSerializer.SerializeToElement("operations"))]);
var second = new DiagramNode(secondId.ToString(), 420, 100, 220, 107, "Approve order", TypeId: descriptor.TypeId,
    TypeVersion: descriptor.Version);
var nodeCommand = new GraphDiagramCommand(
    created.GraphVersion,
    created.DiagramRevision,
    ImmutableArray.Create(DiagramOperations.Upsert(first), DiagramOperations.Upsert(second), DiagramOperations.Select([first.Id])));
Assert(workspaces.TryApply("orders", nodeCommand, out var nodeResult) && nodeResult is { Accepted: true, GraphChanges: not null },
    "Browser node proposals must commit through the graph transaction and presentation sidecar.");
Assert(nodeResult!.AuthoritativeDocument.Nodes.Count == 3 &&
       nodeResult.AuthoritativeDocument.Nodes.Where(node => node.Id is var id && (id == first.Id || id == second.Id))
           .All(node => node.TypeId == descriptor.TypeId) &&
       nodeResult.AuthoritativeDocument.Selection.SequenceEqual([first.Id]),
    "Accepted proposals must return the typed authoritative graph projection and presentation state.");

const string temporaryEdgeId = "browser-temp-edge";
var edge = new DiagramEdge(
    temporaryEdgeId,
    GraphDiagramIds.OutputPort(firstId),
    GraphDiagramIds.InputPort(secondId),
    "then",
    Type: RelationshipKind.DependsOn.QualifiedName);
var edgeCommand = new GraphDiagramCommand(
    nodeResult.GraphVersion,
    nodeResult.DiagramRevision,
    ImmutableArray.Create(DiagramOperations.Upsert(edge)));
Assert(workspaces.TryApply("orders", edgeCommand, out var edgeResult) && edgeResult is { Accepted: true, GraphChanges: not null },
    "A browser-created relationship must commit through the authoritative GraphStore transaction.");
var connected = edgeResult!.GraphChanges!.Changes.Single(change => change.Kind == GraphChangeKind.RelationshipConnected).Relationship
    ?? throw new InvalidOperationException("The relationship change did not expose its authoritative identity.");
Assert(edgeResult.AuthoritativeDocument.Edges.Any(item => item.Id == connected.Id.ToString()) &&
       edgeResult.AuthoritativeDocument.Edges.All(item => item.Id != temporaryEdgeId),
    "The response must replace the browser temporary edge id with the GraphStore edge id.");

var stale = new GraphDiagramCommand(
    nodeResult.GraphVersion,
    edgeResult.DiagramRevision,
    ImmutableArray.Create(DiagramOperations.Select([])));
Assert(workspaces.TryApply("orders", stale, out var conflict) && conflict is { Accepted: false, Code: "GRAPH_VERSION_CONFLICT" } &&
       conflict.AuthoritativeDocument.Edges.Any(item => item.Id == connected.Id.ToString()),
    "A stale proposal must return the current authoritative projection for recovery.");
Assert(workspaces.TryGetSnapshot("orders", out var recovered) && recovered!.GraphVersion == edgeResult.GraphVersion &&
       recovered.Document.Edges.Any(item => item.Id == connected.Id.ToString()),
    "Workspace reads must be projected from current graph and presentation snapshots.");

workspaces.Create("models");
AssertThrows<InvalidOperationException>(() => workspaces.Create("capacity"),
    "The configured workspace bound must be enforced.");
Assert(workspaces.Remove("models") && workspaces.Create("capacity").WorkspaceId == "capacity",
    "Removing a workspace must release bounded capacity.");
AssertThrows<ArgumentException>(() => workspaces.Create("invalid/workspace"),
    "Workspace identifiers must reject route-breaking characters.");

var saved = await workspaces.SaveAsync("orders") ?? throw new InvalidOperationException("The live orders workspace was not saved.");
Assert(File.Exists(Path.Combine(storageDirectory, "orders.json")),
    "Saving a workspace must atomically persist graph and presentation state.");
var persistedOrders = await provider.GetRequiredService<IGraphWorkspaceRepository>().LoadAsync("orders")
    ?? throw new InvalidOperationException("The persisted workspace is missing.");
using (var persistedGraph = JsonDocument.Parse(persistedOrders.GraphJson))
{
    var root = persistedGraph.RootElement;
    Assert(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("changes").GetArrayLength() > 0,
        "Saving preserves schema 1 and complete retained graph history when available.");
    Assert(new[] { "contractVersion", "profileId", "profileVersion", "originAuthority", "graphAddress", "extensionPolicy" }
        .All(name => root.GetProperty(name).ValueKind == JsonValueKind.Null) &&
        root.GetProperty("vocabularies").GetArrayLength() == 0 && root.GetProperty("federationReferences").GetArrayLength() == 0,
        "The historical schema-1 null governance fields and empty vocabulary/reference arrays are preserved without invented admission.");
}
Assert(workspaces.Remove("orders"), "The live workspace must be removable without deleting its durable file.");

var restartedServices = new ServiceCollection();
restartedServices.AddSingleton<INodeTypeRegistry>(nodeTypes);
restartedServices.AddGraphWorkspaceBridge(options => { options.MaximumWorkspaces = 2; options.StorageDirectory = storageDirectory; });
await using var restartedProvider = restartedServices.BuildServiceProvider();
var restartedWorkspaces = restartedProvider.GetRequiredService<IGraphWorkspaceService>();
var reloaded = await restartedWorkspaces.LoadAsync("orders") ?? throw new InvalidOperationException("The persisted orders workspace was not loaded.");
Assert(reloaded.GraphVersion == saved.GraphVersion && reloaded.DiagramRevision == saved.DiagramRevision &&
       reloaded.Document.Nodes.Any(node => node.Id == first.Id && node.X == 80 && node.TypeId == descriptor.TypeId && node.TypeVersion == descriptor.Version) &&
       reloaded.Document.Selection.SequenceEqual([first.Id]) &&
       reloaded.Document.Edges.Any(item => item.Id == connected.Id.ToString()),
    "A new service provider must reload stable semantics and presentation from the durable repository.");
var restartedConflict = new GraphDiagramCommand(saved.GraphVersion - 1, saved.DiagramRevision,
    ImmutableArray.Create(DiagramOperations.Select([])));
Assert(restartedWorkspaces.TryApply("orders", restartedConflict, out var recoveredAfterRestart) &&
       recoveredAfterRestart is { Accepted: false, Code: "GRAPH_VERSION_CONFLICT" } &&
       recoveredAfterRestart.AuthoritativeDocument.Selection.SequenceEqual([first.Id]),
    "Reloaded workspaces must retain authoritative conflict recovery.");

// Fixed schema-v1 graph shape used by the pre-F004 host serializer.
const string legacyGraph = """
{ "schemaVersion": 1, "documentType": "ghostworx.graph.snapshot", "graphId": "41414141-4141-4141-4141-414141414141", "version": 1,
  "nodes": [
    { "id": "41414141-4141-4141-4141-414141414141", "kind": { "namespace": "Ghostworx.System.Graph", "name": "Graph" }, "name": "legacy", "metadata": {} },
    { "id": "42424242-4242-4242-4242-424242424242", "kind": { "namespace": "Ghostagram.NodeType", "name": "server.task@1" }, "name": "legacy task",
      "metadata": { "future": { "codec": "future.codec", "codecVersion": "1.0.0", "value": { "x": 1 }, "futureField": { "retained": true } } },
      "fixtureNodeExtension": { "retained": true } }
  ],
  "relationships": [], "changes": [], "fixtureDocumentExtension": { "retained": true } }
""";
var repository = restartedProvider.GetRequiredService<IGraphWorkspaceRepository>();
await repository.SaveAsync(new("legacy", legacyGraph,
    new GraphPresentationJsonSerializer().Serialize(new GraphPresentationStore().Capture())));
var legacyWorkspace = await restartedWorkspaces.LoadAsync("legacy") ?? throw new InvalidOperationException("The legacy workspace did not load.");
var legacyTask = legacyWorkspace.Document.Nodes.Single(node => node.Id == "42424242424242424242424242424242");
Assert(legacyTask.TypeId == descriptor.TypeId && legacyTask.TypeVersion == descriptor.Version && legacyTask.Properties.Any(property => property.Id == "owner"),
    "Schema-v1 custom kinds retain exact descriptor identity and editable properties after reload.");
var replacement = JsonSerializer.SerializeToElement(new Dictionary<string, bool> { ["changed"] = true });
var replacementProperties = legacyTask.Properties.Select(property => property.Id == "future"
    ? property with { Value = replacement }
    : property).ToArray();
Assert(restartedWorkspaces.TryApply("legacy", new(legacyWorkspace.GraphVersion, legacyWorkspace.DiagramRevision,
    [DiagramOperations.Upsert(legacyTask with { Label = "Edited legacy task", Properties = replacementProperties })]), out var legacyEdit) &&
    legacyEdit is { Accepted: true },
    "A reloaded legacy custom kind can replace imported opaque metadata through its registered descriptor.");
Assert(restartedWorkspaces.TryGetSnapshot("legacy", out var capturedReplacement) && capturedReplacement is not null,
    "The edited workspace exposes an authoritative capture before persistence.");
var capturedTask = capturedReplacement!.Document.Nodes.Single(node => node.Id == legacyTask.Id);
var capturedFuture = capturedTask.Properties.Single(property => property.Id == "future").Value;
Assert(capturedFuture is { ValueKind: JsonValueKind.Object } && capturedFuture.Value.GetProperty("changed").GetBoolean(),
    "Authoritative capture contains the accepted opaque metadata replacement before save.");
Assert(capturedTask.Height == 107 && capturedTask.Properties.Count(property => property.Mode != DiagramPropertyModes.Hidden) == 2 &&
       capturedTask.Properties.Single(property => property.Id == "owner").Type == DiagramPropertyTypes.String &&
       capturedTask.Properties.Single(property => property.Id == "future").Type == DiagramPropertyTypes.Json,
    "The captured node retains both visible text and JSON rows at the descriptor's accepted 107px minimum.");
await restartedWorkspaces.SaveAsync("legacy");
var persistedLegacy = await repository.LoadAsync("legacy") ?? throw new InvalidOperationException("The legacy workspace was not saved.");
using (var legacyDocument = JsonDocument.Parse(persistedLegacy.GraphJson))
{
    var root = legacyDocument.RootElement;
    var node = root.GetProperty("nodes").EnumerateArray()
        .Single(item => item.GetProperty("id").GetGuid() == Guid.Parse("42424242-4242-4242-4242-424242424242"));
    var future = node.GetProperty("metadata").GetProperty("future");
    var historyEdit = root.GetProperty("changes").EnumerateArray()
        .SelectMany(batch => batch.GetProperty("changes").EnumerateArray())
        .Single(change => change.TryGetProperty("metadataKey", out var key) && key.GetString() == "future");
    Assert(root.GetProperty("schemaVersion").GetInt32() == 1 &&
           root.GetProperty("fixtureDocumentExtension").GetProperty("retained").GetBoolean() &&
           node.GetProperty("fixtureNodeExtension").GetProperty("retained").GetBoolean(),
        "Legacy save preserves schema 1 and document/node extensions after an edit.");
    Assert(future.GetProperty("codec").GetString() == "semantic-value" &&
           future.GetProperty("value").GetProperty("changed").GetBoolean() &&
           historyEdit.GetProperty("oldValue").GetProperty("codec").GetString() == "future.codec" &&
           historyEdit.GetProperty("oldValue").GetProperty("value").GetProperty("x").GetInt32() == 1 &&
           historyEdit.GetProperty("oldValue").GetProperty("futureField").GetProperty("retained").GetBoolean() &&
           historyEdit.GetProperty("newValue").GetProperty("codec").GetString() == "semantic-value" &&
           historyEdit.GetProperty("newValue").GetProperty("value").GetProperty("changed").GetBoolean(),
        "Replacing imported opaque metadata remains authoritative through capture and preserves exact old/new history meaning.");
}
Assert(restartedWorkspaces.Remove("legacy"), "The edited legacy workspace must leave memory without deleting its durable file.");
var reloadedLegacy = await restartedWorkspaces.LoadAsync("legacy")
    ?? throw new InvalidOperationException("The edited legacy workspace did not reload.");
var reloadedFuture = reloadedLegacy.Document.Nodes.Single(node => node.Id == "42424242424242424242424242424242")
    .Properties.Single(property => property.Id == "future").Value;
Assert(reloadedFuture is { ValueKind: JsonValueKind.Object } && reloadedFuture.Value.GetProperty("changed").GetBoolean(),
    "A service restart path reloads the accepted replacement instead of the imported opaque predecessor.");
Assert(await restartedWorkspaces.DeleteAsync("legacy"), "Legacy regression fixture is removed after verification.");

// JSON properties are ordinary bounded values, even when their keys resemble serialization markers.
var jsonSnapshot = restartedWorkspaces.Create("json-ingress");
var jsonNode = first with { Id = NodeId.New().ToString(), Label = "JSON ingress" };
var jsonLimits = GraphLegacyMetadataPolicy.Default.Limits;
JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
var mixedJson = Json("""{"array":[null,true,"2026-01-01T00:00:00Z","11111111-1111-1111-1111-111111111111",1,2147483648,1.25,1e100],"$opaque":{"value":true},"$bytes":"not-base64"}""");
await CheckJson("create nested scalar and marker values", mixedJson, true);
await CheckJson("replace nested scalar and marker values", mixedJson, true);
foreach (var value in new[] { "null", "false", "\"plain\"", "-2147483648", "9223372036854775807", "1.25", "1e100" })
    await CheckJson("scalar " + value, Json(value), true);
string Nested(int depth) => new string('[', depth - 1) + "null" + new string(']', depth - 1);
await CheckJson("depth exact", Json(Nested(jsonLimits.MaxSemanticValueDepth)), true);
await CheckJson("depth next", Json(Nested(jsonLimits.MaxSemanticValueDepth + 1)), false);
await CheckJson("items exact", JsonSerializer.SerializeToElement(new object?[jsonLimits.MaxSemanticValueItems]), true);
await CheckJson("items next", JsonSerializer.SerializeToElement(new object?[jsonLimits.MaxSemanticValueItems + 1]), false);
await CheckJson("key exact", JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [new string('k', jsonLimits.MaxMetadataKeyUtf8Bytes)] = null }), true);
await CheckJson("key next", JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [new string('k', jsonLimits.MaxMetadataKeyUtf8Bytes + 1)] = null }), false);
await CheckJson("scalar exact UTF8", JsonSerializer.SerializeToElement(new string('é', jsonLimits.MaxScalarValueUtf8Bytes / 2)), true);
await CheckJson("scalar next UTF8", JsonSerializer.SerializeToElement(new string('é', jsonLimits.MaxScalarValueUtf8Bytes / 2) + "x"), false);
JsonElement Aggregate(int bytes)
{
    var parts = new List<string>();
    while (bytes > 0) { var length = Math.Min(bytes, jsonLimits.MaxScalarValueUtf8Bytes); parts.Add(new string('a', length)); bytes -= length; }
    return JsonSerializer.SerializeToElement(parts);
}
await CheckJson("aggregate exact", Aggregate(jsonLimits.MaxSemanticValueUtf8Bytes), true);
await CheckJson("aggregate next", Aggregate(jsonLimits.MaxSemanticValueUtf8Bytes + 1), false);
await CheckJson("duplicate key", Json("{\"same\":1,\"same\":2}"), false);
await CheckJson("blank key", Json("{\" \":1}"), false);
await CheckJson("nonfinite numeric fallback", Json("1e999"), false);
await CheckJson("undefined DOM", default, false, allowSerializationRejection: true);
Assert(await restartedWorkspaces.DeleteAsync("json-ingress"), "JSON regression fixture is removed after verification.");
Console.WriteLine("PASS current JSON ingress creation/replacement and exact/next semantic bounds with atomic rejection");

async Task CheckJson(string name, JsonElement value, bool accepted, bool allowSerializationRejection = false)
{
    var before = jsonSnapshot;
    var beforeJson = JsonSerializer.Serialize(before.Document);
    try { await restartedWorkspaces.SaveAsync("json-ingress"); }
    catch (Exception exception) { throw new InvalidOperationException($"{name}: save before command failed.", exception); }
    var persistedBefore = await repository.LoadAsync("json-ingress");
    var candidate = jsonNode with { Properties = [new("owner", "Owner", Value: JsonSerializer.SerializeToElement("operations")), new("future", "Future", DiagramPropertyTypes.Json, Value: value)] };
    GraphDiagramCommandResult? result = null;
    var serializationRejected = false;
    Ghostagram.Contracts.GhostagramOperation? operation = null;
    try { operation = DiagramOperations.Upsert(candidate); }
    catch (InvalidOperationException) when (allowSerializationRejection) { serializationRejected = true; }
    catch (ArgumentException) when (allowSerializationRejection) { serializationRejected = true; }
    if (!serializationRejected)
        Assert(restartedWorkspaces.TryApply("json-ingress", new(before.GraphVersion, before.DiagramRevision, [operation!]), out result), name + ": workspace exists.");
    Assert(serializationRejected || result?.Accepted == accepted, $"{name}: expected Accepted={accepted}, actual {result?.Code}: {result?.Message}.");
    Assert(restartedWorkspaces.TryGetSnapshot("json-ingress", out var after) && after is not null, name + ": capture exists.");
    if (accepted)
    {
        var observed = after!.Document.Nodes.Single(node => node.Id == jsonNode.Id).Properties.Single(property => property.Id == "future").Value;
        Assert(observed.HasValue && JsonElement.DeepEquals(value, observed.Value), name + ": authoritative JSON meaning is retained.");
        jsonSnapshot = after;
    }
    else
    {
        Assert(after!.GraphVersion == before.GraphVersion && after.DiagramRevision == before.DiagramRevision && JsonSerializer.Serialize(after.Document) == beforeJson,
            name + ": rejected input preserves revisions and authoritative metadata.");
        var persistedAfter = await repository.LoadAsync("json-ingress");
        Assert(persistedBefore == persistedAfter, name + ": rejection leaves prior persisted state unchanged.");
    }
}
// The host historically saved snapshots even after its bounded history ring expired.
var ringServices = new ServiceCollection();
ringServices.AddSingleton<INodeTypeRegistry>(nodeTypes);
ringServices.AddGraphWorkspaceBridge(options => { options.ChangeHistoryCapacity = 1; options.StorageDirectory = storageDirectory; });
await using var ringProvider = ringServices.BuildServiceProvider();
var ringWorkspaces = ringProvider.GetRequiredService<IGraphWorkspaceService>();
var ringSnapshot = ringWorkspaces.Create("ring");
for (var update = 0; update < 3; update++)
{
    Assert(ringWorkspaces.TryApply("ring", new(ringSnapshot.GraphVersion, ringSnapshot.DiagramRevision,
        [DiagramOperations.Upsert(first with { Label = $"Ring update {update}" })]), out var ringResult) && ringResult is { Accepted: true },
        $"History-ring fixture update {update} is accepted: {ringResult?.Code} {ringResult?.Message}.");
    ringSnapshot = new("ring", ringResult!.GraphVersion, ringResult.DiagramRevision, ringResult.AuthoritativeDocument);
}
await ringWorkspaces.SaveAsync("ring");
var persistedRing = await ringProvider.GetRequiredService<IGraphWorkspaceRepository>().LoadAsync("ring")
    ?? throw new InvalidOperationException("The history-ring workspace did not save.");
using (var ringDocument = JsonDocument.Parse(persistedRing.GraphJson))
    Assert(ringDocument.RootElement.GetProperty("changes").GetArrayLength() == 0 &&
           ringDocument.RootElement.GetProperty("version").GetInt64() == ringSnapshot.GraphVersion,
        "Only unavailable complete history is omitted; the current semantic snapshot is still saved.");
Assert(await ringWorkspaces.DeleteAsync("ring"), "History-ring regression fixture is removed after verification.");

var webBuilder = WebApplication.CreateBuilder();
webBuilder.Services.AddSingleton<INodeTypeRegistry>(nodeTypes);
webBuilder.Services.AddGraphWorkspaceBridge(options => options.StorageDirectory = storageDirectory);
await using var app = webBuilder.Build();
app.MapGraphWorkspaceApi();
var routes = ((IEndpointRouteBuilder)app).DataSources
    .SelectMany(source => source.Endpoints)
    .OfType<RouteEndpoint>()
    .Select(endpoint => endpoint.RoutePattern.RawText)
    .ToHashSet(StringComparer.Ordinal);
Assert(routes.SetEquals([
        "/api/graph-workspaces/{workspaceId}",
        "/api/graph-workspaces/{workspaceId}/load",
        "/api/graph-workspaces/{workspaceId}/commands"
    ]),
    "The server must expose create/read/delete workspace and browser command routes.");

Console.WriteLine("PASS Bridge services resolve through Ghostagram.Server DI");
Console.WriteLine("PASS graph workspace snapshots project GraphStore plus presentation sidecar state");
Console.WriteLine($"PASS authoritative edge recovery {temporaryEdgeId} -> {connected.Id}");
Console.WriteLine("PASS stale proposals return authoritative recovery state");
Console.WriteLine("PASS bounded workspace lifecycle and API routes");
Console.WriteLine("PASS durable graph and presentation restart round trip");
Assert(await restartedWorkspaces.DeleteAsync("orders"), "Deleting a loaded workspace must remove its durable repository document.");
Assert(await restartedWorkspaces.LoadAsync("orders") is null, "A deleted durable workspace must not reload.");
if (Directory.Exists(storageDirectory)) Directory.Delete(storageDirectory, true);
Console.WriteLine("Ghostagram.Server graph workspace integration tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<T>(Action action, string message) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
