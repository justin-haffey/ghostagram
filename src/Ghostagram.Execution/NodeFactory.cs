using System.Text.Json;
using Ghostagram.Core;

namespace Ghostagram.Execution;

public sealed record NodeCreationRequest(
    string NodeId,
    string TypeId,
    double X,
    double Y,
    int? TypeVersion = null,
    string? Label = null,
    string? GroupId = null,
    IReadOnlyDictionary<string, JsonElement?>? PropertyValues = null);

public sealed record NodeCreationResult(DiagramNode Node, IReadOnlyList<DiagramPort> Ports);

public interface INodeFactory
{
    NodeCreationResult Create(NodeCreationRequest request);
}

/// <summary>Creates the same persisted node and port identifiers for the same request and catalog.</summary>
public sealed class DeterministicNodeFactory(INodeTypeRegistry registry) : INodeFactory
{
    public NodeCreationResult Create(NodeCreationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId)) throw new ArgumentException("A node identifier is required.", nameof(request));
        var registration = request.TypeVersion is { } version
            ? registry.GetRequired(request.TypeId, version)
            : registry.GetLatest(request.TypeId);
        var descriptor = registration.Descriptor;
        var values = request.PropertyValues ?? new Dictionary<string, JsonElement?>();
        var unknown = values.Keys.FirstOrDefault(key => descriptor.Properties.All(property => !string.Equals(property.Id, key, StringComparison.Ordinal)));
        if (unknown is not null) throw new ArgumentException($"Property '{unknown}' is not defined by node type '{descriptor.Key}'.", nameof(request));

        var properties = descriptor.Properties.Select(definition =>
        {
            var value = values.TryGetValue(definition.Id, out var supplied) ? supplied : definition.DefaultValue;
            return new DiagramNodeProperty(
                definition.Id,
                definition.Name,
                definition.Type,
                Clone(value),
                definition.Mode,
                definition.Label,
                definition.Description,
                definition.Required,
                definition.Connectable,
                definition.Options?.ToArray(),
                Clone(definition.Metadata));
        }).ToArray();

        var node = new DiagramNode(
            request.NodeId,
            request.X,
            request.Y,
            descriptor.Width,
            descriptor.Height,
            request.Label ?? descriptor.DisplayName,
            request.GroupId,
            Icon: descriptor.Icon,
            Style: descriptor.Style,
            TypeId: descriptor.TypeId,
            TypeVersion: descriptor.Version,
            Properties: properties);
        var ports = descriptor.Ports.OrderBy(port => port.Order).ThenBy(port => port.Id, StringComparer.Ordinal)
            .Select(port => new DiagramPort(
                $"{request.NodeId}:{port.Id}",
                request.NodeId,
                port.Direction,
                port.Scope,
                port.MaxConnections,
                PropertyId: port.PropertyId,
                Label: port.Label,
                Order: port.Order))
            .ToArray();
        return new(node, ports);
    }

    private static JsonElement? Clone(JsonElement? value) => value?.Clone();
}
