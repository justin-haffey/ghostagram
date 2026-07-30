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
Assert(DiagramOperations.Fit().Type == "viewport.fit", "Viewport factory is available.");
Console.WriteLine("Ghostagram.Core focused checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
