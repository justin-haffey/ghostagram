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
    height: 100,
    properties: [new("owner", "Owner", DiagramPropertyTypes.String)]);
var nodeTypes = new NodeTypeRegistry([new NodeSetDescriptor("server-tests", "Server tests", [descriptor])]);
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
var first = new DiagramNode(firstId.ToString(), 80, 100, 220, 100, "Receive order", TypeId: descriptor.TypeId,
    TypeVersion: descriptor.Version, Properties: [new("owner", "Owner", Value: System.Text.Json.JsonSerializer.SerializeToElement("operations"))]);
var second = new DiagramNode(secondId.ToString(), 420, 100, 220, 100, "Approve order", TypeId: descriptor.TypeId,
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
Assert(workspaces.Remove("orders"), "The live workspace must be removable without deleting its durable file.");

var restartedServices = new ServiceCollection();
restartedServices.AddSingleton<INodeTypeRegistry>(nodeTypes);
restartedServices.AddGraphWorkspaceBridge(options => { options.MaximumWorkspaces = 2; options.StorageDirectory = storageDirectory; });
await using var restartedProvider = restartedServices.BuildServiceProvider();
var restartedWorkspaces = restartedProvider.GetRequiredService<IGraphWorkspaceService>();
var reloaded = await restartedWorkspaces.LoadAsync("orders") ?? throw new InvalidOperationException("The persisted orders workspace was not loaded.");
Assert(reloaded.GraphVersion == saved.GraphVersion && reloaded.DiagramRevision == saved.DiagramRevision &&
       reloaded.Document.Nodes.Any(node => node.Id == first.Id && node.X == 80) &&
       reloaded.Document.Selection.SequenceEqual([first.Id]) &&
       reloaded.Document.Edges.Any(item => item.Id == connected.Id.ToString()),
    "A new service provider must reload stable semantics and presentation from the durable repository.");
var restartedConflict = new GraphDiagramCommand(saved.GraphVersion - 1, saved.DiagramRevision,
    ImmutableArray.Create(DiagramOperations.Select([])));
Assert(restartedWorkspaces.TryApply("orders", restartedConflict, out var recoveredAfterRestart) &&
       recoveredAfterRestart is { Accepted: false, Code: "GRAPH_VERSION_CONFLICT" } &&
       recoveredAfterRestart.AuthoritativeDocument.Selection.SequenceEqual([first.Id]),
    "Reloaded workspaces must retain authoritative conflict recovery.");

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
