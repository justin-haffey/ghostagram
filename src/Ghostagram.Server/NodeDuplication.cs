using Ghostagram.Contracts;
using Ghostagram.Core;

namespace Ghostagram.Server;

/// <summary>Builds the server-authoritative operation batch for duplicating one or more nodes.</summary>
public static class NodeDuplication
{
    public static NodeDuplicationPlan CreatePlan(
        DiagramDocument document,
        IReadOnlyList<NodeDuplicatePosition> positions,
        Func<string, string> nextId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(nextId);

        var nodes = document.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var groups = document.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var operations = new List<GhostagramOperation>();
        var duplicateIds = new List<string>();

        foreach (var position in positions
                     .Where(position => !string.IsNullOrWhiteSpace(position.SourceNodeId) && double.IsFinite(position.X) && double.IsFinite(position.Y))
                     .GroupBy(position => position.SourceNodeId, StringComparer.Ordinal)
                     .Select(group => group.Last()))
        {
            if (!nodes.TryGetValue(position.SourceNodeId, out var source)) continue;

            var groupId = position.HasGroupId && groups.Contains(position.GroupId ?? string.Empty)
                ? position.GroupId
                : source.GroupId;
            var duplicate = source with { Id = nextId("node"), X = position.X, Y = position.Y, GroupId = groupId };
            duplicateIds.Add(duplicate.Id);
            operations.Add(DiagramOperations.Upsert(duplicate));
            operations.AddRange(document.Ports
                .Where(port => string.Equals(port.NodeId, source.Id, StringComparison.Ordinal))
                .Select(port => DiagramOperations.Upsert(port with { Id = nextId("port"), NodeId = duplicate.Id })));
        }

        if (duplicateIds.Count > 0) operations.Add(DiagramOperations.Select(duplicateIds));
        return new NodeDuplicationPlan(operations, duplicateIds);
    }
}

public sealed record NodeDuplicatePosition(string SourceNodeId, double X, double Y, string? GroupId, bool HasGroupId);

public sealed record NodeDuplicationPlan(IReadOnlyList<GhostagramOperation> Operations, IReadOnlyList<string> DuplicateNodeIds);
