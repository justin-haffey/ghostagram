using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ghostagram.Contracts;

namespace Ghostagram.Server.Layout;

/// <summary>
/// Deterministic compound Sugiyama-style layout:
/// hierarchy projection -> weak components -> SCC condensation -> weighted ranks
/// -> stable barycentric crossing reduction -> compact coordinate assignment.
/// </summary>
public sealed class GhostLayeredLayoutStrategy : IDiagramLayoutStrategy
{
    public const string AlgorithmName = "ghost-layered";
    public const string AlgorithmVersion = "1.0.0";

    public string Name => AlgorithmName;
    public string Version => AlgorithmVersion;

    public LayoutComputation Compute(
        DiagramSnapshot snapshot,
        DiagramLayoutOptions options,
        int seed,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        ValidateOptions(options);
        var model = LayoutModel.Parse(snapshot.Model);
        if (model.Nodes.Count > options.MaximumNodes)
            throw new DiagramCommandException("LAYOUT_LIMIT_EXCEEDED", $"The graph has {model.Nodes.Count} nodes; the configured limit is {options.MaximumNodes}.");
        if (model.Edges.Count > options.MaximumEdges)
            throw new DiagramCommandException("LAYOUT_LIMIT_EXCEEDED", $"The graph has {model.Edges.Count} edges; the configured limit is {options.MaximumEdges}.");

        var statistics = new LayoutStatistics();
        var compound = new CompoundLayouter(model, options, seed, statistics, cancellationToken);
        var root = compound.Layout();
        var operations = compound.CreateOperations(root);
        stopwatch.Stop();

        var metrics = new DiagramLayoutMetrics(
            model.Nodes.Count,
            model.Edges.Count,
            model.Groups.Count,
            statistics.ComponentCount,
            statistics.StrongComponentCount,
            statistics.CyclicComponentCount,
            statistics.CrossingsBefore,
            statistics.CrossingsAfter,
            Round(root.Width),
            Round(root.Height),
            stopwatch.Elapsed.TotalMilliseconds);
        return new LayoutComputation(operations, metrics);
    }

    private static void ValidateOptions(DiagramLayoutOptions options)
    {
        if (!new[] { "right", "left", "down", "up" }.Contains(options.Direction, StringComparer.OrdinalIgnoreCase))
            throw new DiagramCommandException("LAYOUT_OPTIONS_INVALID", "direction must be right, left, down, or up.");
        foreach (var (value, name) in new[]
        {
            (options.LayerSpacing, "layerSpacing"), (options.NodeSpacing, "nodeSpacing"),
            (options.ComponentSpacing, "componentSpacing"), (options.GroupPadding, "groupPadding"),
            (options.GroupHeader, "groupHeader")
        })
            if (!double.IsFinite(value) || value < 0) throw new DiagramCommandException("LAYOUT_OPTIONS_INVALID", $"{name} must be a finite non-negative number.");
        if (!double.IsFinite(options.OriginX) || !double.IsFinite(options.OriginY))
            throw new DiagramCommandException("LAYOUT_OPTIONS_INVALID", "origin coordinates must be finite.");
        if (options.CrossingSweeps is < 0 or > 24)
            throw new DiagramCommandException("LAYOUT_OPTIONS_INVALID", "crossingSweeps must be between 0 and 24.");
        if (options.MaximumNodes <= 0 || options.MaximumEdges <= 0)
            throw new DiagramCommandException("LAYOUT_OPTIONS_INVALID", "graph limits must be positive.");
    }

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private sealed class CompoundLayouter(
        LayoutModel model,
        DiagramLayoutOptions options,
        int seed,
        LayoutStatistics statistics,
        CancellationToken cancellationToken)
    {
        public ContainerLayout Layout()
        {
            var result = LayoutContainer(null);
            return result with { X = options.OriginX, Y = options.OriginY };
        }

        public ImmutableArray<GhostagramOperation> CreateOperations(ContainerLayout root)
        {
            var operations = ImmutableArray.CreateBuilder<GhostagramOperation>();
            Emit(root, root.X, root.Y, operations);
            return operations.ToImmutable();
        }

        private ContainerLayout LayoutContainer(string? groupId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nested = model.Groups.Values
                .Where(group => string.Equals(group.ParentGroupId, groupId, StringComparison.Ordinal))
                .OrderBy(group => StableKey(group.Id, seed))
                .ThenBy(group => group.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Id, group => LayoutContainer(group.Id), StringComparer.Ordinal);
            var children = new List<FlatItem>();
            children.AddRange(model.Nodes.Values
                .Where(node => string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
                .Select(node => new FlatItem(node.Id, node.Width, node.Height, node.X, node.Y, false)));
            children.AddRange(nested.Select(pair =>
            {
                var group = model.Groups[pair.Key];
                return new FlatItem(group.Id, pair.Value.Width, pair.Value.Height, group.X, group.Y, true);
            }));

            if (children.Count == 0)
            {
                if (groupId is null) return new ContainerLayout(null, 0, 0, 0, 0, []);
                var empty = model.Groups[groupId];
                return new ContainerLayout(groupId, 0, 0, empty.Width, empty.Height, []);
            }

            var childIds = children.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var edges = ProjectEdges(groupId, childIds);
            var flat = FlatLayouter.Layout(children, edges, options, seed, statistics, cancellationToken);
            var leftInset = groupId is null ? 0 : options.GroupPadding;
            var topInset = groupId is null ? 0 : options.GroupHeader + options.GroupPadding;
            var rightInset = groupId is null ? 0 : options.GroupPadding;
            var bottomInset = groupId is null ? 0 : options.GroupPadding;
            var placed = flat.Placements
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new PlacedChild(
                    pair.Key,
                    pair.Value.X + leftInset,
                    pair.Value.Y + topInset,
                    pair.Value.Width,
                    pair.Value.Height,
                    nested.GetValueOrDefault(pair.Key)))
                .ToImmutableArray();
            return new ContainerLayout(
                groupId,
                0,
                0,
                flat.Width + leftInset + rightInset,
                flat.Height + topInset + bottomInset,
                placed);
        }

        private List<FlatEdge> ProjectEdges(string? containerGroupId, HashSet<string> childIds)
        {
            var weights = new Dictionary<(string Source, string Target), int>();
            foreach (var edge in model.Edges)
            {
                var source = DirectItem(containerGroupId, edge.SourceNodeId);
                var target = DirectItem(containerGroupId, edge.TargetNodeId);
                if (source is null || target is null || source == target || !childIds.Contains(source) || !childIds.Contains(target)) continue;
                weights[(source, target)] = weights.GetValueOrDefault((source, target)) + 1;
            }
            return weights
                .OrderBy(pair => pair.Key.Source, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Target, StringComparer.Ordinal)
                .Select(pair => new FlatEdge(pair.Key.Source, pair.Key.Target, pair.Value))
                .ToList();
        }

        private string? DirectItem(string? containerGroupId, string nodeId)
        {
            var node = model.Nodes[nodeId];
            if (string.Equals(node.GroupId, containerGroupId, StringComparison.Ordinal)) return node.Id;
            if (node.GroupId is null) return containerGroupId is null ? node.Id : null;
            var currentId = node.GroupId;
            while (currentId is not null)
            {
                var group = model.Groups[currentId];
                if (string.Equals(group.ParentGroupId, containerGroupId, StringComparison.Ordinal)) return group.Id;
                currentId = group.ParentGroupId;
            }
            return null;
        }

        private void Emit(
            ContainerLayout container,
            double absoluteX,
            double absoluteY,
            ImmutableArray<GhostagramOperation>.Builder operations)
        {
            foreach (var child in container.Children.OrderBy(item => item.Nested is null).ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                var x = absoluteX + child.X;
                var y = absoluteY + child.Y;
                if (child.Nested is not null)
                {
                    var group = model.Groups[child.Id];
                    if (!Nearly(group.X, x) || !Nearly(group.Y, y) || !Nearly(group.Width, child.Width) || !Nearly(group.Height, child.Height))
                        operations.Add(new("group.upsert", Updated(group.Source, x, y, child.Width, child.Height)));
                    Emit(child.Nested, x, y, operations);
                }
                else
                {
                    var node = model.Nodes[child.Id];
                    if (!Nearly(node.X, x) || !Nearly(node.Y, y))
                        operations.Add(new("node.upsert", Updated(node.Source, x, y, node.Width, node.Height)));
                }
            }
        }

        private static JsonElement Updated(JsonElement source, double x, double y, double width, double height)
        {
            var value = JsonNode.Parse(source.GetRawText())!.AsObject();
            value["x"] = Round(x);
            value["y"] = Round(y);
            value["width"] = Round(width);
            value["height"] = Round(height);
            return JsonSerializer.SerializeToElement(value);
        }

        private static bool Nearly(double left, double right) => Math.Abs(left - right) < .0005;
    }

    private static class FlatLayouter
    {
        public static FlatLayout Layout(
            IReadOnlyList<FlatItem> items,
            IReadOnlyList<FlatEdge> edges,
            DiagramLayoutOptions options,
            int seed,
            LayoutStatistics statistics,
            CancellationToken cancellationToken)
        {
            var itemById = items.ToDictionary(item => item.Id, StringComparer.Ordinal);
            var components = WeakComponents(items, edges, seed);
            var placements = new Dictionary<string, FlatPlacement>(StringComparer.Ordinal);
            var crossOffset = 0d;
            var width = 0d;
            var height = 0d;

            foreach (var component in components)
            {
                cancellationToken.ThrowIfCancellationRequested();
                statistics.ComponentCount++;
                var componentEdges = edges.Where(edge => component.Contains(edge.Source) && component.Contains(edge.Target)).ToArray();
                var logical = LayoutComponent(component.Select(id => itemById[id]).ToArray(), componentEdges, options, seed, statistics);
                foreach (var pair in logical.Placements)
                {
                    var item = itemById[pair.Key];
                    var logicalPlacement = pair.Value with { Cross = pair.Value.Cross + crossOffset };
                    var physical = ToPhysical(logicalPlacement, item, logical.PrimaryExtent, options.Direction);
                    placements[pair.Key] = physical;
                    width = Math.Max(width, physical.X + physical.Width);
                    height = Math.Max(height, physical.Y + physical.Height);
                }
                crossOffset += logical.CrossExtent + options.ComponentSpacing;
            }
            return new FlatLayout(placements, width, height);
        }

        private static LogicalLayout LayoutComponent(
            IReadOnlyList<FlatItem> items,
            IReadOnlyList<FlatEdge> edges,
            DiagramLayoutOptions options,
            int seed,
            LayoutStatistics statistics)
        {
            var itemById = items.ToDictionary(item => item.Id, StringComparer.Ordinal);
            var adjacency = items.ToDictionary(item => item.Id, _ => new List<string>(), StringComparer.Ordinal);
            var reverse = items.ToDictionary(item => item.Id, _ => new List<string>(), StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                adjacency[edge.Source].Add(edge.Target);
                reverse[edge.Target].Add(edge.Source);
            }
            foreach (var list in adjacency.Values) list.Sort(StringComparer.Ordinal);
            foreach (var list in reverse.Values) list.Sort(StringComparer.Ordinal);

            var strong = StrongComponents(items.Select(item => item.Id), adjacency, reverse, seed);
            statistics.StrongComponentCount += strong.Count;
            var componentByItem = new Dictionary<string, int>(StringComparer.Ordinal);
            var spans = new int[strong.Count];
            for (var index = 0; index < strong.Count; index++)
            {
                foreach (var id in strong[index]) componentByItem[id] = index;
                var selfLoop = strong[index].Count == 1 && edges.Any(edge => edge.Source == strong[index][0] && edge.Target == strong[index][0]);
                var cyclic = strong[index].Count > 1 || selfLoop;
                if (cyclic) statistics.CyclicComponentCount++;
                spans[index] = cyclic ? Math.Max(1, (int)Math.Ceiling(Math.Sqrt(strong[index].Count))) : 1;
                strong[index].Sort((left, right) => CompareStable(itemById[left], itemById[right], options.Direction, seed));
            }

            var dag = Enumerable.Range(0, strong.Count).ToDictionary(index => index, _ => new HashSet<int>());
            var indegree = new int[strong.Count];
            foreach (var edge in edges)
            {
                var source = componentByItem[edge.Source];
                var target = componentByItem[edge.Target];
                if (source != target && dag[source].Add(target)) indegree[target]++;
            }
            var comparer = Comparer<int>.Create((left, right) =>
            {
                if (left == right) return 0;
                var byKey = StableKey(strong[left][0], seed).CompareTo(StableKey(strong[right][0], seed));
                return byKey != 0 ? byKey : left.CompareTo(right);
            });
            var ready = new SortedSet<int>(Enumerable.Range(0, strong.Count).Where(index => indegree[index] == 0), comparer);
            var baseRank = new int[strong.Count];
            while (ready.Count > 0)
            {
                var source = ready.Min;
                ready.Remove(source);
                foreach (var target in dag[source].Order(comparer))
                {
                    baseRank[target] = Math.Max(baseRank[target], baseRank[source] + spans[source]);
                    if (--indegree[target] == 0) ready.Add(target);
                }
            }

            var rankByItem = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var component = 0; component < strong.Count; component++)
                for (var index = 0; index < strong[component].Count; index++)
                    rankByItem[strong[component][index]] = baseRank[component] + (index % spans[component]);

            var layers = rankByItem
                .GroupBy(pair => pair.Value)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(pair => pair.Key)
                        .OrderBy(id => ExistingCross(itemById[id], options.Direction))
                        .ThenBy(id => StableKey(id, seed))
                        .ThenBy(id => id, StringComparer.Ordinal)
                        .ToList());
            var crossingsBefore = EstimateCrossings(layers, rankByItem, edges);
            var bestCrossings = crossingsBefore;
            var bestLayers = CloneLayers(layers);
            for (var sweep = 0; sweep < options.CrossingSweeps; sweep++)
            {
                Sweep(layers, rankByItem, reverse, downward: true, seed);
                Sweep(layers, rankByItem, adjacency, downward: false, seed);
                var crossings = EstimateCrossings(layers, rankByItem, edges);
                if (crossings <= bestCrossings)
                {
                    bestCrossings = crossings;
                    bestLayers = CloneLayers(layers);
                }
            }
            statistics.CrossingsBefore += crossingsBefore;
            statistics.CrossingsAfter += bestCrossings;

            var primaryOffset = 0d;
            var logical = new Dictionary<string, LogicalPlacement>(StringComparer.Ordinal);
            var layerPrimary = new Dictionary<int, double>();
            var layerCrossSize = new Dictionary<int, double>();
            foreach (var rank in bestLayers.Keys.Order())
            {
                layerPrimary[rank] = primaryOffset;
                var maximumPrimary = bestLayers[rank].Max(id => PrimarySize(itemById[id], options.Direction));
                primaryOffset += maximumPrimary + options.LayerSpacing;
                layerCrossSize[rank] = bestLayers[rank].Sum(id => CrossSize(itemById[id], options.Direction))
                    + Math.Max(0, bestLayers[rank].Count - 1) * options.NodeSpacing;
            }
            var primaryExtent = Math.Max(0, primaryOffset - options.LayerSpacing);
            var crossExtent = layerCrossSize.Values.DefaultIfEmpty(0).Max();
            foreach (var rank in bestLayers.Keys.Order())
            {
                var cross = (crossExtent - layerCrossSize[rank]) / 2;
                foreach (var id in bestLayers[rank])
                {
                    var item = itemById[id];
                    logical[id] = new LogicalPlacement(layerPrimary[rank], cross, primaryExtent);
                    cross += CrossSize(item, options.Direction) + options.NodeSpacing;
                }
            }
            return new LogicalLayout(logical, primaryExtent, crossExtent);
        }

        private static List<HashSet<string>> WeakComponents(IReadOnlyList<FlatItem> items, IReadOnlyList<FlatEdge> edges, int seed)
        {
            var neighbors = items.ToDictionary(item => item.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            foreach (var edge in edges) { neighbors[edge.Source].Add(edge.Target); neighbors[edge.Target].Add(edge.Source); }
            var remaining = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var result = new List<HashSet<string>>();
            foreach (var start in items.Select(item => item.Id).OrderBy(id => StableKey(id, seed)).ThenBy(id => id, StringComparer.Ordinal))
            {
                if (!remaining.Remove(start)) continue;
                var component = new HashSet<string>(StringComparer.Ordinal) { start };
                var pending = new Queue<string>();
                pending.Enqueue(start);
                while (pending.TryDequeue(out var current))
                    foreach (var neighbor in neighbors[current].Order(StringComparer.Ordinal))
                        if (remaining.Remove(neighbor)) { component.Add(neighbor); pending.Enqueue(neighbor); }
                result.Add(component);
            }
            return result;
        }

        private static List<List<string>> StrongComponents(
            IEnumerable<string> ids,
            IReadOnlyDictionary<string, List<string>> adjacency,
            IReadOnlyDictionary<string, List<string>> reverse,
            int seed)
        {
            var ordered = ids.OrderBy(id => StableKey(id, seed)).ThenBy(id => id, StringComparer.Ordinal).ToArray();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var finish = new List<string>();
            foreach (var start in ordered)
            {
                if (!visited.Add(start)) continue;
                var stack = new Stack<(string Id, bool Exit)>();
                stack.Push((start, false));
                while (stack.TryPop(out var frame))
                {
                    if (frame.Exit) { finish.Add(frame.Id); continue; }
                    stack.Push((frame.Id, true));
                    for (var index = adjacency[frame.Id].Count - 1; index >= 0; index--)
                    {
                        var neighbor = adjacency[frame.Id][index];
                        if (visited.Add(neighbor)) stack.Push((neighbor, false));
                    }
                }
            }
            visited.Clear();
            var components = new List<List<string>>();
            for (var index = finish.Count - 1; index >= 0; index--)
            {
                var start = finish[index];
                if (!visited.Add(start)) continue;
                var component = new List<string>();
                var pending = new Stack<string>();
                pending.Push(start);
                while (pending.TryPop(out var current))
                {
                    component.Add(current);
                    foreach (var neighbor in reverse[current])
                        if (visited.Add(neighbor)) pending.Push(neighbor);
                }
                components.Add(component);
            }
            return components;
        }

        private static void Sweep(
            Dictionary<int, List<string>> layers,
            IReadOnlyDictionary<string, int> ranks,
            IReadOnlyDictionary<string, List<string>> neighborsByItem,
            bool downward,
            int seed)
        {
            var positions = Positions(layers);
            var orderedRanks = downward ? layers.Keys.Order().ToArray() : layers.Keys.OrderDescending().ToArray();
            foreach (var rank in orderedRanks.Skip(1))
            {
                var current = layers[rank];
                if (current.Count <= 1) continue;
                var old = current.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
                var scored = current.Select(id =>
                {
                    var neighbors = neighborsByItem[id]
                        .Where(neighbor => downward ? ranks[neighbor] < rank : ranks[neighbor] > rank)
                        .Where(positions.ContainsKey)
                        .Select(neighbor => (double)positions[neighbor])
                        .Order()
                        .ToArray();
                    var barycenter = neighbors.Length == 0 ? old[id] : neighbors.Average();
                    return new { Id = id, Score = barycenter * .85 + old[id] * .15, Old = old[id] };
                });
                layers[rank] = scored
                    .OrderBy(item => item.Score)
                    .ThenBy(item => item.Old)
                    .ThenBy(item => StableKey(item.Id, seed))
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => item.Id)
                    .ToList();
                for (var index = 0; index < layers[rank].Count; index++) positions[layers[rank][index]] = index;
            }
        }

        private static long EstimateCrossings(
            IReadOnlyDictionary<int, List<string>> layers,
            IReadOnlyDictionary<string, int> ranks,
            IReadOnlyList<FlatEdge> edges)
        {
            var positions = Positions(layers);
            long total = 0;
            foreach (var band in edges
                         .Where(edge => ranks[edge.Source] != ranks[edge.Target])
                         .Select(edge => ranks[edge.Source] < ranks[edge.Target]
                             ? (LowRank: ranks[edge.Source], HighRank: ranks[edge.Target], Low: edge.Source, High: edge.Target, edge.Weight)
                             : (LowRank: ranks[edge.Target], HighRank: ranks[edge.Source], Low: edge.Target, High: edge.Source, edge.Weight))
                         .GroupBy(edge => (edge.LowRank, edge.HighRank)))
            {
                var ordered = band
                    .OrderBy(edge => positions[edge.Low])
                    .ThenBy(edge => positions[edge.High])
                    .ToArray();
                var tree = new FenwickTree(ordered.Select(edge => positions[edge.High]).DefaultIfEmpty(0).Max() + 1);
                long seenWeight = 0;
                foreach (var sourceGroup in ordered.GroupBy(edge => positions[edge.Low]))
                {
                    foreach (var edge in sourceGroup)
                        total += (long)edge.Weight * (seenWeight - tree.PrefixSum(positions[edge.High]));
                    foreach (var edge in sourceGroup)
                    {
                        tree.Add(positions[edge.High], edge.Weight);
                        seenWeight += edge.Weight;
                    }
                }
            }
            return total;
        }

        private static Dictionary<string, int> Positions(IReadOnlyDictionary<int, List<string>> layers)
            => layers.SelectMany(pair => pair.Value.Select((id, index) => (id, index)))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

        private static Dictionary<int, List<string>> CloneLayers(IReadOnlyDictionary<int, List<string>> layers)
            => layers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());

        private static FlatPlacement ToPhysical(LogicalPlacement placement, FlatItem item, double primaryExtent, string direction)
        {
            var normalized = direction.ToLowerInvariant();
            return normalized switch
            {
                "left" => new(primaryExtent - placement.Primary - item.Width, placement.Cross, item.Width, item.Height),
                "down" => new(placement.Cross, placement.Primary, item.Width, item.Height),
                "up" => new(placement.Cross, primaryExtent - placement.Primary - item.Height, item.Width, item.Height),
                _ => new(placement.Primary, placement.Cross, item.Width, item.Height)
            };
        }

        private static int CompareStable(FlatItem left, FlatItem right, string direction, int seed)
        {
            var position = ExistingCross(left, direction).CompareTo(ExistingCross(right, direction));
            if (position != 0) return position;
            var key = StableKey(left.Id, seed).CompareTo(StableKey(right.Id, seed));
            return key != 0 ? key : string.CompareOrdinal(left.Id, right.Id);
        }

        private static double ExistingCross(FlatItem item, string direction)
            => direction.Equals("right", StringComparison.OrdinalIgnoreCase) || direction.Equals("left", StringComparison.OrdinalIgnoreCase) ? item.Y : item.X;
        private static double PrimarySize(FlatItem item, string direction)
            => direction.Equals("right", StringComparison.OrdinalIgnoreCase) || direction.Equals("left", StringComparison.OrdinalIgnoreCase) ? item.Width : item.Height;
        private static double CrossSize(FlatItem item, string direction)
            => direction.Equals("right", StringComparison.OrdinalIgnoreCase) || direction.Equals("left", StringComparison.OrdinalIgnoreCase) ? item.Height : item.Width;
    }

    private sealed class LayoutModel(
        Dictionary<string, NodeSpec> nodes,
        Dictionary<string, GroupSpec> groups,
        List<ModelEdge> edges)
    {
        public Dictionary<string, NodeSpec> Nodes { get; } = nodes;
        public Dictionary<string, GroupSpec> Groups { get; } = groups;
        public List<ModelEdge> Edges { get; } = edges;

        public static LayoutModel Parse(JsonElement model)
        {
            if (model.ValueKind != JsonValueKind.Object) throw new DiagramCommandException("INVALID_MODEL", "The layout model must be an object.");
            var groups = Array(model, "groups").Select(item => new GroupSpec(
                String(item, "id"), OptionalString(item, "parentGroupId"),
                Number(item, "x"), Number(item, "y"), Positive(item, "width"), Positive(item, "height"), item.Clone()))
                .ToDictionary(group => group.Id, StringComparer.Ordinal);
            foreach (var group in groups.Values)
            {
                if (group.ParentGroupId is not null && !groups.ContainsKey(group.ParentGroupId))
                    throw new DiagramCommandException("MISSING_REFERENCE", $"Group '{group.Id}' references a missing parent.");
                var ancestors = new HashSet<string>(StringComparer.Ordinal) { group.Id };
                var parent = group.ParentGroupId;
                while (parent is not null)
                {
                    if (!ancestors.Add(parent)) throw new DiagramCommandException("INVALID_MODEL", "Group hierarchy contains a cycle.");
                    parent = groups[parent].ParentGroupId;
                }
            }
            var nodes = Array(model, "nodes").Select(item => new NodeSpec(
                String(item, "id"), OptionalString(item, "groupId"),
                Number(item, "x"), Number(item, "y"), Positive(item, "width"), Positive(item, "height"), item.Clone()))
                .ToDictionary(node => node.Id, StringComparer.Ordinal);
            foreach (var node in nodes.Values)
                if (node.GroupId is not null && !groups.ContainsKey(node.GroupId))
                    throw new DiagramCommandException("MISSING_REFERENCE", $"Node '{node.Id}' references a missing group.");
            var ports = Array(model, "ports").ToDictionary(item => String(item, "id"), item => String(item, "nodeId"), StringComparer.Ordinal);
            var edges = new List<ModelEdge>();
            foreach (var edge in Array(model, "edges"))
            {
                var sourcePort = String(edge, "sourcePortId");
                var targetPort = String(edge, "targetPortId");
                if (!ports.TryGetValue(sourcePort, out var sourceNode) || !ports.TryGetValue(targetPort, out var targetNode))
                    throw new DiagramCommandException("MISSING_REFERENCE", $"Edge '{String(edge, "id")}' references a missing port.");
                edges.Add(new ModelEdge(sourceNode, targetNode));
            }
            return new LayoutModel(nodes, groups, edges);
        }

        private static IEnumerable<JsonElement> Array(JsonElement model, string name)
            => model.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : [];
        private static string String(JsonElement item, string name)
            => OptionalString(item, name) ?? throw new DiagramCommandException("INVALID_MODEL", $"'{name}' must be a non-empty string.");
        private static string? OptionalString(JsonElement item, string name)
            => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
        private static double Number(JsonElement item, string name)
            => item.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number)
                ? number
                : throw new DiagramCommandException("INVALID_MODEL", $"'{name}' must be a finite number.");
        private static double Positive(JsonElement item, string name)
        {
            var value = Number(item, name);
            return value > 0 ? value : throw new DiagramCommandException("INVALID_MODEL", $"'{name}' must be positive.");
        }
    }

    private sealed class LayoutStatistics
    {
        public int ComponentCount;
        public int StrongComponentCount;
        public int CyclicComponentCount;
        public long CrossingsBefore;
        public long CrossingsAfter;
    }

    private sealed class FenwickTree(int size)
    {
        private readonly long[] _values = new long[size + 1];

        public void Add(int index, int value)
        {
            for (var cursor = index + 1; cursor < _values.Length; cursor += cursor & -cursor) _values[cursor] += value;
        }

        public long PrefixSum(int index)
        {
            long result = 0;
            for (var cursor = index + 1; cursor > 0; cursor -= cursor & -cursor) result += _values[cursor];
            return result;
        }
    }

    private sealed record NodeSpec(string Id, string? GroupId, double X, double Y, double Width, double Height, JsonElement Source);
    private sealed record GroupSpec(string Id, string? ParentGroupId, double X, double Y, double Width, double Height, JsonElement Source);
    private sealed record ModelEdge(string SourceNodeId, string TargetNodeId);
    private sealed record FlatItem(string Id, double Width, double Height, double X, double Y, bool IsGroup);
    private sealed record FlatEdge(string Source, string Target, int Weight);
    private sealed record FlatPlacement(double X, double Y, double Width, double Height);
    private sealed record LogicalPlacement(double Primary, double Cross, double PrimaryExtent);
    private sealed record FlatLayout(Dictionary<string, FlatPlacement> Placements, double Width, double Height);
    private sealed record LogicalLayout(Dictionary<string, LogicalPlacement> Placements, double PrimaryExtent, double CrossExtent);
    private sealed record ContainerLayout(string? GroupId, double X, double Y, double Width, double Height, ImmutableArray<PlacedChild> Children);
    private sealed record PlacedChild(string Id, double X, double Y, double Width, double Height, ContainerLayout? Nested);

    private static ulong StableKey(string value, int seed)
    {
        var hash = 14695981039346656037UL ^ unchecked((uint)seed);
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
