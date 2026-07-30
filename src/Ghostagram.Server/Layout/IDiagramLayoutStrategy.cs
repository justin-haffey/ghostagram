using System.Collections.Immutable;
using Ghostagram.Contracts;

namespace Ghostagram.Server.Layout;

public sealed record LayoutComputation(
    ImmutableArray<GhostagramOperation> Operations,
    DiagramLayoutMetrics Metrics);

public interface IDiagramLayoutStrategy
{
    string Name { get; }
    string Version { get; }
    LayoutComputation Compute(DiagramSnapshot snapshot, DiagramLayoutOptions options, int seed, CancellationToken cancellationToken);
}

public interface IDiagramLayoutStrategyResolver
{
    IDiagramLayoutStrategy Resolve(string name);
    IReadOnlyList<object> Capabilities();
}

public sealed class DiagramLayoutStrategyResolver(IEnumerable<IDiagramLayoutStrategy> strategies) : IDiagramLayoutStrategyResolver
{
    private readonly IReadOnlyDictionary<string, IDiagramLayoutStrategy> _strategies =
        strategies.ToDictionary(strategy => strategy.Name, StringComparer.OrdinalIgnoreCase);

    public IDiagramLayoutStrategy Resolve(string name)
        => _strategies.TryGetValue(name, out var strategy)
            ? strategy
            : throw new DiagramCommandException("LAYOUT_UNSUPPORTED", $"Layout algorithm '{name}' is not registered.");

    public IReadOnlyList<object> Capabilities()
        => _strategies.Values
            .OrderBy(strategy => strategy.Name, StringComparer.Ordinal)
            .Select(strategy => (object)new { algorithm = strategy.Name, version = strategy.Version })
            .ToArray();
}
