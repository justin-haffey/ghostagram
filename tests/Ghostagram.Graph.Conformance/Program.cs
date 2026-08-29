using Ghostagram.Graph.Conformance;

var feature = Feature(args);
return feature switch
{
    null => GhostagramConformanceRunner.Run(RunnerOptions.Parse(args)),
    "feature-002" => Feature002ConformanceRunner.Run(Feature002RunnerOptions.Parse(args)),
    "feature-003" => Feature003ConformanceRunner.Run(Feature003RunnerOptions.Parse(args)),
    _ => RejectUnknownFeature()
};

static string? Feature(IReadOnlyList<string> values)
{
    var matches = Enumerable.Range(0, values.Count)
        .Where(index => string.Equals(values[index], "--feature", StringComparison.Ordinal))
        .ToArray();
    if (matches.Length == 0) return null;
    if (matches.Length != 1 || matches[0] + 1 >= values.Count ||
        string.IsNullOrWhiteSpace(values[matches[0] + 1]))
        return string.Empty;
    return values[matches[0] + 1];
}

static int RejectUnknownFeature()
{
    Console.Error.WriteLine("Ghostagram conformance rejected an unknown or malformed feature selector.");
    return 2;
}
