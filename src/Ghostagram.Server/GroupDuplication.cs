using Ghostagram.Contracts;
using Ghostagram.Core;

namespace Ghostagram.Server;

/// <summary>Builds one atomic operation batch for duplicating a group and its contained graph.</summary>
public static class GroupDuplication
{
    public static GroupDuplicationPlan CreatePlan(
        DiagramDocument document,
        GroupDuplicatePosition position,
        Func<string, string> nextId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(nextId);

        if (string.IsNullOrWhiteSpace(position.SourceGroupId)
            || !double.IsFinite(position.X)
            || !double.IsFinite(position.Y))
            return GroupDuplicationPlan.Empty;

        var groups = document.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        if (!groups.TryGetValue(position.SourceGroupId, out var sourceRoot)) return GroupDuplicationPlan.Empty;

        var subtree = GroupSubtree(document.Groups, sourceRoot.Id);
        var subtreeIds = subtree.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var groupIds = subtree.ToDictionary(group => group.Id, _ => nextId("group"), StringComparer.Ordinal);
        var dx = position.X - sourceRoot.X;
        var dy = position.Y - sourceRoot.Y;
        var requestedParent = position.HasParentGroupId
            ? ValidParent(position.ParentGroupId, groups, subtreeIds, sourceRoot.ParentGroupId)
            : sourceRoot.ParentGroupId;

        var operations = new List<GhostagramOperation>();
        foreach (var source in subtree)
        {
            var parentId = source.Id == sourceRoot.Id
                ? requestedParent
                : groupIds[source.ParentGroupId!];
            operations.Add(DiagramOperations.Upsert(source with
            {
                Id = groupIds[source.Id],
                X = source.X + dx,
                Y = source.Y + dy,
                ParentGroupId = parentId
            }));
        }

        var sourceNodes = document.Nodes.Where(node => node.GroupId is not null && subtreeIds.Contains(node.GroupId)).ToArray();
        var nodeIds = sourceNodes.ToDictionary(node => node.Id, _ => nextId("node"), StringComparer.Ordinal);
        foreach (var source in sourceNodes)
            operations.Add(DiagramOperations.Upsert(source with
            {
                Id = nodeIds[source.Id],
                X = source.X + dx,
                Y = source.Y + dy,
                GroupId = groupIds[source.GroupId!]
            }));

        var sourcePorts = document.Ports.Where(port => nodeIds.ContainsKey(port.NodeId)).ToArray();
        var portIds = sourcePorts.ToDictionary(port => port.Id, _ => nextId("port"), StringComparer.Ordinal);
        foreach (var source in sourcePorts)
            operations.Add(DiagramOperations.Upsert(source with
            {
                Id = portIds[source.Id],
                NodeId = nodeIds[source.NodeId],
                ConnectionPolicy = RemapConnectionPolicy(source.ConnectionPolicy, portIds, nodeIds)
            }));

        var duplicateEdgeIds = new List<string>();
        foreach (var source in document.Edges.Where(edge => portIds.ContainsKey(edge.SourcePortId) && portIds.ContainsKey(edge.TargetPortId)))
        {
            var edgeId = nextId("edge");
            duplicateEdgeIds.Add(edgeId);
            operations.Add(DiagramOperations.Upsert(source with
            {
                Id = edgeId,
                SourcePortId = portIds[source.SourcePortId],
                TargetPortId = portIds[source.TargetPortId],
                Waypoints = source.Waypoints?.Select(point => point with { X = point.X + dx, Y = point.Y + dy }).ToArray()
            }));
        }

        var duplicateRootId = groupIds[sourceRoot.Id];
        operations.Add(DiagramOperations.Select([duplicateRootId]));
        return new GroupDuplicationPlan(
            operations,
            duplicateRootId,
            groupIds.Values.ToArray(),
            nodeIds.Values.ToArray(),
            portIds.Values.ToArray(),
            duplicateEdgeIds);
    }

    private static IReadOnlyList<DiagramGroup> GroupSubtree(IEnumerable<DiagramGroup> groups, string rootId)
    {
        var children = groups
            .Where(group => group.ParentGroupId is not null)
            .GroupBy(group => group.ParentGroupId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var byId = groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        var result = new List<DiagramGroup>();
        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        while (queue.TryDequeue(out var id))
        {
            if (!byId.TryGetValue(id, out var group)) continue;
            result.Add(group);
            if (children.TryGetValue(id, out var nested))
                foreach (var child in nested) queue.Enqueue(child.Id);
        }
        return result;
    }

    private static string? ValidParent(
        string? requestedParentId,
        IReadOnlyDictionary<string, DiagramGroup> groups,
        IReadOnlySet<string> sourceSubtreeIds,
        string? fallbackParentId) =>
        requestedParentId is null
            ? null
            : groups.ContainsKey(requestedParentId) && !sourceSubtreeIds.Contains(requestedParentId)
                ? requestedParentId
                : fallbackParentId;

    private static DiagramConnectionPolicy? RemapConnectionPolicy(
        DiagramConnectionPolicy? policy,
        IReadOnlyDictionary<string, string> portIds,
        IReadOnlyDictionary<string, string> nodeIds) =>
        policy is null
            ? null
            : policy with
            {
                AllowPortIds = RemapIds(policy.AllowPortIds, portIds),
                DenyPortIds = RemapIds(policy.DenyPortIds, portIds),
                AllowNodeIds = RemapIds(policy.AllowNodeIds, nodeIds),
                DenyNodeIds = RemapIds(policy.DenyNodeIds, nodeIds)
            };

    private static IReadOnlyList<string>? RemapIds(
        IReadOnlyList<string>? ids,
        IReadOnlyDictionary<string, string> replacements) =>
        ids?.Select(id => replacements.TryGetValue(id, out var replacement) ? replacement : id).ToArray();
}

public sealed record GroupDuplicatePosition(
    string SourceGroupId,
    double X,
    double Y,
    string? ParentGroupId,
    bool HasParentGroupId);

public sealed record GroupDuplicationPlan(
    IReadOnlyList<GhostagramOperation> Operations,
    string? DuplicateRootGroupId,
    IReadOnlyList<string> DuplicateGroupIds,
    IReadOnlyList<string> DuplicateNodeIds,
    IReadOnlyList<string> DuplicatePortIds,
    IReadOnlyList<string> DuplicateEdgeIds)
{
    public static GroupDuplicationPlan Empty { get; } = new([], null, [], [], [], []);
}
