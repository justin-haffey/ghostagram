using System.Collections.Concurrent;
using Ghostagram.Bridge;
using Ghostagram.Core;
using Ghostagram.Execution;
using Ghostworx.System.Graph;
using Ghostworx.System.Graph.Serialization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ghostagram.Server.GraphWorkspaces;

public sealed class GraphWorkspaceOptions
{
    public int MaximumWorkspaces { get; set; } = 64;
    public int ChangeHistoryCapacity { get; set; } = 4096;
    public string? StorageDirectory { get; set; }
    public OrphanHandling PresentationOrphansOnLoad { get; set; } = OrphanHandling.Remove;
}

public sealed record GraphWorkspaceSnapshot(
    string WorkspaceId,
    long GraphVersion,
    long DiagramRevision,
    DiagramDocument Document);

public interface IGraphWorkspaceService
{
    GraphWorkspaceSnapshot Create(string workspaceId);
    bool TryGetSnapshot(string workspaceId, out GraphWorkspaceSnapshot? snapshot);
    bool TryApply(string workspaceId, GraphDiagramCommand command, out GraphDiagramCommandResult? result);
    bool Remove(string workspaceId);
    Task<GraphWorkspaceSnapshot?> LoadAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<GraphWorkspaceSnapshot?> SaveAsync(string workspaceId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string workspaceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns bounded, in-memory graph workspaces. Semantic state lives in a
/// <see cref="GraphStore"/> while geometry and selection live in an independent
/// presentation sidecar; browser proposals always pass through the Bridge adapter.
/// </summary>
public sealed class GraphWorkspaceService : IGraphWorkspaceService
{
    private readonly object _creationGate = new();
    private readonly ConcurrentDictionary<string, Workspace> _workspaces = new(StringComparer.Ordinal);
    private readonly IGraphDiagramProjection _projection;
    private readonly INodeKindDescriptorRegistry _descriptors;
    private readonly IGraphWorkspaceRepository _repository;
    private readonly GraphJsonSerializer _graphSerializer;
    private readonly GraphPresentationJsonSerializer _presentationSerializer;
    private readonly int _maximumWorkspaces;
    private readonly int _changeHistoryCapacity;
    private readonly OrphanHandling _presentationOrphansOnLoad;

    public GraphWorkspaceService(
        IGraphDiagramProjection projection,
        INodeKindDescriptorRegistry descriptors,
        IGraphWorkspaceRepository repository,
        GraphJsonSerializer graphSerializer,
        GraphPresentationJsonSerializer presentationSerializer,
        IOptions<GraphWorkspaceOptions> options)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _graphSerializer = graphSerializer ?? throw new ArgumentNullException(nameof(graphSerializer));
        _presentationSerializer = presentationSerializer ?? throw new ArgumentNullException(nameof(presentationSerializer));
        _maximumWorkspaces = options?.Value.MaximumWorkspaces
            ?? throw new ArgumentNullException(nameof(options));
        if (_maximumWorkspaces < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumWorkspaces must be positive.");
        _changeHistoryCapacity = options.Value.ChangeHistoryCapacity;
        if (_changeHistoryCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "ChangeHistoryCapacity must be positive.");
        _presentationOrphansOnLoad = options.Value.PresentationOrphansOnLoad;
    }

    public GraphWorkspaceSnapshot Create(string workspaceId)
    {
        workspaceId = GraphWorkspaceIds.Require(workspaceId);
        lock (_creationGate)
        {
            if (_workspaces.TryGetValue(workspaceId, out var existing)) return existing.Capture();
            if (_workspaces.Count >= _maximumWorkspaces)
                throw new InvalidOperationException($"Graph workspace capacity of {_maximumWorkspaces} has been reached.");

            var graph = new GraphStore(
                $"ghostagram:{workspaceId}",
                features: null,
                options: StoreOptions());
            var presentation = new GraphPresentationStore();
            var workspace = new Workspace(
                workspaceId,
                graph,
                presentation,
                _projection,
                new GraphDiagramCommandAdapter(graph, presentation, _projection, _descriptors));
            if (!_workspaces.TryAdd(workspaceId, workspace))
                throw new InvalidOperationException($"Graph workspace '{workspaceId}' could not be registered.");
            return workspace.Capture();
        }
    }

    public bool TryGetSnapshot(string workspaceId, out GraphWorkspaceSnapshot? snapshot)
    {
        workspaceId = GraphWorkspaceIds.Require(workspaceId);
        if (_workspaces.TryGetValue(workspaceId, out var workspace))
        {
            snapshot = workspace.Capture();
            return true;
        }

        snapshot = null;
        return false;
    }

    public bool TryApply(string workspaceId, GraphDiagramCommand command, out GraphDiagramCommandResult? result)
    {
        workspaceId = GraphWorkspaceIds.Require(workspaceId);
        ArgumentNullException.ThrowIfNull(command);
        if (_workspaces.TryGetValue(workspaceId, out var workspace))
        {
            result = workspace.Apply(command);
            return true;
        }

        result = null;
        return false;
    }

    public bool Remove(string workspaceId)
    {
        workspaceId = GraphWorkspaceIds.Require(workspaceId);
        lock (_creationGate) return _workspaces.TryRemove(workspaceId, out _);
    }

    public async Task<GraphWorkspaceSnapshot?> LoadAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = GraphWorkspaceIds.Require(workspaceId);
        if (_workspaces.TryGetValue(workspaceId, out var current)) return current.Capture();
        var persisted = await _repository.LoadAsync(workspaceId, cancellationToken);
        if (persisted is null) return null;
        var graph = _graphSerializer.DeserializeGraph(persisted.GraphJson, StoreOptions());
        var presentation = new GraphPresentationStore(_presentationSerializer.Deserialize(persisted.PresentationJson));
        if (_presentationOrphansOnLoad == OrphanHandling.Remove)
            presentation.Reconcile(graph.CaptureSnapshot(), OrphanHandling.Remove, presentation.Revision);
        var loaded = new Workspace(workspaceId, graph, presentation, _projection,
            new GraphDiagramCommandAdapter(graph, presentation, _projection, _descriptors));
        lock (_creationGate)
        {
            if (_workspaces.TryGetValue(workspaceId, out current)) return current.Capture();
            if (_workspaces.Count >= _maximumWorkspaces)
                throw new InvalidOperationException($"Graph workspace capacity of {_maximumWorkspaces} has been reached.");
            if (!_workspaces.TryAdd(workspaceId, loaded))
                throw new InvalidOperationException($"Graph workspace '{workspaceId}' could not be loaded.");
        }
        return loaded.Capture();
    }

    public async Task<GraphWorkspaceSnapshot?> SaveAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = GraphWorkspaceIds.Require(workspaceId);
        if (!_workspaces.TryGetValue(workspaceId, out var workspace)) return null;
        var persisted = workspace.Persist(_graphSerializer, _presentationSerializer);
        await _repository.SaveAsync(persisted.State, cancellationToken);
        return persisted.Snapshot;
    }

    public async Task<bool> DeleteAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        workspaceId = GraphWorkspaceIds.Require(workspaceId);
        bool removed;
        lock (_creationGate) removed = _workspaces.TryRemove(workspaceId, out _);
        return await _repository.DeleteAsync(workspaceId, cancellationToken) || removed;
    }

    private GraphStoreOptions StoreOptions() => new()
    {
        NodeRetention = GraphNodeRetentionMode.Strong,
        ChangeHistoryCapacity = _changeHistoryCapacity
    };

    private sealed class Workspace(
        string id,
        GraphStore graph,
        GraphPresentationStore presentation,
        IGraphDiagramProjection projection,
        IGraphDiagramCommandAdapter adapter)
    {
        private readonly object _gate = new();

        public GraphWorkspaceSnapshot Capture()
        {
            lock (_gate)
            {
                var graphSnapshot = graph.CaptureSnapshot();
                var presentationSnapshot = presentation.Capture();
                return new(id, graphSnapshot.Version, presentationSnapshot.Revision, projection.Project(graphSnapshot, presentationSnapshot));
            }
        }

        public GraphDiagramCommandResult Apply(GraphDiagramCommand command)
        {
            lock (_gate) return adapter.Apply(command);
        }

        public (GraphWorkspacePersistedState State, GraphWorkspaceSnapshot Snapshot) Persist(
            GraphJsonSerializer graphSerializer,
            GraphPresentationJsonSerializer presentationSerializer)
        {
            lock (_gate)
            {
                var graphSnapshot = graph.CaptureSnapshot();
                var presentationSnapshot = presentation.Capture();
                return (
                    new(id, graphSerializer.SerializeGraph(graph), presentationSerializer.Serialize(presentationSnapshot)),
                    new(id, graphSnapshot.Version, presentationSnapshot.Revision, projection.Project(graphSnapshot, presentationSnapshot)));
            }
        }
    }
}

public static class GraphWorkspaceServiceCollectionExtensions
{
    public static IServiceCollection AddGraphWorkspaceBridge(
        this IServiceCollection services,
        Action<GraphWorkspaceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.AddOptions<GraphWorkspaceOptions>();

        services.TryAddSingleton<INodeKindDescriptorRegistry>(provider =>
        {
            var nodeTypes = provider.GetService<INodeTypeRegistry>()?.NodeTypes ?? [];
            return new NodeKindDescriptorRegistry(nodeTypes.Select(descriptor =>
                KeyValuePair.Create(GraphWorkspaceNodeKinds.For(descriptor), descriptor)));
        });
        services.TryAddSingleton<INodePortPresentationProfileRegistry>(provider =>
            new NodePortPresentationProfileRegistry(provider.GetServices<INodePortPresentationProfile>()));
        services.TryAddSingleton<IRelationshipPresentationProfileRegistry>(provider =>
            new RelationshipPresentationProfileRegistry(provider.GetServices<IRelationshipPresentationProfile>()));
        services.TryAddSingleton<INodePresentationMapper>(provider =>
            new DefaultNodePresentationMapper(
                provider.GetRequiredService<INodeKindDescriptorRegistry>(),
                provider.GetRequiredService<INodePortPresentationProfileRegistry>()));
        services.TryAddSingleton<IRelationshipPresentationMapper>(provider =>
            new DefaultRelationshipPresentationMapper(provider.GetRequiredService<IRelationshipPresentationProfileRegistry>()));
        services.TryAddSingleton<IHierarchyPresentationMapper, ContainsHierarchyPresentationMapper>();
        services.TryAddSingleton<IGraphDiagramProjection, GraphDiagramProjection>();
        services.TryAddSingleton<GraphJsonSerializer>();
        services.TryAddSingleton<GraphPresentationJsonSerializer>();
        services.TryAddSingleton<IGraphWorkspaceRepository, FileGraphWorkspaceRepository>();
        services.TryAddSingleton<IGraphWorkspaceService, GraphWorkspaceService>();
        return services;
    }
}

public static class GraphWorkspaceIds
{
    public static string Require(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Workspace ids must contain 1-80 ASCII letters, digits, '.', '-', or '_'.", nameof(value));
        return value;
    }
}

public static class GraphWorkspaceNodeKinds
{
    public static NodeKind For(NodeTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return NodeKind.Define("Ghostagram.NodeType", $"{descriptor.TypeId}@{descriptor.Version}");
    }
}
