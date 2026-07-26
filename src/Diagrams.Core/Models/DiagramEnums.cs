using System.Text.Json.Serialization;

namespace Diagrams.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TemplateKind
{
    Flowchart,
    OrgChart,
    UmlClass,
    UmlSequence,
    Erd,
    C4Context,
    BpmnLite,
    NetworkTopology,
    MindMap,
    DataFlow,
    StateDependency
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortSide
{
    Top,
    Right,
    Bottom,
    Left,
    Center
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortRole
{
    Input,
    Output,
    Bidirectional,
    Event,
    Association,
    Dependency,
    Aggregation,
    Composition,
    Inheritance
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EndpointKind
{
    Dot,
    Rectangle,
    Blank
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConnectorKind
{
    Flowchart,
    Straight,
    Bezier,
    StateMachine
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MarkerKind
{
    None,
    Arrow,
    Diamond,
    HollowDiamond,
    Triangle
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EdgeAnimationKind
{
    None,
    Flow,
    Pulse,
    ReverseFlow
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LayoutKind
{
    Freeform,
    HierarchyTopDown,
    HierarchyLeftRight,
    Tree,
    Grid,
    Radial,
    MindMap
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InspectorFieldKind
{
    Text,
    Multiline,
    Number,
    Select,
    Toggle,
    Color
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommentStatus
{
    Open,
    Resolved
}
