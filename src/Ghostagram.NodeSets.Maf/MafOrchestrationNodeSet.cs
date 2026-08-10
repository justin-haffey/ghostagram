using System.Text.Json;
using Ghostagram.Core;
using Ghostagram.Execution;

namespace Ghostagram.NodeSets.Maf;

/// <summary>
/// Framework-neutral authoring descriptors for Microsoft Agent Framework orchestration.
/// A future adapter package may bind these stable type IDs to MAF without adding MAF types
/// or packages to Ghostagram.Core, Ghostagram.Execution, or this node-set contract.
/// </summary>
public static class MafOrchestrationNodeSet
{
    public const string Id = "maf-orchestration";
    public const string DisplayName = "MAF Orchestration";
    public const string AdapterId = "microsoft-agent-framework";
    private static readonly DiagramNodeStyle DefaultStyle = new("#eef2ff", "#6366f1", "#1e1b4b", "left");

    public static NodeSetDescriptor Descriptor { get; } = new(
        Id,
        DisplayName,
        [
            Start(),
            Agent(),
            DataCapture(),
            Decision(),
            Parallel(),
            Join(),
            Handoff(),
            HumanInput(),
            Loop(),
            Complete()
        ],
        "Composable authoring nodes for future Microsoft Agent Framework adapters.");

    private static NodeTypeDescriptor Start() => new(
        "maf.start", 1, "Start", DisplayName, "mdi:play-circle-outline", 192, 96,
        properties:
        [
            Property("input", "Input", DiagramPropertyTypes.Json, DiagramPropertyModes.Edit, "Initial input", connectable: true)
        ],
        ports:
        [
            new("input", "target", PropertyId: "input", Label: "Input", Order: 0, MaxConnections: 1),
            new("next", "source", Label: "Next", Order: 1)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor Agent() => new(
        "maf.agent-component", 1, "Agent Component", DisplayName, "mdi:robot-outline", 224, 192,
        properties:
        [
            Property("componentKey", "Component key", label: "Component", required: true, description: "DI/component activation key used by the future adapter."),
            Property("instructions", "Instructions", mode: DiagramPropertyModes.Edit, label: "Instructions"),
            Property("input", "Input", DiagramPropertyTypes.Json, DiagramPropertyModes.DisplayAndEdit, "Input", connectable: true),
            Property("output", "Output", DiagramPropertyTypes.Json, DiagramPropertyModes.Display, "Output", connectable: true)
        ],
        ports:
        [
            new("input", "target", PropertyId: "input", Label: "Input", Order: 0),
            new("output", "source", PropertyId: "output", Label: "Output", Order: 1)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor DataCapture() => new(
        "maf.data-capture", 1, "Data Capture", DisplayName, "mdi:clipboard-text-outline", 224, 128,
        properties:
        [
            Property("field", "Field", label: "Field", required: true),
            Property("prompt", "Prompt", mode: DiagramPropertyModes.Edit, label: "Prompt"),
            Property("value", "Value", DiagramPropertyTypes.Json, DiagramPropertyModes.DisplayAndEdit, "Value", connectable: true)
        ],
        ports:
        [
            new("in", "target", Label: "In", Order: 0),
            new("value-in", "target", PropertyId: "value", Label: "Value", Order: 1),
            new("value-out", "source", PropertyId: "value", Label: "Value", Order: 2),
            new("next", "source", Label: "Next", Order: 3)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor Decision() => new(
        "maf.decision", 1, "Route / Decision", DisplayName, "mdi:source-branch", 208, 112,
        properties:
        [
            Property("expression", "Expression", mode: DiagramPropertyModes.Edit, label: "Condition", required: true),
            Property("context", "Context", DiagramPropertyTypes.Json, DiagramPropertyModes.Display, "Context", connectable: true)
        ],
        ports:
        [
            new("context", "target", PropertyId: "context", Label: "Context", Order: 0),
            new("true", "source", Label: "True", Order: 1),
            new("false", "source", Label: "False", Order: 2)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor Parallel() => new(
        "maf.parallel-split", 1, "Parallel Split", DisplayName, "mdi:source-fork", 192, 80,
        properties:
        [
            Property("maxConcurrency", "Maximum concurrency", DiagramPropertyTypes.Integer, DiagramPropertyModes.Edit, "Concurrency", defaultValue: Json("4"))
        ],
        ports:
        [
            new("in", "target", Label: "In", Order: 0),
            new("branches", "source", Label: "Branches", Order: 1),
            new("joined", "source", Label: "Joined", Order: 2)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor Join() => new(
        "maf.join", 1, "Join", DisplayName, "mdi:call-merge", 192, 80,
        properties:
        [
            Property("strategy", "Join strategy", DiagramPropertyTypes.Enum, DiagramPropertyModes.Edit, "Strategy", defaultValue: Json("\"all\""), options: ["all", "any", "quorum"])
        ],
        ports:
        [
            new("branches", "target", Label: "Branches", Order: 0),
            new("next", "source", Label: "Next", Order: 1)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor Handoff() => new(
        "maf.handoff", 1, "Handoff", DisplayName, "mdi:account-arrow-right-outline", 208, 112,
        properties:
        [
            Property("targetComponent", "Target component", label: "Target", required: true, connectable: true),
            Property("payload", "Payload", DiagramPropertyTypes.Json, DiagramPropertyModes.DisplayAndEdit, "Payload", connectable: true)
        ],
        ports:
        [
            new("payload", "target", PropertyId: "payload", Label: "Payload", Order: 0),
            new("target", "target", PropertyId: "targetComponent", Label: "Target", Order: 1),
            new("next", "source", Label: "Next", Order: 2)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor HumanInput() => new(
        "maf.human-input", 1, "Human Input", DisplayName, "mdi:account-edit-outline", 224, 160,
        properties:
        [
            Property("prompt", "Prompt", mode: DiagramPropertyModes.Edit, label: "Prompt", required: true),
            Property("schema", "Response schema", DiagramPropertyTypes.Json, DiagramPropertyModes.Edit, "Schema"),
            Property("response", "Response", DiagramPropertyTypes.Json, DiagramPropertyModes.Display, "Response", connectable: true)
        ],
        ports:
        [
            new("in", "target", Label: "In", Order: 0),
            new("response", "source", PropertyId: "response", Label: "Response", Order: 1)
        ], style: DefaultStyle);

    private static NodeTypeDescriptor Loop() => new(
        "maf.loop-guard", 1, "Loop Guard", DisplayName, "mdi:repeat", 208, 96,
        properties:
        [
            Property("maxIterations", "Maximum iterations", DiagramPropertyTypes.Integer, DiagramPropertyModes.Edit, "Limit", required: true, defaultValue: Json("3")),
            Property("condition", "Continue condition", mode: DiagramPropertyModes.Edit, label: "Continue while")
        ],
        ports:
        [
            new("in", "target", Label: "In", Order: 0),
            new("body", "source", Label: "Body", Order: 1),
            new("complete", "source", Label: "Complete", Order: 2)
        ], style: DefaultStyle, isLoopController: true);

    private static NodeTypeDescriptor Complete() => new(
        "maf.output", 1, "Output", DisplayName, "mdi:stop-circle-outline", 192, 96,
        properties:
        [
            Property("result", "Result", DiagramPropertyTypes.Json, DiagramPropertyModes.Display, "Result", connectable: true)
        ],
        ports:
        [
            new("result", "target", PropertyId: "result", Label: "Result", Order: 0)
        ], style: DefaultStyle);

    private static NodePropertyDefinition Property(
        string id,
        string name,
        string type = DiagramPropertyTypes.String,
        string mode = DiagramPropertyModes.Display,
        string? label = null,
        string? description = null,
        bool required = false,
        bool connectable = false,
        JsonElement? defaultValue = null,
        IReadOnlyList<string>? options = null) =>
        new(id, name, type, mode, label, description, required, connectable, defaultValue, options);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}
