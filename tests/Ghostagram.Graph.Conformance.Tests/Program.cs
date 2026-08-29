using System.Text.Json;
using Ghostagram.Graph.Conformance;
using Ghostagram.Graph.Conformance.Tests;
using Ghostworx.System.Graph.Conformance.Support;

var selectedFeature = Feature(args);
if (selectedFeature == "feature-002") return Feature002Tests.Run(args);
if (selectedFeature == "feature-003") return Feature003Tests.Run(args);
if (selectedFeature is not null)
{
    Console.Error.WriteLine("Ghostagram conformance tests rejected an unknown or malformed feature selector.");
    return 2;
}

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var corpusPath = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "ghostworx-system", "tests", "Ghostworx.System.Graph.Conformance", "Fixtures", "semantic-graph", "v1"));
var schemaPath = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "ghostworx-system", "tests", "Ghostworx.System.Graph.Conformance", "Schemas", "conformance-result.schema.json"));
var testRoot = Path.Combine(Path.GetTempPath(), "ghostagram-conformance-tests", Guid.NewGuid().ToString("N"));
var outputPath = Path.Combine(testRoot, "result.json");

try
{
    var corpus = ConformanceCorpus.Load(corpusPath);
    Assert(corpus.Manifest.Cases.Count == 47, "The frozen FEATURE-001 corpus must contain 47 cases.");
    Assert(Feature001CaseAdapter.AssignedCaseIds.Count == 10, "The Ghostagram allowlist must contain exactly 10 cases.");
    Assert(Feature001CaseAdapter.AssignedCaseIds.All(id => corpus.Manifest.Cases.Any(item => item.Id == id)),
        "Every Ghostagram-assigned case must exist in the manifest.");

    var bridge = GhostagramBridgeHarness.VerifyBridgeContracts();
    Assert(bridge.DeltaProjection && bridge.FullReprojectionFallback
        && bridge.PresentationExclusion && bridge.ExpectedRevisionConflict,
        "Bridge-specific conformance checks did not all pass.");

    var exitCode = GhostagramConformanceRunner.Run(new(corpusPath, schemaPath, outputPath, "tests"));
    Assert(exitCode == 0 && File.Exists(outputPath), "A valid Ghostagram run must return zero and write a result.");
    var envelope = JsonSerializer.Deserialize<ConformanceResultEnvelope>(File.ReadAllText(outputPath),
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true })!;
    Assert(envelope.Participant == GhostagramConformanceRunner.Participant, "Participant identity must be fixed to ghostagram.");
    Assert(envelope.Totals.Total == 47 && envelope.Totals.Passed == 10 && envelope.Totals.Unsupported == 37
        && envelope.Totals.Failed == 0 && envelope.Totals.Missing == 0,
        "The result must contain 10 executed and 37 explicit unsupported cases.");
    Assert(envelope.Cases.Where(item => item.Status == ConformanceCaseStatus.Passed)
        .All(item => item.ObservedFields.ContainsKey("seam")),
        "Every passed case must identify its actual Ghostagram or shared-support seam.");
    ConformanceInvariants.ThrowIfInvalid(corpus.Manifest, envelope);
    ConformanceResultSchemaValidator.Validate(File.ReadAllText(outputPath), schemaPath);

    Console.WriteLine("Ghostagram.Graph.Conformance tests passed (47 cases: 10 executed, 37 unsupported).");
}
finally
{
    if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
}

return 0;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

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
