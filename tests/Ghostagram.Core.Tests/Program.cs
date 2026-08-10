using Ghostagram.Core;
using System.Text.Json;

var document = DiagramBuilder.Create("test")
    .Group("stage", "Stage", 0, 0, 400, 240)
    .Node("start", "Start", 20, 40, groupId: "stage")
    .Port("start-out", "start", "right", direction: "source")
    .Node("review", "Review", 220, 40, groupId: "stage")
    .Port("review-in", "review", "left", direction: "target")
    .Edge("handoff", "start-out", "review-in", "handoff")
    .Build();

Assert(document.Nodes.Count == 2, "Builder preserves nodes.");
Assert(document.Groups.Single().Id == "stage", "Builder preserves groups.");
var operation = DiagramOperations.Upsert(document.Nodes.Single(node => node.Id == "start"));
Assert(operation.Type == "node.upsert", "Node upsert type is stable.");
Assert(operation.Value.GetProperty("label").GetString() == "Start", "Operation payload uses web JSON names.");
var stoppedEdge = DiagramOperations.Upsert(new DiagramEdge("edge", "start-out", "review-in", Animation: false));
Assert(stoppedEdge.Value.GetProperty("animation").ValueKind == System.Text.Json.JsonValueKind.False, "An explicit false animation value survives C# operation serialization.");
var clearedEdgeLabel = DiagramOperations.Upsert(new DiagramEdge("edge", "start-out", "review-in", Label: string.Empty));
Assert(clearedEdgeLabel.Value.GetProperty("label").GetString() == string.Empty, "An explicit empty edge label survives C# operation serialization and suppresses inherited text.");
Assert(DiagramOperations.Fit().Type == "viewport.fit", "Viewport factory is available.");
var groups = new[]
{
    new DiagramGroup("outer", 0, 0, 400, 240),
    new DiagramGroup("inner", 80, 40, 160, 120),
    new DiagramGroup("collapsed", 0, 0, 500, 500, Collapsed: true)
};
Assert(DiagramGroupMembership.ResolveGroupId(groups, 112, 72, 96, 64) == "inner", "A node dropped inside nested groups joins the smallest open group.");
Assert(DiagramGroupMembership.ResolveGroupId(groups, 300, 72, 64, 64) == "outer", "A node dropped in an outer group joins that group.");
Assert(DiagramGroupMembership.ResolveGroupId(groups, 480, 480, 64, 64) is null, "Collapsed groups do not accept dropped nodes.");

var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
const string legacyJson = """
{
  "documentId":"legacy",
  "nodes":[{"id":"n1","x":1,"y":2,"futureNodeFlag":{"enabled":true}}],
  "ports":[{"id":"p1","nodeId":"n1","futurePortMode":"stream"}],
  "edges":[],
  "futureDocumentVersion":"vNext"
}
""";
var legacy = JsonSerializer.Deserialize<DiagramDocument>(legacyJson, webJson) ?? throw new InvalidOperationException("Legacy document did not deserialize.");
Assert(legacy.Nodes.Single().Properties.Count == 0, "Legacy nodes default to no dynamic properties.");
Assert(legacy.Nodes.Single().TypeId is null && legacy.Nodes.Single().TypeVersion == 1, "Legacy nodes retain neutral type defaults.");
var legacyRoundTrip = JsonSerializer.Serialize(legacy, webJson);
using var legacyRoundTripJson = JsonDocument.Parse(legacyRoundTrip);
Assert(legacyRoundTripJson.RootElement.GetProperty("futureDocumentVersion").GetString() == "vNext", "Unknown document fields survive a typed round trip.");
Assert(legacyRoundTripJson.RootElement.GetProperty("nodes")[0].GetProperty("futureNodeFlag").GetProperty("enabled").GetBoolean(), "Unknown node fields survive a typed round trip.");
Assert(legacyRoundTripJson.RootElement.GetProperty("ports")[0].GetProperty("futurePortMode").GetString() == "stream", "Unknown port fields survive a typed round trip.");

var values = new[]
{
    Property("text", DiagramPropertyTypes.String, "\"hello\""),
    Property("flag", DiagramPropertyTypes.Boolean, "true"),
    Property("count", DiagramPropertyTypes.Integer, "42"),
    Property("amount", DiagramPropertyTypes.Decimal, "12.5"),
    Property("date", DiagramPropertyTypes.Date, "\"2026-08-08\""),
    Property("when", DiagramPropertyTypes.DateTime, "\"2026-08-08T12:00:00Z\""),
    Property("choice", DiagramPropertyTypes.Enum, "\"blue\""),
    Property("nothing", DiagramPropertyTypes.Json, "null"),
    Property("custom", "sample/customer", "{\"customerId\":17,\"tags\":[\"vip\"]}")
};
var typedNode = new DiagramNode("typed", 0, 0, TypeId: "sample.typed", Properties: values);
var typedRoundTrip = JsonSerializer.Deserialize<DiagramNode>(JsonSerializer.Serialize(typedNode, webJson), webJson)!;
Assert(typedRoundTrip.Properties.Count == values.Length, "All property values survive serialization.");
Assert(typedRoundTrip.Properties.Single(property => property.Id == "nothing").Value is null, "A JSON null remains a null property value after serialization.");
Assert(typedRoundTrip.Properties.Single(property => property.Id == "custom").Value?.GetProperty("customerId").GetInt32() == 17, "Custom JSON datatypes remain lossless.");
Console.WriteLine("Ghostagram.Core focused checks passed.");

static DiagramNodeProperty Property(string id, string type, string json) =>
    new(id, id, type, JsonDocument.Parse(json).RootElement.Clone());

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
