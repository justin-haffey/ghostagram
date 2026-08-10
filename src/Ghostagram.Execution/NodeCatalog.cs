using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text.Json;
using Ghostagram.Core;

namespace Ghostagram.Execution;

public readonly record struct NodeTypeKey(string TypeId, int Version)
{
    public override string ToString() => $"{TypeId}@{Version}";
}

public sealed record NodePropertyDefinition(
    string Id,
    string Name,
    string Type = DiagramPropertyTypes.String,
    string Mode = DiagramPropertyModes.Display,
    string? Label = null,
    string? Description = null,
    bool Required = false,
    bool Connectable = false,
    JsonElement? DefaultValue = null,
    IReadOnlyList<string>? Options = null,
    JsonElement? Metadata = null,
    string? SectionId = null,
    DiagramPropertyEditor? Editor = null);

public sealed record NodeSectionDefinition(
    string Id,
    string Title,
    string? ParentSectionId = null,
    int Order = 0,
    bool Collapsible = true);

public sealed record NodePortDefinition(
    string Id,
    string Direction = "both",
    string Scope = "*",
    string? PropertyId = null,
    string? Label = null,
    int Order = 0,
    int MaxConnections = -1,
    string? Anchor = null);

public sealed class NodeTypeDescriptor
{
    public NodeTypeDescriptor(
        string typeId,
        int version,
        string displayName,
        string paletteGroup,
        string? icon = null,
        double width = 208,
        double height = 112,
        IEnumerable<NodePropertyDefinition>? properties = null,
        IEnumerable<NodePortDefinition>? ports = null,
        IReadOnlyDictionary<string, JsonElement>? metadata = null,
        DiagramNodeStyle? style = null,
        bool isLoopController = false,
        IEnumerable<NodeSectionDefinition>? sections = null,
        DiagramNodePresentation? presentation = null)
    {
        if (string.IsNullOrWhiteSpace(typeId)) throw new ArgumentException("A node type identifier is required.", nameof(typeId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "A node type version must be positive.");
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(paletteGroup)) throw new ArgumentException("A palette group is required.", nameof(paletteGroup));

        TypeId = typeId;
        Version = version;
        DisplayName = displayName;
        PaletteGroup = paletteGroup;
        Icon = icon;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Node dimensions must be finite and positive.");
        Width = width;
        Height = height;
        Style = style;
        IsLoopController = isLoopController;
        Properties = Array.AsReadOnly((properties ?? []).Select(CloneProperty).ToArray());
        Ports = Array.AsReadOnly((ports ?? []).Select(port => port with { }).ToArray());
        Sections = Array.AsReadOnly((sections ?? []).Select(section => section with { }).ToArray());
        Presentation = ClonePresentation(presentation, Sections.Count > 0 ? height : null);
        Metadata = metadata is null
            ? FrozenDictionary<string, JsonElement>.Empty
            : metadata.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);

        EnsureUnique(Properties.Select(property => property.Id), "property");
        EnsureUnique(Ports.Select(port => port.Id), "port");
        EnsureUnique(Sections.Select(section => section.Id), "section");
        ValidateSections(Sections);
        foreach (var property in Properties)
        {
            if (string.IsNullOrWhiteSpace(property.Id) || string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(property.Type))
                throw new ArgumentException("Node property identifiers, names, and types are required.", nameof(properties));
            if (property.Mode is not (DiagramPropertyModes.Display or DiagramPropertyModes.Edit or DiagramPropertyModes.DisplayAndEdit or DiagramPropertyModes.Hidden))
                throw new ArgumentException($"Property '{property.Id}' has invalid mode '{property.Mode}'.", nameof(properties));
            if (property.Type == DiagramPropertyTypes.Enum && property.Options is not { Count: > 0 })
                throw new ArgumentException($"Enum property '{property.Id}' requires at least one option.", nameof(properties));
            if (property.Type == DiagramPropertyTypes.Enum && property.Options!.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException($"Enum property '{property.Id}' has a blank option.", nameof(properties));
            if (property.Type == DiagramPropertyTypes.Enum && property.Options!.Distinct(StringComparer.Ordinal).Count() != property.Options!.Count)
                throw new ArgumentException($"Enum property '{property.Id}' has duplicate options.", nameof(properties));
            if (!PropertyValueRules.IsCompatible(property.Type, property.DefaultValue, property.Options, out var reason))
                throw new ArgumentException($"Default for property '{property.Id}' is invalid. {reason}", nameof(properties));
            if (property.SectionId is not null && Sections.All(section => section.Id != property.SectionId))
                throw new ArgumentException($"Property '{property.Id}' references unknown section '{property.SectionId}'.", nameof(properties));
            ValidateEditor(property);
        }
        var minimumHeight = MinimumExpandedHeight(Properties, Sections);
        var expandedHeight = Presentation?.ExpandedHeight ?? height;
        if (expandedHeight < minimumHeight)
            throw new ArgumentOutOfRangeException(nameof(height), $"Node height must be at least {minimumHeight}px for its visible property rows.");
        foreach (var port in Ports)
        {
            if (string.IsNullOrWhiteSpace(port.Id) || string.IsNullOrWhiteSpace(port.Scope))
                throw new ArgumentException("Node port identifiers and scopes are required.", nameof(ports));
            if (port.Direction is not ("source" or "target" or "both"))
                throw new ArgumentException($"Port '{port.Id}' has invalid direction '{port.Direction}'.", nameof(ports));
            if (port.Anchor is not (null or "left" or "right" or "top" or "bottom"))
                throw new ArgumentException($"Port '{port.Id}' has invalid fixed anchor '{port.Anchor}'.", nameof(ports));
            if (port.MaxConnections < -1) throw new ArgumentException($"Port '{port.Id}' has invalid MaxConnections '{port.MaxConnections}'.", nameof(ports));
            if (port.Order < 0) throw new ArgumentException($"Port '{port.Id}' has a negative order.", nameof(ports));
        }
        var propertyIds = Properties.Select(property => property.Id).ToHashSet(StringComparer.Ordinal);
        var invalidPort = Ports.FirstOrDefault(port => port.PropertyId is not null && !propertyIds.Contains(port.PropertyId));
        if (invalidPort is not null)
            throw new ArgumentException($"Port '{invalidPort.Id}' references unknown property '{invalidPort.PropertyId}'.", nameof(ports));
        var nonConnectablePort = Ports.FirstOrDefault(port => port.PropertyId is not null && !Properties.Single(property => property.Id == port.PropertyId).Connectable);
        if (nonConnectablePort is not null)
            throw new ArgumentException($"Port '{nonConnectablePort.Id}' references non-connectable property '{nonConnectablePort.PropertyId}'.", nameof(ports));
        ValidatePresentation(Presentation, Sections);
    }

    public string TypeId { get; }
    public int Version { get; }
    public string DisplayName { get; }
    public string PaletteGroup { get; }
    public string? Icon { get; }
    public double Width { get; }
    public double Height { get; }
    public DiagramNodeStyle? Style { get; }
    public bool IsLoopController { get; }
    public IReadOnlyList<NodePropertyDefinition> Properties { get; }
    public IReadOnlyList<NodePortDefinition> Ports { get; }
    public IReadOnlyList<NodeSectionDefinition> Sections { get; }
    public DiagramNodePresentation? Presentation { get; }
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; }
    public NodeTypeKey Key => new(TypeId, Version);

    private static void EnsureUnique(IEnumerable<string> ids, string kind)
    {
        var duplicate = ids.GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new ArgumentException($"Duplicate node {kind} id '{duplicate.Key}'.");
    }

    private static NodePropertyDefinition CloneProperty(NodePropertyDefinition property) => property with
    {
        DefaultValue = property.DefaultValue?.Clone(),
        Metadata = property.Metadata?.Clone(),
        Options = property.Options is null ? null : Array.AsReadOnly(property.Options.ToArray()),
        Editor = CloneEditor(property.Editor)
    };

    private static DiagramNodePresentation? ClonePresentation(DiagramNodePresentation? presentation, double? defaultExpandedHeight)
    {
        if (presentation is null && defaultExpandedHeight is null) return null;
        presentation ??= new();
        return presentation with
        {
            ExpandedHeight = presentation.ExpandedHeight ?? defaultExpandedHeight,
            CollapsedSectionIds = presentation.CollapsedSectionIds is null
                ? Array.Empty<string>()
                : Array.AsReadOnly(presentation.CollapsedSectionIds.ToArray()),
            ExtensionData = CloneExtensionData(presentation.ExtensionData)
        };
    }

    private static double MinimumExpandedHeight(
        IReadOnlyList<NodePropertyDefinition> properties,
        IReadOnlyList<NodeSectionDefinition> sections)
    {
        var visible = properties.Where(property => property.Mode != DiagramPropertyModes.Hidden).ToArray();
        if (visible.Length == 0 && sections.Count == 0) return 30;
        var rowHeight = (NodePropertyDefinition property) =>
        {
            var kind = property.Editor?.Kind is { } explicitKind and not DiagramPropertyEditorKinds.Auto
                ? explicitKind
                : property.Type switch
                {
                    DiagramPropertyTypes.Boolean => DiagramPropertyEditorKinds.Toggle,
                    DiagramPropertyTypes.Integer or DiagramPropertyTypes.Decimal => DiagramPropertyEditorKinds.Number,
                    DiagramPropertyTypes.Date => DiagramPropertyEditorKinds.Date,
                    DiagramPropertyTypes.DateTime => DiagramPropertyEditorKinds.DateTime,
                    DiagramPropertyTypes.Enum => DiagramPropertyEditorKinds.Select,
                    DiagramPropertyTypes.Json => DiagramPropertyEditorKinds.Json,
                    _ => DiagramPropertyEditorKinds.Text
                };
            return kind is DiagramPropertyEditorKinds.Multiline or DiagramPropertyEditorKinds.Json ? 48d
                : kind == DiagramPropertyEditorKinds.Range ? 28d
                : 20d;
        };
        return 30 + sections.Count * 23 + visible.Sum(property => rowHeight(property) + 1) + 7;
    }

    private static DiagramPropertyEditor? CloneEditor(DiagramPropertyEditor? editor) => editor is null
        ? null
        : editor with { ExtensionData = CloneExtensionData(editor.ExtensionData) };

    private static IDictionary<string, JsonElement>? CloneExtensionData(IDictionary<string, JsonElement>? extensionData) => extensionData is null
        ? null
        : new ReadOnlyDictionary<string, JsonElement>(extensionData.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal));

    private static void ValidateSections(IReadOnlyList<NodeSectionDefinition> sections)
    {
        const int maximumDepth = 8;
        var byId = sections.ToDictionary(section => section.Id, StringComparer.Ordinal);
        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.Id) || string.IsNullOrWhiteSpace(section.Title))
                throw new ArgumentException("Node section identifiers and titles are required.", nameof(sections));
            if (section.Order < 0)
                throw new ArgumentException($"Section '{section.Id}' has a negative order.", nameof(sections));
            if (section.ParentSectionId is not null && !byId.ContainsKey(section.ParentSectionId))
                throw new ArgumentException($"Section '{section.Id}' references unknown parent section '{section.ParentSectionId}'.", nameof(sections));

            var visited = new HashSet<string>(StringComparer.Ordinal) { section.Id };
            var current = section;
            var depth = 1;
            while (current.ParentSectionId is { } parentId)
            {
                if (!visited.Add(parentId))
                    throw new ArgumentException($"Section '{section.Id}' participates in a parent cycle.", nameof(sections));
                depth++;
                if (depth > maximumDepth)
                    throw new ArgumentException($"Section '{section.Id}' exceeds the maximum nesting depth of {maximumDepth}.", nameof(sections));
                current = byId[parentId];
            }
        }
    }

    private static void ValidateEditor(NodePropertyDefinition property)
    {
        if (property.Editor is not { } editor) return;
        var compatible = editor.Kind switch
        {
            DiagramPropertyEditorKinds.Auto => true,
            DiagramPropertyEditorKinds.Text or DiagramPropertyEditorKinds.Multiline or DiagramPropertyEditorKinds.Color => property.Type == DiagramPropertyTypes.String,
            DiagramPropertyEditorKinds.Toggle => property.Type == DiagramPropertyTypes.Boolean,
            DiagramPropertyEditorKinds.Number or DiagramPropertyEditorKinds.Range => property.Type is DiagramPropertyTypes.Integer or DiagramPropertyTypes.Decimal,
            DiagramPropertyEditorKinds.Date => property.Type == DiagramPropertyTypes.Date,
            DiagramPropertyEditorKinds.DateTime => property.Type == DiagramPropertyTypes.DateTime,
            DiagramPropertyEditorKinds.Select => property.Type == DiagramPropertyTypes.Enum,
            DiagramPropertyEditorKinds.Json => property.Type == DiagramPropertyTypes.Json,
            _ => false
        };
        if (!compatible)
            throw new ArgumentException($"Editor '{editor.Kind}' is incompatible with property '{property.Id}' of type '{property.Type}'.", "properties");
        if (editor.Minimum is { } minimum && editor.Maximum is { } maximum && minimum > maximum)
            throw new ArgumentException($"Editor for property '{property.Id}' has a minimum greater than its maximum.", "properties");
        if (editor.Step is <= 0)
            throw new ArgumentException($"Editor for property '{property.Id}' must have a positive step.", "properties");
        if (editor.Kind is not (DiagramPropertyEditorKinds.Number or DiagramPropertyEditorKinds.Range) &&
            (editor.Minimum is not null || editor.Maximum is not null || editor.Step is not null))
            throw new ArgumentException($"Editor '{editor.Kind}' for property '{property.Id}' cannot define numeric bounds.", "properties");
    }

    private static void ValidatePresentation(DiagramNodePresentation? presentation, IReadOnlyList<NodeSectionDefinition> sections)
    {
        if (presentation is null) return;
        if (presentation.DisplayMode is not (DiagramNodeDisplayModes.Expanded or DiagramNodeDisplayModes.Compact or DiagramNodeDisplayModes.Collapsed))
            throw new ArgumentException($"Node presentation has invalid display mode '{presentation.DisplayMode}'.", "presentation");
        if (presentation.ExpandedHeight is not { } expandedHeight || !double.IsFinite(expandedHeight) || expandedHeight <= 0)
            throw new ArgumentException("Node presentation expanded height must be finite and positive.", "presentation");
        var collapsedIds = presentation.CollapsedSectionIds ?? [];
        var duplicate = collapsedIds.GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Node presentation contains duplicate collapsed section id '{duplicate.Key}'.", "presentation");
        foreach (var id in collapsedIds)
        {
            var section = sections.FirstOrDefault(item => item.Id == id)
                ?? throw new ArgumentException($"Node presentation references unknown collapsed section '{id}'.", "presentation");
            if (!section.Collapsible)
                throw new ArgumentException($"Node presentation collapses non-collapsible section '{id}'.", "presentation");
        }
    }
}

public sealed class NodeSetDescriptor
{
    public NodeSetDescriptor(string id, string displayName, IEnumerable<NodeTypeDescriptor> nodeTypes, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A node-set identifier is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A node-set display name is required.", nameof(displayName));
        Id = id;
        DisplayName = displayName;
        Description = description;
        NodeTypes = Array.AsReadOnly(nodeTypes.ToArray());
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public IReadOnlyList<NodeTypeDescriptor> NodeTypes { get; }
}

public interface INodeHandler
{
    Type StateType { get; }
    ValueTask<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, object? state, CancellationToken cancellationToken = default);
}

public interface INodeHandler<TState> : INodeHandler
{
    ValueTask<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TState state, CancellationToken cancellationToken = default);
}

public abstract class NodeHandler<TState> : INodeHandler<TState>
{
    public Type StateType => typeof(TState);
    public abstract ValueTask<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, TState state, CancellationToken cancellationToken = default);

    async ValueTask<NodeExecutionResult> INodeHandler.ExecuteAsync(NodeExecutionContext context, object? state, CancellationToken cancellationToken)
    {
        if (state is not TState typedState)
            throw new ArgumentException($"Node handler '{GetType().Name}' requires state '{typeof(TState).FullName}'.", nameof(state));
        return await ExecuteAsync(context, typedState, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record NodeRegistration(
    NodeTypeDescriptor Descriptor,
    Func<IServiceProvider, INodeHandler>? HandlerFactory = null,
    Func<DiagramNode, object?>? StateBinder = null)
{
    /// <summary>Golden path for a DI-resolved typed handler whose state is projected from persisted properties.</summary>
    public static NodeRegistration Create<THandler, TState>(NodeTypeDescriptor descriptor, Func<DiagramNode, TState> bindState)
        where THandler : class, INodeHandler<TState> =>
        new(
            descriptor,
            services => services.GetService(typeof(THandler)) as THandler
                ?? throw new InvalidOperationException($"Node handler '{typeof(THandler).FullName}' is not registered in DI."),
            node => bindState(node));

    public INodeHandler ResolveHandler(IServiceProvider services) => HandlerFactory?.Invoke(services)
        ?? throw new InvalidOperationException($"Node type '{Descriptor.Key}' has no execution handler registration.");
    public object? BindState(DiagramNode node) => StateBinder?.Invoke(node)
        ?? throw new InvalidOperationException($"Node type '{Descriptor.Key}' has no persisted-state binder.");
}

public interface INodeTypeRegistry
{
    IReadOnlyList<NodeSetDescriptor> NodeSets { get; }
    IReadOnlyList<NodeTypeDescriptor> NodeTypes { get; }
    bool TryGet(string typeId, int version, out NodeRegistration registration);
    NodeRegistration GetRequired(string typeId, int version);
    NodeRegistration GetLatest(string typeId);
}

/// <summary>An immutable catalog snapshot suitable for singleton DI registration.</summary>
public sealed class NodeTypeRegistry : INodeTypeRegistry
{
    private readonly FrozenDictionary<NodeTypeKey, NodeRegistration> _registrations;
    private readonly FrozenDictionary<string, NodeRegistration> _latest;

    public NodeTypeRegistry(IEnumerable<NodeSetDescriptor> nodeSets, IEnumerable<NodeRegistration>? registrations = null)
    {
        NodeSets = Array.AsReadOnly(nodeSets.ToArray());
        var duplicateSet = NodeSets.GroupBy(set => set.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicateSet is not null) throw new InvalidOperationException($"Duplicate node-set id '{duplicateSet.Key}'.");
        var descriptors = NodeSets.SelectMany(set => set.NodeTypes).ToArray();
        var explicitRegistrations = (registrations ?? []).ToDictionary(item => item.Descriptor.Key);
        var all = descriptors.Select(descriptor => explicitRegistrations.TryGetValue(descriptor.Key, out var registration)
                ? registration
                : new NodeRegistration(descriptor))
            .Concat(explicitRegistrations.Values.Where(item => descriptors.All(descriptor => descriptor.Key != item.Descriptor.Key)))
            .ToArray();

        var duplicate = all.GroupBy(item => item.Descriptor.Key).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Duplicate node type registration '{duplicate.Key}'.");

        _registrations = all.ToFrozenDictionary(item => item.Descriptor.Key);
        _latest = all.GroupBy(item => item.Descriptor.TypeId, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.OrderByDescending(item => item.Descriptor.Version).First(), StringComparer.Ordinal);
        NodeTypes = Array.AsReadOnly(all.Select(item => item.Descriptor).OrderBy(item => item.TypeId, StringComparer.Ordinal).ThenBy(item => item.Version).ToArray());
    }

    public IReadOnlyList<NodeSetDescriptor> NodeSets { get; }
    public IReadOnlyList<NodeTypeDescriptor> NodeTypes { get; }
    public bool TryGet(string typeId, int version, out NodeRegistration registration) => _registrations.TryGetValue(new(typeId, version), out registration!);
    public NodeRegistration GetRequired(string typeId, int version) => TryGet(typeId, version, out var registration)
        ? registration
        : throw new KeyNotFoundException($"Node type '{typeId}@{version}' is not registered.");
    public NodeRegistration GetLatest(string typeId) => _latest.TryGetValue(typeId, out var registration)
        ? registration
        : throw new KeyNotFoundException($"Node type '{typeId}' is not registered.");
}
