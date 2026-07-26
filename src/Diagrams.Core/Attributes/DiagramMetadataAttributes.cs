using Diagrams.Core.Models;

namespace Diagrams.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DiagramStencilAttribute(
    string key,
    string displayName,
    TemplateKind templateKind,
    string category,
    string description) : Attribute
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;
    public TemplateKind TemplateKind { get; } = templateKind;
    public string Category { get; } = category;
    public string Description { get; } = description;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PortPresetAttribute(
    string id,
    string displayName,
    PortRole role,
    EndpointKind endpointKind) : Attribute
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public PortRole Role { get; } = role;
    public EndpointKind EndpointKind { get; } = endpointKind;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class LayoutCapabilityAttribute(
    LayoutKind layoutKind,
    string displayName,
    bool supportsGroups = true) : Attribute
{
    public LayoutKind LayoutKind { get; } = layoutKind;
    public string DisplayName { get; } = displayName;
    public bool SupportsGroups { get; } = supportsGroups;
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class InspectorFieldAttribute(
    string key,
    string label,
    InspectorFieldKind kind,
    int order = 0,
    string? placeholder = null,
    params string[] options) : Attribute
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public InspectorFieldKind Kind { get; } = kind;
    public int Order { get; } = order;
    public string? Placeholder { get; } = placeholder;
    public IReadOnlyList<string> Options { get; } = options;
}

