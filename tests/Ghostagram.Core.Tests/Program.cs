using Ghostagram.Core;

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
Console.WriteLine("Ghostagram.Core focused checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
