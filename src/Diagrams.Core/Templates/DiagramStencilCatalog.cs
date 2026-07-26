using System.Collections.Immutable;
using System.Reflection;
using Diagrams.Core.Attributes;
using Diagrams.Core.Models;

namespace Diagrams.Core.Templates;

public sealed record PortBlueprint(
    string Key,
    PortSide Side,
    PortRole Role,
    EndpointKind EndpointKind,
    string Anchor,
    int MaxConnections,
    string Label);

public sealed record InspectorFieldDefinition(
    string Key,
    string Label,
    InspectorFieldKind Kind,
    int Order,
    string? Placeholder,
    IReadOnlyList<string> Options);

public sealed record StencilDefinition(
    string Key,
    string DisplayName,
    TemplateKind TemplateKind,
    string Category,
    string Description,
    string StyleToken,
    DiagramBounds DefaultBounds,
    ImmutableDictionary<string, string?> DefaultProperties,
    ImmutableArray<PortBlueprint> Ports,
    Type PropertySchemaType,
    ImmutableArray<InspectorFieldDefinition> InspectorFields);

public sealed record PortPresetDefinition(
    string Id,
    string DisplayName,
    PortRole Role,
    EndpointKind EndpointKind,
    EdgeMarkers Markers);

public interface IDiagramStencilDefinition
{
    string StyleToken { get; }
    DiagramBounds DefaultBounds { get; }
    Type PropertySchemaType { get; }
    ImmutableDictionary<string, string?> DefaultProperties { get; }
    ImmutableArray<PortBlueprint> Ports { get; }
}

public interface IPortPresetDefinition
{
    EdgeMarkers Markers { get; }
}

public sealed class DiagramStencilCatalog
{
    private readonly ImmutableDictionary<string, StencilDefinition> _definitions;

    public DiagramStencilCatalog()
    {
        _definitions = Assembly.GetExecutingAssembly()
            .DefinedTypes
            .Where(type => !type.IsAbstract && typeof(IDiagramStencilDefinition).IsAssignableFrom(type))
            .Select(type =>
            {
                var attribute = type.GetCustomAttribute<DiagramStencilAttribute>()
                    ?? throw new InvalidOperationException($"Stencil type '{type.Name}' is missing DiagramStencilAttribute.");
                var instance = (IDiagramStencilDefinition)Activator.CreateInstance(type.AsType())!;
                return new StencilDefinition(
                    attribute.Key,
                    attribute.DisplayName,
                    attribute.TemplateKind,
                    attribute.Category,
                    attribute.Description,
                    instance.StyleToken,
                    instance.DefaultBounds,
                    instance.DefaultProperties,
                    instance.Ports,
                    instance.PropertySchemaType,
                    GetInspectorFields(instance.PropertySchemaType));
            })
            .ToImmutableDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<StencilDefinition> GetAll()
        => _definitions.Values
            .OrderBy(definition => definition.Category)
            .ThenBy(definition => definition.DisplayName)
            .ToImmutableArray();

    public StencilDefinition Get(string key)
        => _definitions.TryGetValue(key, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Stencil '{key}' is not registered.");

    public (DiagramNode Node, ImmutableArray<DiagramPort> Ports) CreateInstance(
        string stencilKey,
        double x,
        double y,
        string? label = null,
        string? groupId = null)
    {
        var definition = Get(stencilKey);
        var nodeId = $"node-{Guid.NewGuid():N}";
        var ports = definition.Ports.Select((port, index) => new DiagramPort(
                Id: $"{nodeId}:{port.Key}:{index}",
                NodeId: nodeId,
                Side: port.Side,
                Role: port.Role,
                EndpointKind: port.EndpointKind,
                Anchor: port.Anchor,
                MaxConnections: port.MaxConnections,
                Label: port.Label))
            .ToImmutableArray();

        var bounds = definition.DefaultBounds with { X = x, Y = y };
        var node = new DiagramNode(
            nodeId,
            stencilKey,
            bounds,
            label ?? definition.DisplayName,
            ports.Select(port => port.Id).ToImmutableArray(),
            groupId,
            definition.DefaultProperties,
            definition.StyleToken);

        return (node, ports);
    }

    public IReadOnlyList<InspectorFieldDefinition> GetInspectorFields(string stencilKey)
        => Get(stencilKey).InspectorFields;

    private static ImmutableArray<InspectorFieldDefinition> GetInspectorFields(Type propertySchemaType)
        => propertySchemaType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<InspectorFieldAttribute>()))
            .Where(entry => entry.Attribute is not null)
            .Select(entry => new InspectorFieldDefinition(
                entry.Attribute!.Key,
                entry.Attribute.Label,
                entry.Attribute.Kind,
                entry.Attribute.Order,
                entry.Attribute.Placeholder,
                entry.Attribute.Options))
            .OrderBy(field => field.Order)
            .ToImmutableArray();
}

public sealed class PortPresetCatalog
{
    private readonly ImmutableDictionary<string, PortPresetDefinition> _presets;

    public PortPresetCatalog()
    {
        _presets = Assembly.GetExecutingAssembly()
            .DefinedTypes
            .Where(type => !type.IsAbstract && typeof(IPortPresetDefinition).IsAssignableFrom(type))
            .Select(type =>
            {
                var attribute = type.GetCustomAttribute<PortPresetAttribute>()
                    ?? throw new InvalidOperationException($"Port preset '{type.Name}' is missing PortPresetAttribute.");
                var instance = (IPortPresetDefinition)Activator.CreateInstance(type.AsType())!;
                return new PortPresetDefinition(
                    attribute.Id,
                    attribute.DisplayName,
                    attribute.Role,
                    attribute.EndpointKind,
                    instance.Markers);
            })
            .ToImmutableDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PortPresetDefinition> GetAll()
        => _presets.Values.OrderBy(preset => preset.DisplayName).ToImmutableArray();
}

public sealed class BasicNodeSchema
{
    [InspectorField("summary", "Summary", InspectorFieldKind.Multiline, order: 1, placeholder: "Describe the node")]
    public string Summary { get; init; } = string.Empty;

    [InspectorField("owner", "Owner", InspectorFieldKind.Text, order: 2, placeholder: "Team or persona")]
    public string Owner { get; init; } = string.Empty;
}

public sealed class DataNodeSchema
{
    [InspectorField("entity", "Entity", InspectorFieldKind.Text, order: 1, placeholder: "Table or entity name")]
    public string Entity { get; init; } = string.Empty;

    [InspectorField("keyField", "Key Field", InspectorFieldKind.Text, order: 2, placeholder: "Primary key or identifier")]
    public string KeyField { get; init; } = string.Empty;
}

public sealed class NetworkNodeSchema
{
    [InspectorField("address", "Address", InspectorFieldKind.Text, order: 1, placeholder: "IP or hostname")]
    public string Address { get; init; } = string.Empty;

    [InspectorField("segment", "Segment", InspectorFieldKind.Text, order: 2, placeholder: "Network zone")]
    public string Segment { get; init; } = string.Empty;
}

public sealed class SequenceNodeSchema
{
    [InspectorField("participant", "Participant", InspectorFieldKind.Text, order: 1, placeholder: "Role or system")]
    public string Participant { get; init; } = string.Empty;

    [InspectorField("timeline", "Timeline", InspectorFieldKind.Text, order: 2, placeholder: "Phase or swimlane")]
    public string Timeline { get; init; } = string.Empty;
}

public sealed class FlowNodeSchema
{
    [InspectorField("state", "State", InspectorFieldKind.Select, order: 1, placeholder: null, "Draft", "Review", "Approved", "Blocked")]
    public string State { get; init; } = "Draft";

    [InspectorField("notes", "Notes", InspectorFieldKind.Multiline, order: 2, placeholder: "Operational notes")]
    public string Notes { get; init; } = string.Empty;
}

[DiagramStencil("process", "Process", TemplateKind.Flowchart, "Flow", "General process or task step.")]
public sealed class ProcessStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "default";
    public DiagramBounds DefaultBounds => new(0, 0, 180, 84);
    public Type PropertySchemaType => typeof(FlowNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("state", "Draft")
        .Add("notes", string.Empty);
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("in", PortSide.Left, PortRole.Input, EndpointKind.Dot, "Left", 8, "Input"),
        new("out", PortSide.Right, PortRole.Output, EndpointKind.Dot, "Right", 8, "Output"),
        new("event", PortSide.Bottom, PortRole.Event, EndpointKind.Dot, "Bottom", 6, "Signal")
    ];
}

[DiagramStencil("decision", "Decision", TemplateKind.Flowchart, "Flow", "Decision or branch point.")]
public sealed class DecisionStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "signal";
    public DiagramBounds DefaultBounds => new(0, 0, 160, 100);
    public Type PropertySchemaType => typeof(FlowNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("state", "Review")
        .Add("notes", string.Empty);
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("top", PortSide.Top, PortRole.Input, EndpointKind.Dot, "Top", 6, "Entry"),
        new("right", PortSide.Right, PortRole.Output, EndpointKind.Dot, "Right", 6, "Yes"),
        new("bottom", PortSide.Bottom, PortRole.Output, EndpointKind.Dot, "Bottom", 6, "No"),
        new("left", PortSide.Left, PortRole.Dependency, EndpointKind.Dot, "Left", 6, "Return")
    ];
}

[DiagramStencil("actor", "Actor", TemplateKind.OrgChart, "People", "Person, role, or external actor.")]
public sealed class ActorStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "accent";
    public DiagramBounds DefaultBounds => new(0, 0, 170, 92);
    public Type PropertySchemaType => typeof(BasicNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("summary", string.Empty)
        .Add("owner", string.Empty);
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("top", PortSide.Top, PortRole.Input, EndpointKind.Dot, "Top", 6, "Manager"),
        new("bottom", PortSide.Bottom, PortRole.Output, EndpointKind.Dot, "Bottom", 12, "Reports")
    ];
}

[DiagramStencil("service", "Service", TemplateKind.UmlClass, "Systems", "Application, service, or component.")]
public sealed class ServiceStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "ink";
    public DiagramBounds DefaultBounds => new(0, 0, 210, 108);
    public Type PropertySchemaType => typeof(BasicNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("summary", "Component responsibility")
        .Add("owner", "Platform");
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("dependency", PortSide.Top, PortRole.Dependency, EndpointKind.Rectangle, "Top", 8, "Dependency"),
        new("input", PortSide.Left, PortRole.Input, EndpointKind.Rectangle, "Left", 16, "Inbound"),
        new("output", PortSide.Right, PortRole.Output, EndpointKind.Rectangle, "Right", 16, "Outbound"),
        new("event", PortSide.Bottom, PortRole.Event, EndpointKind.Dot, "Bottom", 12, "Event")
    ];
}

[DiagramStencil("database", "Database", TemplateKind.Erd, "Data", "Persistent data store or entity.")]
public sealed class DatabaseStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "copper";
    public DiagramBounds DefaultBounds => new(0, 0, 200, 102);
    public Type PropertySchemaType => typeof(DataNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("entity", string.Empty)
        .Add("keyField", "Id");
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("left", PortSide.Left, PortRole.Association, EndpointKind.Rectangle, "Left", 12, "Relation"),
        new("right", PortSide.Right, PortRole.Association, EndpointKind.Rectangle, "Right", 12, "Relation")
    ];
}

[DiagramStencil("storage", "Storage", TemplateKind.DataFlow, "Data", "Queue, file store, or document repository.")]
public sealed class StorageStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "default";
    public DiagramBounds DefaultBounds => new(0, 0, 190, 92);
    public Type PropertySchemaType => typeof(DataNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("entity", "Payload")
        .Add("keyField", "Partition");
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("left", PortSide.Left, PortRole.Input, EndpointKind.Dot, "Left", 12, "Read"),
        new("right", PortSide.Right, PortRole.Output, EndpointKind.Dot, "Right", 12, "Write")
    ];
}

[DiagramStencil("event", "Event", TemplateKind.StateDependency, "Events", "Event, transition, or signal.")]
public sealed class EventStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "accent";
    public DiagramBounds DefaultBounds => new(0, 0, 150, 76);
    public Type PropertySchemaType => typeof(BasicNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("summary", "Trigger")
        .Add("owner", string.Empty);
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("top", PortSide.Top, PortRole.Event, EndpointKind.Dot, "Top", 12, "Listen"),
        new("bottom", PortSide.Bottom, PortRole.Event, EndpointKind.Dot, "Bottom", 12, "Emit")
    ];
}

[DiagramStencil("device", "Device", TemplateKind.NetworkTopology, "Systems", "Server, appliance, or device.")]
public sealed class DeviceStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "ink";
    public DiagramBounds DefaultBounds => new(0, 0, 180, 88);
    public Type PropertySchemaType => typeof(NetworkNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("address", string.Empty)
        .Add("segment", "Core");
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("input", PortSide.Top, PortRole.Input, EndpointKind.Rectangle, "Top", 12, "Input"),
        new("left", PortSide.Left, PortRole.Bidirectional, EndpointKind.Rectangle, "Left", 20, "Ingress"),
        new("right", PortSide.Right, PortRole.Bidirectional, EndpointKind.Rectangle, "Right", 20, "Egress")
    ];
}

[DiagramStencil("container", "Container", TemplateKind.C4Context, "Architecture", "Container, boundary, or deployment unit.")]
public sealed class ContainerStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "accent";
    public DiagramBounds DefaultBounds => new(0, 0, 220, 110);
    public Type PropertySchemaType => typeof(BasicNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("summary", "Container responsibility")
        .Add("owner", "Architecture");
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("input", PortSide.Left, PortRole.Input, EndpointKind.Rectangle, "Left", 16, "Inbound"),
        new("output", PortSide.Right, PortRole.Output, EndpointKind.Rectangle, "Right", 16, "Outbound"),
        new("event", PortSide.Bottom, PortRole.Event, EndpointKind.Dot, "Bottom", 8, "Event")
    ];
}

[DiagramStencil("persona", "Persona", TemplateKind.C4Context, "Architecture", "User, role, or external actor.")]
public sealed class PersonaStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "signal";
    public DiagramBounds DefaultBounds => new(0, 0, 170, 88);
    public Type PropertySchemaType => typeof(BasicNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("summary", "Audience")
        .Add("owner", string.Empty);
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("out", PortSide.Right, PortRole.Output, EndpointKind.Dot, "Right", 12, "Interaction"),
        new("in", PortSide.Left, PortRole.Input, EndpointKind.Dot, "Left", 12, "Feedback")
    ];
}

[DiagramStencil("gateway", "Gateway", TemplateKind.BpmnLite, "Workflow", "Gateway, decision, or approval checkpoint.")]
public sealed class GatewayStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "signal";
    public DiagramBounds DefaultBounds => new(0, 0, 170, 98);
    public Type PropertySchemaType => typeof(FlowNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("state", "Review")
        .Add("notes", string.Empty);
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("input", PortSide.Left, PortRole.Input, EndpointKind.Dot, "Left", 12, "Inbound"),
        new("success", PortSide.Right, PortRole.Output, EndpointKind.Dot, "Right", 12, "Approved"),
        new("return", PortSide.Bottom, PortRole.Dependency, EndpointKind.Dot, "Bottom", 8, "Return")
    ];
}

[DiagramStencil("lifeline", "Lifeline", TemplateKind.UmlSequence, "Sequence", "Participant lifeline for sequence interactions.")]
public sealed class LifelineStencilDefinition : IDiagramStencilDefinition
{
    public string StyleToken => "ink";
    public DiagramBounds DefaultBounds => new(0, 0, 170, 280);
    public Type PropertySchemaType => typeof(SequenceNodeSchema);
    public ImmutableDictionary<string, string?> DefaultProperties => ImmutableDictionary<string, string?>.Empty
        .Add("participant", "Participant")
        .Add("timeline", "Main");
    public ImmutableArray<PortBlueprint> Ports =>
    [
        new("left", PortSide.Left, PortRole.Input, EndpointKind.Dot, "ContinuousLeft", 30, "Inbound"),
        new("right", PortSide.Right, PortRole.Output, EndpointKind.Dot, "ContinuousRight", 30, "Outbound"),
        new("event", PortSide.Bottom, PortRole.Event, EndpointKind.Dot, "Bottom", 8, "Signal")
    ];
}

[PortPreset("input", "Input", PortRole.Input, EndpointKind.Dot)]
public sealed class InputPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.Arrow);
}

[PortPreset("output", "Output", PortRole.Output, EndpointKind.Dot)]
public sealed class OutputPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.Arrow);
}

[PortPreset("bidirectional", "Bidirectional", PortRole.Bidirectional, EndpointKind.Rectangle)]
public sealed class BidirectionalPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.Arrow, MarkerKind.Arrow);
}

[PortPreset("event", "Event", PortRole.Event, EndpointKind.Dot)]
public sealed class EventPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.Triangle);
}

[PortPreset("association", "Association", PortRole.Association, EndpointKind.Rectangle)]
public sealed class AssociationPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.None);
}

[PortPreset("dependency", "Dependency", PortRole.Dependency, EndpointKind.Dot)]
public sealed class DependencyPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.Arrow);
}

[PortPreset("aggregation", "Aggregation", PortRole.Aggregation, EndpointKind.Rectangle)]
public sealed class AggregationPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.HollowDiamond);
}

[PortPreset("composition", "Composition", PortRole.Composition, EndpointKind.Rectangle)]
public sealed class CompositionPortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.Diamond);
}

[PortPreset("inheritance", "Inheritance", PortRole.Inheritance, EndpointKind.Rectangle)]
public sealed class InheritancePortPresetDefinition : IPortPresetDefinition
{
    public EdgeMarkers Markers => new(MarkerKind.None, MarkerKind.Triangle);
}
