using System.Collections.ObjectModel;
using Ghostagram.Core;
using Ghostworx.System.Graph;

namespace Ghostagram.Bridge;

public sealed class NodePortPresentationProfileRegistry : INodePortPresentationProfileRegistry
{
    private readonly IReadOnlyDictionary<NodeKind, INodePortPresentationProfile> profiles;

    public NodePortPresentationProfileRegistry(IEnumerable<INodePortPresentationProfile>? profiles = null)
    {
        var values = (profiles ?? []).ToArray();
        var duplicate = values.GroupBy(profile => profile.Kind).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate node port presentation profile for '{duplicate.Key}'.", nameof(profiles));
        this.profiles = new ReadOnlyDictionary<NodeKind, INodePortPresentationProfile>(values.ToDictionary(profile => profile.Kind));
    }

    public bool TryGet(NodeKind kind, out INodePortPresentationProfile profile) => profiles.TryGetValue(kind, out profile!);
}

public sealed class RelationshipPresentationProfileRegistry : IRelationshipPresentationProfileRegistry
{
    private readonly IReadOnlyDictionary<RelationshipKind, IRelationshipPresentationProfile> profiles;

    public RelationshipPresentationProfileRegistry(IEnumerable<IRelationshipPresentationProfile>? profiles = null)
    {
        var values = (profiles ?? []).ToArray();
        var duplicate = values.GroupBy(profile => profile.Kind).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate relationship presentation profile for '{duplicate.Key}'.", nameof(profiles));
        this.profiles = new ReadOnlyDictionary<RelationshipKind, IRelationshipPresentationProfile>(values.ToDictionary(profile => profile.Kind));
    }

    public bool TryGet(RelationshipKind kind, out IRelationshipPresentationProfile profile) => profiles.TryGetValue(kind, out profile!);
}
