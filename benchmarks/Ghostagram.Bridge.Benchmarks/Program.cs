using System.Diagnostics;
using System.Text.Json;
using Ghostagram.Bridge;
using Ghostworx.System.Graph;

var repetitions = ReadPositiveInt(args, "--repetitions", 5);
var sizes = ReadSizes(args) ?? [100, 500, 1_000];
var projection = new GraphDiagramProjection();
var deltaProjection = new GraphDiagramDeltaProjector(projection);
var presentation = new GraphPresentationStore().Capture();
var results = new List<Measurement>();

foreach (var size in sizes)
{
    var before = CreateSnapshot(size, renamedIndex: null, version: 1);
    var after = CreateSnapshot(size, renamedIndex: size - 1, version: 2);
    var current = projection.Project(before, presentation);
    var changedId = NodeIdFor(size - 1);
    var batch = new GraphChangeBatch(1, 2, [new GraphChange(2, GraphChangeKind.NodeRenamed, changedId) { OldValue = $"node-{size - 1}", NewValue = $"node-{size - 1}-renamed" }]);

    _ = projection.Project(after, presentation);
    _ = deltaProjection.Project(batch, current, after, presentation);

    var full = Measure(repetitions, () => projection.Project(after, presentation));
    var incremental = Measure(repetitions, () => deltaProjection.Project(batch, current, after, presentation));
    results.Add(new(size, before.Relationships.Count, repetitions, full.TotalMilliseconds, full.TotalMilliseconds / repetitions,
        incremental.TotalMilliseconds, incremental.TotalMilliseconds / repetitions));
}

Console.WriteLine("Ghostagram Bridge projection benchmark (Release recommended; no pass/fail threshold)");
Console.WriteLine("Nodes\tEdges\tRepetitions\tFull total ms\tFull mean ms\tIncremental total ms\tIncremental mean ms");
foreach (var result in results)
    Console.WriteLine($"{result.Nodes}\t{result.Edges}\t{result.Repetitions}\t{result.FullTotalMilliseconds:F3}\t{result.FullMeanMilliseconds:F3}\t{result.IncrementalTotalMilliseconds:F3}\t{result.IncrementalMeanMilliseconds:F3}");
Console.WriteLine("JSON " + JsonSerializer.Serialize(new
{
    schema = "ghostagram.bridge.benchmark.v1",
    runtime = Environment.Version.ToString(),
    measurements = results
}, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

static TimeSpan Measure(int repetitions, Action action)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var stopwatch = Stopwatch.StartNew();
    for (var repetition = 0; repetition < repetitions; repetition++) action();
    stopwatch.Stop();
    return stopwatch.Elapsed;
}

static GraphSnapshot CreateSnapshot(int size, int? renamedIndex, long version)
{
    var kind = NodeKind.Define("Benchmark", "Node");
    var nodes = Enumerable.Range(0, size).Select(index => new GraphNodeSnapshot(
        NodeIdFor(index),
        index == 0 ? NodeKind.Graph : kind,
        index == renamedIndex ? $"node-{index}-renamed" : $"node-{index}",
        new Dictionary<string, object?> { ["ordinal"] = (long)index })).ToArray();
    var relationships = Enumerable.Range(1, Math.Max(0, size - 1)).Select(index => new GraphRelationshipSnapshot(
        new GraphEdge(EdgeIdFor(index), NodeIdFor(index - 1), NodeIdFor(index), RelationshipKind.DependsOn, CreatedAtVersion: 1),
        new Dictionary<string, object?>())).ToArray();
    return new(NodeIdFor(0), version, nodes, relationships);
}

static NodeId NodeIdFor(int index) => new(DeterministicGuid(index + 1));
static EdgeId EdgeIdFor(int index) => new(DeterministicGuid(1_000_000 + index));
static Guid DeterministicGuid(int value)
{
    Span<byte> bytes = stackalloc byte[16];
    BitConverter.TryWriteBytes(bytes, value);
    return new Guid(bytes);
}

static int ReadPositiveInt(string[] arguments, string name, int fallback)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length && int.TryParse(arguments[index + 1], out var parsed) && parsed > 0 ? parsed : fallback;
}

static int[]? ReadSizes(string[] arguments)
{
    var index = Array.IndexOf(arguments, "--sizes");
    if (index < 0 || index + 1 >= arguments.Length) return null;
    var parsed = arguments[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => int.TryParse(value, out var size) && size > 1 ? size : 0).ToArray();
    return parsed.All(size => size > 1) ? parsed : throw new ArgumentException("--sizes requires comma-separated integers greater than one.");
}

sealed record Measurement(
    int Nodes,
    int Edges,
    int Repetitions,
    double FullTotalMilliseconds,
    double FullMeanMilliseconds,
    double IncrementalTotalMilliseconds,
    double IncrementalMeanMilliseconds);
