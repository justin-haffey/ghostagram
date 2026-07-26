using System.Collections.Immutable;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Models;
using Diagrams.Core.Routing;

namespace Diagrams.Core.Validation;

public sealed class DiagramValidationEngine : IValidationEngine
{
    public IReadOnlyList<ValidationIssue> Validate(DiagramDocument document)
    {
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();

        foreach (var node in document.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Label))
            {
                issues.Add(new ValidationIssue(
                    $"node-label-{node.Id}",
                    ValidationSeverity.Warning,
                    "NODE_LABEL_MISSING",
                    "Node is missing a label.",
                    "node",
                    node.Id));
            }

            if (document.ResolveLayer(node.LayerId).IsLocked)
            {
                issues.Add(new ValidationIssue(
                    $"node-layer-locked-{node.Id}",
                    ValidationSeverity.Info,
                    "NODE_ON_LOCKED_LAYER",
                    $"Node '{node.Label}' is on a locked layer.",
                    "node",
                    node.Id));
            }
        }

        foreach (var edge in document.Edges)
        {
            if (document.FindPort(edge.SourcePortId) is null || document.FindPort(edge.TargetPortId) is null)
            {
                issues.Add(new ValidationIssue(
                    $"edge-port-{edge.Id}",
                    ValidationSeverity.Error,
                    "EDGE_PORT_MISSING",
                    "Edge references a missing source or target port.",
                    "edge",
                    edge.Id));
            }

            if (edge.Waypoints.Length > 0 && !EdgeRouteResolver.SupportsManualWaypoints(edge))
            {
                issues.Add(new ValidationIssue(
                    $"edge-waypoint-unsupported-{edge.Id}",
                    ValidationSeverity.Warning,
                    "EDGE_WAYPOINT_CONNECTOR_UNSUPPORTED",
                    $"Edge '{edge.Id}' uses waypoints with unsupported connector kind '{edge.ConnectorKind}'.",
                    "edge",
                    edge.Id));
            }

            if (HasDuplicateConsecutiveWaypoints(edge))
            {
                issues.Add(new ValidationIssue(
                    $"edge-waypoint-duplicate-{edge.Id}",
                    ValidationSeverity.Warning,
                    "EDGE_WAYPOINT_DUPLICATE",
                    "Edge contains duplicate consecutive waypoints.",
                    "edge",
                    edge.Id));
            }

            if (HasOffCanvasWaypoint(document, edge))
            {
                issues.Add(new ValidationIssue(
                    $"edge-waypoint-off-canvas-{edge.Id}",
                    ValidationSeverity.Warning,
                    "EDGE_WAYPOINT_OFF_CANVAS",
                    "Edge contains waypoint coordinates outside the canvas bounds.",
                    "edge",
                    edge.Id));
            }
        }

        foreach (var group in document.Groups)
        {
            if (group.ChildNodeIds.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    $"group-empty-{group.Id}",
                    ValidationSeverity.Warning,
                    "GROUP_EMPTY",
                    $"Group '{group.Label}' has no members.",
                    "group",
                    group.Id));
            }
        }

        var hiddenLayers = document.Layers.Where(layer => !layer.IsVisible).Select(layer => layer.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (document.Nodes.All(node => hiddenLayers.Contains(node.LayerId)))
        {
            issues.Add(new ValidationIssue(
                "all-content-hidden",
                ValidationSeverity.Info,
                "ALL_CONTENT_HIDDEN",
                "All nodes are currently on hidden layers.",
                "document",
                document.DocumentId));
        }

        var overlaps = document.Nodes
            .SelectMany((node, index) => document.Nodes.Skip(index + 1).Select(other => (node, other)))
            .Where(pair => string.Equals(pair.node.LayerId, pair.other.LayerId, StringComparison.OrdinalIgnoreCase))
            .Where(pair => Intersects(pair.node.Bounds, pair.other.Bounds))
            .Take(3)
            .ToArray();

        foreach (var overlap in overlaps)
        {
            issues.Add(new ValidationIssue(
                $"overlap-{overlap.node.Id}-{overlap.other.Id}",
                ValidationSeverity.Warning,
                "NODE_OVERLAP",
                $"'{overlap.node.Label}' overlaps '{overlap.other.Label}'.",
                "node",
                overlap.node.Id));
        }

        return issues.ToImmutable();
    }

    private static bool Intersects(DiagramBounds left, DiagramBounds right)
        => left.X < right.Right
           && left.Right > right.X
           && left.Y < right.Bottom
           && left.Bottom > right.Y;

    private static bool HasDuplicateConsecutiveWaypoints(DiagramEdge edge)
        => edge.Waypoints.Zip(edge.Waypoints.Skip(1))
            .Any(pair => pair.First == pair.Second);

    private static bool HasOffCanvasWaypoint(DiagramDocument document, DiagramEdge edge)
        => edge.Waypoints.Any(point =>
            point.X < 0
            || point.Y < 0
            || point.X > document.Canvas.Width
            || point.Y > document.Canvas.Height);
}
