using System.Collections.Immutable;
using System.Reflection;
using Diagrams.Core.Abstractions;
using Diagrams.Core.Attributes;
using Diagrams.Core.Models;

namespace Diagrams.Core.Layouts;

public sealed class DiagramLayoutEngine : IDiagramLayoutEngine
{
    private readonly ImmutableArray<LayoutDescriptor> _descriptors;

    public DiagramLayoutEngine()
    {
        _descriptors = Assembly.GetExecutingAssembly()
            .DefinedTypes
            .Where(type => typeof(LayoutSpec).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<LayoutCapabilityAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => new LayoutDescriptor(attribute!.LayoutKind, attribute.DisplayName, attribute.SupportsGroups))
            .OrderBy(descriptor => descriptor.DisplayName)
            .ToImmutableArray();
    }

    public IReadOnlyList<LayoutDescriptor> GetSupportedLayouts() => _descriptors;

    public DiagramDocument ApplyLayout(DiagramDocument document, LayoutSpec spec, string? groupId = null)
    {
        var scopedNodes = ResolveNodes(document, groupId);
        if (scopedNodes.Length == 0)
        {
            return document;
        }

        var positioned = spec switch
        {
            FreeformLayoutSpec => scopedNodes.Select(node => (node.Id, node.Bounds)).ToImmutableArray(),
            HierarchyTopDownLayoutSpec hierarchy => ArrangeHierarchy(scopedNodes, document, hierarchy, horizontal: true),
            HierarchyLeftRightLayoutSpec hierarchy => ArrangeHierarchy(scopedNodes, document, hierarchy, horizontal: false),
            TreeLayoutSpec tree => ArrangeTree(scopedNodes, document, tree),
            GridLayoutSpec grid => ArrangeGrid(scopedNodes, grid),
            RadialLayoutSpec radial => ArrangeRadial(scopedNodes, document, radial),
            MindMapLayoutSpec mindMap => ArrangeMindMap(scopedNodes, document, mindMap),
            _ => scopedNodes.Select(node => (node.Id, node.Bounds)).ToImmutableArray()
        };

        var laidOut = ApplyBounds(document, positioned, groupId, spec.Padding);
        var affectedNodeIds = scopedNodes.Select(node => node.Id).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        return ClearAffectedWaypoints(laidOut, affectedNodeIds);
    }

    private static ImmutableArray<DiagramNode> ResolveNodes(DiagramDocument document, string? groupId)
        => string.IsNullOrWhiteSpace(groupId)
            ? document.Nodes
            : document.Nodes.Where(node => node.GroupId == groupId).ToImmutableArray();

    private static DiagramDocument ApplyBounds(
        DiagramDocument document,
        ImmutableArray<(string NodeId, DiagramBounds Bounds)> positioned,
        string? groupId,
        double padding)
    {
        var boundsById = positioned.ToImmutableDictionary(entry => entry.NodeId, entry => entry.Bounds, StringComparer.OrdinalIgnoreCase);
        var updatedNodes = document.Nodes.Select(node =>
            boundsById.TryGetValue(node.Id, out var bounds)
                ? node with { Bounds = bounds }
                : node).ToImmutableArray();

        if (string.IsNullOrWhiteSpace(groupId))
        {
            return document with { Nodes = updatedNodes };
        }

        var group = document.FindGroup(groupId);
        if (group is null)
        {
            return document with { Nodes = updatedNodes };
        }

        var left = positioned.Min(entry => entry.Bounds.X) - padding;
        var top = positioned.Min(entry => entry.Bounds.Y) - padding;
        var right = positioned.Max(entry => entry.Bounds.Right) + padding;
        var bottom = positioned.Max(entry => entry.Bounds.Bottom) + padding;

        return document with
        {
            Nodes = updatedNodes,
            Groups = document.Groups.Select(existing => existing.Id == groupId
                ? existing with { Bounds = new DiagramBounds(left, top, right - left, bottom - top) }
                : existing).ToImmutableArray()
        };
    }

    private static ImmutableArray<(string NodeId, DiagramBounds Bounds)> ArrangeHierarchy(
        ImmutableArray<DiagramNode> nodes,
        DiagramDocument document,
        LayoutSpec spec,
        bool horizontal)
    {
        var order = TopologicalOrder(nodes, document);
        var xSpacing = spec switch
        {
            HierarchyTopDownLayoutSpec topDown => topDown.HorizontalSpacing,
            HierarchyLeftRightLayoutSpec leftRight => leftRight.HorizontalSpacing,
            _ => 240
        };
        var ySpacing = spec switch
        {
            HierarchyTopDownLayoutSpec topDown => topDown.VerticalSpacing,
            HierarchyLeftRightLayoutSpec leftRight => leftRight.VerticalSpacing,
            _ => 180
        };

        var levels = ComputeLevels(order, nodes, document);
        var indexedByLevel = levels.GroupBy(entry => entry.Level).OrderBy(group => group.Key);
        var positions = new List<(string NodeId, DiagramBounds Bounds)>();

        foreach (var level in indexedByLevel)
        {
            var orderedNodes = level.Select(entry => nodes.First(node => node.Id == entry.NodeId)).ToArray();
            for (var index = 0; index < orderedNodes.Length; index++)
            {
                var node = orderedNodes[index];
                var x = horizontal ? spec.Padding + (index * xSpacing) : spec.Padding + (level.Key * xSpacing);
                var y = horizontal ? spec.Padding + (level.Key * ySpacing) : spec.Padding + (index * ySpacing);
                positions.Add((node.Id, node.Bounds with { X = x, Y = y }));
            }
        }

        return positions.ToImmutableArray();
    }

    private static ImmutableArray<(string NodeId, DiagramBounds Bounds)> ArrangeTree(
        ImmutableArray<DiagramNode> nodes,
        DiagramDocument document,
        TreeLayoutSpec spec)
    {
        var order = TopologicalOrder(nodes, document);
        var levels = ComputeLevels(order, nodes, document);
        return levels.Select((entry, index) =>
        {
            var node = nodes.First(candidate => candidate.Id == entry.NodeId);
            var siblingIndex = levels.Take(index).Count(previous => previous.Level == entry.Level);
            var x = spec.Padding + (siblingIndex * spec.HorizontalSpacing);
            var y = spec.Padding + (entry.Level * spec.VerticalSpacing);
            return (node.Id, node.Bounds with { X = x, Y = y });
        }).ToImmutableArray();
    }

    private static ImmutableArray<(string NodeId, DiagramBounds Bounds)> ArrangeGrid(
        ImmutableArray<DiagramNode> nodes,
        GridLayoutSpec spec)
        => nodes.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .Select((node, index) =>
            {
                var row = index / Math.Max(1, spec.Columns);
                var column = index % Math.Max(1, spec.Columns);
                var x = spec.Padding + (column * spec.CellWidth);
                var y = spec.Padding + (row * spec.CellHeight);
                return (node.Id, node.Bounds with { X = x, Y = y });
            })
            .ToImmutableArray();

    private static ImmutableArray<(string NodeId, DiagramBounds Bounds)> ArrangeRadial(
        ImmutableArray<DiagramNode> nodes,
        DiagramDocument document,
        RadialLayoutSpec spec)
    {
        var ordered = TopologicalOrder(nodes, document);
        var centerNodeId = ordered.Length > 0 ? ordered[0].Id : nodes[0].Id;
        var positions = new List<(string NodeId, DiagramBounds Bounds)>();
        var centerX = spec.Padding + (spec.RadiusStep * 1.4);
        var centerY = spec.Padding + (spec.RadiusStep * 1.2);

        foreach (var node in nodes)
        {
            if (node.Id == centerNodeId)
            {
                positions.Add((node.Id, node.Bounds with { X = centerX, Y = centerY }));
                continue;
            }

            var index = positions.Count;
            var angle = (Math.PI * 2 * index) / Math.Max(1, nodes.Length - 1);
            var x = centerX + (Math.Cos(angle) * spec.RadiusStep);
            var y = centerY + (Math.Sin(angle) * spec.RadiusStep);
            positions.Add((node.Id, node.Bounds with { X = x, Y = y }));
        }

        return positions.ToImmutableArray();
    }

    private static ImmutableArray<(string NodeId, DiagramBounds Bounds)> ArrangeMindMap(
        ImmutableArray<DiagramNode> nodes,
        DiagramDocument document,
        MindMapLayoutSpec spec)
    {
        var ordered = TopologicalOrder(nodes, document);
        var rootNode = ordered.Length > 0 ? ordered[0] : nodes[0];
        var positions = new List<(string NodeId, DiagramBounds Bounds)>
        {
            (rootNode.Id, rootNode.Bounds with { X = spec.Padding + spec.PrimaryRadius, Y = spec.Padding + spec.PrimaryRadius })
        };

        var branches = ordered.Skip(1).Select(item => nodes.First(node => node.Id == item.Id)).ToArray();
        for (var index = 0; index < branches.Length; index++)
        {
            var direction = index % 2 == 0 ? 1 : -1;
            var branchIndex = index / 2;
            var x = spec.Padding + spec.PrimaryRadius + (direction * spec.PrimaryRadius);
            var y = spec.Padding + spec.PrimaryRadius + (branchIndex * spec.SecondarySpacing) - (spec.SecondarySpacing / 2);
            positions.Add((branches[index].Id, branches[index].Bounds with { X = x, Y = y }));
        }

        return positions.ToImmutableArray();
    }

    private static ImmutableArray<(string NodeId, int Level)> ComputeLevels(
        ImmutableArray<DiagramNode> order,
        ImmutableArray<DiagramNode> nodes,
        DiagramDocument document)
    {
        var incoming = BuildIncomingCounts(nodes, document);
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in order)
        {
            var parents = document.Edges
                .Where(edge => document.FindNodeIdByPort(edge.TargetPortId) == node.Id)
                .Select(edge => document.FindNodeIdByPort(edge.SourcePortId))
                .Where(id => id is not null)
                .Cast<string>()
                .ToArray();

            levels[node.Id] = parents.Length == 0 ? 0 : parents.Max(parent => levels.GetValueOrDefault(parent, 0)) + 1;
        }

        return order.Select(node => (node.Id, levels.GetValueOrDefault(node.Id, incoming[node.Id] == 0 ? 0 : 1))).ToImmutableArray();
    }

    private static ImmutableArray<DiagramNode> TopologicalOrder(ImmutableArray<DiagramNode> nodes, DiagramDocument document)
    {
        var incoming = BuildIncomingCounts(nodes, document);
        var outgoing = nodes.ToDictionary(
            node => node.Id,
            node => document.Edges
                .Where(edge => document.FindNodeIdByPort(edge.SourcePortId) == node.Id)
                .Select(edge => document.FindNodeIdByPort(edge.TargetPortId))
                .Where(id => id is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<DiagramNode>(nodes.Where(node => incoming[node.Id] == 0).OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase));
        var ordered = new List<DiagramNode>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            ordered.Add(node);
            foreach (var targetId in outgoing[node.Id])
            {
                incoming[targetId]--;
                if (incoming[targetId] == 0)
                {
                    queue.Enqueue(nodes.First(candidate => candidate.Id == targetId));
                }
            }
        }

        if (ordered.Count != nodes.Length)
        {
            ordered.AddRange(nodes.Where(node => ordered.All(orderedNode => orderedNode.Id != node.Id))
                .OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase));
        }

        return ordered.ToImmutableArray();
    }

    private static Dictionary<string, int> BuildIncomingCounts(ImmutableArray<DiagramNode> nodes, DiagramDocument document)
    {
        var counts = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in document.Edges)
        {
            var targetNodeId = document.FindNodeIdByPort(edge.TargetPortId);
            if (targetNodeId is not null && counts.ContainsKey(targetNodeId))
            {
                counts[targetNodeId]++;
            }
        }

        return counts;
    }

    private static DiagramDocument ClearAffectedWaypoints(DiagramDocument document, ImmutableHashSet<string> affectedNodeIds)
    {
        if (affectedNodeIds.Count == 0)
        {
            return document;
        }

        var edges = document.Edges.Select(edge =>
        {
            if (edge.Waypoints.Length == 0)
            {
                return edge;
            }

            var sourceNodeId = document.FindNodeIdByPort(edge.SourcePortId);
            var targetNodeId = document.FindNodeIdByPort(edge.TargetPortId);
            return (sourceNodeId is not null && affectedNodeIds.Contains(sourceNodeId))
                || (targetNodeId is not null && affectedNodeIds.Contains(targetNodeId))
                ? edge with { Waypoints = [] }
                : edge;
        }).ToImmutableArray();

        return document with { Edges = edges };
    }
}
