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
Assert(legacy.Nodes.Single().Sections is null && legacy.Nodes.Single().Presentation is null, "Legacy nodes remain simple when advanced presentation fields are absent.");
Assert(legacy.Nodes.Single().RendererKey is null && legacy.Nodes.Single().RendererVersion is null, "Legacy nodes default to the standard renderer.");
var legacyRoundTrip = JsonSerializer.Serialize(legacy, webJson);
using var legacyRoundTripJson = JsonDocument.Parse(legacyRoundTrip);
Assert(legacyRoundTripJson.RootElement.GetProperty("futureDocumentVersion").GetString() == "vNext", "Unknown document fields survive a typed round trip.");
Assert(legacyRoundTripJson.RootElement.GetProperty("nodes")[0].GetProperty("futureNodeFlag").GetProperty("enabled").GetBoolean(), "Unknown node fields survive a typed round trip.");
Assert(legacyRoundTripJson.RootElement.GetProperty("ports")[0].GetProperty("futurePortMode").GetString() == "stream", "Unknown port fields survive a typed round trip.");
Assert(!legacyRoundTripJson.RootElement.GetProperty("nodes")[0].TryGetProperty("sections", out _) &&
       !legacyRoundTripJson.RootElement.GetProperty("nodes")[0].TryGetProperty("presentation", out _) &&
       !legacyRoundTripJson.RootElement.GetProperty("nodes")[0].TryGetProperty("rendererKey", out _) &&
       !legacyRoundTripJson.RootElement.GetProperty("nodes")[0].TryGetProperty("rendererVersion", out _),
    "Legacy simple-node JSON does not acquire advanced presentation or renderer fields on round trip.");

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

var customRenderedNode = new DiagramNode("custom-rendered", 0, 0, RendererKey: "test.card", RendererVersion: 2);
var customRenderedJson = JsonSerializer.Serialize(customRenderedNode, webJson);
using (var customRenderedShape = JsonDocument.Parse(customRenderedJson))
{
    Assert(customRenderedShape.RootElement.GetProperty("rendererKey").GetString() == "test.card", "Renderer keys use the stable web JSON field name.");
    Assert(customRenderedShape.RootElement.GetProperty("rendererVersion").GetInt32() == 2, "Renderer versions survive serialization.");
}
var customRenderedRoundTrip = JsonSerializer.Deserialize<DiagramNode>(customRenderedJson, webJson)!;
Assert(customRenderedRoundTrip.RendererKey == "test.card" && customRenderedRoundTrip.RendererVersion == 2, "Custom renderer identity survives a typed round trip.");

var advancedNode = new DiagramNode(
    "advanced", 10, 20, 280, 196, "Advanced",
    Properties:
    [
        new("name", "Name", SectionId: "identity", Editor: new(DiagramPropertyEditorKinds.Text, "Customer name")),
        new("confidence", "Confidence", DiagramPropertyTypes.Decimal, SectionId: "tuning", Editor: new(DiagramPropertyEditorKinds.Range, Minimum: 0, Maximum: 1, Step: 0.05m))
    ],
    Sections:
    [
        new("identity", "Identity", Order: 10),
        new("preferences", "Preferences", Order: 20),
        new("tuning", "Tuning", "preferences", 10)
    ],
    Presentation: new(DiagramNodeDisplayModes.Expanded, 196, ["tuning"]));
var advancedJson = JsonSerializer.Serialize(advancedNode, webJson);
using (var advancedShape = JsonDocument.Parse(advancedJson))
{
    var root = advancedShape.RootElement;
    Assert(root.GetProperty("sections")[2].GetProperty("parentSectionId").GetString() == "preferences", "Nested sections use flat parentSectionId references in camel-case JSON.");
    Assert(root.GetProperty("properties")[1].GetProperty("sectionId").GetString() == "tuning", "Properties serialize their section reference.");
    Assert(root.GetProperty("properties")[1].GetProperty("editor").GetProperty("kind").GetString() == "range", "Typed editor hints serialize in the property contract.");
    Assert(root.GetProperty("presentation").GetProperty("collapsedSectionIds")[0].GetString() == "tuning", "Collapsed section state is persisted on node presentation.");
}
var advancedRoundTrip = JsonSerializer.Deserialize<DiagramNode>(advancedJson, webJson)!;
Assert(advancedRoundTrip.Sections!.Count == 3 && advancedRoundTrip.Presentation!.ExpandedHeight == 196, "Advanced node sections and authoritative geometry survive a typed round trip.");
Console.WriteLine("Ghostagram.Core focused checks passed.");

static DiagramNodeProperty Property(string id, string type, string json) =>
    new(id, id, type, JsonDocument.Parse(json).RootElement.Clone());

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
